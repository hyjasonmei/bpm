/**
 * ExpressionEditorModal — full-screen CEL editor with a guideline panel.
 *
 * Used in place of inline CEL textareas for conditional / validator /
 * derivedFrom expressions in FORMS, and for gateway `condition` in
 * DECISIONS. Buffered: edits commit on Save, dismissed on Cancel.
 *
 *   <ExpressionEditorModal
 *     open={open}
 *     title="顯示條件 (Conditional)"
 *     shape="boolean"
 *     initial={value}
 *     contextFieldIds={['leave_type', 'days', 'reason']}
 *     contextVariables={['AUTO_APPROVE_LIMIT']}
 *     onCancel={() => setOpen(false)}
 *     onCommit={(v) => { onChange(v); setOpen(false) }}
 *   />
 */
import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/modal'
import { ExpressionInput } from '@/components/wizard/ExpressionInput'
import type { ExpressionShape } from '@/lib/expressions'

interface Snippet {
  hint: string
  code: string
}
const SNIPPETS: Record<string, Snippet[]> = {
  '日期 / 時間': [
    { hint: '兩個日期相差天數 > 7', code: 'daysBetween(date_range.start, date_range.end) > 7' },
    { hint: '工作日數 > 5', code: 'businessDaysBetween(date_range.start, date_range.end) > 5' },
    { hint: '截止日未過', code: 'now() <= deadline' },
  ],
  '數字': [
    { hint: '金額大於上限', code: 'amount > 50000' },
    { hint: '介於範圍', code: 'value >= 0 && value <= 10000' },
    { hint: '非零正整數', code: 'value > 0' },
  ],
  '欄位互斥 / 多選': [
    { hint: '某幾種值', code: "leave_type in ['sick', 'official']" },
    { hint: '不等於某狀態', code: "status != 'cancelled'" },
  ],
  '字串': [
    { hint: 'email 屬於公司網域', code: "email.endsWith('@acme.com')" },
    { hint: '名稱包含 admin', code: "lower(name).contains('admin')" },
    { hint: '非空字串', code: 'size(reason) > 0' },
  ],
  '變數比對': [
    { hint: '超過自動核准上限', code: 'amount > ${AUTO_APPROVE_LIMIT}' },
    { hint: '在白名單變數內', code: "dept_id in ${ALLOWED_DEPTS}" },
  ],
}

const OPERATORS = ['==', '!=', '<', '<=', '>', '>=', '&&', '||', '!', 'in']
const FUNCTIONS: { sig: string; desc: string }[] = [
  { sig: 'len(list)', desc: '取長度' },
  { sig: 'size(s | list)', desc: '同上' },
  { sig: 'lower(s) / upper(s)', desc: '大小寫轉換' },
  { sig: 's.startsWith / endsWith / contains', desc: '字串檢查（or contains(s, x)）' },
  { sig: 'daysBetween(a, b)', desc: '兩日期差（含週末）' },
  { sig: 'businessDaysBetween(a, b)', desc: '工作日差' },
  { sig: 'now()', desc: '當下 timestamp' },
]

interface Props {
  open: boolean
  /** Heading text shown in modal title bar */
  title: string
  shape: ExpressionShape
  initial: string
  /** Other field IDs the user can reference in this expression. */
  contextFieldIds?: string[]
  /** Flow-level variable names (no `${}` wrapper). */
  contextVariables?: string[]
  placeholder?: string
  onCancel: () => void
  onCommit: (value: string) => void
}

