import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Database, RefreshCw, Router, ShieldAlert } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useAdminApi } from '../api/ApiContext'
import { useToast } from '../components/Toast'
import { Button, EmptyState, ErrorState, LoadingState, PageHeader, Panel, PanelHeader, SearchInput, SelectInput, StatusPill, TableFrame, formatDate } from '../components/ui'

export function RdsIntegrationPage() {
  const api = useAdminApi()
  const toast = useToast()
  const client = useQueryClient()
  const [profileId, setProfileId] = useState('')
  const [search, setSearch] = useState('')
  const [type, setType] = useState('')
  const [busy, setBusy] = useState(false)
  const status = useQuery({ queryKey: ['rds-status'], queryFn: ({ signal }) => api.rdsStatus(signal), refetchOnWindowFocus: false })
  const profiles = useQuery({ queryKey: ['rds-navigation-profiles'], queryFn: ({ signal }) => api.rdsNavigationProfiles(signal) })
  const mappings = useQuery({ queryKey: ['rds-mappings'], queryFn: ({ signal }) => api.rdsSlotMappings(undefined, signal) })
  const workSites = useQuery({
    queryKey: ['rds-worksites', profileId, type],
    queryFn: ({ signal }) => api.rdsWorkSites({ navigationProfileId: profileId, type: type === '' ? undefined : Number(type) }, signal),
    enabled: !!profileId,
  })

  useEffect(() => {
    if (!profileId && profiles.data?.length) setProfileId(profiles.data.find((item) => item.status === 'PUBLISHED')?.id ?? profiles.data[0].id)
  }, [profileId, profiles.data])

  const filtered = useMemo(() => (workSites.data ?? []).filter((site) => `${site.siteId} ${site.area ?? ''} ${site.groupName ?? ''}`.toLocaleLowerCase('tr').includes(search.toLocaleLowerCase('tr'))), [search, workSites.data])
  const selectedProfile = profiles.data?.find((item) => item.id === profileId)
  const mappedCount = mappings.data?.filter((item) => item.rdsWorkSiteId).length ?? 0

  const sync = async () => {
    if (!profileId || !selectedProfile) return
    if (!window.confirm(`${selectedProfile.machineCode} / ${selectedProfile.code} profilinin şu anda RDS'te yüklü sahneyi temsil ettiğini onaylıyor musunuz? Bu işlem RDS'i yalnız okur.`)) return
    setBusy(true)
    try {
      const result = await api.syncRdsWorkSites(profileId)
      await Promise.all([
        client.invalidateQueries({ queryKey: ['rds-worksites'] }),
        client.invalidateQueries({ queryKey: ['rds-status'] }),
        client.invalidateQueries({ queryKey: ['rds-mappings'] }),
      ])
      toast.success('WorkSite aynası güncellendi', `${result.received} okundu · ${result.created} yeni · ${result.updated} güncel`)
    } catch (error) { toast.error(error, 'RDS senkronizasyonu tamamlanamadı') } finally { setBusy(false) }
  }

  if (status.isLoading || profiles.isLoading || mappings.isLoading) return <LoadingState label="RDS okuma durumu hazırlanıyor…" />
  const failure = status.error ?? profiles.error ?? mappings.error
  if (failure) return <ErrorState error={failure} onRetry={() => void Promise.all([status.refetch(), profiles.refetch(), mappings.refetch()])} />

  return <div className="page">
    <PageHeader eyebrow="Salt okunur dış entegrasyon" title="RDS Entegrasyonu" description="Canlı RDS kanıtını görüntüleyin, açıkça seçilen navigation profile'a aynalayın ve lojistik slot eşlemelerini denetleyin." actions={<Button variant="secondary" onClick={() => void status.refetch()}><RefreshCw size={16} /> Bağlantıyı doğrula</Button>} />
    <div className="warning-banner"><ShieldAlert size={20} /><div><strong>RDS değerleri dış sistem kanıtıdır.</strong><span>Bu ekran görev oluşturmaz, WorkSite durumunu değiştirmez ve robot hareketi başlatmaz. İsim benzerliği otomatik eşleme üretmez.</span></div></div>
    <div className="rds-summary">
      <Panel><PanelHeader title="RDS Connection" description="Sunucu tarafından güvenli biçimde gösterilen salt okunur endpoint" action={<StatusPill value={status.data?.reachable ? 'CONNECTED' : status.data?.enabled ? 'UNAVAILABLE' : 'DISABLED'} tone={status.data?.reachable ? 'success' : 'warning'} />} /><div className="rds-card-body"><code>{status.data?.baseAddressDisplaySafe ?? 'Yapılandırılmadı'}</code><span>Son başarılı okuma: {formatDate(status.data?.lastSuccessfulRead)}</span>{status.data?.businessMessage && <small>RDS: {status.data.businessStatus} · {status.data.businessMessage}</small>}{status.data?.errorCode && <small className="danger-text">{status.data.errorCode} · {status.data.errorMessage}</small>}</div></Panel>
      <Panel><PanelHeader title="Mapping Coverage" description="TKG mapping tablosu; RDS'e yazılmaz" action={<StatusPill value={`${mappedCount} MAPPED`} tone="info" />} /><div className="rds-card-body"><strong>{mappings.data?.length ?? 0} layout bölgesi</strong><span>{(mappings.data?.length ?? 0) - mappedCount} bölge eşlenmemiş veya MACHINE referansıdır.</span></div></Panel>
    </div>
    <Panel><PanelHeader title="WorkSite Synchronization" description="RDS'te o anda yüklü sahneyi temsil eden TKG navigation profile'ını admin açıkça seçmelidir." action={<Button disabled={!profileId || busy || !status.data?.enabled} onClick={() => void sync()}><Database size={16} /> Salt okunur senkronize et</Button>} /><div className="rds-sync-controls"><SelectInput aria-label="Navigation profile" value={profileId} onChange={(event) => setProfileId(event.target.value)}><option value="">Profil seçin</option>{profiles.data?.map((profile) => <option key={profile.id} value={profile.id}>{profile.machineCode} · {profile.code} v{profile.version} · {profile.status}</option>)}</SelectInput><span><Router size={16} /> İsim, slot, varyant veya map adından profil tahmini yapılmaz.</span></div></Panel>
    <Panel><PanelHeader title="Mirrored WorkSite Browser" description="filled / locked / disabled / syncFailed değerleri RDS'in son okunan kanıtıdır." action={<StatusPill value={`${filtered.length} WORKSITE`} tone="neutral" />} /><div className="table-toolbar table-toolbar--split"><SearchInput value={search} onChange={setSearch} placeholder="siteId, area veya grup ara…" /><SelectInput aria-label="WorkSite tipi" value={type} onChange={(event) => setType(event.target.value)}><option value="">Tüm tipler</option><option value="1">Physical (1)</option><option value="0">Logical (0)</option></SelectInput></div>{!profileId ? <EmptyState title="Navigation profile seçin" description="WorkSite aynası profile göre ayrılır." /> : workSites.isLoading ? <LoadingState label="WorkSite aynası okunuyor…" /> : workSites.error ? <ErrorState error={workSites.error} onRetry={() => void workSites.refetch()} /> : filtered.length ? <TableFrame><thead><tr><th>siteId</th><th>Alan / Grup</th><th>Tip</th><th>filled</th><th>locked</th><th>disabled</th><th>syncFailed</th><th>content</th><th>Readiness</th></tr></thead><tbody>{filtered.map((site) => <tr key={site.id}><td><strong>{site.siteId}</strong><small className="table-subtitle">{formatDate(site.lastSyncAt)}</small></td><td>{site.area ?? '—'}<small className="table-subtitle">{site.groupName ?? '—'}</small></td><td><StatusPill value={site.type === 1 ? 'PHYSICAL' : site.type === 0 ? 'LOGICAL' : 'UNKNOWN'} tone={site.type === 1 ? 'info' : 'neutral'} /></td><td>{site.filled ?? '—'}</td><td>{site.locked ?? '—'}</td><td>{site.disabled ?? '—'}</td><td>{site.syncFailed ?? '—'}</td><td><code>{site.content === null || site.content === '' ? '∅' : site.content}</code></td><td><StatusPill value={site.executionReady ? 'READY' : 'NOT READY'} tone={site.executionReady ? 'success' : 'warning'} /></td></tr>)}</tbody></TableFrame> : <EmptyState title="WorkSite bulunamadı" description="Seçilen profil ve filtreler için aynalanmış kayıt yok." />}</Panel>
  </div>
}
