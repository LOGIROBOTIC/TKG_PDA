import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AuthProvider, useAuth } from '../auth/AuthContext'
import { session } from '../test/fixtures'
import { ApiProvider } from './ApiContext'

function LogoutProbe() {
  const { logout } = useAuth()
  return <button onClick={logout}>logout</button>
}

describe('Admin API session isolation', () => {
  it('clears cached administration data when the authenticated token changes', async () => {
    const queryClient = new QueryClient()
    queryClient.setQueryData(['users'], [{ username: 'first-admin' }])

    render(<QueryClientProvider client={queryClient}><AuthProvider initialSession={session}><ApiProvider><LogoutProbe /></ApiProvider></AuthProvider></QueryClientProvider>)
    expect(queryClient.getQueryData(['users'])).toBeDefined()
    await userEvent.click(screen.getByRole('button', { name: 'logout' }))
    await waitFor(() => expect(queryClient.getQueryData(['users'])).toBeUndefined())
  })
})
