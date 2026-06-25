import { fetchStream } from './client'
import type { DbName, SelectedModule, LogLine } from '../types'

export interface ApiLogEntry {
  timestamp: string
  level: 'INFO' | 'STEP' | 'OK' | 'RUN' | 'WARN' | 'ERROR' | 'DETAIL'
  message: string
  step?: string
}

export interface ApiDeployModule {
  name: string
  type: string
  opType: string
}

export interface ApiDeployRequest {
  dbName: DbName
  modules: ApiDeployModule[]
}

export interface ApiDeployDone {
  type: 'done'
  sessionId: number
}

export type ApiDeployStreamEvent = (ApiLogEntry & { type?: never }) | ApiDeployDone

function isDeployDone(event: any): event is ApiDeployDone {
  return event.type === 'done'
}

export function startDeploy(
  dbName: DbName,
  modules: SelectedModule[],
  onLog: (line: LogLine, step?: string) => void,
  onDone: (sessionId: number) => void,
  onError?: (error: Error) => void,
  signal?: AbortSignal,
): Promise<void> {
  const request: ApiDeployRequest = {
    dbName,
    modules: modules.map(m => ({
      name: m.name,
      type: m.type,
      opType: m.opType,
    })),
  }

  return fetchStream<ApiDeployStreamEvent>(
    '/deploy/stream',
    (event) => {
      if (isDeployDone(event)) {
        onDone(event.sessionId)
      } else if ('timestamp' in event) {
        const logEntry: LogLine = {
          timestamp: event.timestamp,
          level: event.level,
          message: event.message,
        }
        onLog(logEntry, event.step)
      }
    },
    {
      method: 'POST',
      body: JSON.stringify(request),
      signal,
    },
    onError,
  )
}
