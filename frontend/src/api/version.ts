import { fetchJson } from './client'

export interface VersionInfo {
  version: string
  informationalVersion: string
}

export function getApiVersion(): Promise<VersionInfo> {
  return fetchJson<VersionInfo>('/version')
}
