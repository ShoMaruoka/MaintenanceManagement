import { fetchJson, fetchStream } from './client'
import type { LogLine } from '../types'

export type WebSourceDbName = 'kaios' | 'gos' | 'paf' | 'duskin'

export interface ApiWebSourcePilotTargetInfo {
  name: string
  destWebSourcePath: string
  destImagePath: string
}

export interface ApiWebSourceInfo {
  dbName: string
  webSourcePath: string
  commonImagePath: string
  deployedPath: string
  mariaDbDeployedPath: string
  filesPath: string
  /** Pilot SQL Server 適用ルート。未設定時は空 */
  pilotSqlDeployPath: string
  /** Pilot MariaDB 適用ルート。未設定時は空（Issue #35 B1） */
  pilotMariaDbSqlDeployPath: string
  pilotTargets: ApiWebSourcePilotTargetInfo[]
  /** Webソースコピーで Web.config をコピーしないか（DB 設定。未設定時 true） */
  excludeWebConfig: boolean
  /** 除外設定の最終更新者（未設定時 null） */
  excludeWebConfigUpdatedBy?: string | null
  /** 除外設定の最終更新日時（UTC ISO 8601。未設定時 null） */
  excludeWebConfigUpdatedAt?: string | null
}

export interface ApiWebSourceSettings {
  excludeWebConfig: boolean
  updatedBy?: string | null
  updatedAt?: string | null
}

/** Web.config 除外設定を保存する（admin のみ。それ以外は 403）。 */
export async function updateWebSourceSettings(
  dbName: WebSourceDbName,
  excludeWebConfig: boolean,
  updatedBy: string,
): Promise<ApiWebSourceSettings> {
  return fetchJson<ApiWebSourceSettings>(`/web-source-prepare/${dbName}/settings`, {
    method: 'PUT',
    body: JSON.stringify({ excludeWebConfig, updatedBy }),
  })
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
  skipped?: boolean
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