export function ExpressionEditorModal({
  open, title, shape, initial,
  contextFieldIds = [], contextVariables = [],
  placeholder,
  onCancel, onCommit,
}: Props) {
  const [value, setValue] = useState(initial)
  useEffect(() => { if (open) setValue(initial) }, [open, initial])

  function insert(text: string) {
    setValue(prev => {
      if (!prev) return text
      const needsSpace = !/\s$/.test(prev) && !/^\s/.test(text)
      return prev + (needsSpace ? ' ' : '') + text
    })
  }

  return (
    <Modal
      open={open}
      onClose={onCancel}
      title={title}
      size="lg"
      footer={
        <>
          <div className="mr-auto text-xs text-ink-muted">
            shape: <span className="font-mono text-ink">{shape}</span>
            <span className="ml-3">CEL（Common Expression Language）</span>
          </div>
          <Button variant="ghost" onClick={onCancel}>Cancel</Button>
          <Button variant="primary" onClick={() => onCommit(value)}>Save</Button>
        </>
      }
    >
      <div className="grid h-full grid-cols-[1fr_280px] gap-0">
        {/* Editor side */}
        <div className="space-y-3 border-r border-rule p-4">
          <p className="text-xs text-ink-muted">
            {shape === 'boolean'
              ? '寫一段運算回 true / false 的表達式（譬如「金額大於 5 萬」）。'
              : '寫一段運算回任意值的表達式。'}
            右側可點 example / 欄位 / operator 直接插入。
          </p>
          <ExpressionInput
            value={value}
            onChange={setValue}
            shape={shape}
            placeholder={placeholder}
          />

          {/* Inline snippet pills (most-used quick picks) */}
          <div>
            <p className="mb-1.5 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">範例 · examples</p>
            <div className="space-y-3">
              {Object.entries(SNIPPETS).map(([cat, items]) => (
                <div key={cat}>
                  <div className="mb-1 text-[11px] font-semibold text-ink-muted">{cat}</div>
                  <div className="flex flex-col gap-1">
                    {items.map((s, i) => (
                      <button
                        key={i}
                        type="button"
                        onClick={() => setValue(s.code)}
                        className="group flex items-baseline gap-2 rounded border border-transparent px-2 py-1 text-left text-xs hover:border-rule hover:bg-slate-50"
                        title="點按取代目前表達式"
                      >
                        <span className="text-ink-muted">{s.hint}</span>
                        <code className="ml-auto font-mono text-[10.5px] text-ink">{s.code}</code>
                      </button>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Reference side */}
        <div className="space-y-4 overflow-y-auto bg-slate-50/40 p-4 text-xs">
          <RefSection title="可引用的欄位" emptyHint="本表單沒有其他欄位可引用。">
            {contextFieldIds.length > 0 && (
              <div className="flex flex-wrap gap-1">
                {contextFieldIds.map(id => (
                  <ClickChip key={id} onClick={() => insert(id)} mono>{id}</ClickChip>
                ))}
              </div>
            )}
          </RefSection>

          <RefSection title="可引用的變數" emptyHint="目前沒有流程變數（在 VARIABLES 設）。">
            {contextVariables.length > 0 && (
              <div className="flex flex-wrap gap-1">
                {contextVariables.map(name => (
                  <ClickChip key={name} onClick={() => insert('${' + name + '}')} mono>{`\${${name}}`}</ClickChip>
                ))}
              </div>
            )}
          </RefSection>

          <RefSection title="Operators">
            <div className="flex flex-wrap gap-1">
              {OPERATORS.map(op => (
                <ClickChip key={op} onClick={() => insert(op)} mono>{op}</ClickChip>
              ))}
            </div>
          </RefSection>

          <RefSection title="內建函式">
            <ul className="space-y-1">
              {FUNCTIONS.map(f => (
                <li key={f.sig} className="leading-snug">
                  <code className="font-mono text-[10.5px] text-ink">{f.sig}</code>
                  <span className="ml-1 text-ink-muted">— {f.desc}</span>
                </li>
              ))}
            </ul>
          </RefSection>

          <div className="rounded border border-rule bg-white p-2 text-[11px] text-ink-muted">
            CEL 一覽：
            <a
              href="https://github.com/google/cel-spec/blob/master/doc/langdef.md"
              target="_blank"
              rel="noreferrer"
              className="ml-1 text-primary hover:underline"
            >官方語法</a>
          </div>
        </div>
      </div>
    </Modal>
  )
}

function RefSection({ title, emptyHint, children }: { title: string; emptyHint?: string; children?: React.ReactNode }) {
  const isEmpty = !children
  return (
    <div>
      <p className="mb-1.5 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{title}</p>
      {isEmpty
        ? <p className="text-[11px] text-ink-faint">{emptyHint}</p>
        : children}
    </div>
  )
}

function ClickChip({ onClick, children, mono = false }: { onClick: () => void; children: React.ReactNode; mono?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={[
        'rounded border border-rule bg-white px-1.5 py-0.5 text-[11px] text-ink hover:border-primary hover:bg-primary/5 hover:text-primary',
        mono ? 'font-mono' : '',
      ].join(' ')}
      title="點按插入到游標"
    >
      {children}
    </button>
  )
}
