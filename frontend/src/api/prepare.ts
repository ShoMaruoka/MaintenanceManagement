import { fetchJson, fetchStream } from './client'
import type { DbName, LogLine } from '../types'

export interface ApiPrepareFileInfo {
  fileName: string
  source: 'deployed' | 'hold'
  dbType: 'sqlserver' | 'mariadb'
  /** STG 適用時の操作区分。実行履歴から逆引きできない場合は '不明' */
  opType: string
}

/** 自動デプロイしない Table / UserDefinedTableType の手動適用待ち項目 */
export interface ApiManualApplyItem {
  moduleType: 'Table' | 'UserDefinedTableType'
  moduleName: string
  /** 新規 | 更新 | 削除 | 不明 */
  opType: string
  /** STG 適用（Git マージ）実行日時。空の場合あり */
  stgAppliedAt: string
  stgAppliedBy: string
  /** deployed_manual 配下の SQL ファイル名。Git に定義が無い場合は空 */
  fileName: string
}

export interface ApiPrepareDbEntry {
  dbName: DbName
  files: ApiPrepareFileInfo[]
  /** Files 配下の相対パス（例: Images/flash/img/a.png）。無ければ空配列 */
  imageFiles: string[]
  manualItems: ApiManualApplyItem[]
}

export interface ApiPrepareSelection {
  dbName: DbName
  fileName: string
  source: 'deployed' | 'hold'
  dbType: 'sqlserver' | 'mariadb'
  apply: boolean
}

export interface ApiPrepareImageSelection {
  dbName: DbName
  relativePath: string
  apply: boolean
}

export interface ApiPrepareManualSelection {
  dbName: DbName
  moduleType: string
  moduleName: string
  /** true = 本番へ手動適用済みとして消化する。false = 次回まで持ち越す */
  apply: boolean
}

export interface ApiPrepareRequest {
  executedBy: string
  selections: ApiPrepareSelection[]
  imageSelections: ApiPrepareImageSelection[]
  manualSelections: ApiPrepareManualSelection[]
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
  manual: number
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
  imageSelections: ApiPrepareImageSelection[],
  manualSelections: ApiPrepareManualSelection[],
  executedBy: string,
  onLog: (line: LogLine) => void,
  onDone: (applied: number, held: number, manual: number) => void,
  onError?: (error: Error) => void,
): Promise<void> {
  const request: ApiPrepareRequest = { executedBy, selections, imageSelections, manualSelections }

  return fetchStream<ApiPrepareStreamEvent>(
    '/prepare/stream',
    (event) => {
      if (isPrepareDone(event)) {
        onDone(event.applied, event.held, event.manual)
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
