# TKG PDA — Güncel Proje Baseline'ı

Bu belge, Phase 01–09 tamamlanması sonrasındaki güncel sistemi özetler. Database/customer-data baseline'ı dondurulmuştur; Phase 08B/08C ve multi-platform operator Phase 09 mevcut şemayı migration değiştirmeden kullanır.

WP-33 Operator UX V2, bu baseline üzerinde migration eklemeden müşterinin `N. Kasa Doldu / N. Kasa Boş / Tüm Kasaları Kaldır` dilini ve global business-only görev kuyruğunu ekler. Mantıksal kasa numarası runtime layout ordering'den türetilir; fiziksel routing backend'de kalır.

> Phase 08B PR #8 `main`e merge edilmiştir. Canlı simulator kabulü için [PHASE_08C_RDS_SIMULATOR_COMMISSIONING.md](PHASE_08C_RDS_SIMULATOR_COMMISSIONING.md) belgesine bakın.

## 1. Kapsam ve phase durumu

| Kod | Aşama | Durum |
|---|---|---|
| 01 | Database Architecture | Kabul edildi |
| 01.1 | Schema Consistency | Kabul edildi |
| 02A | Textual Master Data | Kabul edildi |
| 02B | Physical Layout Extraction | Kabul edildi |
| 03 | Business Workflow Engine | Kabul edildi |
| 04 | Backend API + Authentication/RBAC | Kabul edildi |
| 05 | Admin Configuration API | Kabul edildi |
| 06 | Admin Web | Tamamlandı |
| 07A | PDA + RDS Contract Readiness | Tamamlandı |
| 07B | Flutter PDA Application | Tamamlandı |
| 08A | Live RDS Connectivity + WorkSite Mapping | Tamamlandı |
| 08B | RDS Task Dispatch + Reconciliation | Tamamlandı |
| 08C | RDS Wind Task Commissioning + Simulator E2E | Tamamlandı |
| 09 | Multi-Platform Operator Client — Android + Web/PWA | Tamamlandı |
| 10 | System Integration & Tests | Planlandı |
| 11 | PLC Integration | Gelecek aşama |

Baseline, müşteri kaynaklarındaki lojistik gerçeği normalize eder; robot davranışı üretmez. RDS WorkSite ID'leri müşteri seed'ine eklenmemiştir; Phase 08A'da açık navigation-profile seçimiyle canlı RDS'ten TKG aynasına alınır ve admin tarafından açıkça eşlenir. Robot X/Y, SEER harita koordinatları ve route/stitch üretilmemiştir.

## 2. Database mimarisi

PostgreSQL modeli beş şemaya ayrılır:

| Şema | Sorumluluk |
|---|---|
| `auth` | kullanıcılar, roller ve makine yetkileri |
| `master` | makine/variant, part/container, planning, layout/navigation ve task template tanımları |
| `ops` | aktif seçimler, slot durumu, taşıma talepleri ve event geçmişi |
| `integration` | RDS WorkSite eşlemesi ve RDS task yansımaları |
| `audit` | istemci cihazları ve değişiklik denetim kayıtları |

Canonical migration `db/migrations/001_initial_schema.sql` dosyasıdır. Baseline schema migration yeni bir değişiklik olmadan korunmuştur.

Önemli model kararları:

- Bir fiziksel layout birden fazla variant tarafından paylaşılabilir (`master.variant_layout_assignments`).
- Navigation profile da variant'lara assignment tablosuyla bağlanır ve paylaşılabilir.
- Planning uniqueness, grouped ve ungrouped satırlar için iki partial unique index ile variant bağlamında korunur.
- Gerçek part kayıtları planning girdisi eksik olsa bile silinmez; hesaplanamayan metrikler `NULL` kalır.
- `integration.rds_worksites.navigation_profile_id` nullable olarak korunmuştur; domain kararı verilmeden constraint sıkılaştırılmamıştır.

Tam ilişkiler için [database/ERD.md](database/ERD.md) belgesine bakın.

## 3. Textual customer-data baseline — Phase 02A

Sekiz müşteri Excel dosyasından deterministik olarak üretilen kayıtlar:

| Kayıt | Adet |
|---|---:|
| Unique part | 161 |
| Container type | 10 |
| Product group | 51 |
| Product group part | 140 |
| Planning row | 191 |
| Formula error cell | 33 |
| Data issue | 159 |

Data issue dağılımı:

