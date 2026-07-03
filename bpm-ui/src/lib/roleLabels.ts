export function roleLabel(code: string | null | undefined): string {
  if (!code) return ''
  const map: Record<string, string> = {
    FINANCE: '財務', PROCUREMENT: '採購', HR_MANAGER: '人資', VP: 'VP', DIRECTOR: '主管', APPROVER: '主管',
  }
  return map[code] ?? code
}
