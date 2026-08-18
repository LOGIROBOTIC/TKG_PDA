import { createContext, useCallback, useContext, useEffect, useMemo, useState, type PropsWithChildren } from 'react'
import { loginAdmin } from '../api/client'
import type { AdminSession } from '../api/types'

const STORAGE_KEY = 'tkg.admin.session.v1'

interface AuthValue {
  session: AdminSession | null
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthValue | null>(null)

const readSession = (): AdminSession | null => {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const value = JSON.parse(raw) as AdminSession
    if (value.clientType !== 'ADMIN_WEB' || new Date(value.expiresAt).getTime() <= Date.now()) {
      sessionStorage.removeItem(STORAGE_KEY)
      return null
    }
    return value
  } catch {
    sessionStorage.removeItem(STORAGE_KEY)
    return null
  }
}

export function AuthProvider({ children }: PropsWithChildren) {
  const [session, setSession] = useState<AdminSession | null>(readSession)

  const logout = useCallback(() => {
    sessionStorage.removeItem(STORAGE_KEY)
    setSession(null)
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    const next = await loginAdmin(username, password)
    if (next.clientType !== 'ADMIN_WEB' || !next.user.roles.includes('ADMIN')) {
      throw new Error('Sunucu geçerli bir ADMIN_WEB oturumu üretmedi.')
    }
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next))
    setSession(next)
  }, [])

  useEffect(() => {
    window.addEventListener('tkg:unauthorized', logout)
    return () => window.removeEventListener('tkg:unauthorized', logout)
  }, [logout])

  useEffect(() => {
    if (!session) return
    const remaining = new Date(session.expiresAt).getTime() - Date.now()
    const timer = window.setTimeout(logout, Math.min(Math.max(0, remaining), 2_147_483_647))
    return () => window.clearTimeout(timer)
  }, [logout, session])

  const value = useMemo(() => ({ session, login, logout }), [session, login, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export const useAuth = () => {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used inside AuthProvider')
  return value
}

export const authStorageKey = STORAGE_KEY
