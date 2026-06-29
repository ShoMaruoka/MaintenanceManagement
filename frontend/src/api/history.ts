import { fetchJson } from './client'
import type { DeploySession, DeploySessionDetail } from '../types'

interface ApiDeploySession {
  sessionId: number
  dbName: string
  executedBy: string
  executedAt: string
  status: string
  errorMessage?: string
  details: DeploySessionDetail[]
}

export async function getSessions(limit: number = 50): Promise<DeploySession[]> {
  const sessions = await fetchJson<ApiDeploySession[]>(`/history/sessions?limit=${limit}`)
  return sessions.map(formatSession)
}

export async function getSession(sessionId: number): Promise<DeploySession> {
  const session = await fetchJson<ApiDeploySession>(`/history/sessions/${sessionId}`)
  return formatSession(session)
}

function formatExecutedAt(isoStr: string): string {
  try {
    const d = new Date(isoStr)
    if (isNaN(d.getTime())) return isoStr
    const mm = String(d.getMonth() + 1).padStart(2, '0')
    const dd = String(d.getDate()).padStart(2, '0')
    const hh = String(d.getHours()).padStart(2, '0')
    const mi = String(d.getMinutes()).padStart(2, '0')
    return `${mm}-${dd} ${hh}:${mi}`
  } catch {
    return isoStr
  }
}

function buildModuleSummary(details: DeploySessionDetail[]): string {
  if (details.length === 0) return ''

  const typeCounts: Record<string, { 新規: number; 更新: number; 削除: number }> = {}
  for (const d of details) {
    const typeKey = d.moduleType === 'StoredProcedure' ? 'SP'
      : d.moduleType === 'Function' ? 'Func'
      : d.moduleType === 'VIEW' ? 'View'
      : d.moduleType === 'Table' ? 'Table'
      : d.moduleType === 'MariaDB' ? 'MariaDB'
      : d.moduleType
    if (!typeCounts[typeKey]) typeCounts[typeKey] = { 新規: 0, 更新: 0, 削除: 0 }
    const op = d.opType as '新規' | '更新' | '削除'
    if (op in typeCounts[typeKey]) typeCounts[typeKey][op]++
  }

  return Object.entries(typeCounts)
    .map(([type, counts]) => {
      const total = counts['新規'] + counts['更新'] + counts['削除']
      const ops: string[] = []
      if (counts['新規']) ops.push(`新規${counts['新規']}`)
      if (counts['更新']) ops.push(`更新${counts['更新']}`)
      if (counts['削除']) ops.push(`削除${counts['削除']}`)
      return ops.length > 0 ? `${type}×${total}（${ops.join('・')}）` : `${type}×${total}`
    })
    .join('・')
}

function formatSession(session: ApiDeploySession): DeploySession {
  const moduleCount = session.details.length
  const modules = buildModuleSummary(session.details)

  return {
    sessionId: session.sessionId,
    dbName: session.dbName as any,
    executedBy: session.executedBy,
    executedAt: formatExecutedAt(session.executedAt),
    status: session.status as any,
    modules: modules || `${moduleCount} モジュール`,
    moduleCount,
    details: session.details,
  }
}
