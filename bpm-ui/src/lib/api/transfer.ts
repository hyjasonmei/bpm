import { apiFetch } from '@/lib/apiFetch'

export interface TransferCandidate {
  userId: string
  name: string
  email: string | null
}

export interface TransferResult {
  ok: boolean
  error: string | null
}

export async function searchTransferCandidates(q: string): Promise<TransferCandidate[]> {
  const res = await apiFetch(`/api/case-transfer/candidates?q=${encodeURIComponent(q)}`)
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return (await res.json()) as TransferCandidate[]
}

export async function transferCase(
  flowCode: string,
  caseId: string,
  toUserId: string,
  reason: string,
): Promise<TransferResult> {
  const res = await apiFetch(`/api/case-transfer/${encodeURIComponent(flowCode)}/${caseId}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ toUserId, reason }),
  })
  // 4xx carries { ok:false, error } — surface the code, not a thrown error,
  // so the modal can map it to a friendly message.
  try {
    return (await res.json()) as TransferResult
  } catch {
    return { ok: false, error: `http_${res.status}` }
  }
}

/** error code → 使用者訊息（未列出的顯 generic） */
export function transferErrorMessage(error: string | null): string {
  switch (error) {
    case 'role_stage_not_transferable': return '此關卡為角色佇列，整組皆可簽核，不需轉簽。'
    case 'not_current_assignee': return '您不是目前簽核人，無法轉簽。'
    case 'target_not_active': return '轉簽對象已停用或不存在。'
    case 'target_is_current': return '對象已是目前簽核人。'
    case 'reason_required': return '請填寫轉簽理由。'
    case 'not_found_or_closed': return '案件不存在或已結案。'
    default: return '轉簽失敗，請稍後再試。'
  }
}
