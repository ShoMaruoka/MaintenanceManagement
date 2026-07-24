import { fetchJson, fetchStream } from './client'
import type { LogLine } from '../types'

export type WebSourceDbName = 'kaios' | 'gos'

export type WebSourceDeployMode = 'mirror' | 'full'

export interface ApiWebSourcePilotTargetInfo {
  name: string
  destWebSourcePath: string
}

export interface ApiWebSourceInfo {
  dbName: string
  webSourcePath: string
  pilotTargets: ApiWebSourcePilotTargetInfo[]
}

export interface ApiWebSourceDeployRequest {
  mode: WebSourceDeployMode
  executedBy: string
}

export interface ApiWebSourceTargetResult {
  targetName: string
  success: boolean
  errorMessage?: string | null
}

export interface ApiWebSourceDeployDone {
  type: 'done'
  runId: string
  success: boolean
  targets: ApiWebSourceTargetResult[]
}

export type ApiWebSourceStreamEvent = (LogLine & { type?: never }) | ApiWebSourceDeployDone

function isWebSourceDeployDone(event: any): event is ApiWebSourceDeployDone {
  return event.type === 'done'
}

export async function getWebSourceInfo(dbName: WebSourceDbName): Promise<ApiWebSourceInfo> {
  return fetchJson<ApiWebSourceInfo>(`/web-source-prepare/${dbName}/info`)
}

export function startWebSourceDeploy(
  dbName: WebSourceDbName,
  mode: WebSourceDeployMode,
  executedBy: string,
  onLog: (line: LogLine) => void,
  onDone: (result: ApiWebSourceDeployDone) => void,
  onError?: (error: Error) => void,
): Promise<void> {
  const request: ApiWebSourceDeployRequest = { mode, executedBy }

  return fetchStream<ApiWebSourceStreamEvent>(
    `/web-source-prepare/${dbName}/stream`,
    (event) => {
      if (isWebSourceDeployDone(event)) {
        onDone(event)
      } else if ('timestamp' in event) {
        onLog({
          timestamp: event.timestamp,
          level: event.level as any,
          message: event.message,
        })
      }
    },
    {
      method: 'POST',
      body: JSON.stringify(request),
    },
    onError,
  )
}