| Kod | Adet |
|---|---:|
| `UNKNOWN_CONTAINER_MAPPING` | 60 |
| `EXCEL_FORMULA_ERROR` | 33 |
| `SOURCE_VALUE_CONFLICT` | 25 |
| `MISSING_PLANNING_INPUT` | 23 |
| `AUTHORITATIVE_DUPLICATE_VALUE_CONFLICT` | 10 |
| `CONTAINER_DIMENSION_SOURCE_CONFLICT` | 3 |
| `CONTAINER_CATEGORY_SOURCE_RECONCILED` | 2 |
| `CONTAINER_CATEGORY_UNRESOLVED` | 1 |
| `PART_CODE_SOURCE_DISCREPANCY` | 2 |

Çözülmemiş container category code yalnız `1285`'tir; blank planning mapping'leri `NULL` kalır. Customer görselleri/planning kanıtıyla `1284`, `285C`, `BYK`, `ORT`, `KCK` ve `ARABA` uzlaştırılmıştır; çelişkili type-level ölçüler yine `NULL` kalır. `#DIV/0!` üreten gerçek parçalar master data'ya alınır, etkilenmiş planning metrikleri `NULL` kalır.

`67115BN050-10-HAOS` canonical part kodudur. SH-01-A fiziksel kaynakta görünen HMTR varyasyonu canonical kaydı değiştirmez; provenance/data issue içinde müşteri teyidi bekleyen discrepancy olarak tutulur.

## 4. Physical-layout baseline — Phase 02B

Beş fiziksel layout workbook'undan üretilen kayıtlar:

| Kayıt | Adet |
|---|---:|
| Layout | 21 |
| Variant-layout assignment | 24 |
| Layout slot | 183 |
| Lojistik pozisyon | 172 |
| Makine/hücre bölgesi | 11 |
| HIGH confidence slot | 133 |
| MEDIUM confidence slot | 50 |
| LOW confidence kabul edilen slot | 0 |
| LOW confidence dışlanan aday | 13 |

### 4.1 Slot rolü invariant'ı

`master.layout_slots.slot_role` alanı lojistik pozisyonları makine/hücre bölgelerinden ayırır. On bir makine/hücre kaydı `slot_role = 'MACHINE'` değerini taşır; kalan 172 kayıt lojistik pozisyondur.

`MACHINE` kayıtları:

- PDA üzerinde fiziksel/ekran yerleşim referansı olarak gösterilebilir,
- taşıma doluluk durumunun işletildiği lojistik pozisyon değildir,
- `ops.transport_requests.source_slot_id` olamaz,
- `ops.transport_requests.destination_slot_id` olamaz,
- Phase 03'te source/destination slot gruplarına dahil edilmemelidir.

Şema gerekli ayrımı zaten `slot_role` ile temsil eder; bu bakım turunda migration değiştirilmemiştir. Mevcut FK'ler rolü filtrelemediği için Phase 03 command/domain validation bu kuralı açıkça uygulamalı ve otomatik test etmelidir.

### 4.2 Layout paylaşımı ve SEALER

- SH-01-P1/P2 variant'ları aynı kanıtlanmış fiziksel layout'u paylaşır.
- SH-02-P1/P2 variant'ları aynı kanıtlanmış fiziksel layout'u paylaşır.
- SEALER V1/V2 aynı fiziksel layout'u paylaşır.
- SEALER V3 için fiziksel kanıt bulunmadığından layout assignment oluşturulmamıştır.

### 4.3 Açık fiziksel veri konuları

- 28 slotun part ilişkisi çözülememiştir.
- 74 slotun container ilişkisi çözülememiştir.
- 54 lojistik slot customer-source nominal container footprint'i taşır; kalan 129 region'ın footprint'i `NULL` kalır. Bu ölçüler robot yaklaşımı/clearance değildir.
- 14 kaynak bölge güvenli yorumlanamadığı için LOW confidence aday olarak seed dışı bırakılmıştır (13 slot adayı ve bir kanıtlanmamış SEALER V3 bölgesi bağlamı).
- Ekran geometrisi 0–1 aralığındaki `ui_*` alanlarıdır; fiziksel veya robot koordinatı değildir.

Her layout için deterministik karşılaştırma görselleri `data/generated/phase_02b/review/` altında tutulur.

## 5. Üretim ve provenance zinciri

Uygulama sırası değişmez:

