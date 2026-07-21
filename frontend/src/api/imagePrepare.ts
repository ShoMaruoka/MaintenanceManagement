import type { DbName } from '../types'

const API_BASE = '/api'

export interface ApiImageTreeEntry {
  name: string
  relativePath: string
  isDirectory: boolean
  children: ApiImageTreeEntry[]
}

export interface ApiImageCategoryNode {
  name: string
  entries: ApiImageTreeEntry[]
}

export interface ApiImagePrepareTree {
  dbName: DbName
  categories: ApiImageCategoryNode[]
}

export interface ApiImageUploadSavedFile {
  relativePath: string
  overwritten: boolean
}

export interface ApiImageUploadResponse {
  dbName: DbName
  dryRun: boolean
  saved: ApiImageUploadSavedFile[]
}

export interface ApiImageCreateFolderResponse {
  dbName: DbName
  relativePath: string
  dryRun: boolean
  created: boolean
}

export class ImagePrepareConflictError extends Error {
  conflicts: string[]

  constructor(message: string, conflicts: string[]) {
    super(message)
    this.name = 'ImagePrepareConflictError'
    this.conflicts = conflicts
  }
}

async function readApiError(response: Response): Promise<string> {
  try {
    const body = await response.json() as { error?: string; conflicts?: string[] }
    if (body.error && body.conflicts?.length) {
      return `${body.error}: ${body.conflicts.join(', ')}`
    }
    if (body.error) return body.error
  } catch {
    // ignore
  }
  return `API Error: ${response.status} ${response.statusText}`
}

export async function getImageTree(dbName: DbName): Promise<ApiImagePrepareTree> {
  const response = await fetch(`${API_BASE}/image-prepare/${dbName}/tree`)
  if (!response.ok) throw new Error(await readApiError(response))
  return response.json() as Promise<ApiImagePrepareTree>
}

export async function createImageFolder(
  dbName: DbName,
  category: string,
  relativeSubPath: string,
): Promise<ApiImageCreateFolderResponse> {
  const response = await fetch(`${API_BASE}/image-prepare/${dbName}/folders`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ category, relativeSubPath }),
  })
  if (!response.ok) throw new Error(await readApiError(response))
  return response.json() as Promise<ApiImageCreateFolderResponse>
}

export async function uploadImages(
  dbName: DbName,
  category: string,
  relativeSubPath: string,
  files: File[],
  overwrite = false,
): Promise<ApiImageUploadResponse> {
  const form = new FormData()
  form.append('category', category)
  if (relativeSubPath.trim()) form.append('relativeSubPath', relativeSubPath.trim())
  form.append('overwrite', String(overwrite))
  for (const file of files) {
    form.append('files', file)
  }

  const response = await fetch(`${API_BASE}/image-prepare/${dbName}/upload`, {
    method: 'POST',
    body: form,
  })

  if (response.status === 409) {
    const body = await response.json() as { error?: string; conflicts?: string[] }
    throw new ImagePrepareConflictError(
      body.error ?? '同名のファイルが既に存在します',
      body.conflicts ?? [],
    )
  }

  if (!response.ok) throw new Error(await readApiError(response))
  return response.json() as Promise<ApiImageUploadResponse>
}

/** サブフォルダパスのクライアント側検証。問題なければ null。 */
export function validateSubPath(relativeSubPath: string): string | null {
  const trimmed = relativeSubPath.trim()
  if (!trimmed) return null

  const normalized = trimmed.replace(/\\/g, '/')
  if (normalized.startsWith('/') || normalized.includes(':')) {
    return '絶対パスは指定できません'
  }

  const parts = normalized.split('/').filter(Boolean)
  if (parts.length > 2) {
    return 'サブフォルダは最大 2 階層までです（例: flash/img）'
  }
  if (parts.some(p => p === '.' || p === '..')) {
    return '相対参照 (.. や .) は使用できません'
  }
  return null
}
