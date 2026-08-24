import { createContext, useCallback, useContext, useEffect, useMemo, useState, type PropsWithChildren } from 'react'
import { loginAdmin } from '../api/client'
import type { AdminSession } from '../api/types'

interface AuthValue {
  session: AdminSession | null
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthValue | null>(null)

const isUsableSession = (value: AdminSession): boolean =>
  value.clientType === 'ADMIN_WEB'
  && value.user.roles.includes('ADMIN')
  && new Date(value.expiresAt).getTime() > Date.now()

/**
 * Admin Web keeps the bearer JWT in application runtime memory only.
 *
 * The token is never written to localStorage, sessionStorage, IndexedDB, cookies,
 * the URL/query string, source or configuration files. A full browser reload discards
 * the in-memory state and returns the operator to the login screen — this is
 * intentional because the accepted Phase 05 auth schema has no refresh-token or
 * server-side session model. `initialSession` is a test-only seam for injecting an
 * authenticated principal; production never provides it.
 */
export function AuthProvider({ children, initialSession = null }: PropsWithChildren<{ initialSession?: AdminSession | null }>) {
  const [session, setSession] = useState<AdminSession | null>(() =>
    initialSession && isUsableSession(initialSession) ? initialSession : null)

  const logout = useCallback(() => {
    setSession(null)
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    const next = await loginAdmin(username, password)
    if (!isUsableSession(next)) {
      throw new Error('Sunucu geçerli bir ADMIN_WEB oturumu üretmedi.')
    }
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