1. `db/migrations/001_initial_schema.sql`
2. `db/seeds/generated/002a_customer_textual_master_data.sql`
3. `db/seeds/generated/002b_customer_physical_layouts.sql`

Phase 02A/02B çıktıları elle düzenlenmez. Pipeline'lar kaynak dosyaları salt okunur kullanır; source manifest, workbook SHA-256, sheet ve hücre aralıklarıyla provenance sağlar. Ham müşteri kaynakları Git repository'sine alınmaz.

## 6. Kabul doğrulamaları

Kabul edilen baseline sonucunda:

- Phase 02A otomatik testleri: 10/10 başarılı.
- Phase 02B otomatik testleri: 13/13 başarılı.
- PostgreSQL 18.3 fresh database: `001 + 002a + 002b + 002b` başarıyla uygulandı.
- FK/orphan, cross-machine assignment, duplicate layout/slot ve bounds ihlali: 0.
- İkinci import mantıksal DB durumunu değiştirmedi.
- Phase 02B integration testi aynı fresh cluster içindeki ilk/ikinci seed digest'lerinin eşitliğini doğruladı. Digest seed tarafından üretilen UUID/timestamp alanlarını da içerdiği için farklı fresh cluster'lar arasında sabit fingerprint olarak kullanılmaz.
- Üretilen SQL/JSON/CSV/review PNG çıktıları deterministiktir.
- Kaynak müşteri dosyaları byte-identical kalmıştır.

Güncel komutlar ve yeniden üretim adımları [db/seeds/README.md](../db/seeds/README.md) içindedir.

## 7. Backend ve yönetim baseline'ı

- Phase 03, üç lojistik operasyonun state machine, slot reservation, idempotency, cancellation ve variant-switch guard'larını uygular.
- Phase 04, device-bound PDA JWT, RBAC, machine permission, operational context ve transport HTTP boundary'sini sağlar.
- Phase 05, device-less ADMIN Web JWT ile user/device/permission ve master-configuration API'sini sağlar.
- Phase 06, bu boundary üzerinde React/TypeScript Admin Web, görsel permission matrisi, data-quality/audit ekranları ve normalize 2D layout editörünü sağlar.
- Phase 07A, aynı operational-context endpoint'inde aktif variant product group/part'larını, aktif talepleri, role-sensitive `availableActions`, task-template readiness ile persisted RDS WorkSite/task projeksiyonunu sağlar.
- `WAREHOUSE_OPERATOR` yalnızca `BRING_EMPTY` oluşturabilir ve kendi `BRING_EMPTY` talebini iptal edebilir; line-side remove işlemleri otomatik yetki değildir.
- Admin mutasyonları `ETag/If-Match` optimistic concurrency ve transaction içi audit kullanır.
- Layoutlar draft üzerinde düzenlenir; published/archived sürümler immutable kalır ve yeni sürüm clone/publish akışından geçer.
- Hiçbir Admin API metodu `ops` runtime state'ini veya transport status'unu doğrudan değiştirmez.

### 7.1 Admin Web sınırları

- JWT yalnız uygulama çalışma-zamanı belleğinde (React state) tutulur; `localStorage`, `sessionStorage`, `IndexedDB`, cookie, URL, `.env` veya source code'a yazılmaz ve console'a loglanmaz. Bellek-tabanlı olduğu için tam sayfa yenilemesi oturumu bilinçli olarak sonlandırır ve yeniden girişi zorunlu kılar; kabul edilmiş şemada refresh-token/oturum modeli yoktur.
- Logout veya token değişiminde server-state cache tamamen temizlenir; aynı sekmede farklı admin hesapları arasında veri taşınmaz.
- Admin istemcisi `X-Device-Code` göndermez; PDA kimlik doğrulaması ayrı kalır.
- Bütün mutasyonlar API üzerinden geçer ve `If-Match`; slot güncellemesi ayrıca `X-Layout-Version` kullanır.
- Layout editörü yalnız 0–1 normalize ekran/fiziksel referans geometrisini düzenler ve **Robot Haritası Değildir** uyarısını kalıcı gösterir.
- Unknown footprint veya association değerleri UI tarafından tahmin edilmez.
- Phase 06 veritabanı migration'ı veya backend state bypass'ı eklemez.

### 7.2 PDA/RDS contract sınırı

