import { fetchJson, fetchStream } from './client'
import type { LogLine } from '../types'

export type WebSourceDbName = 'kaios' | 'gos'

export interface ApiWebSourcePilotTargetInfo {
  name: string
  destWebSourcePath: string
}

export interface ApiWebSourceInfo {
  dbName: string
  webSourcePath: string
  pilotTargets: ApiWebSourcePilotTargetInfo[]
}

export type WebSourceDeployStep = 'both' | 'web' | 'sql'

export interface ApiWebSourceDeployRequest {
  executedBy: string
  step: WebSourceDeployStep
}

export interface ApiWebSourceTargetResult {
  targetName: string
  success: boolean
  errorMessage?: string | null
}

export interface ApiWebSourceSqlDeployResult {
  success: boolean
  exitCode?: number | null
  errorMessage?: string | null
}

export interface ApiWebSourceDeployDone {
  type: 'done'
  runId: string
  success: boolean
  targets: ApiWebSourceTargetResult[]
  sqlDeploy?: ApiWebSourceSqlDeployResult | null
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
  executedBy: string,
  step: WebSourceDeployStep,
  onLog: (line: LogLine) => void,
  onDone: (result: ApiWebSourceDeployDone) => void,
  onError?: (error: Error) => void,
): Promise<void> {
  const request: ApiWebSourceDeployRequest = { executedBy, step }

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
