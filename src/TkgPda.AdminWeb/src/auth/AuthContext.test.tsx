import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { AuthProvider, authStorageKey, useAuth } from './AuthContext'
import { jsonResponse } from '../test/render'
import { session } from '../test/fixtures'

function Probe() {
  const { session: current, login, logout } = useAuth()
  const [error, setError] = useState('')
  return <div><span data-testid="user">{current?.user.username ?? 'anonymous'}</span><button onClick={() => void login('admin', 'long-test-password').catch((reason: Error) => setError(reason.message))}>login</button><button onClick={logout}>logout</button>{error && <span role="alert">{error}</span>}</div>
}

describe('Admin Web authentication', () => {
  it('stores a valid ADMIN_WEB session only in sessionStorage', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(session))
    vi.stubGlobal('fetch', fetchMock)
    render(<AuthProvider><Probe /></AuthProvider>)
    await userEvent.click(screen.getByRole('button', { name: 'login' }))
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('admin'))
    expect(JSON.parse(sessionStorage.getItem(authStorageKey) ?? '{}')).toMatchObject({ clientType: 'ADMIN_WEB' })
    expect(localStorage.getItem(authStorageKey)).toBeNull()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/admin/auth/login')
  })

  it('rejects a device-bound token at the Admin Web boundary', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ ...session, clientType: 'DEVICE_BOUND' })))
    render(<AuthProvider><Probe /></AuthProvider>)
    await userEvent.click(screen.getByRole('button', { name: 'login' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('ADMIN_WEB')
    expect(sessionStorage.getItem(authStorageKey)).toBeNull()
  })

  it('drops expired persisted tokens during bootstrap', () => {
    sessionStorage.setItem(authStorageKey, JSON.stringify({ ...session, expiresAt: '2000-01-01T00:00:00Z' }))
    render(<AuthProvider><Probe /></AuthProvider>)
    expect(screen.getByTestId('user')).toHaveTextContent('anonymous')
    expect(sessionStorage.getItem(authStorageKey)).toBeNull()
  })

  it('clears the session when an authenticated API request reports 401', async () => {
    sessionStorage.setItem(authStorageKey, JSON.stringify(session))
    render(<AuthProvider><Probe /></AuthProvider>)
    expect(screen.getByTestId('user')).toHaveTextContent('admin')
    window.dispatchEvent(new Event('tkg:unauthorized'))
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('anonymous'))
  })
})
