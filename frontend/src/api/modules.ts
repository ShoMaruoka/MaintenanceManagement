import { fetchJson } from './client'
import type { DbName, Module, ModuleType } from '../types'

export interface DbListItem {
  name: DbName
  devDb: string
  stgDb: string
  prdDb: string
}

export interface ApiModuleInfo {
  name: string
  type: ModuleType
  modifyDate: string
  gitOnly: boolean
  isDeleteCandidate: boolean
}

export interface ApiModuleResponse {
  dbName: DbName
  storedProcedures: ApiModuleInfo[]
  functions: ApiModuleInfo[]
  views: ApiModuleInfo[]
  tables: ApiModuleInfo[]
  userDefinedTableTypes: ApiModuleInfo[]
  mariaDb: ApiModuleInfo[]
}

export async function getDbList(): Promise<DbListItem[]> {
  return fetchJson<DbListItem[]>('/modules')
}

export async function getModules(dbName: DbName): Promise<Record<ModuleType, Module[]>> {
  const response = await fetchJson<ApiModuleResponse>(`/modules/${dbName}`)

  return {
    StoredProcedure: formatModules(response.storedProcedures),
    Function: formatModules(response.functions),
    VIEW: formatModules(response.views),
    Table: formatModules(response.tables),
    UserDefinedTableType: formatModules(response.userDefinedTableTypes),
    MariaDB: formatModules(response.mariaDb),
  }
}

function formatModules(items: ApiModuleInfo[]): Module[] {
  return items.map(m => ({
    name: m.name,
    modifyDate: m.modifyDate,
    type: m.type,
    isDeleteCandidate: m.isDeleteCandidate,
  }))
}
