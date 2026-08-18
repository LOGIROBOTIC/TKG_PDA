import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive, CopyPlus, Edit3, ExternalLink, Plus, Rocket, Save } from 'lucide-react'
import { useMemo, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAdminApi } from '../api/ApiContext'
import type { Layout, Machine } from '../api/types'
import { useToast } from '../components/Toast'
import { Button, EmptyState, ErrorState, Field, LoadingState, Modal, PageHeader, Panel, SearchInput, SelectInput, StatusPill, TableFrame, TextInput, formatDate } from '../components/ui'

type LifecycleAction = { kind: 'publish' | 'archive' | 'clone'; layout: Layout }

export function LayoutsPage() {
  const api = useAdminApi()
  const toast = useToast()
  const client = useQueryClient()
  const layouts = useQuery({ queryKey: ['layouts'], queryFn: ({ signal }) => api.layouts(undefined, signal) })
  const machines = useQuery({ queryKey: ['machines'], queryFn: ({ signal }) => api.machines(signal) })
  const [machineId, setMachineId] = useState('')
  const [search, setSearch] = useState('')
  const [createOpen, setCreateOpen] = useState(false)
  const [rename, setRename] = useState<Layout | null>(null)
  const [lifecycle, setLifecycle] = useState<LifecycleAction | null>(null)
  const [busy, setBusy] = useState(false)
  const filtered = useMemo(() => (layouts.data ?? []).filter((layout) => (!machineId || layout.machineId === machineId) && `${layout.code} ${layout.name}`.toLowerCase().includes(search.toLowerCase())), [layouts.data, machineId, search])
  const refresh = async () => { await client.invalidateQueries({ queryKey: ['layouts'] }) }
  const execute = async (action: () => Promise<unknown>, message: string, close?: () => void) => { setBusy(true); try { const result = await action(); await refresh(); toast.success(message); close?.(); return result } catch (error) { toast.error(error); await refresh() } finally { setBusy(false) } }
  if (layouts.isLoading || machines.isLoading) return <LoadingState label="Layout sürümleri yükleniyor…" />
  const failure = layouts.error ?? machines.error
  if (failure) return <ErrorState error={failure} onRetry={() => { void layouts.refetch(); void machines.refetch() }} />

  const machineName = (id: string) => machines.data?.find((item) => item.id === id)?.code ?? '—'
  return <div className="page"><PageHeader eyebrow="Fiziksel lojistik modeli" title="Layout sürümleri" description="Müşterinin fiziksel yerleşimini draft, publish ve archive yaşam döngüsüyle yönetin." actions={<Button onClick={() => setCreateOpen(true)}><Plus size={17} /> Yeni draft</Button>} />
    <div className="warning-banner"><strong>Robot Haritası Değildir</strong><span>Bu alan yalnız normalize 0–1 ekran/fiziksel yerleşim referansıdır. Robot koordinatı, waypoint, route veya stitch üretmez.</span></div>
    <Panel><div className="table-toolbar table-toolbar--split"><div className="toolbar-group"><SearchInput value={search} onChange={setSearch} placeholder="Layout kodu veya adı ara…" /><SelectInput value={machineId} onChange={(event) => setMachineId(event.target.value)}><option value="">Tüm makineler</option>{machines.data?.map((machine) => <option value={machine.id} key={machine.id}>{machine.code}</option>)}</SelectInput></div><span>{filtered.length} sürüm</span></div>
      {filtered.length ? <TableFrame><thead><tr><th>Layout</th><th>Makine</th><th>Sürüm</th><th>Slot / Atama</th><th>Yayın</th><th>Durum</th><th className="align-right">İşlemler</th></tr></thead><tbody>{filtered.map((layout) => <tr key={layout.id}><td><strong>{layout.code}</strong><small className="table-subtitle">{layout.name}</small></td><td>{machineName(layout.machineId)}</td><td><span className="version-chip">v{layout.version}</span></td><td>{layout.slotCount} slot · {layout.assignedVariantCount} varyant</td><td>{formatDate(layout.publishedAt)}</td><td><StatusPill value={layout.status} /></td><td><div className="row-actions"><Link className="button button--ghost button--sm" to={`/layouts/${layout.id}/editor`}><Edit3 size={15} /> {layout.status === 'DRAFT' ? 'Editör' : 'Görüntüle'}</Link>{layout.status === 'DRAFT' && <><Button size="sm" variant="ghost" onClick={() => setRename(layout)}>Ad</Button><Button size="sm" variant="primary" onClick={() => setLifecycle({ kind: 'publish', layout })}><Rocket size={15} /> Yayınla</Button></>}{layout.status !== 'DRAFT' && layout.status !== 'ARCHIVED' && <Button size="sm" variant="ghost" onClick={() => setLifecycle({ kind: 'clone', layout })}><CopyPlus size={15} /> Yeni sürüm</Button>}{layout.status === 'PUBLISHED' && <Button size="sm" variant="danger" onClick={() => setLifecycle({ kind: 'archive', layout })}><Archive size={15} /></Button>}</div></td></tr>)}</tbody></TableFrame> : <EmptyState title="Layout bulunamadı" description="Filtreleri değiştirin veya yeni bir draft oluşturun." />}
    </Panel>
    {createOpen && <CreateLayoutDialog open machines={machines.data ?? []} busy={busy} onClose={() => setCreateOpen(false)} onSave={(draft) => execute(() => api.createLayout(draft), 'Draft layout oluşturuldu', () => setCreateOpen(false))} />}
    {rename && <RenameDialog layout={rename} busy={busy} onClose={() => setRename(null)} onSave={(name) => execute(() => api.updateLayout(rename, name), 'Layout adı güncellendi', () => setRename(null))} />}
    {lifecycle && <LifecycleDialog action={lifecycle} busy={busy} onClose={() => setLifecycle(null)} onConfirm={() => execute(() => lifecycle.kind === 'publish' ? api.publishLayout(lifecycle.layout) : lifecycle.kind === 'archive' ? api.archiveLayout(lifecycle.layout) : api.cloneLayout(lifecycle.layout), lifecycle.kind === 'publish' ? 'Layout yayınlandı' : lifecycle.kind === 'archive' ? 'Layout arşivlendi' : 'Yeni draft sürümü oluşturuldu', () => setLifecycle(null))} />}
  </div>
}

