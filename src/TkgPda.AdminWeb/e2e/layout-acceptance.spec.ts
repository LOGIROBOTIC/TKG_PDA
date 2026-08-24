import { psql } from './harness'
import { expect, loginAdminApi, loginAsAdmin, test, uniqueCode } from './support'

interface ApiLayout { id: string; code: string; concurrencyToken: number }
interface ApiSlot {
  id: string
  layoutId: string
  code: string
  displayName: string
  role: string
  defaultContainerTypeId: string | null
  x: number | null
  y: number | null
  width: number | null
  height: number | null
  displayOrder: number
  allowsEmptyLoad: boolean
  allowsFullLoad: boolean
  isEnabled: boolean
  metadata: Record<string, unknown>
  concurrencyToken: number
}

const slotName = (layoutCode: string, slotCode: string): string =>
  psql(
    `SELECT s.display_name FROM master.layout_slots s ` +
    `JOIN master.layouts l ON l.id = s.layout_id ` +
    `WHERE l.code = '${layoutCode}' AND s.code = '${slotCode}'`,
  ).trim()

test.describe.serial('layout lifecycle', () => {
  const machineName = 'SH-01-A'
  const layoutCode = uniqueCode('E2E-LAYOUT')
  const slotCode = uniqueCode('E2E-SLOT')

  test('08 · create a DRAFT layout', async ({ page }) => {
    await loginAsAdmin(page)
    await page.getByRole('link', { name: 'Fiziksel Layoutlar' }).click()
    await expect(page.getByText('Robot Haritası Değildir')).toBeVisible()
    await page.getByRole('button', { name: 'Yeni draft' }).click()

    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Makine').selectOption({ label: `${machineName} · ${machineName}` })
    await dialog.getByLabel('Layout kodu').fill(layoutCode)
    await dialog.getByLabel('Layout adı').fill('E2E Draft Layout')
    await dialog.getByRole('button', { name: 'Oluştur' }).click()

    await expect(page.getByText('Draft layout oluşturuldu')).toBeVisible()
    const row = page.locator('tr', { hasText: layoutCode })
    await expect(row.getByText('DRAFT', { exact: true })).toBeVisible()
  })

  test('09 · edit and save a layout slot on the draft', async ({ page }) => {
    await loginAsAdmin(page)
    await page.getByRole('link', { name: 'Fiziksel Layoutlar' }).click()
    await page.getByPlaceholder('Layout kodu veya adı ara…').fill(layoutCode)
    await page.locator('tr', { hasText: layoutCode }).getByRole('link', { name: 'Editör' }).click()

    // Draft editor is editable: the save control is present.
    await expect(page.getByRole('button', { name: 'Seçili slotu kaydet' })).toBeVisible()

    await page.getByRole('button', { name: 'Slot ekle' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Slot kodu').fill(slotCode)
    await dialog.getByLabel('Görünen ad').fill('E2E Lojistik Slotu')
    await dialog.getByRole('button', { name: 'Slot oluştur' }).click()
    await expect(page.getByText('Slot oluşturuldu')).toBeVisible()

    await page.getByTestId(`slot-${slotCode}`).click()
    await page.getByLabel('Görünen ad').fill('E2E Güncel Slot')
    await page.getByRole('button', { name: 'Seçili slotu kaydet' }).click()
    await expect(page.getByText('Slot kaydedildi')).toBeVisible()
    expect(slotName(layoutCode, slotCode)).toBe('E2E Güncel Slot')
  })

  test('10 · a stale ETag yields the concurrency-conflict UX and never overwrites newer data', async ({ page, request }) => {
    await loginAsAdmin(page)
    await page.getByRole('link', { name: 'Fiziksel Layoutlar' }).click()
    await page.getByPlaceholder('Layout kodu veya adı ara…').fill(layoutCode)
    await page.locator('tr', { hasText: layoutCode }).getByRole('link', { name: 'Editör' }).click()

    // Browser loads the slot (ETag A).
    await page.getByTestId(`slot-${slotCode}`).click()
    // A second real ADMIN_WEB actor changes the same slot through the Phase 05 API,
    // invalidating ETag A held by the browser.
    const secondActorToken = await loginAdminApi(request)
    const auth = { Authorization: `Bearer ${secondActorToken}` }
    const layoutResponse = await request.get('/api/v1/admin/layouts', { headers: auth })
    expect(layoutResponse.ok()).toBe(true)
    const currentLayout = ((await layoutResponse.json()) as ApiLayout[]).find((item) => item.code === layoutCode)
    expect(currentLayout).toBeTruthy()
    const slotsResponse = await request.get(`/api/v1/admin/layouts/${currentLayout!.id}/slots`, { headers: auth })
    expect(slotsResponse.ok()).toBe(true)
    const currentSlot = ((await slotsResponse.json()) as ApiSlot[]).find((item) => item.code === slotCode)
    expect(currentSlot).toBeTruthy()
    const secondActorName = 'E2E İkinci Yönetici Değeri'
    const secondActorUpdate = await request.put(`/api/v1/admin/layouts/${currentLayout!.id}/slots/${currentSlot!.id}`, {
      headers: {
        ...auth,
        'If-Match': `"${currentSlot!.concurrencyToken}"`,
        'X-Layout-Version': `"${currentLayout!.concurrencyToken}"`,
      },
      data: {
        displayName: secondActorName,
        role: currentSlot!.role,
        defaultContainerTypeId: currentSlot!.defaultContainerTypeId,
        x: currentSlot!.x,
        y: currentSlot!.y,
        width: currentSlot!.width,
        height: currentSlot!.height,
        displayOrder: currentSlot!.displayOrder,
        allowsEmptyLoad: currentSlot!.allowsEmptyLoad,
        allowsFullLoad: currentSlot!.allowsFullLoad,
        isEnabled: currentSlot!.isEnabled,
        metadata: currentSlot!.metadata,
      },
    })
    expect(secondActorUpdate.ok(), await secondActorUpdate.text()).toBe(true)
    expect(slotName(layoutCode, slotCode)).toBe(secondActorName)

    // Browser submits with the now-stale ETag A.
    await page.getByLabel('Görünen ad').fill('E2E Çakışan Yazma')
    await page.getByRole('button', { name: 'Seçili slotu kaydet' }).click()

    // Dedicated Turkish stale-data copy with the stable domain code and a correlation id.
    await expect(page.getByText('Kayıt başka bir yönetici tarafından değiştirildi')).toBeVisible()
    await expect(page.getByText(/CONCURRENCY_CONFLICT · [0-9a-f]{8}-[0-9a-f]{4}/)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sunucudan yeniden yükle' })).toBeVisible()

    // The newer server record was not overwritten by the stale submission.
    expect(slotName(layoutCode, slotCode)).toBe(secondActorName)
    await page.getByRole('button', { name: 'Sunucudan yeniden yükle' }).click()
    await expect(page.getByRole('button', { name: 'Sunucudan yeniden yükle' })).toHaveCount(0)
  })

  test('11 · publish the draft layout', async ({ page }) => {
    await loginAsAdmin(page)
    await page.getByRole('link', { name: 'Fiziksel Layoutlar' }).click()
    await page.getByPlaceholder('Layout kodu veya adı ara…').fill(layoutCode)
    await page.locator('tr', { hasText: layoutCode }).getByRole('button', { name: 'Yayınla' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Yayınla' }).click()

    await expect(page.getByText('Layout yayınlandı')).toBeVisible()
    await page.getByPlaceholder('Layout kodu veya adı ara…').fill(layoutCode)
    await expect(page.locator('tr', { hasText: layoutCode }).getByText('PUBLISHED', { exact: true })).toBeVisible()
  })

  test('12 · the publish produced an audit entry', async ({ page }) => {
    await loginAsAdmin(page)
    await page.getByRole('link', { name: 'Audit Geçmişi' }).click()
    await expect(page.getByRole('heading', { name: 'Audit geçmişi' })).toBeVisible()
    await page.getByRole('combobox').selectOption('LAYOUT')
    await expect(page.getByText('LAYOUT_PUBLISHED').first()).toBeVisible()
  })
})

test('editor acceptance · seeded published layout renders MACHINE + logistics, read-only, bounded, non-robot', async ({ page }) => {
  await loginAsAdmin(page)
  const machineCode = 'SH-01-A'
  const seededCode = psql(
    `SELECT l.code FROM master.layouts l JOIN master.machines m ON m.id = l.machine_id ` +
    `WHERE m.code = '${machineCode}' AND l.status = 'PUBLISHED' ` +
    `AND EXISTS (SELECT 1 FROM master.layout_slots s WHERE s.layout_id = l.id AND s.slot_role = 'MACHINE') ` +
    `AND EXISTS (SELECT 1 FROM master.layout_slots s WHERE s.layout_id = l.id AND s.slot_role <> 'MACHINE') ` +
    `ORDER BY l.code LIMIT 1`,
  ).trim()
  expect(seededCode, 'a seeded published layout with a MACHINE region and logistics slots must exist').not.toBe('')

  await page.getByRole('link', { name: 'Fiziksel Layoutlar' }).click()
  await page.getByRole('combobox').selectOption({ label: machineCode })
  await page.getByPlaceholder('Layout kodu veya adı ara…').fill(seededCode)
  await page.locator('tr', { hasText: seededCode }).getByRole('link', { name: 'Görüntüle' }).click()

  // "It is not a robot map" — the disclaimer and normalized (not robot) axis labels are present.
  await expect(page.getByText('Robot Haritası Değildir')).toBeVisible()
  await expect(page.getByText(/Robot X\/Y, waypoint, rota, map veya stitch/)).toBeVisible()
  await expect(page.getByText('Normalize X →')).toBeVisible()

  // PUBLISHED layouts are read-only: no save control, inspector inputs disabled.
  await expect(page.getByText('Salt okunur')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Seçili slotu kaydet' })).toHaveCount(0)

  // A MACHINE region renders distinctly and is labelled with its MACHINE role.
  const machineRegion = page.locator('.layout-slot--machine').first()
  await expect(machineRegion).toBeVisible()
  await expect(machineRegion.locator('em')).toHaveText('MACHINE · taşıma dışı')

  // Logistics (non-MACHINE) slots also render.
  expect(await page.locator('.layout-slot:not(.layout-slot--machine)').count()).toBeGreaterThan(0)

  // Normalized geometry stays within the 0–1 (0–100%) display box.
  for (const slot of await page.locator('.layout-slot').all()) {
    const geometry = await slot.evaluate((element) => {
      const style = (element as HTMLElement).style
      return { left: Number.parseFloat(style.left), top: Number.parseFloat(style.top), width: Number.parseFloat(style.width), height: Number.parseFloat(style.height) }
    })
    expect(geometry.left).toBeGreaterThanOrEqual(0)
    expect(geometry.top).toBeGreaterThanOrEqual(0)
    expect(geometry.width).toBeGreaterThanOrEqual(0)
    expect(geometry.height).toBeGreaterThanOrEqual(0)
    expect(geometry.left + geometry.width).toBeLessThanOrEqual(100.000001)
    expect(geometry.top + geometry.height).toBeLessThanOrEqual(100.000001)
  }

  // Selecting a slot keeps the inspector read-only on a published layout.
  await machineRegion.click()
  await expect(page.getByLabel('Görünen ad')).toBeDisabled()
})
