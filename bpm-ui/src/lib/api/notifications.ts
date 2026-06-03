import { apiFetch } from '@/lib/apiFetch'

/** One in-app notification row for the header bell. */
export interface NotificationDto {
  id: string
  title: string
  body: string
  link: string | null
  flowCode: string | null
  isRead: boolean
  createdAt: string
}

export interface NotificationsResponse {
  unreadCount: number
  items: NotificationDto[]
}

export async function fetchMyNotifications(limit = 20): Promise<NotificationsResponse> {
  const res = await apiFetch(`/api/notifications/mine?limit=${limit}`)
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return (await res.json()) as NotificationsResponse
}

export async function markNotificationRead(id: string): Promise<void> {
  const res = await apiFetch(`/api/notifications/${id}/read`, { method: 'POST' })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
}

export async function markAllNotificationsRead(): Promise<void> {
  const res = await apiFetch('/api/notifications/read-all', { method: 'POST' })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
}
