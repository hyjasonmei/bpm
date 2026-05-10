import { Plus, Trash2, AlertCircle } from 'lucide-react'
import { Field, Input, Textarea } from '@/components/ui/form'
import type { DraftSpec, TestCase } from '@/lib/onboarding'

export function StepTest({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const upsert = (tc: TestCase) => {
    const others = draft.testCases.filter(x => x.id !== tc.id)
    setDraft({ ...draft, testCases: [...others, tc] })
  }
  const remove = (id: string) => setDraft({ ...draft, testCases: draft.testCases.filter(x => x.id !== id) })

  const addCase = () => {
    const n = draft.testCases.length + 1
    upsert({
      id: `tc_${n}`,
      name: `Test case ${n}`,
      inputs: {},
      expectedPath: [],
      expectedApprovers: [],
    })
  }

  const allUserTaskFields = draft.userTasks.flatMap(t => t.fields.map(f => ({ taskId: t.id, ...f })))

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-start justify-between">
        <p className="text-xs text-ink-muted max-w-xl">
          每個 test case 模擬一筆案件：給 input → 流程引擎走出 expectedPath → 預期的 approver 接到。
          至少要有一個 test case 才能 Submit。
        </p>
        <button
          onClick={addCase}
          className="flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
        >
          <Plus className="h-3.5 w-3.5" /> Add Test Case
        </button>
      </div>

      {draft.testCases.length === 0 && (
        <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
          還沒有 test case。最少 1 個。
        </div>
      )}

      {[...draft.testCases].sort((a, b) => a.id.localeCompare(b.id)).map(tc => (
        <TestCaseCard
          key={tc.id}
          tc={tc}
          fields={allUserTaskFields}
          flowNodes={draft.flow.nodes.map(n => n.id)}
          approvalNodes={draft.flow.nodes.filter(n => n.type === 'approval').map(n => n.id)}
          onChange={upsert}
          onRemove={() => remove(tc.id)}
        />
      ))}
    </div>
  )
}

function TestCaseCard({
  tc, fields, flowNodes, approvalNodes, onChange, onRemove,
}: {
  tc: TestCase
  fields: { taskId: string; id: string; label: { 'zh-TW': string }; type: string }[]
  flowNodes: string[]
  approvalNodes: string[]
  onChange: (tc: TestCase) => void
  onRemove: () => void
}) {
  const inputsJson = JSON.stringify(tc.inputs ?? {}, null, 2)
  const pathStr = (tc.expectedPath ?? []).join(' → ')

  const onInputsChange = (text: string) => {
    try {
      const parsed = JSON.parse(text)
      onChange({ ...tc, inputs: parsed })
    } catch {
      // keep typing — error shown via icon
    }
  }
  const inputsValid = (() => {
    try { JSON.parse(inputsJson); return true } catch { return false }
  })()

  return (
    <div className="rounded-md border border-rule bg-white">
      <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-3 py-2">
        <div className="flex items-center gap-2">
          <span className="font-mono text-[11px] text-ink-faint">{tc.id}</span>
          <Input
            className="h-7 max-w-md text-sm font-semibold"
            value={tc.name}
            onChange={e => onChange({ ...tc, name: e.target.value })}
          />
        </div>
        <button
          onClick={onRemove}
          className="flex h-7 w-7 items-center justify-center rounded text-ink-faint hover:bg-rose-50 hover:text-danger"
          title="Remove"
        >
          <Trash2 className="h-4 w-4" />
        </button>
      </div>

      <div className="p-3 space-y-3">
        <Field label="Inputs (JSON)" hint={`對應欄位：${fields.map(f => f.id).join(', ') || '(尚無 user task field)'}`}>
          <div className="relative">
            <Textarea
              rows={Math.min(8, inputsJson.split('\n').length)}
              className="font-mono text-[11px]"
              value={inputsJson}
              onChange={e => onInputsChange(e.target.value)}
            />
            {!inputsValid && (
              <div className="absolute right-2 top-2 text-amber-600" title="JSON parse error">
                <AlertCircle className="h-4 w-4" />
              </div>
            )}
          </div>
        </Field>

        <Field label="Expected path" hint="從 startEvent 到 endEvent 經過的 node id，逗號分隔">
          <Input
            value={(tc.expectedPath ?? []).join(',')}
            onChange={e => onChange({ ...tc, expectedPath: e.target.value.split(',').map(s => s.trim()).filter(Boolean) })}
            placeholder="start_1, task_apply, ..."
          />
          {pathStr && <p className="mt-1 text-[11px] font-mono text-ink-faint">{pathStr}</p>}
          <p className="mt-1 text-[10px] italic text-ink-faint">
            可用 node id：{flowNodes.join(', ')}
          </p>
        </Field>

        <Field label="Expected approvers (each line: nodeId=userId1,userId2)" hint={`approval nodes：${approvalNodes.join(', ') || '(無)'}`}>
          <Textarea
            rows={Math.max(2, (tc.expectedApprovers ?? []).length + 1)}
            className="font-mono text-[11px]"
            value={(tc.expectedApprovers ?? []).map(a => `${a.nodeId}=${a.userIds.join(',')}`).join('\n')}
            onChange={e => {
              const parsed = e.target.value
                .split('\n')
                .map(line => line.trim())
                .filter(Boolean)
                .map(line => {
                  const [nodeId, ids = ''] = line.split('=')
                  return { nodeId: nodeId.trim(), userIds: ids.split(',').map(s => s.trim()).filter(Boolean) }
                })
              onChange({ ...tc, expectedApprovers: parsed })
            }}
            placeholder="approval_manager=u_wang_manager"
          />
        </Field>

        <details className="text-xs">
          <summary className="cursor-pointer text-ink-muted">Advanced — http status / validation errors（負路徑用）</summary>
          <div className="mt-2 grid grid-cols-2 gap-2">
            <Field label="expectedHttpStatus" hint="若預期 4xx/5xx 而非走完路徑，填這裡（會清掉 expectedPath）">
              <Input
                type="number"
                value={tc.expectedHttpStatus ?? ''}
                onChange={e => onChange({ ...tc, expectedHttpStatus: e.target.value ? Number(e.target.value) : undefined })}
                placeholder="400"
              />
            </Field>
            <Field label="expectedValidationErrors (one per line)">
              <Textarea
                rows={2}
                className="text-[11px]"
                value={(tc.expectedValidationErrors ?? []).join('\n')}
                onChange={e => onChange({ ...tc, expectedValidationErrors: e.target.value.split('\n').filter(s => s.trim()) })}
                placeholder="quote_file is required when amount >= 10000"
              />
            </Field>
          </div>
        </details>
      </div>
    </div>
  )
}
