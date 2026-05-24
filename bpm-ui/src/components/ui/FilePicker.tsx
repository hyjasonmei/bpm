import * as React from 'react'
import { Paperclip, X, Loader2, FileText } from 'lucide-react'

import { BPM_SVC_URL, getJwt } from '@/lib/apiFetch'
import { cn } from '@/lib/cn'

export interface FilePickerValue {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
}

export interface FilePickerProps {
  /** Currently-selected file metadata (or null when empty). Controlled. */
  value: FilePickerValue | null
  /** Fires after a successful upload, or with null when the user clears. */
  onChange: (next: FilePickerValue | null) => void
  /** HTML accept attribute (e.g. ".pdf,.jpg,.png,.doc,image/*"). */
  accept?: string
  /** Soft cap for the warning hint; the backend enforces its own hard cap. */
  maxBytesHint?: number
  /** Disables the picker (read-only mode in task panels). */
  disabled?: boolean
  /** Optional fallback label when value is present but server didn't return a name. */
  placeholder?: string
  className?: string
}

const DEFAULT_MAX_HINT = 10 * 1024 * 1024

/**
 * Core file-upload primitive consumed by chef-cooked feature forms.
 * POSTs multipart/form-data to /api/files and surfaces the returned
 * `{id, fileName, contentType, sizeBytes}` to the caller via onChange.
 * Feature code stores `value.id` inside form-data JSON; backend handlers
 * read it back via GET /api/files/{id}.
 */
export function FilePicker({
  value,
  onChange,
  accept,
  maxBytesHint = DEFAULT_MAX_HINT,
  disabled,
  placeholder = 'Click or drop a file',
  className,
}: FilePickerProps) {
  const inputRef = React.useRef<HTMLInputElement | null>(null)
  const [busy, setBusy] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)
  const [dragOver, setDragOver] = React.useState(false)

  async function upload(file: File) {
    setError(null)

    if (file.size > maxBytesHint) {
      setError(`File too large (${prettyBytes(file.size)} > ${prettyBytes(maxBytesHint)})`)
      return
    }

    setBusy(true)
    try {
      // multipart/form-data — let the browser set the boundary, so don't
      // hand-roll Content-Type. apiFetch only adds Authorization.
      const fd = new FormData()
      fd.append('file', file)

      // apiFetch normally adds Authorization. Re-use its base URL + token but
      // bypass its default Content-Type-leaning init so the boundary is set
      // by the browser.
      const res = await fetch(`${BPM_SVC_URL}/api/files`, {
        method: 'POST',
        body: fd,
        headers: authHeaders(),
      })

      if (res.status === 413) {
        setError('File too large (server cap)')
        return
      }
      if (!res.ok) {
        setError(`Upload failed (${res.status})`)
        return
      }

      const meta = (await res.json()) as FilePickerValue
      onChange(meta)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  function clear() {
    setError(null)
    onChange(null)
    if (inputRef.current) inputRef.current.value = ''
  }

  const hasValue = !!value

  return (
    <div className={cn('space-y-1', className)}>
      <label
        onDragOver={e => {
          if (disabled || busy) return
          e.preventDefault()
          setDragOver(true)
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={e => {
          if (disabled || busy) return
          e.preventDefault()
          setDragOver(false)
          const file = e.dataTransfer.files?.[0]
          if (file) void upload(file)
        }}
        className={cn(
          'flex min-h-8 cursor-pointer items-center gap-2 rounded-md border border-dashed border-rule bg-card px-3 py-1.5 text-sm text-ink',
          disabled && 'cursor-not-allowed bg-slate-50 text-ink-muted',
          dragOver && !disabled && 'border-primary bg-primary/5',
          busy && 'cursor-progress',
        )}
      >
        {busy ? (
          <>
            <Loader2 className="h-3.5 w-3.5 animate-spin text-ink-muted" />
            <span className="text-ink-muted">Uploading…</span>
          </>
        ) : hasValue ? (
          <>
            <FileText className="h-3.5 w-3.5 text-primary" />
            <span className="truncate font-medium text-ink">{value!.fileName}</span>
            <span className="text-xs text-ink-faint">{prettyBytes(value!.sizeBytes)}</span>
          </>
        ) : (
          <>
            <Paperclip className="h-3.5 w-3.5 text-ink-muted" />
            <span className="text-ink-muted">{placeholder}</span>
            <span className="ml-auto text-xs text-ink-faint">≤ {prettyBytes(maxBytesHint)}</span>
          </>
        )}
        <input
          ref={inputRef}
          type="file"
          className="hidden"
          accept={accept}
          disabled={disabled || busy}
          onChange={e => {
            const file = e.target.files?.[0]
            if (file) void upload(file)
          }}
        />
        {hasValue && !disabled && !busy && (
          <button
            type="button"
            onClick={e => {
              // The wrapping <label> would re-open the picker — stop that here.
              e.preventDefault()
              e.stopPropagation()
              clear()
            }}
            className="ml-auto rounded p-0.5 text-ink-faint hover:bg-slate-100 hover:text-ink"
            aria-label="Remove file"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        )}
      </label>
      {error && <div className="text-xs text-danger">{error}</div>}
    </div>
  )
}

/** Open-in-new-tab link helper for read-only views of a previously-uploaded file. */
export function fileDownloadUrl(id: string): string {
  return `${BPM_SVC_URL}/api/files/${id}`
}

function authHeaders(): HeadersInit {
  const token = getJwt()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

function prettyBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / 1024 / 1024).toFixed(1)} MB`
}
