import { createContext, useContext, useState, ReactNode } from 'react'

const KEY_USER = 'currentUser'
const KEY_ROLE = 'currentRole'

export type UserRole = 'admin' | 'user'

interface UserContextValue {
  currentUser: string | null
  currentRole: UserRole | null
  selectUser: (userName: string, role: UserRole) => void
  clearUser: () => void
}

const UserContext = createContext<UserContextValue | null>(null)

export function UserProvider({ children }: { children: ReactNode }) {
  const [currentUser, setCurrentUser] = useState<string | null>(
    () => localStorage.getItem(KEY_USER)
  )
  const [currentRole, setCurrentRole] = useState<UserRole | null>(
    () => (localStorage.getItem(KEY_ROLE) as UserRole | null)
  )

  const selectUser = (userName: string, role: UserRole) => {
    localStorage.setItem(KEY_USER, userName)
    localStorage.setItem(KEY_ROLE, role)
    setCurrentUser(userName)
    setCurrentRole(role)
  }

  const clearUser = () => {
    localStorage.removeItem(KEY_USER)
    localStorage.removeItem(KEY_ROLE)
    setCurrentUser(null)
    setCurrentRole(null)
  }

  return (
    <UserContext.Provider value={{ currentUser, currentRole, selectUser, clearUser }}>
      {children}
    </UserContext.Provider>
  )
}

export function useUser(): UserContextValue {
  const ctx = useContext(UserContext)
  if (!ctx) throw new Error('useUser must be used within UserProvider')
  return ctx
}
