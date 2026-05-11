/**
 * CEL expression validation client + React hook.
 *
 * Talks to `POST /api/specs/validate-expression` (see
 * bpm-svc/src/Api/Spec/SpecValidationController.cs). The wizard wires the hook
 * into each `conditional` / `validator` / `derivedFrom` / gateway-condition
 * input so authors get inline ✓ / ✗ feedback as they type, debounced 500 ms.
 *
 * Bilingual (zh-TW + en) error messages — common evaluator-error fragments
 * are mapped to friendlier 中文 strings; everything else falls back to the
 * raw evaluator message so we never hide a real parse/scope error.
 */
import { useEffect, useRef, useState } from 'react'
import { apiFetch } from '@/lib/apiFetch'

export type ExpressionShape = 'boolean' | 'any'

export interface ValidateSampleContext {
  formData?: Record<string, unknown>
  now?: string
}

export interface ValidateExpressionResponse {
  valid: boolean
  errors?: string[]
  evaluatedSample?: unknown
}

interface RawResponse {
  valid: boolean
  errors?: string[]
  evaluatedSample?: unknown
}

/**
 * POST /api/specs/validate-expression. Non-200 responses become
 * `{ valid: false, errors: [<status>] }` so callers don't need a try/catch.
 *
 * Pass an `AbortSignal` (e.g. from `useDebouncedExpressionValidator`) to
 * cancel an in-flight request when the user keeps typing.
 */
export async function validateExpression(
  expression: string,
  shape: ExpressionShape,
  sampleContext?: ValidateSampleContext,
  signal?: AbortSignal,
): Promise<ValidateExpressionResponse> {
  try {
    const res = await apiFetch('/api/specs/validate-expression', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        expression,
        shape,
        sample_context: sampleContext
          ? { form_data: sampleContext.formData, now: sampleContext.now }
          : null,
      }),
      signal,
    })
    if (!res.ok) {
      return { valid: false, errors: [`HTTP ${res.status}`] }
    }
    const body = (await res.json()) as RawResponse
    return {
      valid: body.valid,
      errors: body.errors,
      evaluatedSample: body.evaluatedSample,
    }
  } catch (err) {
    // Aborted by caller — let the hook treat as "no result yet" by
    // re-throwing a sentinel. Network failures fall through as invalid.
    if ((err as { name?: string })?.name === 'AbortError') throw err
    return { valid: false, errors: [(err as Error).message ?? 'network error'] }
  }
}

/* ── Bilingual error mapping ───────────────────────────────────────────── */

/**
 * Tiny dictionary of common evaluator-error fragments → friendlier 中文.
 * Matched as substrings, case-sensitive. The unmatched fallback is the raw
 * evaluator string — Jason explicitly wants that as the safety net so we
 * never hide a real parse/scope error behind a translation.
 */
const ERROR_DICT: ReadonlyArray<readonly [pattern: string, zh: string]> = [
  ['expression is required', '請填入運算式'],
  ['Syntax error', '語法錯誤'],
  ['undeclared reference', '引用了未定義的欄位'],
  ['undefined field', '欄位不存在'],
  ['token recognition error', '無法辨識的符號'],
  ['no such overload', '函式參數型別不符'],
  ['runtime error during sample evaluation', '範例資料計算時發生錯誤'],
]

export function localizeExpressionError(raw: string): string {
  for (const [pattern, zh] of ERROR_DICT) {
    if (raw.includes(pattern)) return `${zh}（${raw}）`
  }
  return raw
}

/* ── Debounced React hook ──────────────────────────────────────────────── */

export type ValidationStatus = 'idle' | 'pending' | 'valid' | 'invalid'

export interface ValidationState {
  status: ValidationStatus
  errors: string[]
  /** When the API returned a sample evaluation (sampleContext path), surface it. */
  evaluatedSample?: unknown
}

const IDLE: ValidationState = { status: 'idle', errors: [] }

/**
 * Debounced expression validator. Calling pattern:
 *
 *   const { status, errors } = useDebouncedExpressionValidator(expr, 'boolean')
 *
 * - Empty / whitespace-only `expr` → `idle` (no fetch, no error).
 * - During the debounce window or while a fetch is in flight → `pending`.
 * - Cancellation-safe: every keystroke aborts the previous fetch via
 *   AbortController; an `isMounted` ref prevents `setState` after unmount.
 */
export function useDebouncedExpressionValidator(
  expression: string,
  shape: ExpressionShape,
  sampleContext?: ValidateSampleContext,
  delayMs: number = 500,
): ValidationState {
  const [state, setState] = useState<ValidationState>(IDLE)

  // Stringify the sampleContext so the effect's dep array stays referentially
  // stable; otherwise a freshly-built object every render re-fires the fetch.
  const sampleKey = sampleContext ? JSON.stringify(sampleContext) : ''

  const isMountedRef = useRef(true)
  useEffect(() => {
    isMountedRef.current = true
    return () => {
      isMountedRef.current = false
    }
  }, [])

  useEffect(() => {
    if (!expression || expression.trim() === '') {
      setState(IDLE)
      return
    }
    setState({ status: 'pending', errors: [] })

    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      const ctx = sampleKey ? (JSON.parse(sampleKey) as ValidateSampleContext) : undefined
      validateExpression(expression, shape, ctx, controller.signal)
        .then(res => {
          if (!isMountedRef.current) return
          setState({
            status: res.valid ? 'valid' : 'invalid',
            errors: (res.errors ?? []).map(localizeExpressionError),
            evaluatedSample: res.evaluatedSample,
          })
        })
        .catch(err => {
          // AbortError: superseded by next keystroke — keep current state.
          if ((err as { name?: string })?.name === 'AbortError') return
          if (!isMountedRef.current) return
          setState({ status: 'invalid', errors: [(err as Error).message] })
        })
    }, delayMs)

    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [expression, shape, sampleKey, delayMs])

  return state
}
