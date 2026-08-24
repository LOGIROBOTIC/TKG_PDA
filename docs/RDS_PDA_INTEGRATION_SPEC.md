# TKG PDA — RDS/PDA Entegrasyon Sözleşmesi

**Durum:** Phase 08A read-only connectivity baseline

**Canlı entegrasyon:** WorkSite ve Wind Task sorgusu salt okunur olarak kanıtlandı; dispatch/mutation/callback Phase 08B'ye ertelendi

**Şema kaynağı:** `db/migrations/001_initial_schema.sql`

Bu belge Flutter PDA ile SEER RDS entegrasyonu arasındaki sorumlulukları sabitler. Phase 08A server-side read client, WorkSite aynası ve açık admin mapping'i uygular; Flutter RDS'e bağlanmaz ve RDS'e mutation gönderilmez.

## 1. Kaynak gerçekleri ve güven sınırı

```mermaid
flowchart LR
    P[Flutter PDA] -->|JWT + X-Device-Code| A[TKG ASP.NET Core API]
    A --> D[Application / Domain]
    D --> DB[(TKG PostgreSQL)]
    DB -->|Phase 08A WorkSite mirror| R[RdsReadClient]
    R -->|salt okunur query| S[SEER RDS]
    S --> C[Core / CBD15]
```

- **TKG iş gerçeğinin kaynağıdır:** kullanıcı/rol/makine yetkisi, variant ve layout seçimi, product group/part/container kanıtı, slotun `VACANT/OCCUPIED/UNKNOWN` ve `EMPTY/FULL/UNKNOWN` semantiği, taşıma talebi, idempotency ve audit.
- **RDS yürütme ve filo gerçeğinin kaynağıdır:** WorkSite'ın güncel yürütme durumu, Wind Task durumu, robot ataması, trafik/bekleme ve robot yürütme sonucu.
- Flutter yalnız TKG API ile konuşur. RDS adresi veya credential'ı Flutter'a verilmez; Flutter doğrudan RDS endpoint'i çağırmaz.
- TKG domain kuralları RDS veya istemci tarafından yeniden tanımlanmaz. RDS callback/poll sonucu TKG'nin kontrollü application/integration boundary'sinden geçirilir.

## 2. LayoutSlot ↔ RDS WorkSite

Kalıcı ilişki:

```text
master.layout_slots.id
  ↕ integration.layout_slot_rds_mapping.layout_slot_id
integration.rds_worksites.id
```

RDS `siteId`, TKG slot kodundan türetilmez. Eşleme yoksa `isMapped=false` döner ve bütün WorkSite alanları `NULL` kalır; bu durum müşteri master-data bozukluğu sayılmaz, execution readiness eksikliğidir.

Phase 07A read contract mevcut kalıcı alanları olduğu gibi projekte eder:

| PDA alanı | PostgreSQL kanıtı |
|---|---|
| `siteId` | `rds_site_id` |
| `area` | `area` |
| `groupName` | `group_name` |
| `type` | `rds_type` |
| `filled` | `last_filled` |
| `locked` | `last_locked` |
| `disabled` | `last_disabled` |
| `syncFailed` | `last_sync_failed` |
| `content` | `last_content` |
| `agvId` | `last_agv_id` |
| `lastSyncAt` | `last_sync_at` |

RDS WorkSite sözleşmesinin `filled`, `locked`, `lockedBy`, `disabled`, `syncFailed`, `content`, `type` ve `groupName` alanları Phase 08A'da canlı cevapta doğrulanmıştır. Mevcut TKG şeması `lockedBy` için ayrı kolon içermez; exact canlı payload `raw_state_json` içinde korunur ve projection ayrı alan uydurmaz.

### 2.1 Doluluk ve yük semantiği

| TKG iş durumu | RDS projeksiyonu |
|---|---|
| `VACANT` | `filled = 0` |
| `OCCUPIED` | `filled = 1` |
| `UNKNOWN` | kesin eşleme yapılmaz |

