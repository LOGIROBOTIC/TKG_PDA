import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RdsIntegrationPage } from './RdsIntegrationPage'
import { jsonResponse, renderAuthenticated } from '../test/render'

const profile = { id: 'np-1', machineId: 'm-1', machineCode: 'SH-01-A', code: 'NAV-A', name: 'Navigation A', version: 1, status: 'PUBLISHED' }
const workSite = { id: 'rw-1', navigationProfileId: 'np-1', siteId: 'SITE-01', area: 'A', groupName: 'G', type: 1, filled: 0, locked: 0, disabled: 0, syncFailed: 0, content: '', agvId: null, lastSyncAt: '2026-08-24T08:00:00Z', executionReady: true }
const taskTemplate = { id: 'tt-1', variantId: 'v-1', variantCode: 'V1', productGroupId: null, productGroupCode: null, operation: 'BRING_EMPTY', rdsTaskLabel: 'TKG_SIM_TASK', priority: 10, config: { rdsInputBindings: [{ param: 'fromSite', source: 'SOURCE_RDS_SITE_ID', required: true }] }, isActive: true }
const robotAlarm = { robotId: 'sim_01', robotModel: 'AMB-150', severity: 'fatal', code: 'F1', description: 'Fatal one', occurrenceCount: 3, occurredAt: '2026-08-30T10:00:00Z', dispatchable: false, undispatchableReason: 'low_battery' }

const mockRdsApi = (options?: { alarms?: unknown; alarmsResponse?: Response }) => {
  const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url.endsWith('/rds/status')) return Promise.resolve(jsonResponse({ enabled: true, reachable: true, stale: false, lastSuccessfulRead: '2026-08-24T08:00:00Z', baseAddressDisplaySafe: 'http://rds.test:8080', businessStatus: 200, businessMessage: 'Success', errorCode: null, errorMessage: null }))
    if (url.endsWith('/rds/navigation-profiles')) return Promise.resolve(jsonResponse([profile]))
    if (url.includes('/rds/worksites?')) return Promise.resolve(jsonResponse([workSite]))
    if (url.endsWith('/rds/slot-mappings')) return Promise.resolve(jsonResponse([]))
    if (url.endsWith('/rds/task-templates')) return Promise.resolve(jsonResponse([taskTemplate]))
    if (url.endsWith('/rds/alarms')) return Promise.resolve(options?.alarmsResponse ?? jsonResponse(options?.alarms ?? []))
    if (url.endsWith('/rds/task-templates/validate') && init?.method === 'POST') return Promise.resolve(jsonResponse({ validated: true, taskLabelExists: true, taskRecordExists: false, code: 200, message: 'VALIDATED', rawResponse: {} }))
    if (url.endsWith('/rds/worksites/sync') && init?.method === 'POST') return Promise.resolve(jsonResponse({ navigationProfileId: 'np-1', received: 1, created: 1, updated: 0, syncedAt: '2026-08-24T08:00:00Z', businessCode: 200, businessMessage: 'Success' }))
    return Promise.resolve(jsonResponse([]))
  })
  vi.stubGlobal('fetch', fetchMock)
  renderAuthenticated(<RdsIntegrationPage />)
  return fetchMock
}

describe('RDS integration page', () => {
  it('labels RDS data as external read evidence and does not mutate during load', async () => {
    const fetchMock = mockRdsApi()
    expect(await screen.findByText('RDS değerleri dış sistem kanıtıdır.')).toBeVisible()
    expect(await screen.findByText('SITE-01')).toBeVisible()
    expect(screen.getByText('http://rds.test:8080')).toBeVisible()
    expect(fetchMock.mock.calls.every(([, init]) => !init?.method || init.method === 'GET')).toBe(true)
  })

  it('requires an explicit scene confirmation before read-only synchronization', async () => {
    const fetchMock = mockRdsApi()
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    await userEvent.click(await screen.findByRole('button', { name: /Salt okunur senkronize et/ }))
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => String(url).endsWith('/rds/worksites/sync') && init?.method === 'POST')).toBe(true))
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('şu anda RDS'))
  })

  it('shows explicit task bindings and validates through TKG without dispatching', async () => {
    const fetchMock = mockRdsApi()
    expect(await screen.findByText('TKG_SIM_TASK')).toBeVisible()
    expect(screen.getByText('fromSite')).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: /Doğrula/ }))
    await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => String(url).endsWith('/rds/task-templates/validate') && init?.method === 'POST')).toBe(true))
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('create-transport'))).toBe(false)
  })

  it('shows the current robot alarm table with severity, code and detail', async () => {
    mockRdsApi({ alarms: [robotAlarm] })
    expect(await screen.findByText('sim_01')).toBeVisible()
    expect(screen.getByText('AMB-150')).toBeVisible()
    expect(screen.getByText('FATAL')).toBeVisible()
    expect(screen.getByText('F1')).toBeVisible()
    expect(screen.getByText('Fatal one')).toBeVisible()
    expect(screen.getByText('3')).toBeVisible()
    expect(screen.getByText('2026-08-30T10:00:00Z')).toBeVisible()
    expect(screen.getByText(/NO.*low_battery/)).toBeVisible()
  })

  it('shows an empty state when there are no active robot alarms', async () => {
    mockRdsApi({ alarms: [] })
    expect(await screen.findByText('Aktif robot alarmı yok')).toBeVisible()
  })

  it('shows a distinct failure state with retry when the alarm read fails, not an empty table', async () => {
    const fetchMock = mockRdsApi({
      alarmsResponse: jsonResponse({ status: 503, title: 'Service Unavailable', detail: 'RDS okuma servisi geçici olarak kullanılamıyor.', code: 'RDS_UNAVAILABLE', correlationId: 'corr-1' }, { status: 503 }),
    })
    expect(await screen.findByText('RDS okuma servisi geçici olarak kullanılamıyor.')).toBeVisible()
    expect(screen.queryByText('Aktif robot alarmı yok')).not.toBeInTheDocument()

    await userEvent.click(screen.getAllByRole('button', { name: /Yenile/ })[0])
    await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/rds/alarms')).length).toBeGreaterThan(1))
  })
})
