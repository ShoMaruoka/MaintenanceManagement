export type DbName = 'kaios' | 'gos' | 'paf' | 'duskin'

export type ModuleType = 'StoredProcedure' | 'Function' | 'VIEW' | 'Table' | 'UserDefinedTableType' | 'MariaDB'

export type OpType = '新規' | '更新' | '削除'

export type SessionStatus = 'running' | 'success' | 'failed'

export interface DbConfig {
  name: DbName
  devDb: string
}

export interface Module {
  name: string
  modifyDate: string
  type: ModuleType
}

export interface SelectedModule {
  name: string
  type: ModuleType
  opType: OpType
}

export interface DeploySession {
  sessionId: number
  dbName: DbName
  executedBy: string
  executedAt: string
  status: SessionStatus
  modules: string
  moduleCount: number
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