- PDA iş kurallarını yeniden kurmaz; müşteri kasa eylemleri Application policy tarafından legal `BRING_EMPTY`, `REMOVE_FULL`, `REMOVE_EMPTY` operasyonlarına çevrilir ve enabled/disabled sebebiyle döner.
- `WAREHOUSE_OPERATOR` yalnız `BRING_EMPTY`; `ADMIN_WEB` ise hiçbir PDA transport capability almaz.
- Aktif talep bir slotu meşgul ettiğinde ikinci action `SLOT_BUSY` olur; `occupancy_state` değerlerine `RESERVED` eklenmez.
- RDS WorkSite ve task alanları yalnız `integration` şemasındaki persisted kanıttan okunur. Mapping veya template yokluğu açık/nullable kalır; WorkSite ID veya task label üretilmez.
- PDA execution state, TKG business status ile kalıcı RDS task kanıtının kararlı projeksiyonudur. PDA canlı RDS çağrısı yapmaz; Phase 08B dispatch/cancel/reconciliation yalnız backend integration boundary'sindedir.
- Normalize layout geometri robot/RDS/map koordinatı değildir.

Normative gelecek entegrasyon sözleşmesi [RDS_PDA_INTEGRATION_SPEC.md](RDS_PDA_INTEGRATION_SPEC.md) belgesindedir.

### 7.3 Flutter PDA sınırı

- Aynı Flutter operator istemcisi Android endüstriyel PDA ve Windows Edge/Chrome Web/PWA hedeflerini destekler. Android Device Code secure storage'da, Web Device Code yalnız `tkg.pda.device-code` origin-local key'inde tutulur; JWT her iki platformda yalnız process/tab belleğindedir. Parola ve operational state persist edilmez.
- Makine listesi, compatible variant, cancellation ve execution state tamamen backend projection'ından gelir; Flutter iş kuralı veya canonical makine listesi üretmez.
- Normal Operator akışı `machine -> part -> optional ALT/RELATED -> compatible variant -> operation` şeklindedir. Üç operation aynı seçim sayfasında görünür; ayrı "İşleme Devam Et" veya physical-layout ekranı yoktur.
- Flutter source/destination slot veya `EMPTY_STORAGE` seçmez. Normalize layout Admin konfigürasyonunda korunur; normal Operator routing yüzeyi değildir.
- Aynı maddi transport retry'ı aynı idempotency key'i kullanır; çift dokunma tek in-flight submission ile sınırlanır.
- Offline command queue yoktur. Ağ kaybında bütün mutation'lar kapanır ve context yeniden doğrulanmadan açılmaz.
- Flutter yalnız TKG API'ye bağlanır; canlı RDS, PLC, SAP veya robot API çağrısı içermez.
- Güncel Flutter kabulü 14 unit, 28 widget ve fresh PostgreSQL/gerçek API üzerinde 20 HTTP contract testiyle doğrulanmıştır.

### 7.4 Phase 08A salt-okunur RDS baseline'ı

- Yerel RDS `http://127.0.0.1:8080` üzerinde doğrulandı; 22 physical WorkSite (3 filled, 19 unfilled) ve 0 mevcut Wind Task gözlendi.
- Server-side `RdsReadClient` yalnız WorkSite ve Wind Task query endpoint'lerini çağırır; HTTP ile birlikte body `code/msg` doğrulanır.
- Admin, mevcut RDS sahnesini temsil eden navigation profile'ı açıkça seçer. Ad/kod benzerliğinden profil veya slot mapping türetilmez.
- WorkSite aynası `integration.rds_worksites`, açık slot mapping'i mevcut `integration.layout_slot_rds_mapping` tablosunu kullanır; migration yoktur.
- `MACHINE` region ve logical WorkSite mapping reddedilir. Disabled/locked/syncFailed physical kimlik eşlenebilir fakat execution readiness false kalır.
- Bu maddeler Phase 08A'nın kabul sınırını kaydeder. Phase 08B backend-only execution capability eklemiştir; Flutter'a RDS configuration hâlâ verilmez.

## 8. Phase 08B execution baseline'ı

