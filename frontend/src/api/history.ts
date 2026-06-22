import { fetchJson } from './client'
import type { DeploySession } from '../types'

export interface ApiDeploySessionDetail {
  detailId: number
  sessionId: number
  opType: string
  moduleType: string
  moduleName: string
  result: string
}

export interface ApiDeploySession {
  sessionId: number
  dbName: string
  executedBy: string
  executedAt: string
  status: string
  errorMessage?: string
  details: ApiDeploySessionDetail[]
}

export async function getSessions(limit: number = 50): Promise<DeploySession[]> {
  const sessions = await fetchJson<ApiDeploySession[]>(`/history/sessions?limit=${limit}`)
  return sessions.map(formatSession)
}

export async function getSession(sessionId: number): Promise<DeploySession> {
  const session = await fetchJson<ApiDeploySession>(`/history/sessions/${sessionId}`)
  return formatSession(session)
}

function formatSession(session: ApiDeploySession): DeploySession {
  const moduleCount = session.details.length
  const modules = session.details
    .map(d => `${d.moduleType}/${d.moduleName}`)
    .join(', ')

  return {
    sessionId: session.sessionId,
    dbName: session.dbName as any,
    executedBy: session.executedBy,
    executedAt: session.executedAt,
    status: session.status as any,
    modules: modules || 'No modules',
    moduleCount,
  }
}
