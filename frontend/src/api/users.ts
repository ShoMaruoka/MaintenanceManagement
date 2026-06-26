import { fetchJson } from './client'
import type { AppUser } from '../types'

export const getUsers = () => fetchJson<AppUser[]>('/users')

export const addUser = (userName: string, displayName: string, role: 'admin' | 'user' = 'user') =>
  fetchJson<AppUser>('/users', {
    method: 'POST',
    body: JSON.stringify({ userName, displayName, role }),
  })

export const deleteUser = (userName: string) =>
  fetch(`/api/users/${encodeURIComponent(userName)}`, { method: 'DELETE' })