- RDS mutation yalnız server-side worker ve authenticated callback boundary'sindedir; Flutter doğrudan RDS'e erişmez.
- `taskRecordId`, TKG TransportRequest UUID'sidir. RDS task kaydı network öncesi tekil yazılır; create response kaybında aynı identity query/retry edilir.
- Task template input'ları whitelist binding ile çözülür; global label/input adı hard-code edilmez.
- `SENT_TO_RDS/RUNNING` iptali stop request sonrasında callback/query kanıtı bekler.
- Terminal task kanıtı ilgili navigation profile WorkSite aynasını tazeler; callback önce terminalleştirirse başarılı-refresh audit'i oluşana kadar kalıcı worker sorgusu bu işi yeniden seçer. Unknown `content` slot EMPTY/FULL anlamına çevrilmez.
- PLC olmadığı için `EMPTY↔FULL` değişimi ayrı device-bound load-state operasyonudur; occupancy değişmez.
- Yerel scene'de `sim_01` ve `sim_02` simulator robotları var, fiziksel robot yok. Phase 08C'de development-only `TKG_SIM_MOVE` task'ı sabit `sim_01`, AP15→AP20 ve iki `Wait` bloğuyla doğrulandı.
- RDS `boot.js` script artifact'i kuruldu; authenticated validation, create, callback, polling reconciliation ve aynı `taskRecordId` retry canlı geçti.

## 9. Phase 08C simulator commissioning baseline'ı

- Manuel RDS UI koşusu `30e7f8aa-3d5e-4f59-bb84-0b998f879d5d` ile `sim_01` üzerinde `Finished/1003` oldu.
- Tek TKG TransportRequest `4cd854a5-924c-464c-9fa9-644034f87146`, RDS'de aynı `taskRecordId` ile tam bir kez oluştu.
- TKG `QUEUED → RUNNING → COMPLETED`, RDS `1000 → 1003`; authenticated `taskDone` callback audit'i kaydedildi.
- Aynı RDS identity retry'ı `IDEMPOTENT_REPLAY` döndürdü ve instance sayısı 1 kaldı.
- 22 WorkSite'ın `filled/content/locked/disabled/syncFailed` digest'i önce/sonra aynı kaldı.
- `TKG_SIM_MOVE` production `BRING_EMPTY/REMOVE_FULL/REMOVE_EMPTY` değildir; fixed AP/Wait görevi WorkSite, LOAD/UNLOAD, fork/jack veya PLC kullanmaz.

## 10. Phase 09 multi-platform operator baseline'ı

Donmuş ürün şartı:

> TKG Operator Client must support both Android industrial PDAs and Windows-based production-line touch terminals through Web/PWA. Both platforms use the same Flutter codebase, TKG API contracts, device-bound security model, and server-authoritative workflow rules.

- Windows production client, Edge/Chrome içinde Flutter Web/PWA'dır; native Windows `.exe` ve ikinci React operator uygulaması yoktur.
- React Admin Web ayrı `ADMIN_WEB`; Flutter Web ayrı device-bound operator flow'udur.
- Web Device Code origin-scoped persistent storage'da kalır. JWT, password, user, permission, context ve command persist edilmez; refresh/restart login'e döner.
- Same-origin deployment önerilir. Ayrı origin gerektiğinde exact configured CORS kullanılır; wildcard/cookie credentials yoktur.
- `/operator/` production build'i CanvasKit/WASM, ikon ve gerekli fallback fontlarını yerelden sunar; public CDN/Internet runtime bağımlılığı yoktur.
- Service worker yalnız static shell/asset cache'idir. API mutation cache, Background Sync veya offline replay queue yoktur.
- 1024×600, 1280×720, 1366×768 ve 1920×1080 touch yerleşimleri test edilmiştir. `MACHINE` region noninteractive kalır.
- Chrome ve gerçek Edge browser E2E, fresh PostgreSQL 18.3 ve gerçek API ile çalışır. RDS browser'dan erişilmez ve Phase 09'da canlı robot hareketi yapılmaz.

Ayrıntılar [PHASE_09_MULTIPLATFORM_OPERATOR.md](PHASE_09_MULTIPLATFORM_OPERATOR.md) ve [OPERATOR_WEB_DEPLOYMENT.md](OPERATOR_WEB_DEPLOYMENT.md) belgelerindedir.

## 11. Sonraki aşama sınırı

Sonraki ayrı çalışma, gerçek mapped physical RDS WorkSite'ları ve robot-modeline özgü aksiyonlarla production `BRING_EMPTY`, `REMOVE_FULL` ve `REMOVE_EMPTY` Wind Task'larını tasarlamalıdır. SEER map/profile/route, PLC, SAP ve robot koordinatı henüz başlamamıştır.
