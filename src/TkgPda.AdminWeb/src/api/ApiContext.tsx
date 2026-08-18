import { useQueryClient } from '@tanstack/react-query'
import { createContext, useContext, useEffect, useMemo, useRef, type PropsWithChildren } from 'react'
import { createAdminApi, type AdminApi } from './client'
import { useAuth } from '../auth/AuthContext'

const ApiContext = createContext<AdminApi | null>(null)

export function ApiProvider({ children }: PropsWithChildren) {
  const { session } = useAuth()
  const queryClient = useQueryClient()
  const previousToken = useRef(session?.accessToken ?? null)

  useEffect(() => {
    const currentToken = session?.accessToken ?? null
    if (previousToken.current !== currentToken) queryClient.clear()
    previousToken.current = currentToken
  }, [queryClient, session?.accessToken])

  const api = useMemo(() => session ? createAdminApi(session.accessToken) : null, [session])
  return <ApiContext.Provider value={api}>{children}</ApiContext.Provider>
}

export const useAdminApi = () => {
  const value = useContext(ApiContext)
  if (!value) throw new Error('Admin API requires an authenticated session')
  return value
}