function CreateLayoutDialog({ open, machines, busy, onClose, onSave }: { open: boolean; machines: Machine[]; busy: boolean; onClose: () => void; onSave: (draft: Pick<Layout, 'machineId' | 'code' | 'name'>) => void }) {
  const [draft, setDraft] = useState({ machineId: machines[0]?.id ?? '', code: '', name: '' })
  const submit = (event: FormEvent) => { event.preventDefault(); onSave(draft) }
  return <Modal open={open} onClose={onClose} title="Yeni draft layout" description="Aynı makine ve kod için aynı anda yalnız bir draft bulunabilir." footer={<><Button variant="ghost" onClick={onClose}>Vazgeç</Button><Button form="layout-create" type="submit" disabled={busy || !draft.machineId}><Save size={16} /> Oluştur</Button></>}><form id="layout-create" className="form-grid" onSubmit={submit}><Field label="Makine"><SelectInput value={draft.machineId} onChange={(event) => setDraft({ ...draft, machineId: event.target.value })}>{machines.map((machine) => <option value={machine.id} key={machine.id}>{machine.code} · {machine.displayName}</option>)}</SelectInput></Field><Field label="Layout kodu"><TextInput required value={draft.code} onChange={(event) => setDraft({ ...draft, code: event.target.value })} /></Field><div className="form-span"><Field label="Layout adı"><TextInput required value={draft.name} onChange={(event) => setDraft({ ...draft, name: event.target.value })} /></Field></div></form></Modal>
}

function RenameDialog({ layout, busy, onClose, onSave }: { layout: Layout; busy: boolean; onClose: () => void; onSave: (name: string) => void }) {
  const [name, setName] = useState(layout.name)
  return <Modal open onClose={onClose} title="Draft adını değiştir" description={`${layout.code} · v${layout.version} · ETag ${layout.concurrencyToken}`} footer={<><Button variant="ghost" onClick={onClose}>Vazgeç</Button><Button disabled={busy || !name} onClick={() => onSave(name)}><Save size={16} /> Kaydet</Button></>}><Field label="Layout adı"><TextInput value={name} onChange={(event) => setName(event.target.value)} /></Field></Modal>
}

function LifecycleDialog({ action, busy, onClose, onConfirm }: { action: LifecycleAction; busy: boolean; onClose: () => void; onConfirm: () => void }) {
  const config = action.kind === 'publish' ? { title: 'Layout sürümünü yayınla', text: 'Yapısal doğrulama çalışacak; aynı kodun eski published sürümü varsa atamalar yeni sürüme taşınacak.', button: 'Yayınla', icon: Rocket } : action.kind === 'archive' ? { title: 'Layout sürümünü arşivle', text: 'Aktif runtime veya transport referansı varsa API bu işlemi reddedecek.', button: 'Arşivle', icon: Archive } : { title: 'Yeni draft sürümü oluştur', text: 'Slotlar yeni kimliklerle kopyalanacak ve published kaynak değişmeden kalacak.', button: 'Sürümü oluştur', icon: CopyPlus }
  const Icon = config.icon
  return <Modal open onClose={onClose} title={config.title} description={`${action.layout.code} · v${action.layout.version}`} footer={<><Button variant="ghost" onClick={onClose}>Vazgeç</Button><Button variant={action.kind === 'archive' ? 'danger' : 'primary'} disabled={busy} onClick={onConfirm}><Icon size={16} /> {config.button}</Button></>}><div className="confirm-content"><Icon /><p>{config.text}</p><div className="info-banner"><ExternalLink size={17} /> Runtime seçimi bu işlemle otomatik değiştirilmez.</div></div></Modal>
}
