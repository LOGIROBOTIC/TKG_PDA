import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { LayoutEditorPage } from './LayoutEditorPage'
import { containers, layouts, machines, slots } from '../test/fixtures'
import { jsonResponse, renderAuthenticated } from '../test/render'

const mockEditorApi = (layoutData = layouts, slotData = slots) => {
  const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (init?.method === 'PUT') return Promise.resolve(jsonResponse({ ...slotData[0], displayName: 'Güncel Slot', concurrencyToken: 103 }, { headers: { ETag: '"103"' } }))
    if (url.endsWith('/layouts')) return Promise.resolve(jsonResponse(layoutData))
    if (url.endsWith('/layouts/l-1/slots')) return Promise.resolve(jsonResponse(slotData))
    if (url.endsWith('/machines')) return Promise.resolve(jsonResponse(machines))
    if (url.endsWith('/containers')) return Promise.resolve(jsonResponse(containers))
    return Promise.resolve(jsonResponse([]))
  })
  vi.stubGlobal('fetch', fetchMock)
  renderAuthenticated(<Routes><Route path="/layouts/:layoutId/editor" element={<LayoutEditorPage />} /></Routes>, '/layouts/l-1/editor')
  return fetchMock
}

describe('Normalized layout editor', () => {
  it('labels the canvas as not being a robot map and renders proven slots', async () => {
    mockEditorApi()
    expect(await screen.findByText('Robot Haritası Değildir')).toBeVisible()
    expect(await screen.findByTestId('slot-SLOT-01')).toBeVisible()
    expect(screen.getByText('Normalize X →')).toBeVisible()
  })

  it('loads a selected slot into the inspector', async () => {
    mockEditorApi()
    await userEvent.click(await screen.findByTestId('slot-SLOT-01'))
    expect(screen.getByRole('heading', { name: 'SLOT-01' })).toBeVisible()
    expect(screen.getByLabelText('Görünen ad')).toHaveValue('Boş Kasa')
    expect(screen.getByText('101')).toBeVisible()
  })

  it('offers only the slot roles accepted by the database and Admin API', async () => {
    mockEditorApi()
    await userEvent.click(await screen.findByTestId('slot-SLOT-01'))
    const options = Array.from((screen.getByLabelText('Slot rolü') as HTMLSelectElement).options).map((option) => option.value)
    expect(options).toEqual(['MACHINE', 'STAGING', 'EMPTY_STORAGE', 'FULL_STORAGE', 'WAREHOUSE', 'PICKUP', 'DROP', 'SHARED', 'OTHER'])
  })

  it('saves a slot with both optimistic concurrency headers', async () => {
    const fetchMock = mockEditorApi()
    await userEvent.click(await screen.findByTestId('slot-SLOT-01'))
    await userEvent.clear(screen.getByLabelText('Görünen ad'))
    await userEvent.type(screen.getByLabelText('Görünen ad'), 'Güncel Slot')
    await userEvent.click(screen.getByRole('button', { name: /Seçili slotu kaydet/ }))
    await waitFor(() => expect(fetchMock.mock.calls.some(([, init]) => (init as RequestInit | undefined)?.method === 'PUT')).toBe(true))
    const mutation = fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === 'PUT')!
    const headers = mutation[1].headers as Headers
    expect(headers.get('If-Match')).toBe('"101"')
    expect(headers.get('X-Layout-Version')).toBe('"81"')
  })

  it('detects overlapping client-side geometry without auto-moving records', async () => {
    mockEditorApi(layouts, [slots[0], { ...slots[1], x: .2, y: .2 }])
    expect(await screen.findByText('2 slot çakışıyor')).toBeVisible()
    expect(screen.getByTestId('slot-SLOT-01')).toHaveClass('overlap')
    fireEvent.keyDown(screen.getByTestId('layout-canvas'), { key: 'ArrowRight' })
  })

  it('keeps published versions read-only', async () => {
    mockEditorApi([{ ...layouts[0], status: 'PUBLISHED' }])
    expect(await screen.findByText('Salt okunur')).toBeVisible()
    expect(screen.getByRole('button', { name: /Slot ekle/ })).toBeDisabled()
  })
})
