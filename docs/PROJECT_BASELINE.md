# TKG PDA — Güncel Proje Baseline'ı

Bu belge, Phase 01–06 tamamlanması ve Phase 07A backend contract uygulaması sonrasındaki güncel sistemi özetler. Database/customer-data baseline'ı dondurulmuştur; Phase 07A mevcut API ve şemayı migration değiştirmeden kullanır.

> Phase 06 PR #4 `main`e merge edilmiştir. Phase 07A ayrıntıları için [PHASE_07A_PDA_RDS_CONTRACT_READINESS.md](PHASE_07A_PDA_RDS_CONTRACT_READINESS.md) belgesine bakın.

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
| 07A | PDA + RDS Contract Readiness | Uygulandı |
| 07B | Flutter PDA Application | Planlandı |
| 08 | RDS Task Integration | Planlandı |
| 09 | SEER Map/Route/Profile Integration | Planlandı |
| 10 | System Integration & Tests | Planlandı |
| 11 | PLC Integration | Gelecek aşama |

Baseline, müşteri kaynaklarındaki lojistik gerçeği normalize eder; robot davranışı üretmez. RDS WorkSite ID'leri, robot X/Y, SEER harita koordinatları, route/stitch ve navigation profile verileri seed edilmemiştir.

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
| `CONTAINER_CATEGORY_UNRESOLVED` | 4 |
| `CONTAINER_CATEGORY_CONFLICT` | 2 |
| `PART_CODE_SOURCE_DISCREPANCY` | 2 |

Çözülmemiş container kodları `(blank)`, `1284`, `1285`, `285C`, `ARABA`, `KCK` ve `ORT` olarak raporlanır; eşleşme veya ölçü tahmin edilmez. `#DIV/0!` üreten gerçek parçalar master data'ya alınır, etkilenmiş planning metrikleri `NULL` kalır.

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
- 80 slotun container ilişkisi çözülememiştir.
- 183 slot için güvenilir fiziksel footprint ölçüsü yoktur; tamamı `NULL` kalır.
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

- PDA iş kurallarını yeniden kurmaz; `BRING_EMPTY`, `REMOVE_FULL`, `REMOVE_EMPTY` seçenekleri Application policy tarafından enabled/disabled sebebiyle döner.
- `WAREHOUSE_OPERATOR` yalnız `BRING_EMPTY`; `ADMIN_WEB` ise hiçbir PDA transport capability almaz.
- Aktif talep bir slotu meşgul ettiğinde ikinci action `SLOT_BUSY` olur; `occupancy_state` değerlerine `RESERVED` eklenmez.
- RDS WorkSite ve task alanları yalnız `integration` şemasındaki persisted kanıttan okunur. Mapping veya template yokluğu açık/nullable kalır; WorkSite ID veya task label üretilmez.
- PDA execution state, TKG business status ile kalıcı RDS task kanıtının kararlı projeksiyonudur. Canlı RDS çağrısı ve status mutation yoktur.
- Normalize layout geometri robot/RDS/map koordinatı değildir.

Normative gelecek entegrasyon sözleşmesi [RDS_PDA_INTEGRATION_SPEC.md](RDS_PDA_INTEGRATION_SPEC.md) belgesindedir.

## 8. Sonraki aşama sınırı

Phase 07B Flutter PDA uygulamasıdır. Canlı RDS task/WorkSite/callback/alarm entegrasyonu Phase 08'e aittir. SEER map/profile, PLC, SAP, robot koordinatı ve execution callback entegrasyonu henüz başlamamıştır.
