import { api } from '@/flowcook/api'

export interface DatasetColumnDef { key: string; label: string; type: string }
export interface DatasetDto { id: string; key: string; name: string; description: string | null; columns: DatasetColumnDef[]; isActive: boolean; rowCount: number }
export interface DatasetRowDto { id: string; datasetId: string; cells: Record<string, string>; isActive: boolean; sortOrder: number }

export interface CreateDatasetRequest { key: string; name: string; description: string | null; columns: DatasetColumnDef[] }
export interface UpdateDatasetRequest { name?: string; description?: string | null; columns?: DatasetColumnDef[]; isActive?: boolean }
export interface AddRowRequest { cells: Record<string, string> }
export interface UpdateRowRequest { cells?: Record<string, string>; isActive?: boolean; sortOrder?: number }

export const listDatasets = () => api<DatasetDto[]>('/api/datasets')
export const createDataset = (req: CreateDatasetRequest) => api<DatasetDto>('/api/datasets', { method: 'POST', json: req })
export const updateDataset = (id: string, req: UpdateDatasetRequest) => api<DatasetDto>(`/api/datasets/${id}`, { method: 'PUT', json: req })
export const deleteDataset = (id: string) => api<void>(`/api/datasets/${id}`, { method: 'DELETE' })

export const listRows = (id: string) => api<DatasetRowDto[]>(`/api/datasets/${id}/rows`)
export const addRow = (id: string, req: AddRowRequest) => api<DatasetRowDto>(`/api/datasets/${id}/rows`, { method: 'POST', json: req })
export const updateRow = (rowId: string, req: UpdateRowRequest) => api<DatasetRowDto>(`/api/datasets/rows/${rowId}`, { method: 'PUT', json: req })
export const deleteRow = (rowId: string) => api<void>(`/api/datasets/rows/${rowId}`, { method: 'DELETE' })
