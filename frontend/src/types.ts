export interface AppUser {
  userName: string
  displayName: string
  role: 'admin' | 'user'
}

export type DbName = 'kaios' | 'gos' | 'paf' | 'duskin'

export type ModuleType = 'StoredProcedure' | 'Function' | 'VIEW' | 'Table' | 'UserDefinedTableType' | 'Stored' | 'MariaDbFunction' | 'MariaDbTable'

export type OpType = '新規' | '更新' | '削除'

export type SessionStatus = 'running' | 'success' | 'failed'

export interface DbConfig {
  name: DbName
  devDb: string
  stgDb: string
  prdDb: string
}

export interface Module {
  name: string
  modifyDate: string
  type: ModuleType
  isDeleteCandidate: boolean
  isNewCandidate: boolean
}

export interface SelectedModule {
  name: string
  type: ModuleType
  opType: OpType
}

export interface DeploySessionDetail {
  detailId: number
  sessionId: number
  opType: OpType
  moduleType: ModuleType
  moduleName: string
  result: string
}

export interface DeploySession {
  sessionId: number
  dbName: DbName
  executedBy: string
  executedAt: string
  status: SessionStatus
  modules: string
  moduleCount: number
  details?: DeploySessionDetail[]
  detailsFetched?: boolean
  logDetail?: string
}

export interface LogLine {
  timestamp: string
  level: 'INFO' | 'STEP' | 'OK' | 'RUN' | 'WARN' | 'ERROR' | 'DETAIL'
  message: string
}

export type DeployStep = 'generate' | 'git-update' | 'merge' | 'sql-convert' | 'deploy' | 'record'

export interface StepState {
  key: DeployStep
  label: string
  status: 'pending' | 'running' | 'done' | 'error'
}

export interface ProductionFile {
  dbName: DbName
  sqlServerFiles: string[]
  mariaDbFiles: string[]
}

export type MultiDbModules = { db: DbName; modules: SelectedModule[] }[]

export interface ProductionReadyLog {
  logId: number
  executedBy: string
  executedAt: string
  appliedFiles: number
  heldFiles: number
  manualFiles: number
  result: string
}

export interface PilotRunTarget {
  targetName: string
  result: string
  mode: string
}

/** 実行履歴一覧の Pilot 適用（GET /api/history/pilot-runs）。logDetail は含まない。 */
export interface PilotRunSummary {
  runId: string
  dbName: DbName
  executedBy: string
  executedAt: string
  stepLabel: string
  result: 'success' | 'failed'
  summary: string
}

/** 実行履歴詳細の Pilot 適用（GET /api/history/pilot-runs/{runId}）。 */
export interface PilotRunDetail extends PilotRunSummary {
  targets: PilotRunTarget[]
  logDetail?: string
  detailsFetched?: boolean
}

/** Pilot 最終適用の要約（GET /api/history/stats）。 */
export interface PilotDeploySummary {
  dbName: string
  executedAt: string
  executedBy: string
}

/** ダッシュボード上部のサマリーカード用の集計値（GET /api/history/stats）。 */
export interface DashboardStats {
  lastPrepare: ProductionReadyLog | null
  lastPilotKaios: PilotDeploySummary | null
  lastPilotGos: PilotDeploySummary | null
  days: number
  totalSessions: number
  successSessions: number
  runningCount: number
  runningDbName: string | null
  runningExecutedBy: string | null
}
