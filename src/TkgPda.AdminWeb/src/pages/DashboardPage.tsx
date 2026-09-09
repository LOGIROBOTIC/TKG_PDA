import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, ArrowUpRight, Bot, Boxes, Map, PackageOpen, Smartphone, Users } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useAdminApi } from '../api/ApiContext'
import { EmptyState, ErrorState, LoadingState, PageHeader, Panel, PanelHeader, StatusPill, compactNumber, formatDate } from '../components/ui'

export function DashboardPage() {
  const api = useAdminApi()
  const users = useQuery({ queryKey: ['users'], queryFn: ({ signal }) => api.users(signal) })
  const devices = useQuery({ queryKey: ['devices'], queryFn: ({ signal }) => api.devices(signal) })
  const machines = useQuery({ queryKey: ['machines'], queryFn: ({ signal }) => api.machines(signal) })
  const layouts = useQuery({ queryKey: ['layouts'], queryFn: ({ signal }) => api.layouts(undefined, signal) })
  const parts = useQuery({ queryKey: ['parts'], queryFn: ({ signal }) => api.parts(signal) })
  const containers = useQuery({ queryKey: ['containers'], queryFn: ({ signal }) => api.containers(signal) })
  const issues = useQuery({ queryKey: ['data-issues'], queryFn: ({ signal }) => api.dataIssues(signal) })
  const audit = useQuery({ queryKey: ['audit', 8], queryFn: ({ signal }) => api.audit({ limit: 8 }, signal) })
  const robots = useQuery({ queryKey: ['rds-robots'], queryFn: ({ signal }) => api.rdsRobots(signal), refetchOnWindowFocus: false })
  const all = [users, devices, machines, layouts, parts, containers, issues, audit]
  if (all.some((query) => query.isLoading)) return <LoadingState label="Yönetim özeti hazırlanıyor…" />
  const failure = all.find((query) => query.error)?.error
  if (failure) return <ErrorState error={failure} onRetry={() => all.forEach((query) => void query.refetch())} />

  const highIssues = issues.data?.filter((issue) => issue.severity === 'HIGH').length ?? 0
  const cards = [
    { label: 'Aktif kullanıcı', value: users.data?.filter((item) => item.isActive).length ?? 0, meta: `${users.data?.length ?? 0} toplam`, icon: Users, to: '/users' },
    { label: 'Tanımlı makine', value: machines.data?.length ?? 0, meta: 'müşteri operasyon kapsamı', icon: Boxes, to: '/machines' },
    { label: 'Published layout', value: layouts.data?.filter((item) => item.status === 'PUBLISHED').length ?? 0, meta: `${layouts.data?.filter((item) => item.status === 'DRAFT').length ?? 0} draft`, icon: Map, to: '/layouts' },
    { label: 'Veri kalite konusu', value: issues.data?.length ?? 0, meta: `${highIssues} yüksek öncelik`, icon: AlertTriangle, to: '/data-quality' },
  ]

  return (
    <div className="page">
      <PageHeader eyebrow="Operasyon konfigürasyonu" title="Genel bakış" description="Master data, erişim ve fiziksel layout sağlığının canlı yönetim özeti." />
      <div className="stat-grid">
        {cards.map(({ label, value, meta, icon: Icon, to }) => <Link className="stat-card" to={to} key={label}><div><span>{label}</span><strong>{compactNumber(value)}</strong><small>{meta}</small></div><div className="stat-card__icon"><Icon /></div><ArrowUpRight className="stat-card__arrow" size={17} /></Link>)}
      </div>
      <div className="dashboard-grid">
        <Panel>
          <PanelHeader title="Konfigürasyon nabzı" description="Yayın, cihaz ve filo durumlarının dağılımı" action={<Link className="text-link" to="/rds">RDS Filosu <ArrowUpRight size={15} /></Link>} />
          <div className="health-list">
            <div><span><Smartphone /> Aktif PDA cihazları</span><strong>{devices.data?.filter((item) => item.type === 'PDA' && item.isActive).length ?? 0}</strong></div>
            <div><span><Bot /> Aktif robot filosu</span><strong>{robots.data ? `${robots.data.filter((r) => r.connectivity === 'ONLINE').length}/${robots.data.length} Çevrimiçi` : '—'}</strong></div>
            <div><span><Map /> Draft layout sürümleri</span><strong>{layouts.data?.filter((item) => item.status === 'DRAFT').length ?? 0}</strong></div>
            <div><span><PackageOpen /> Fiziksel ölçüsü eksik kasalar</span><strong>{containers.data?.filter((item) => item.lengthMm === null || item.widthMm === null || item.heightMm === null).length ?? 0}</strong></div>
            <div><span><Boxes /> Aktif canonical parçalar</span><strong>{parts.data?.filter((item) => item.isActive).length ?? 0}</strong></div>
          </div>
        </Panel>
        <Panel>
          <PanelHeader title="Son yönetim hareketleri" description="Değişmez audit izinin son kayıtları" action={<Link className="text-link" to="/audit">Tümünü gör <ArrowUpRight size={15} /></Link>} />
          {audit.data?.length ? <div className="activity-list">{audit.data.map((item) => <div key={item.id}><span className="activity-dot" /><div><strong>{item.action.replaceAll('_', ' ')}</strong><small>{item.actorUsername ?? 'Sistem'} · {item.entityType}</small></div><time>{formatDate(item.createdAt)}</time></div>)}</div> : <EmptyState title="Audit kaydı yok" description="İlk yapılandırma değişikliği burada görünecek." />}
        </Panel>
      </div>
      <Panel>
        <PanelHeader title="Öncelikli veri konuları" description="Operasyonu durdurmayan fakat müşteri teyidi gerektiren kanıtlar" action={<Link className="text-link" to="/data-quality">İncele <ArrowUpRight size={15} /></Link>} />
        <div className="issue-strip">
          {(issues.data ?? []).slice(0, 5).map((issue) => <div key={`${issue.kind}-${issue.entityId}`}><StatusPill value={issue.severity} /><strong>{issue.code}</strong><span>{issue.message}</span></div>)}
        </div>
      </Panel>
    </div>
  )
}
