import { useQuery } from '@tanstack/react-query'
import { Braces, ChevronDown, ChevronRight, Fingerprint } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useAdminApi } from '../api/ApiContext'
import { EmptyState, ErrorState, LoadingState, PageHeader, Panel, SearchInput, SelectInput, StatusPill, TableFrame, formatDate } from '../components/ui'

export function AuditPage() {
  const api = useAdminApi(); const [entityType, setEntityType] = useState(''); const [search, setSearch] = useState(''); const [expanded, setExpanded] = useState<number | null>(null)
  const query = useQuery({ queryKey: ['audit', 250, entityType], queryFn: ({ signal }) => api.audit({ limit: 250, entityType: entityType || undefined }, signal) })
  const entityTypes = useMemo(() => [...new Set((query.data ?? []).map((item) => item.entityType))].sort(), [query.data])
  const filtered = useMemo(() => (query.data ?? []).filter((item) => `${item.action} ${item.actorUsername ?? ''} ${item.entityType} ${item.entityId ?? ''}`.toLowerCase().includes(search.toLowerCase())), [query.data, search])
  if (query.isLoading) return <LoadingState label="Audit geçmişi yükleniyor…" />
  if (query.error) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />
  return <div className="page"><PageHeader eyebrow="İzlenebilirlik" title="Audit geçmişi" description="Kim, neyi, ne zaman değiştirdi? Güvenli before/after kanıtını correlation ID ile inceleyin." />
    <Panel><div className="table-toolbar table-toolbar--split"><div className="toolbar-group"><SearchInput value={search} onChange={setSearch} placeholder="Aksiyon, kullanıcı veya entity ara…" /><SelectInput value={entityType} onChange={(event) => setEntityType(event.target.value)}><option value="">Tüm entity türleri</option>{entityTypes.map((value) => <option key={value}>{value}</option>)}</SelectInput></div><span>Son {filtered.length} kayıt</span></div>{filtered.length ? <TableFrame><thead><tr><th aria-label="Detay" /><th>Zaman</th><th>Aktör</th><th>Aksiyon</th><th>Entity</th><th>Correlation</th></tr></thead><tbody>{filtered.map((item) => <AuditRows key={item.id} item={item} open={expanded === item.id} onToggle={() => setExpanded(expanded === item.id ? null : item.id)} />)}</tbody></TableFrame> : <EmptyState title="Audit kaydı bulunamadı" description="Filtreleri değiştirin veya ilk yapılandırma hareketini bekleyin." />}</Panel>
  </div>
}

function AuditRows({ item, open, onToggle }: { item: Awaited<ReturnType<ReturnType<typeof useAdminApi>['audit']>>[number]; open: boolean; onToggle: () => void }) {
  return <><tr className="clickable-row" onClick={onToggle}><td>{open ? <ChevronDown size={17} /> : <ChevronRight size={17} />}</td><td>{formatDate(item.createdAt)}</td><td><strong>{item.actorUsername ?? 'Sistem'}</strong><small className="table-subtitle">{item.ipAddress ?? 'IP yok'}</small></td><td><StatusPill value={item.action.replace('ADMIN_', '')} tone="info" /></td><td>{item.entityType}<small className="table-subtitle mono">{item.entityId ?? '—'}</small></td><td><span className="correlation"><Fingerprint size={15} /> {item.correlationId?.slice(0, 8) ?? '—'}</span></td></tr>{open && <tr className="audit-detail"><td colSpan={6}><div><section><h4><Braces size={16} /> Önce</h4><pre>{JSON.stringify(item.before, null, 2) || 'null'}</pre></section><section><h4><Braces size={16} /> Sonra</h4><pre>{JSON.stringify(item.after, null, 2) || 'null'}</pre></section></div></td></tr>}</>
}