`EMPTY/FULL` TKG'nin iş semantiğidir. Phase 08'de RDS WorkSite `content` veya desteklenen extended field ile eşitlenebilir; gerçek müşteri RDS projesinde doğrulanmadan `EmptyTray`, `FullTray` gibi tutorial string'leri wire contract olarak dondurulamaz. Eşlemeler konfigürasyonla yönetilmelidir.

`locked` veya aktif TKG talebi slotun meşgul/rezerve olmasını gösterebilir. Bunun için `ops.slot_runtime_state.occupancy_state` alanına `RESERVED` eklenmez. Mevcut izinli değerler yalnız `VACANT`, `OCCUPIED`, `UNKNOWN` olarak kalır.

`slot_role = MACHINE` kayıtları yalnız normalize ekran/fiziksel referanstır; hiçbir RDS taşıma source/destination'ı olamaz.

## 3. Wind Task dispatch stratejisi

Phase 08B için amaçlanan dispatch yönü:

```mermaid
sequenceDiagram
    participant T as TKG backend
    participant A as RdsAdapter
    participant R as Protected RDS script endpoint
    T->>A: validated TransportRequest + TaskTemplate
    A->>R: jj.createWindTask(...)
    R-->>A: HTTP response + body code/msg
    A-->>T: persisted dispatch evidence
```

- Tek bir global Wind Task label hard-code edilmez. `master.task_templates`, `variant_id + product_group_id + task_type_id` bağlamında `rds_task_label`, source/destination slot group, priority ve `config_json` sağlar.
- Uygun aktif template veya `rds_task_label` yoksa bu gerçek `RdsTaskLabelConfigured=false` olarak görünür; label tahmin edilmez ve Phase 08 dispatch readiness kuralı görevi durdurmalıdır.
- Phase 07A yalnız aktif variant'ın template kayıtlarını okur; Wind Task oluşturmaz.
- Amaçlanan correlation, canlı doğrulamaya tabi olmak üzere `ops.transport_requests.id ↔ RDS taskRecordId` ilişkisidir.
- `request_no`, uygun RDS projelerinde `outOrderNo` ile ilişkilendirilir. Kanonik UUID correlation'ın yerine tek başına kullanılmaz.
- HTTP 200 iş başarısı anlamına gelmez. `RdsAdapter` response body içindeki `code/msg` alanlarını doğrulamadan dispatch, query, stop veya WorkSite update işlemini başarılı sayamaz.

## 4. Task sorgu, iptal ve reconciliation

Phase 08B execution entegrasyonunda kullanılacak RDS operasyonları:

