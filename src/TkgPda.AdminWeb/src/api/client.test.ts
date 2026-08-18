import { ApiProblem, createAdminApi } from './client'
import { layouts, slots } from '../test/fixtures'
import { jsonResponse } from '../test/render'

describe('Admin API client', () => {
  it('sends bearer, entity ETag and parent layout ETag for slot updates', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(slots[0], { headers: { ETag: '"103"' } }))
    vi.stubGlobal('fetch', fetchMock)
    const draft = { ...slots[0] }
    const { id: _id, layoutId: _layoutId, concurrencyToken: _token, code: _code, ...payload } = draft
    await createAdminApi('jwt-value').updateSlot(layouts[0], slots[0], payload)
    const request = fetchMock.mock.calls[0]
    const options = request[1] as RequestInit
    const headers = options.headers as Headers
    expect(request[0]).toBe('/api/v1/admin/layouts/l-1/slots/s-1')
    expect(headers.get('Authorization')).toBe('Bearer jwt-value')
    expect(headers.get('If-Match')).toBe('"101"')
    expect(headers.get('X-Layout-Version')).toBe('"81"')
  })

  it('does not add a device header to Admin Web requests', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)
    await createAdminApi('jwt').users()
    const headers = fetchMock.mock.calls[0][1].headers as Headers
    expect(headers.has('X-Device-Code')).toBe(false)
  })

  it('surfaces structured concurrency conflicts', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Conflict', status: 409, detail: 'Changed', code: 'CONCURRENCY_CONFLICT', correlationId: 'corr-1' }, { status: 409 })))
    const promise = createAdminApi('jwt').publishLayout(layouts[0])
    await expect(promise).rejects.toMatchObject({ status: 409, code: 'CONCURRENCY_CONFLICT', isConcurrencyConflict: true })
  })

  it('dispatches the centralized logout event on authenticated 401', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Unauthorized', status: 401, detail: 'Expired', code: 'AUTHENTICATION_REQUIRED' }, { status: 401 })))
    const listener = vi.fn(); window.addEventListener('tkg:unauthorized', listener)
    await expect(createAdminApi('jwt').machines()).rejects.toBeInstanceOf(ApiProblem)
    expect(listener).toHaveBeenCalledOnce()
    window.removeEventListener('tkg:unauthorized', listener)
  })

  it('converts non-ProblemDetails failures into a safe generic error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('gateway', { status: 502, statusText: 'Bad Gateway' })))
    await expect(createAdminApi('jwt').devices()).rejects.toMatchObject({ status: 502, code: 'UNEXPECTED_HTTP_RESPONSE' })
  })
})
