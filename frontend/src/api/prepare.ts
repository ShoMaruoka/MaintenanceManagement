import { fetchJson, fetchStream } from './client'
import type { DbName, LogLine } from '../types'

export interface ApiPrepareFileInfo {
  fileName: string
  source: 'deployed' | 'hold'
  dbType: 'sqlserver' | 'mariadb'
}

export interface ApiPrepareDbEntry {
  dbName: DbName
  files: ApiPrepareFileInfo[]
}

export interface ApiPrepareSelection {
  dbName: DbName
  fileName: string
  source: 'deployed' | 'hold'
  dbType: 'sqlserver' | 'mariadb'
  apply: boolean
}

export interface ApiPrepareRequest {
  selections: ApiPrepareSelection[]
}

export interface ApiPrepareLogEntry {
  timestamp: string
  level: string
  message: string
}

export interface ApiPrepareDone {
  type: 'done'
  applied: number
  held: number
}

export type ApiPrepareStreamEvent = (ApiPrepareLogEntry & { type?: never }) | ApiPrepareDone

function isPrepareDone(event: any): event is ApiPrepareDone {
  return event.type === 'done'
}

export async function getPrepareFiles(): Promise<ApiPrepareDbEntry[]> {
  return fetchJson<ApiPrepareDbEntry[]>('/prepare/files')
}

export function startPrepare(
  selections: ApiPrepareSelection[],
  onLog: (line: LogLine) => void,
  onDone: (applied: number, held: number) => void,
  onError?: (error: Error) => void,
): Promise<void> {
  const request: ApiPrepareRequest = { selections }

  return fetchStream<ApiPrepareStreamEvent>(
    '/prepare/stream',
    (event) => {
      if (isPrepareDone(event)) {
        onDone(event.applied, event.held)
      } else if ('timestamp' in event) {
        const logEntry: LogLine = {
          timestamp: event.timestamp,
          level: event.level as any,
          message: event.message,
        }
        onLog(logEntry)
      }
    },
    {
      method: 'POST',
      body: JSON.stringify(request),
    },
    onError,
  )
}