- Task sorgu: `/api/queryWindTask` (Phase 08A client parsing'i salt okunur olarak kanıtlandı)
- Task durdurma: `/api/stop-task`
- Task oluşturma: korumalı özel RDS script endpoint'i üzerinden `jj.createWindTask(...)`

`SENT_TO_RDS` ve `RUNNING` talepler PDA'nın mevcut cancel endpoint'iyle iptal edilmez; `CANCELLATION_AFTER_EXECUTION_DEFERRED` koduyla reddedilir. Phase 08, `/api/stop-task` sonucu ile TKG slot state reconciliation kuralını ayrıca tanımlamalıdır. HTTP cevabı gelmeden veya callback/poll kanıtı oluşmadan TKG fiziksel gerçeği varsayımla değiştirilmez.

Birincil event yolu:

```text
RDS taskDone / taskFailed / taskStopped / workSiteChanged / ilgili event
→ korumalı TKG callback endpoint'i
→ RdsAdapter doğrulaması
→ Application integration boundary
→ PostgreSQL transaction + audit
```

Güvenlik ağı:

```text
TKG reconciliation worker
→ /api/queryWindTask
→ response code/msg + task state doğrulaması
→ idempotent reconciliation
```

Callback kimlik doğrulama, replay koruması, imza/IP policy, retry ve dead-letter davranışı Phase 08'de tanımlanacaktır.

## 5. PDA execution projeksiyonu

PDA ham RDS statüsünü tek UI durumu olarak kullanmaz. `ops.transport_requests` ile kalıcı `integration.rds_tasks` kanıtından şu kararlı durumlar üretilir:

| PDA state | Anlam |
|---|---|
| `REQUESTED` | TKG talebi oluşturuldu/doğrulanıyor |
| `QUEUED` | TKG kuyruğunda |
| `WAITING_FOR_ROBOT` | RDS sınırında, atanmış robot/hareket kanıtı yok |
| `ROBOT_ASSIGNED` | kalıcı `agvId` var; fiziksel hareket iddia edilmez |
| `RUNNING` | TKG veya kalıcı RDS başlangıç kanıtı |
| `WAITING` | suspended veya trafik bekleme koşulu |
| `COMPLETED` | başarıyla tamamlandı |
| `FAILED` | abnormal/exception/failure |
| `TERMINATED` | durduruldu/iptal edildi |

Bilinen RDS durumları: `1000 running`, `1001 terminated`, `1002 suspended`, `1003 finished`, `1004 abnormal finish`, `1005 restart exception`, `1006 abnormal interruption`. `1000` tek başına robotun hareket ettiğini kanıtlamaz. Terminal TKG business status, gecikmiş/çelişkili kalıcı RDS statüsüne üstün gelir.

## 6. Hata ve operatör mesajı

PDA sözleşmesi `failureCode`, `failureCategory`, Türkçe/güvenli `operatorMessage`, yetkili teknik görünüm için `technicalMessage` ve `correlationId` taşır.

| RDS/Core örneği | PDA kodu | Kategori | Sonuç |
|---|---|---|---|
| `60008`, `fail_lock_bin` | `WORKSITE_BUSY` | `WORKSITE` | güvenli operatör açıklaması |
| `60009`, `no_path` | `PATH_UNAVAILABLE` | `NAVIGATION` | güvenli operatör açıklaması |
| `60011`, `task_failed` | `EXECUTION_FAILED` | `EXECUTION` | güvenli operatör açıklaması |
| `54002`, `54003` | `TRAFFIC_WAITING` | `TRAFFIC` | `WAITING`; otomatik `FAILED` değildir |

Ham/Çince teknik metin birincil PDA mesajı olamaz. Phase 07A'da teknik ayrıntı yalnız device-bound `ADMIN`/`SUPERVISOR` projeksiyonunda verilir. Tam alarm ingestion, kod kataloğu ve localization Phase 08 kapsamıdır.

## 7. Koordinat ve map sınırı

`master.layout_slots.ui_x/ui_y/ui_width/ui_height` yalnız 0–1 normalize Admin/PDA ekran geometrisidir. Robot, SEER, RDS, harita veya milimetre koordinatı değildir. Phase 07A route, waypoint, stitch, map veya navigation-profile switching üretmez.

Public RDS API düzeyinde map/profile switching henüz kanıtlanmamıştır. Endpoint veya davranış uydurulamaz; `SeerMapAdapter` ayrı gelecek çalışmadır.

## 8. Phase sınırları

Phase 08A'da kesinlikle bulunmaz:

- RDS credential, production IP veya WebSocket;
- Wind Task create/stop/dispatch (yalnız read query parser'ı vardır);
- WorkSite yazma, callback kaydı veya alarm ingestion;
- Flutter'dan direct RDS bağlantısı;
- robot/map koordinatı veya SEER navigation davranışı.

Canlı read davranışları `RdsApi_en.md`, kurulu yerel RDS ve opt-in testlerle kanıtlanmıştır. Dispatch, stop/cancel reconciliation ve callback davranışı Phase 08B'de ayrıca kanıtlanmadan production contract sayılmaz.
