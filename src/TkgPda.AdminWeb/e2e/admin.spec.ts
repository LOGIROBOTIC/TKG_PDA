import { expect, test, type Page, type Request } from '@playwright/test'
import { containers, devices, issues, layouts, machines, roles, session, slots, users } from '../src/test/fixtures'

const parts = [{ id: 'p-1', code: '67115BN050-10-HAOS', description: 'Canonical müşteri parçası', isActive: true, concurrencyToken: 111 }]
const audit = [{ id: 'a-1', actorUserId: 'u-admin', actorUsername: 'admin', action: 'LAYOUT_UPDATED', entityType: 'LAYOUT', entityId: 'l-1', before: null, after: { name: 'Müşteri Fiziksel Planı' }, createdAt: new Date().toISOString() }]

interface MockOptions {
  slotConflict?: boolean
  onSlotUpdate?: (request: Request) => void
}

async function mockAdminApi(page: Page, options: MockOptions = {}) {
  await page.route('**/api/v1/admin/**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname

    if (path === '/api/v1/admin/auth/login') return route.fulfill({ json: session })
    if (path === '/api/v1/admin/layouts/l-1/slots/s-1' && request.method() === 'PUT') {
      options.onSlotUpdate?.(request)
      if (options.slotConflict) return route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({ status: 409, title: 'Concurrency conflict', detail: 'Kayıt daha yeni bir sürüme taşındı.', code: 'CONCURRENCY_CONFLICT', correlationId: 'corr-e2e-409' }),
      })
      return route.fulfill({ json: { ...slots[0], ...request.postDataJSON(), concurrencyToken: 103 }, headers: { ETag: '"103"' } })
    }
    if (path === '/api/v1/admin/layouts/l-1/slots') return route.fulfill({ json: slots })

    const responses: Record<string, unknown> = {
      '/api/v1/admin/roles': roles,
      '/api/v1/admin/users': users,
      '/api/v1/admin/devices': devices,
      '/api/v1/admin/machines': machines,
      '/api/v1/admin/variants': [],
      '/api/v1/admin/layouts': layouts,
      '/api/v1/admin/parts': parts,
      '/api/v1/admin/product-groups': [],
      '/api/v1/admin/containers': containers,
      '/api/v1/admin/data-issues': issues,
      '/api/v1/admin/audit-logs': audit,
    }
    const body = responses[path]
    if (body !== undefined) return route.fulfill({ json: body })
    return route.fulfill({ status: 404, json: { status: 404, title: 'Mock route missing', detail: path, code: 'E2E_MOCK_MISSING' } })
  })
}

async function login(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Kullanıcı adı').fill('admin')
  await page.getByLabel('Parola').fill('correct-horse-battery-staple')
  await page.getByRole('button', { name: 'Yönetim merkezine gir' }).click()
}

test('ADMIN_WEB login opens the live management dashboard without a device code', async ({ page }) => {
  await mockAdminApi(page)
  await login(page)
  await expect(page.getByRole('heading', { name: 'Genel bakış' })).toBeVisible()
  await expect(page.getByText('ADMIN_WEB')).toBeVisible()
  await expect(page.getByText('1', { exact: true }).first()).toBeVisible()
  await page.getByRole('link', { name: 'Kullanıcılar & Yetkiler' }).click()
  await expect(page.getByRole('heading', { name: 'Kullanıcılar & yetkiler' })).toBeVisible()
  await expect(page.getByText('Hat Operatörü')).toBeVisible()
})

test('permission matrix exposes persisted grants and the ADMIN bypass', async ({ page }) => {
  await mockAdminApi(page)
  await login(page)
  await page.goto('/users')
  const permissionButtons = page.getByRole('button', { name: 'Yetkiler' })
  await permissionButtons.first().click()
  await expect(page.getByText(/ADMIN rolü global machine erişimine sahiptir/)).toBeVisible()
  await page.getByRole('button', { name: 'Tamam' }).click()
  await permissionButtons.nth(1).click()
  await expect(page.getByRole('grid')).toBeVisible()
  await expect(page.getByLabel('Görüntüle')).toBeChecked()
  await expect(page.getByLabel('Operasyon')).toBeChecked()
  await expect(page.getByLabel('Varyant')).not.toBeChecked()
})

test('normalized layout editor saves with entity and parent ETags', async ({ page }) => {
  let headers: Record<string, string> = {}
  await mockAdminApi(page, { onSlotUpdate: (request) => { headers = request.headers() } })
  await login(page)
  await page.goto('/layouts/l-1/editor')
  await expect(page.getByText('Robot Haritası Değildir')).toBeVisible()
  await expect(page.getByText(/Robot X\/Y, waypoint, rota, map veya stitch/)).toBeVisible()
  await page.getByTestId('slot-SLOT-01').click()
  await page.getByLabel('Görünen ad').fill('Güncel Kasa Alanı')
  await page.getByRole('button', { name: 'Seçili slotu kaydet' }).click()
  await expect(page.getByText('Slot kaydedildi')).toBeVisible()
  expect(headers['if-match']).toBe('"101"')
  expect(headers['x-layout-version']).toBe('"81"')
  expect(headers['x-device-code']).toBeUndefined()
})

test('409 ProblemDetails becomes an actionable stale-data message', async ({ page }) => {
  await mockAdminApi(page, { slotConflict: true })
  await login(page)
  await page.goto('/layouts/l-1/editor')
  await page.getByTestId('slot-SLOT-01').click()
  await page.getByRole('button', { name: 'Seçili slotu kaydet' }).click()
  await expect(page.getByText('Kayıt başka bir yönetici tarafından değiştirildi')).toBeVisible()
  await expect(page.getByText(/CONCURRENCY_CONFLICT · corr-e2e-409/)).toBeVisible()
})
