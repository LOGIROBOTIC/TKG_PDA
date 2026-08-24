import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { AuthProvider, useAuth } from './AuthContext'
import { jsonResponse } from '../test/render'
import { session } from '../test/fixtures'

function Probe() {
  const { session: current, login, logout } = useAuth()
  const [error, setError] = useState('')
  return <div><span data-testid="user">{current?.user.username ?? 'anonymous'}</span><button onClick={() => void login('admin', 'long-test-password').catch((reason: Error) => setError(reason.message))}>login</button><button onClick={logout}>logout</button>{error && <span role="alert">{error}</span>}</div>
}

const serializedStores = () => {
  const dump = (store: Storage) => Object.keys(store).map((key) => `${key}=${store.getItem(key)}`).join('\n')
  return `${dump(localStorage)}\n${dump(sessionStorage)}`
}

describe('Admin Web authentication', () => {
  it('holds the ADMIN_WEB access token only in runtime memory, never in web storage', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(session))
    vi.stubGlobal('fetch', fetchMock)
    render(<AuthProvider><Probe /></AuthProvider>)
    await userEvent.click(screen.getByRole('button', { name: 'login' }))
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('admin'))

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/admin/auth/login')
    // The token is present in memory (proven by the authenticated UI) but absent from any web store.
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(serializedStores()).not.toContain(session.accessToken)
  })

  it('does not restore an authenticated session from web storage on bootstrap', () => {
    // Simulate a hostile/leftover storage entry: memory-only auth must ignore it entirely.
    sessionStorage.setItem('tkg.admin.session.v1', JSON.stringify(session))
    localStorage.setItem('tkg.admin.session.v1', JSON.stringify(session))
    render(<AuthProvider><Probe /></AuthProvider>)
    expect(screen.getByTestId('user')).toHaveTextContent('anonymous')
  })

  it('rejects a device-bound token at the Admin Web boundary and stores nothing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ ...session, clientType: 'DEVICE_BOUND' })))
    render(<AuthProvider><Probe /></AuthProvider>)
    await userEvent.click(screen.getByRole('button', { name: 'login' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('ADMIN_WEB')
    expect(screen.getByTestId('user')).toHaveTextContent('anonymous')
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })

  it('drops an injected already-expired session during bootstrap', () => {
    render(<AuthProvider initialSession={{ ...session, expiresAt: '2000-01-01T00:00:00Z' }}><Probe /></AuthProvider>)
    expect(screen.getByTestId('user')).toHaveTextContent('anonymous')
  })

  it('clears an in-memory session when its access-token expiry is reached', () => {
    vi.useFakeTimers()
    try {
      render(<AuthProvider initialSession={{ ...session, expiresAt: new Date(Date.now() + 100).toISOString() }}><Probe /></AuthProvider>)
      expect(screen.getByTestId('user')).toHaveTextContent('admin')
      act(() => vi.advanceTimersByTime(101))
      expect(screen.getByTestId('user')).toHaveTextContent('anonymous')
    } finally {
      vi.useRealTimers()
    }
  })

  it('rejects an already-expired login response and stores nothing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ ...session, expiresAt: '2000-01-01T00:00:00Z' })))
    render(<AuthProvider><Probe /></AuthProvider>)
    await userEvent.click(screen.getByRole('button', { name: 'login' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('ADMIN_WEB')
    expect(screen.getByTestId('user')).toHaveTextContent('anonymous')
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
  })

  it('clears the authenticated principal when logout is invoked', async () => {
    render(<AuthProvider initialSession={session}><Probe /></AuthProvider>)
    expect(screen.getByTestId('user')).toHaveTextContent('admin')
    await userEvent.click(screen.getByRole('button', { name: 'logout' }))
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('anonymous'))
  })

  it('clears the authenticated principal when an authenticated API request reports 401', async () => {
    render(<AuthProvider initialSession={session}><Probe /></AuthProvider>)
    expect(screen.getByTestId('user')).toHaveTextContent('admin')
    window.dispatchEvent(new Event('tkg:unauthorized'))
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('anonymous'))
  })
})
