import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import type { PropsWithChildren, ReactElement } from 'react'
import { ApiProvider } from '../api/ApiContext'
import { AuthProvider } from '../auth/AuthContext'
import { ToastProvider } from '../components/Toast'
import { session } from './fixtures'

export const jsonResponse = (body: unknown, init?: ResponseInit) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json', ...init?.headers }, ...init })

export const renderAuthenticated = (element: ReactElement, route = '/') => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  function Providers({ children }: PropsWithChildren) {
    return <MemoryRouter initialEntries={[route]}><QueryClientProvider client={client}><AuthProvider initialSession={session}><ApiProvider><ToastProvider>{children}</ToastProvider></ApiProvider></AuthProvider></QueryClientProvider></MemoryRouter>
  }
  return render(element, { wrapper: Providers })
}
