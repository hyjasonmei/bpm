import { Plus, Trash2, AlertCircle, RotateCcw, Users } from 'lucide-react'
import { Field, Input, Select, Textarea } from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { type DraftSpec, emptySampleOrg } from '@/lib/onboarding'
import type { TestCaseSnapshot, UserSnapshot, DepartmentSnapshot, SampleOrgSnapshot } from '@/types/flowLibrary'

/**
 * Step TEST — captures (a) the bundle's `test-cases/*.json` payload and
 * (b) the bundle's `sample-org.json` payload, both in their persisted
 * `*Snapshot` shapes so the GO_LIVE build is a passthrough.
 *
 * PR-I7 §8.5 (test cases) and §8.6 (sample-org editor) consolidate these
 * into one step because they're tightly coupled — sample-org users are
 * the only valid principals the repro runner can resolve when replaying
 * the test cases against the persisted bundle.
 *
 * Bilingual labels (zh + en) per project convention.
 */
export function StepTest({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  return (
    <div className="flex flex-col gap-6">
      <SampleOrgEditor draft={draft} setDraft={setDraft} />
      <TestCasesEditor draft={draft} setDraft={setDraft} />
    </div>
  )
}

/* ──────────────────────────── Sample Org ──────────────────────────── */

function SampleOrgEditor({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const org = draft.sampleOrg
  const setOrg = (next: SampleOrgSnapshot) => setDraft({ ...draft, sampleOrg: next })

  const reset = () => {
    if (!confirm('重設成預設樣本組織？(4 名 user + 1 部門)')) return
    setOrg(emptySampleOrg())
  }

  const addUser = () => {
    const id = crypto.randomUUID()
    setOrg({
      ...org,
      users: [
        ...org.users,
        { id, email: `user${org.users.length + 1}@acme.tld`, fullName: `User ${org.users.length + 1}`, managerId: null, departmentId: org.departments[0]?.id ?? null },
      ],
    })
  }
  const updateUser = (id: string, patch: Partial<UserSnapshot>) => {
    setOrg({ ...org, users: org.users.map(u => u.id === id ? { ...u, ...patch } : u) })
  }
  const removeUser = (id: string) => {
    // Strip cascading manager refs so we never leave dangling FKs.
    setOrg({
      ...org,
      users: org.users.filter(u => u.id !== id).map(u => u.managerId === id ? { ...u, managerId: null } : u),
      departments: org.departments.map(d => d.headUserId === id ? { ...d, headUserId: null } : d),
    })
  }

  const addDept = () => {
    const id = crypto.randomUUID()
    const code = `DEPT${org.departments.length + 1}`
    setOrg({
      ...org,
      departments: [...org.departments, { id, code, name: code, parentId: null, headUserId: null }],
    })
  }
  const updateDept = (id: string, patch: Partial<DepartmentSnapshot>) => {
    setOrg({ ...org, departments: org.departments.map(d => d.id === id ? { ...d, ...patch } : d) })
  }
  const removeDept = (id: string) => {
    setOrg({
      ...org,
      departments: org.departments.filter(d => d.id !== id),
      users: org.users.map(u => u.departmentId === id ? { ...u, departmentId: null } : u),
    })
  }

  return (
    <section className="rounded-md border border-rule bg-white">
      <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-3 py-2">
        <div className="flex items-center gap-2">
          <Users className="h-4 w-4 text-ink-muted" />
          <h3 className="text-sm font-semibold text-ink">Sample Org · 樣本組織</h3>
          <span className="text-[11px] text-ink-faint">{org.users.length} users · {org.departments.length} dept</span>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="xs" onClick={reset}><RotateCcw className="h-3 w-3" /> Reset to default</Button>
        </div>
      </div>

      <div className="p-3 space-y-4">
        <p className="text-[11px] text-ink-muted">
          樣本組織會打包進 bundle，並在新環境用做 reproducibility check 的測試 user 池。Approver 路徑（如 <code>submitter.manager</code>）會在這份組織圖上解析。
        </p>

        {/* Users table */}
        <div>
          <div className="mb-1 flex items-center justify-between">
            <h4 className="text-xs font-semibold text-ink-muted uppercase tracking-wider">Users</h4>
            <Button variant="outline" size="xs" onClick={addUser}><Plus className="h-3 w-3" /> Add user</Button>
          </div>
          <div className="overflow-hidden rounded border border-rule">
            <table className="w-full text-xs">
              <thead className="bg-slate-50">
                <tr className="text-left text-[10px] uppercase tracking-wider text-ink-faint">
                  <th className="px-2 py-1.5">Email</th>
                  <th className="px-2 py-1.5">Full Name · 姓名</th>
                  <th className="px-2 py-1.5">Manager</th>
                  <th className="px-2 py-1.5">Department · 部門</th>
                  <th className="px-2 py-1.5 w-8"></th>
                </tr>
              </thead>
              <tbody>
                {org.users.length === 0 && (
                  <tr><td colSpan={5} className="px-3 py-3 text-center text-ink-faint">尚無 user，請至少新增 1 名（GO LIVE 必須）</td></tr>
                )}
                {org.users.map(u => (
                  <tr key={u.id} className="border-t border-rule">
                    <td className="px-2 py-1">
                      <Input className="h-7 text-[11px]" value={u.email} onChange={e => updateUser(u.id, { email: e.target.value })} />
                    </td>
                    <td className="px-2 py-1">
                      <Input className="h-7 text-[11px]" value={u.fullName} onChange={e => updateUser(u.id, { fullName: e.target.value })} />
                    </td>
                    <td className="px-2 py-1">
                      <Select className="h-7 text-[11px]" value={u.managerId ?? ''} onChange={e => updateUser(u.id, { managerId: e.target.value || null })}>
                        <option value="">— (no manager)</option>
                        {org.users.filter(o => o.id !== u.id).map(o => (
                          <option key={o.id} value={o.id}>{o.fullName} ({o.email})</option>
                        ))}
                      </Select>
                    </td>
                    <td className="px-2 py-1">
                      <Select className="h-7 text-[11px]" value={u.departmentId ?? ''} onChange={e => updateUser(u.id, { departmentId: e.target.value || null })}>
                        <option value="">—</option>
                        {org.departments.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
                      </Select>
                    </td>
                    <td className="px-2 py-1 text-right">
                      <button onClick={() => removeUser(u.id)} className="rounded p-1 text-ink-faint hover:bg-rose-50 hover:text-danger" title="Remove user"><Trash2 className="h-3.5 w-3.5" /></button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Departments table */}
        <div>
          <div className="mb-1 flex items-center justify-between">
            <h4 className="text-xs font-semibold text-ink-muted uppercase tracking-wider">Departments · 部門</h4>
            <Button variant="outline" size="xs" onClick={addDept}><Plus className="h-3 w-3" /> Add department</Button>
          </div>
          <div className="overflow-hidden rounded border border-rule">
            <table className="w-full text-xs">
              <thead className="bg-slate-50">
                <tr className="text-left text-[10px] uppercase tracking-wider text-ink-faint">
                  <th className="px-2 py-1.5">Code</th>
                  <th className="px-2 py-1.5">Name · 名稱</th>
                  <th className="px-2 py-1.5">Head · 部門長</th>
                  <th className="px-2 py-1.5 w-8"></th>
                </tr>
              </thead>
              <tbody>
                {org.departments.length === 0 && (
                  <tr><td colSpan={4} className="px-3 py-3 text-center text-ink-faint">尚無 department</td></tr>
                )}
                {org.departments.map(d => (
                  <tr key={d.id} className="border-t border-rule">
                    <td className="px-2 py-1">
                      <Input className="h-7 text-[11px] font-mono" value={d.code} onChange={e => updateDept(d.id, { code: e.target.value })} />
                    </td>
                    <td className="px-2 py-1">
                      <Input className="h-7 text-[11px]" value={d.name} onChange={e => updateDept(d.id, { name: e.target.value })} />
                    </td>
                    <td className="px-2 py-1">
                      <Select className="h-7 text-[11px]" value={d.headUserId ?? ''} onChange={e => updateDept(d.id, { headUserId: e.target.value || null })}>
                        <option value="">—</option>
                        {org.users.map(u => <option key={u.id} value={u.id}>{u.fullName}</option>)}
                      </Select>
                    </td>
                    <td className="px-2 py-1 text-right">
                      <button onClick={() => removeDept(d.id)} className="rounded p-1 text-ink-faint hover:bg-rose-50 hover:text-danger" title="Remove dept"><Trash2 className="h-3.5 w-3.5" /></button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </section>
  )
}

/* ──────────────────────────── Test Cases ──────────────────────────── */

function TestCasesEditor({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const upsert = (tc: TestCaseSnapshot) => {
    const idx = draft.testCases.findIndex(x => x.id === tc.id)
    const next = idx >= 0
      ? draft.testCases.map(x => x.id === tc.id ? tc : x)
      : [...draft.testCases, tc]
    setDraft({ ...draft, testCases: next })
  }
  const remove = (id: string) => setDraft({ ...draft, testCases: draft.testCases.filter(x => x.id !== id) })

  const addCase = () => {
    const n = draft.testCases.length + 1
    upsert({
      id: `tc_${n}`,
      name: `Test case ${n}`,
      inputs: {},
      expectedTrace: autoWalkTrace(draft),
      expectedFinalStatus: 'Completed',
    })
  }

  const allUserTaskFields = draft.userTasks.flatMap(t => t.fields.map(f => ({ taskId: t.id, ...f })))
  const flowNodeIds = draft.flow.nodes.map(n => n.id)

  return (
    <section className="rounded-md border border-rule bg-white">
      <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-3 py-2">
        <div>
          <h3 className="text-sm font-semibold text-ink">Test Cases · 測試案</h3>
          <p className="text-[11px] text-ink-muted">每個 case 給 input → 走出 expectedTrace。至少 1 個 (validators.test 阻擋下一步)</p>
        </div>
        <Button variant="primary" size="xs" onClick={addCase}><Plus className="h-3 w-3" /> Add Test Case</Button>
      </div>

      <div className="p-3 space-y-3">
        {draft.testCases.length === 0 && (
          <div className="rounded border border-dashed border-rule p-8 text-center text-sm text-ink-faint">
            還沒有 test case。最少 1 個。
          </div>
        )}

        {[...draft.testCases].sort((a, b) => a.id.localeCompare(b.id)).map(tc => (
          <TestCaseCard
            key={tc.id}
            tc={tc}
            fields={allUserTaskFields}
            flowNodeIds={flowNodeIds}
            onChange={upsert}
            onRemove={() => remove(tc.id)}
          />
        ))}
      </div>
    </section>
  )
}

function TestCaseCard({
  tc, fields, flowNodeIds, onChange, onRemove,
}: {
  tc: TestCaseSnapshot
  fields: { taskId: string; id: string; label: { 'zh-TW': string }; type: string }[]
  flowNodeIds: string[]
  onChange: (tc: TestCaseSnapshot) => void
  onRemove: () => void
}) {
  const inputsJson = JSON.stringify(tc.inputs ?? {}, null, 2)
  const traceStr = (tc.expectedTrace ?? []).join(' → ')

  const onInputsChange = (text: string) => {
    try {
      const parsed = JSON.parse(text)
      onChange({ ...tc, inputs: parsed })
    } catch { /* keep typing */ }
  }
  const inputsValid = (() => { try { JSON.parse(inputsJson); return true } catch { return false } })()

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
        <Field label="Inputs (JSON) · 表單資料" hint={`對應欄位：${fields.map(f => f.id).join(', ') || '(尚無 user task field)'}`}>
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

        <Field label="Expected trace · 預期經過節點 (逗號分隔)" hint="從 startEvent 到 endEvent 的 node id 序列">
          <Input
            value={(tc.expectedTrace ?? []).join(',')}
            onChange={e => onChange({ ...tc, expectedTrace: e.target.value.split(',').map(s => s.trim()).filter(Boolean) })}
            placeholder="start_1, task_apply, ..."
          />
          {traceStr && <p className="mt-1 text-[11px] font-mono text-ink-faint">{traceStr}</p>}
          <p className="mt-1 text-[10px] italic text-ink-faint">可用 node id：{flowNodeIds.join(', ') || '(無)'}</p>
        </Field>

        <Field label="Expected final status · 預期最終狀態">
          <Select value={tc.expectedFinalStatus ?? 'Completed'} onChange={e => onChange({ ...tc, expectedFinalStatus: e.target.value })}>
            <option value="Completed">Completed (走完流程)</option>
            <option value="Cancelled">Cancelled (中途取消)</option>
            <option value="Http400">Http400 (input validation 失敗)</option>
            <option value="Http422">Http422 (business rule 失敗)</option>
          </Select>
        </Field>
      </div>
    </div>
  )
}

/**
 * Walk the spec's flow graph from the first startEvent, following edges
 * naively (first non-default branch wins; no actual gateway evaluation
 * because we don't have inputs yet at "Add" time). Returns a best-effort
 * default that the user can hand-edit.
 */
function autoWalkTrace(draft: DraftSpec): string[] {
  const start = draft.flow.nodes.find(n => n.type === 'startEvent')
  if (!start) return []
  const trace: string[] = []
  const seen = new Set<string>()
  let cursor: string | undefined = start.id
  while (cursor && !seen.has(cursor)) {
    trace.push(cursor)
    seen.add(cursor)
    const node = draft.flow.nodes.find(n => n.id === cursor)
    if (!node || node.type === 'endEvent') break
    const out = draft.flow.edges.filter(e => e.source === cursor)
    if (out.length === 0) break
    // Prefer the non-default branch when there's a choice — surfaces the
    // "interesting" path first; user can switch later.
    const next = out.find(e => !e.isDefault) ?? out[0]
    cursor = next.target
  }
  return trace
}
