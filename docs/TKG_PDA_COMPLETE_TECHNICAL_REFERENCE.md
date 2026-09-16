# TKG_PDA Teknik Referans

> Kaynak kod ve sistem mantığının tam teknik referansı


- **Dokümante edilen branch:** `agent/rds-runtime-dispatch-fix`
- **Dokümante edilen commit:** `e84728badd16c347f0ace2e9f903a58831c022ca`
- **Doküman üretim tarihi:** 2026-09-09

---


## 1 Doküman Hakkında

Bu doküman TKG_PDA projesinin tamamını, mimariden tek tek kaynak dosyalara kadar açıklayan teknik referanstır. Amacı, projeyi hiç görmemiş bir yazılım mühendisinin sistemi devralabilmesidir. Özet değildir; her ilgili kaynak dosyanın kendi açıklaması vardır.


### 1.1 Dokümante edilen sürüm

| Alan | Değer |
|---|---|
| Branch | agent/rds-runtime-dispatch-fix |
| Commit | e84728badd16c347f0ace2e9f903a58831c022ca |
| Baz alınan main | 08e309f |
| Üretim tarihi | 2026-09-09 |

> **UYARI:** Bu branch `main` ile birleştirilmemiştir. `main` üzerinde bulunmayan üç grup değişiklik içerir: (1) Operator/Admin UI bütünlük çalışması (`fa21ffe`, `63ca4a2`, `7bdb91b`), (2) RDS commissioning runtime çalışması (`52ce250`, `c452e67`), (3) yerel geliştirme yığını temizliği (`e84728b`). Dokümanda anlatılan davranış bu branch'in davranışıdır; `main` bu üç grubu içermez.


### 1.2 Kanıt seviyeleri

Doküman boyunca aşağıdaki etiketler kullanılır. Etiketsiz teknik ifadeler kod okumasına dayanır.

| Etiket | Anlamı |
|---|---|
| CODE-CONFIRMED | İfade, dokümante edilen commit'teki kaynak koddan doğrudan okunmuştur. |
| DATABASE-CONFIRMED | İfade, migration/şema dosyalarından veya çalışan bir veritabanı sorgusundan doğrulanmıştır. |
| DOCUMENTATION-CONFIRMED | İfade, `RDS DÖKÜMANLARI/` altındaki SEER dokümanlarından veya depo içi dokümanlardan doğrulanmıştır. |
| COMMISSIONING VALIDATION REQUIRED | Kaynak koddan kanıtlanamaz; sahada fiziksel devreye alma ile doğrulanmalıdır. |
| UNVERIFIED | Ne kod ne doküman kanıtı bulunamadı; varsayım olarak işaretlenmiştir. |


### 1.3 Doküman nasıl okunmalı

- Sistemi ilk kez devralıyorsanız: bölüm 2-5 (mimari, teknoloji, repo yapısı) ve bölüm 6 (domain modeli) zorunlu başlangıçtır.
- Bir hatayı araştırıyorsanız: bölüm 30 (Troubleshooting) ve bölüm 29 (hata kodları) karar ağaçları içerir.
- Belirli bir dosyayı arıyorsanız: bölüm 27 dosya-dosya referans, bölüm 32 tam dosya indeksidir.
- RDS/robot güvenliği ile ilgileniyorsanız: bölüm 11 ve 18-21 birlikte okunmalıdır.


## 2 Proje Özeti

TKG_PDA, bir üretim tesisinde operatörlerin el terminali (PDA) üzerinden kasa (konteyner) lojistiği talebi oluşturmasını ve bu taleplerin SEER RDS robot filosu tarafından fiziksel olarak yerine getirilmesini sağlayan uçtan uca bir sistemdir.

Sistem üç kullanıcı yüzeyi sunar: sahada operatörün kullandığı Flutter tabanlı **Operator PDA**, konfigürasyonu yöneten React tabanlı **Admin Web** ve bunların üzerinde çalıştığı ASP.NET Core **API**. Kalıcı durum PostgreSQL'de tutulur, fiziksel hareket ise RDS adaptörü üzerinden RDSCore'a devredilir.


### 2.1 Sistemin çözdüğü problem

Operatör, hangi fiziksel rafın hangi robot noktasına karşılık geldiğini bilmek zorunda kalmadan iş yapabilmelidir. Operatör sadece iş niyetini ifade eder: *hangi makine*, *hangi parça*, *hangi mantıksal kasa numarası*, *hangi işlem*. Fiziksel yönlendirmeyi (kaynak/hedef slot seçimi, WorkSite eşlemesi, robot seçimi) sunucu yapar. Bu ayrım dokümanın tamamında tekrar eden en önemli tasarım kararıdır.


### 2.2 Temel tasarım ilkeleri

1. **Tek çalışma yolu.** TKG'de simülasyon ve gerçek robot için ayrı iş mantığı yoktur. RDS'in simülasyonu mu yoksa fiziksel robotu mu sürdüğü TKG'yi ilgilendirmez.
2. **Envanter gerçeği TKG'ye aittir.** Slot doluluk/yük durumunun sahibi TKG'dir; RDS telemetrisi bu gerçeği ezemez.
3. **Fiziksel mutasyon varsayılan olarak kapalıdır.** `Rds:DispatchEnabled` varsayılanı `false`'tur ve yalnız çalışma ortamından açılır.
4. **Fail-closed.** Kanıt eksik, belirsiz veya çelişkiliyse sistem hareketi reddeder; tahmin etmez.
5. **Operatör iş sözleşmesi teknik değildir.** Ham RDS tanımlayıcıları (task id, WorkSite, AGV id, uuid) operatör yüzeyine hiç serialize edilmez.


## 3 Sistem Mimarisi

Katmanlar tek yönlü bağımlıdır: istemciler yalnız API ile, API Application/Domain ile, Application ise PostgreSQL ve RDS adaptörü ile konuşur. İstemciler hiçbir koşulda RDS ile doğrudan haberleşmez.

```text
   Flutter Operator PDA                 React Admin Web
   (src/TkgPda.Pda)                     (src/TkgPda.AdminWeb)
            |  HTTPS/JSON + JWT + X-Device-Code       |  HTTPS/JSON + JWT
            +--------------------+--------------------+
                                 |
                                 v
                    ASP.NET Core API  (src/TkgPda.Api)
                    ApiEndpoints / AdminEndpoints / RdsIntegrationEndpoints
                                 |
                                 v
                Application  (src/TkgPda.Application)
                RdsExecutionCoordinator, PdaOperationalContextPolicy,
                ContainerActionService, RdsMutationPolicy
                                 |
                    +------------+-------------------------+
                    |                                      |
                    v                                      v
      PostgreSQL (Infrastructure.Postgres)      RDS Adapter (Api katmanı)
      master / ops / integration /              RdsReadClient (8080)
      auth / audit şemaları                     RdsCoreNativeOrderClient (8088)
                                                           |
                                                           v
                                                    RDSCore  (SEER)
                                                           |
                                                           v
                                        RDS Simulation  veya  Fiziksel robot
```


### 3.1 Simülasyon ve gerçek robot ayrımı

TKG kaynak kodunda `SimulationMode`, `IsSimulation`, `SimulationDispatch` gibi hiçbir kavram yoktur; üretim kaynağında yapılan arama sıfır sonuç verir. Bunun nedeni mimari bir karardır: RDS zaten kendi içinde simülasyon ile fiziksel donanım arasında geçiş yapar. TKG'nin bu farkı bilmesi hem gereksiz hem de tehlikelidir, çünkü "simülasyondayız" varsayımı gerçek robotu hareket ettiren bir yolda yanlışsa sonucu fizikseldir.

> **KANIT:** CODE-CONFIRMED — `src/` altındaki tüm C#, Dart ve TypeScript üretim kaynağında simülasyon moduna dair üretim davranışı değiştiren hiçbir sembol bulunmamaktadır. Test sahteleri (fake/mock) bu kuralın dışındadır ve tests/ altında kalır.


### 3.2 İki ayrı anahtar: Rds.Enabled ve Rds.DispatchEnabled

Bu ikisi sık karıştırılır ama farklı sorulara cevap verir. Ayrı olmalarının nedeni, telemetri okumanın güvenli, hareket başlatmanın ise fiziksel sonuç doğuran bir eylem olmasıdır.

| Anahtar | Sorduğu soru | Varsayılan | Kapalıyken ne olur |
|---|---|---|---|
| Rds.Enabled | RDS entegrasyonu hiç aktif mi? | true | Ne okuma ne yazma yapılır; worker yalnız reconcile için ayakta kalmaz. |
| Rds.DispatchEnabled | TKG şu anda RDS'e hareket mutasyonu göndermeye yetkili mi? | false | Telemetri/okuma ve güvenlik durdurmaları çalışır; hareket başlatan mutasyonlar reddedilir. |

> **UYARI:** `DispatchEnabled` bir simülasyon bayrağı DEĞİLDİR. `localhost` veya `127.0.0.1` olması da hareketi otomatik olarak güvenli kılmaz: RDSCore aynı makinede çalışıp gerçek bir robotu sürüyor olabilir. Bu yüzden kod hiçbir yerde adrese bakarak mutasyon izni vermez.


### 3.3 Katman sorumlulukları

| Katman | Sorumluluk | Sorumlu olmadığı |
|---|---|---|
| TkgPda.Domain | Değişmez iş kuralları, durum makinesi, slot durum geçişleri | I/O, veritabanı, HTTP |
| TkgPda.Application | İş akışı orkestrasyonu, RDS yürütme koordinasyonu, projeksiyonlar | SQL cümleleri, HTTP istemcisi |
| TkgPda.Infrastructure.Postgres | SQL, işlem sınırları, advisory lock, idempotency | İş kuralı kararı |
| TkgPda.Api | HTTP yüzeyi, kimlik/yetki, RDS HTTP adaptörleri, konfigürasyon | İş kuralı kararı |
| TkgPda.Pda (Flutter) | Operatör iş yüzeyi | Fiziksel yönlendirme kararı |
| TkgPda.AdminWeb (React) | Konfigürasyon ve teknik teşhis yüzeyi | Operatör iş akışı |


## 4 Teknoloji Yığını

| Bileşen | Teknoloji | Kaynak |
|---|---|---|
| API | .NET 10 / ASP.NET Core Minimal API | src/TkgPda.Api/TkgPda.Api.csproj |
| Uygulama/Domain | C# 13, net10.0 | src/TkgPda.Application, src/TkgPda.Domain |
| Veritabanı erişimi | Npgsql (ham SQL, EF Core yok) | src/TkgPda.Infrastructure.Postgres |
| Veritabanı | PostgreSQL 18 | db/migrations |
| Operatör istemcisi | Flutter / Dart | src/TkgPda.Pda/pubspec.yaml |
| Admin istemcisi | React + TypeScript + Vite | src/TkgPda.AdminWeb/package.json |
| Admin state | TanStack Query | src/TkgPda.AdminWeb/src/api |
| Test | xUnit, flutter_test, Vitest, Playwright | tests/, src/TkgPda.Pda/test, src/TkgPda.AdminWeb |
| Robot kontrol | SEER RDS / RDSCore HTTP API | RDS DÖKÜMANLARI/ (untracked referans) |

> **NOT:** EF Core bilinçli olarak kullanılmaz. Şemanın tek gerçek kaynağı `db/migrations` altındaki SQL dosyalarıdır; ORM migration üretimi projede yasaktır. Bunun nedeni, şemanın müşteri veri modeline ve commissioning kısıtlarına göre elle tasarlanmış olmasıdır.


## 5 Repository Yapısı

| Dizin | İçerik |
|---|---|
| src/TkgPda.Api | HTTP yüzeyi, kimlik doğrulama, RDS HTTP adaptörleri, konfigürasyon bağlama |
| src/TkgPda.Application | İş akışı servisleri, RDS yürütme koordinatörü, politikalar, projeksiyonlar |
| src/TkgPda.Domain | Saf iş kuralları: durum makinesi, transport kuralları, enum'lar |
| src/TkgPda.Infrastructure.Postgres | Npgsql tabanlı store implementasyonları |
| src/TkgPda.Pda | Flutter operatör uygulaması (lib/, test/, integration_test/) |
| src/TkgPda.AdminWeb | React admin uygulaması (src/, e2e/) |
| db/migrations | Şemanın tek gerçek kaynağı; sıralı SQL migration'ları |
| db/seeds/generated | Müşteri kaynak verisinden üretilmiş seed SQL |
| tests | xUnit test projeleri (Domain, Application, Auth, Api, Infrastructure.Postgres) |
| tools | Geliştirme ve veri dönüştürme araçları |
| scripts | Commissioning çalıştırma betiği |
| docs | Proje dokümantasyonu (bu doküman dahil) |
| data/generated | Müşteri kaynak verisinden üretilmiş ara çıktılar |
| RDS DÖKÜMANLARI/ | SEER satıcı dokümanları — git tarafından takip EDİLMEZ, değiştirilmez |


## 6 Domain Modeli ve Değişmez İş Kuralları

Domain katmanı projenin donmuş (frozen) kurallarını barındırır. Bu kurallar müşteri ile mutabık kalınmıştır ve teknik kolaylık için değiştirilemez.


### 6.1 Occupancy ve Load: iki ayrı eksen

Bir slotun durumu tek bir değerle değil, iki bağımsız eksenle ifade edilir. Bu ayrım kritik olduğu için veritabanı seviyesinde CHECK kısıtı ile korunur.

| Eksen | Değerler | Anlamı |
|---|---|---|
| OccupancyState | VACANT, OCCUPIED, UNKNOWN | Slotta fiziksel bir kasa var mı? |
| LoadState | EMPTY, FULL, UNKNOWN | Varsa, o kasanın içinde ürün var mı? |

> **UYARI:** RESERVED asla bir occupancy değeri değildir. Bir slotun aktif taşıma talebi tarafından rezerve edilmiş olması, fiziksel doluluğunu değiştirmez. Rezervasyon `ops.transport_requests` üzerinden türetilir; `ops.slot_runtime_state` üzerinde bir REZERVE durumu yoktur. CODE-CONFIRMED: `TransportStateMachine.IsReservationActive`.

Veritabanı `ck_slot_load_requires_occupancy` kısıtı ile mantıksız kombinasyonları engeller: dolu olmayan bir slot EMPTY/FULL yük durumu taşıyamaz. DATABASE-CONFIRMED — `db/migrations/001_initial_schema.sql`.


### 6.2 Taşıma operasyonları

| Operasyon | Anlamı | Operatör seçebilir mi? |
|---|---|---|
| BRING_EMPTY | Boş kasa getir (kaynak: EMPTY_STORAGE tampon, hedef: makine slotu) | Evet — "Kasa Boş" |
| REMOVE_FULL | Dolu kasayı kaldır (kaynak: makine slotu) | Evet — "Kasa Doldu" |
| REMOVE_EMPTY | Boş kasayı kaldır | HAYIR — yalnız iç orkestrasyon |

> **UYARI:** REMOVE_EMPTY bir operatör aksiyonu DEĞİLDİR. Domain'de vardır ve "Tüm Kasaları Kaldır" akışı OCCUPIED+EMPTY pozisyonlar için bunu sunucu tarafında üretir; ancak genel taşıma oluşturma ucu bunu her rol için reddeder. CODE-CONFIRMED: `ApiEndpoints.EnsureTransportOperationAllowed` → `OPERATION_NOT_OPERATOR_SELECTABLE` (HTTP 422).


### 6.3 WP-33 operatör akış kuralları

WP-33, operatörün gördüğü mantıksal kasa aksiyonlarını tanımlar. Operatör fiziksel slot seçmez; mantıksal kasa numarası (1. Kasa, 2. Kasa ...) seçer.


#### 6.3.1 "N. Kasa Doldu" (REMOVE_FULL semantiği)

| Slot durumu | Sonuç |
|---|---|
| OCCUPIED + EMPTY | Yük durumu atomik olarak EMPTY→FULL yapılır ve REMOVE_FULL talebi oluşturulur |
| OCCUPIED + FULL | Zaten dolu; REMOVE_FULL idempotent olarak oluşturulur |
| VACANT | Reddedilir — `OCCUPIED_CONTAINER_REQUIRED` |
| UNKNOWN (herhangi bir eksende) | Reddedilir — `BUSINESS_RULE_NOT_CONFIGURED` |


#### 6.3.2 "N. Kasa Boş" (BRING_EMPTY semantiği)

VACANT + UNKNOWN durumundaki bir mantıksal pozisyon için boş kasa getirtir. Bu aksiyon **REMOVE_EMPTY anlamına gelmez**; isim benzerliği yanıltıcıdır. Kaynak, `EMPTY_STORAGE` rolündeki tampon slottan `FOR UPDATE ... SKIP LOCKED` ile tahsis edilir.


#### 6.3.3 "Tüm Kasaları Kaldır" (Remove All)

Kapsamı **varyant/layout**tır, seçili parça değil. Her mantıksal pozisyon için durumuna göre operasyon türetilir ve tek bir batch içinde gönderilir.

| Pozisyon durumu | Türetilen operasyon |
|---|---|
| OCCUPIED + FULL | REMOVE_FULL |
| OCCUPIED + EMPTY | REMOVE_EMPTY (yalnız burada, sunucu tarafından üretilir) |
| VACANT + UNKNOWN | Atlanır (null döner) |
| UNKNOWN | Tüm batch reddedilir — `REMOVE_ALL_POSITION_UNKNOWN` |
| Çelişkili durum | Tüm batch reddedilir — `REMOVE_ALL_POSITION_CONFLICT` |

> **KANIT:** CODE-CONFIRMED — `src/TkgPda.Application/ContainerActions.cs`, `ContainerActionPolicy.ResolveRemoveAll`.


### 6.4 Envanter gerçeğinin sahipliği

RDS telemetrisi TKG envanterini güncellemez. RDS bir WorkSite için `filled` bilgisi döndürse bile bu bilgi `ops.slot_runtime_state` üzerine yazılmaz. Bunun nedeni, RDS'in kendi WorkSite modelinin TKG'nin iş modeliyle birebir örtüşmemesi ve robotun görmediği manuel operatör müdahalelerinin TKG tarafında bilinmesidir. RDS verisi yalnız yürütme hazırlığı (readiness) değerlendirmesinde kullanılır.


## 7 Transport Request Yaşam Döngüsü

> **UYARI:** Yaygın bir yanılgı: `WAITING_PREPARATION` ve `READY` birer *durum* değildir. Bunlar ayrı ve dik (orthogonal) bir eksendir. CODE-CONFIRMED: `src/TkgPda.Domain/WorkflowEnums.cs`.


### 7.1 İki eksen

| Eksen | Tip | Değerler |
|---|---|---|
| Durum | TransportRequestStatus | Created, Validating, Queued, SentToRds, Running, Completed, Failed, Cancelled |
| Hazırlık | TransportReadinessState | NotRequired, WaitingPreparation, Ready |


### 7.2 Durum geçiş diyagramı

```text
  Created ──► Validating ──► Queued ──► SentToRds ──► Running ──► Completed
     │            │            │            │            │
     │            ├──► Failed  │            ├──► Failed  ├──► Failed
     │            │            │            │            │
     └──► Cancelled ◄─────────-┴────────────┴────────────┘

  Terminal durumlar: Completed, Failed, Cancelled  (çıkış geçişi yoktur)
```

Geçişler `TransportStateMachine.LegalTransitions` sözlüğünde tanımlıdır ve `EnsureTransition` dışında hiçbir yol durum değiştiremez; illegal geçiş `ILLEGAL_STATUS_TRANSITION` ile reddedilir. CODE-CONFIRMED: `src/TkgPda.Domain/TransportStateMachine.cs`.


### 7.3 Durumların anlamı

| Durum | Nasıl girilir | Kim değiştirir | Etki |
|---|---|---|---|
| Created | Operatör talebi oluşturur | Workflow store | Rezervasyon aktif; slot meşgul sayılır |
| Validating | İş akışı doğrulama başlar | Workflow store | Rezervasyon aktif |
| Queued | Doğrulama başarılı; dispatch bekliyor | Workflow store | Worker'ın topladığı havuz |
| SentToRds | Native order RDS'e gönderildi | RdsExecutionCoordinator | integration.rds_tasks kaydı oluşur |
| Running | RDS siparişi çalışıyor | Reconciliation / callback | Robot fiziksel hareket halinde |
| Completed | RDS FINISHED bildirdi | Reconciliation / callback | Slot durumları uygulanır |
| Failed | Doğrulama veya yürütme başarısız | Workflow / reconciliation | Slot durumu korunur veya geri alınır |
| Cancelled | Operatör iptal etti | CancelAsync | Yürütme başladıysa /terminate ile durdurulur |


### 7.4 İptal kuralları

`CanCancelBeforeExecution` yalnız Created, Validating ve Queued için true döner. Bu, RDS'e hiç gidilmemiş talepler için yerel iptaldir. SentToRds ve Running durumundaki talepler de iptal edilebilir, ancak bu yol native `/terminate` çağrısı gerektirir ve `CancelAsync` içinde ayrı ele alınır.

> **NOT:** Operatör kuyruğu (`/transport-requests/queue`) `CanCancel` alanını `CanCancelBeforeExecution` üzerinden hesaplar. Bu, koordinatörün gerçek yeteneğinden daha dardır: RUNNING bir talep kuyrukta iptal edilemez görünür ama `/operational-context` üzerinden iptal edilebilir. Bu tutarsızlık bilinçli değil, tespit edilmiş bir farktır.


### 7.5 Idempotency

Her taşıma talebi bir `Idempotency-Key` başlığı ile oluşturulur. Aynı anahtarla yapılan tekrar istekleri yeni kayıt üretmez; mevcut kaydı döndürür ve `IdempotentReplay` bayrağını set eder. Bu, ağ belirsizliğinde (timeout, bağlantı kopması) çift fiziksel hareketi engelleyen birincil mekanizmadır. Veritabanında `uq_transport_idempotency` benzersiz indeksi ile zorlanır. DATABASE-CONFIRMED.


## 8 Container Action Pipeline

Operatörün bir kasa butonuna basmasından RDS'e sipariş gitmesine kadar geçen yol tek bir hattır. Bu bölüm o hattın her durağını sırayla anlatır.

1. **Flutter — `_BusinessActionButton`.** `direct-action-REMOVE_FULL-{n}` veya `direct-action-BRING_EMPTY-{n}` anahtarlı buton `controller.submitContainerAction(position, action)` çağırır. Onay diyaloğu YOKTUR; WP-33 doğrudan gönderim ister.
2. **Flutter — `AppController.submitContainerAction`.** İstek gövdesine mantıksal pozisyon numarası ve aksiyon konur; fiziksel slot kimliği istemcide hiç bulunmaz. Idempotency anahtarı burada üretilir.
3. **API — `ApiEndpoints`.** Kimlik, cihaz ve rol doğrulanır. `EnsureTransportOperationAllowed` operatörün seçemeyeceği operasyonları burada eler.
4. **Application — `ContainerActionPolicy`.** Slotun mevcut occupancy/load durumundan hangi domain operasyonunun üretileceğine karar verir; uygun değilse iş kuralı hatası fırlatır.
5. **Infrastructure — `PostgresTransportWorkflowStore.ContainerActions`.** Tek bir transaction içinde: idempotency advisory lock alınır, gerekiyorsa yük durumu EMPTY→FULL güncellenir, `ops.transport_requests` satırı yazılır, olay `ops.transport_request_events` tablosuna işlenir.
6. **Worker — `RdsExecutionCoordinator.DispatchQueuedAsync`.** Kuyruktaki talepler periyodik olarak toplanır (`Rds:WorkerIntervalSeconds`, varsayılan 3 sn).
7. **Coordinator — `DispatchNativeOrderAsync`.** Navigation profile çözümlenir, robot kirası alınır, telemetri doğrulanır, gerekiyorsa harita hazırlığı yapılır, `/setOrder` gönderilir.
8. **Reconciliation — `ReconcileAsync`.** RDS'teki sipariş durumu periyodik okunur ve TKG durumu buna göre ilerletilir.

> **NOT:** Idempotency anahtarı zincirin en başında üretilir ve sonuna kadar taşınır. Operatör aynı butona iki kez bassa bile ikinci istek aynı anahtarla gelir ve yeni bir fiziksel hareket doğurmaz.


## 9 RDS Entegrasyon Mimarisi

RDS entegrasyonu tek bir istemci değil, farklı sözleşmeleri olan dört adaptörden oluşur. Her biri ayrı bir taban adres ve ayrı bir sorumluluk taşır.

| Adaptör | Dosya | Taban adres | Sorumluluk |
|---|---|---|---|
| RdsReadClient | src/TkgPda.Api/RdsReadClient.cs | Rds:BaseUrl (varsayılan :8080) | WorkSite/WindTask okuma, salt-okunur telemetri |
| RdsExecutionClient | src/TkgPda.Api/RdsExecutionClient.cs | Rds:BaseUrl | WindTask tabanlı yürütme motoru |
| RdsCoreNativeOrderClient | src/TkgPda.Api/RdsCoreNativeOrderClient.cs | RdsCore:BaseUrl (varsayılan :8088) | Native order motoru: /setOrder, /terminate, /robotsStatus, /lock, /switchMap, /unlock |
| RdsCommissioningDiscoveryClient | src/TkgPda.Api/RdsCommissioningDiscoveryClient.cs | RdsCore:BaseUrl | Devreye alma keşfi: park noktaları, mesafe sorgusu, çekirdek parametreler |


### 9.1 İzin verilen RDSCore operasyonları

`RdsCoreNativeOrderClient` içinde her operasyon bir kayıt tablosunda (`Registry`) HTTP metodu, yolu ve mutasyon olup olmadığıyla birlikte tanımlıdır. Kayıtlı olmayan hiçbir yol çağrılamaz; bu, denemelik bir RDSCore ucunun koda sızmasını engelleyen yapısal bir kısıttır.

| Operasyon | Metot | Yol | Mutasyon |
|---|---|---|---|
| SetOrder | POST | /setOrder | Evet |
| OrderDetails | GET | /orderDetails/{id} | Hayır |
| Orders | GET | /orders | Hayır |
| OrderDetailsByExternalId | GET | /orderDetailsByExternalId/{externalId} | Hayır |
| OrderDetailsByBlockId | GET | /orderDetailsByBlockId/{blockId} | Hayır |
| BlockDetailsById | GET | /blockDetailsById/{blockId} | Hayır |
| Terminate | POST | /terminate | Evet |
| SetPriority | POST | /setPriority | Evet |
| ClearRobotAllError | POST | /clearRobotAllError | Evet |
| GotoSitePause | POST | /gotoSitePause | Evet |
| GotoSiteResume | POST | /gotoSiteResume | Evet |
| ManualFinished | POST | /manualFinished | Evet |
| RedoFailedOrder | POST | /redoFailedOrder | Evet |
| Dispatchable | POST | /dispatchable | Evet |
| RobotsStatus | GET | /robotsStatus | Hayır |
| Lock | POST | /lock | Evet |
| Unlock | POST | /unlock | Evet |
| SwitchMap | POST | /switchMap | Evet |

> **KANIT:** CODE-CONFIRMED — `src/TkgPda.Api/RdsCoreNativeOrderClient.cs`, `Registry` sözlüğü.


### 9.2 Commissioning keşif uçları

Devreye alma sırasında sahanın gerçek geometrisini okumak için kullanılan salt-okunur uçlar: `/binCheck`, `/devicesDetails`, `/getRobotParkPoints`, `/getChargeParam`, `/getDisablePoints`, `/getDisablePaths`, `/getCoreParam`, `/queryConnectivity`, `/queryDist`. Bunların hiçbiri hareket doğurmaz.

> **UYARI:** COMMISSIONING VALIDATION REQUIRED — Bu uçlardan dönen park noktası, mesafe ve çekirdek parametre değerleri sahaya özgüdür. Depoda bulunan hiçbir örnek değer üretim haritasının gerçeği değildir.


## 10 Mutasyon Politikası ve Fiziksel Hareket Kapısı

`RdsMutationPolicy` projedeki en güvenlik-kritik tek dosyadır. Bir RDS operasyonunun fiziksel işe göre sınıfını tanımlar ve global dispatch kapısının ne anlama geldiğinin tek tanımıdır.


### 10.1 Dört mutasyon sınıfı

| Sınıf | Tanım | Kapı kapalıyken |
|---|---|---|
| ReadOnly | Yalnız okuma. Hiç kapılanmaz. | İzinli |
| SafetyStop | Zaten süren işi durdurur veya duraklatır: `/terminate`, `/gotoSitePause`, robotu dispatch dışı bırakma. | İzinli — hareketi azaltır |
| MotionEnabling | Fiziksel işi başlatır, sürdürür, yeniden etkinleştirir veya ilerletir: `/gotoSiteResume`, `dispatchable=true`, `/clearRobotAllError`, `/manualFinished`, `/redoFailedOrder`. | Reddedilir |
| ExecutionMutation | Yürütme kimliğini veya sırasını değiştirir: `/setOrder`, WindTask oluşturma, `/setPriority`, ve dispatch öncesi `/lock` + `/switchMap` + `/unlock` hazırlığı. | Reddedilir |

```csharp
public static bool IsAllowed(RdsMutationClass mutationClass, bool globalDispatchEnabled) =>
    mutationClass switch
    {
        RdsMutationClass.ReadOnly => true,
        RdsMutationClass.SafetyStop => true,
        _ => globalDispatchEnabled
    };
```


### 10.2 Neden `/clearRobotAllError` MotionEnabling sayılır?

Adı bir temizleme işlemi gibi görünse de, robotu tutan hatayı kaldırmak robotu yeniden hareket edebilir hale getirir. Politika, operasyonun adına değil fiziksel sonucuna bakar. Aynı mantıkla `/manualFinished` bir sonraki bloğa ilerlettiği, `/redoFailedOrder` bir siparişi yeniden çalıştırdığı için MotionEnabling'dir.


### 10.3 `/dispatchable` sınıflaması dinamiktir

```csharp
public static RdsMutationClass ClassifyDispatchable(string? type) =>
    (type ?? string.Empty).Trim().ToLowerInvariant() == "dispatchable"
        ? RdsMutationClass.MotionEnabling
        : RdsMutationClass.SafetyStop;
```

Aynı uç, gövdesindeki `type` alanına göre farklı sınıfa düşer: robotu dispatch edilebilir yapmak hareketi mümkün kılar, dispatch dışına almak ise güvenliği artırır.


### 10.4 Rol yetkisi kapıyı ikame etmez

> **UYARI:** Bir operasyonun yalnız Admin/Supervisor'a açık olması onu izinli kılmaz. `EnsureMutationAllowed` her mutasyon yolunda ayrıca çağrılır ve `RDS_DISPATCH_DISABLED` ile reddeder. Kaynak kodundaki yorum bunu açıkça söyler: *Being Admin/Supervisor-only does not by itself make an operation permissible.*


### 10.5 Dispatch uygunluğu bileşimi

```csharp
private bool IsGlobalDispatchAllowed => settings.DispatchEnabled;

private bool IsNativeMode => nativeOrderClient is not null
    && nativeOrderClient.Mode == RdsExecutionMode.RdsCoreNativeOrder
    && nativeOrderClient.MutationEnabled;

public bool IsDispatchEnabled => IsGlobalDispatchAllowed
    && (executionClient.DispatchEnabled || IsNativeMode);
```

Native motorun etkin olması global kapıyı asla atlayamaz: bileşim `AND`'dir. Motor seçimi ile fiziksel izin ayrı sorulardır.


## 11 Robot Kimliği ve Alias Çözümleme

SEER `/robotsStatus` üzerinde iki ayrı kimlik uzayı yayınlar ve bu ayrım geçmişte gerçek bir hataya yol açmıştır.

| Alan | Anlamı | Hangi API'ler sözleşir |
|---|---|---|
| uuid | Robotun iç kimliği | /getRobotParkPoints, /queryDist (uuid anahtarlı harita döner) |
| vehicle_id | Dispatch tanımlayıcısı | /setOrder (vehicle), /lock, /unlock, /switchMap, /manualFinished, /redoFailedOrder, /dispatchable |

> **UYARI:** İki değer yerel simülatörde çoğunlukla aynı, gerçek donanımda çoğunlukla farklıdır. Bu yüzden hiçbiri evrensel olarak kanonik sayılamaz. Bu tip var olmadan önce iki okuyucu birbiriyle çelişiyordu — biri `uuid ?? vehicle_id`, diğeri `vehicle_id ?? uuid` tercih ediyordu; sonuç, kilit korelasyonunun kimlik uzayları arasında karşılaştırma yapması ve gerçek robotta sessizce başarısız olurken simülatörde geçmesiydi.


### 11.1 RdsRobotIdentity türetilmiş özellikleri

| Özellik | Çözüm sırası | Cevapladığı soru |
|---|---|---|
| MutationTarget | VehicleId ?? Uuid | Bu robotu hedeflerken RDSCore'a ne göndereyim? |
| DiscoveryKey | Uuid ?? VehicleId | Keşif uçları hangi kimliği bekliyor? |
| RobotLeaseKey | VehicleId ?? Uuid | Bu robotu hangi dağıtık kira kapsıyor? |
| HasValue | MutationTarget is not null | Hedeflenebilir mi? |

`RobotLeaseKey` bugün `MutationTarget` ile aynı şekilde çözülür ama bilinçli olarak ayrı bir özelliktir. İlk ikisi *bu SEER ucu ne istiyor?* sorusuna, üçüncüsü *hangi kira bu robotu kapsıyor?* sorusuna cevap verir. Ayrı tutulmaları, gelecekte bir uç sözleşmesi değişirse kira alanının sessizce ikiye bölünmesini imkânsız kılar.

Tek sert gereksinim şudur: aynı robotun iki alias'ı asla iki farklı kira üretmemelidir. Bu yüzden her çağıran kimliği önce telemetriden çözer (`ResolveRobotLeaseKeyAsync`); yalnız uuid'e sahip bir çağıran ile yalnız vehicle_id'ye sahip bir çağıran aynı değere varır.


### 11.2 Matches() — iki uzayda birden eşleme

Korelasyon girdileri (denetim kilit parmak izleri, görev şablonu `vehicle` değerleri, navigasyon profilleri) her iki uzayda da kaydedilmiş olabilir. `Matches` ikisinden birinde eşleşmeyi kabul eder, hiçbirinde eşleşmemeyi reddeder ve boş bir adaya asla genişlemez.


### 11.3 Alias çözümleme: NoMatch / Unique / Ambiguous

Bir alias iki uzayda da aranınca çakışma mümkün olur. Bu çakışma eskiden çağıranlar tarafından `FirstOrDefault` ile çözülüyordu — yani RDSCore hangi robotu önce listelediyse o hedefleniyordu. Hareket doğuran bir mutasyon için bu, mümkün olan en kötü hata biçimidir.

| Senaryo | Fiziksel makine sayısı | Sonuç |
|---|---|---|
| Tek robot, uuid == vehicle_id == "X" | 1 | Unique |
| Robot A uuid "X", Robot B vehicle_id "X" | 2 | Ambiguous — fail closed |
| Aynı robot payload'da iki kez | 1 | Unique |
| Hiçbir robot eşleşmiyor | 0 | NoMatch |

Ayrıklık tek bir çözülmüş string üzerinden değil, **tüm kimlik çifti** üzerinden hesaplanır. Yalnız lease key kullanmak `(uuid=X, vehicle=null)` ile `(uuid=null, vehicle=X)` çiftlerini aynı değere indirger ve tam da yakalanmak istenen belirsizliği gizlerdi.

```csharp
var distinctMachines = matches
    .Select(robot => (
        Uuid: Normalize(robot.ResolvedIdentity.Uuid),
        Vehicle: Normalize(robot.ResolvedIdentity.VehicleId)))
    .Distinct()
    .Count();

return distinctMachines == 1 ? RdsRobotMatch.Unique(matches[0]) : RdsRobotMatch.Ambiguous;
```

Belirsizlik dışarıya `ROBOT_ALIAS_AMBIGUOUS` hata kodu ile verilir.


## 12 Robot Kirası, Serileştirme ve TOCTOU

TKG aynı fiziksel robot üzerinde eşzamanlı iki operasyon yürütmeyi engellemek zorundadır. Talep başına kira bunun için yeterli değildir: iki farklı TransportRequest kendi talep kirasını tutup yine de aynı robota çözülebilir.


### 12.1 İki ayrı kira alanı

| Kira | Anahtar biçimi | Kapsam |
|---|---|---|
| Talep kirası | tkg:rds:{requestId:D} | Bir TransportRequest |
| Robot kirası | tkg:robot:{robotLeaseKey} (küçük harfe indirgenmiş) | Bir fiziksel robot |

Ad alanı öneki anahtar dizesinin kendi içinde taşınır; bu sayede bir robot anahtarı bir talep anahtarı ile çarpışamaz — ikisi aynı hash uzayını paylaşsa bile.


### 12.2 Neden session düzeyinde advisory lock?

```csharp
// Dedicated connection, SESSION-level advisory lock
"SELECT pg_try_advisory_lock(hashtextextended(@key, 0))"
command.Parameters.AddWithValue("key", $"tkg:robot:{robotLeaseKey.Trim().ToLowerInvariant()}");
```

İşlem düzeyinde (`pg_advisory_xact_lock`) bir kilit commit anında serbest bırakılır — yani fiziksel mutasyon henüz gönderilmeden önce. Bu yüzden robot kirası kendi ayrılmış bağlantısında session düzeyinde tutulur ve `await using` bloğunun örtük `finally`'sinde bırakılır: başarı, adaptör istisnası ve iptal yollarının hepsinde.

> **NOT:** Advisory lock PostgreSQL üzerinde tutulduğu için serileştirme tek bir işletim sistemi süreci içinde değil, TKG süreçleri arasında geçerlidir. Birden fazla API örneği çalıştığında da doğru kalır.


### 12.3 TOCTOU: EnsureLeaseCoversTarget

Kira, yetkili (authoritative) yeniden okumadan **önce** bir aday alias'tan alınır. Bu yüzden yeniden okuma meşru olarak farklı bir fiziksel robota inebilir: RDSCore arada siparişi başka robota atamış olabilir. Başka bir robotun kirasını tutarken bu robotu mutasyona uğratmak kiranın tüm amacını yok ederdi.

```csharp
private static void EnsureLeaseCoversTarget(string heldLeaseKey, RdsCoreRobotTelemetry target, string operation)
{
    var targetKey = target.ResolvedIdentity.RobotLeaseKey;
    if (!string.Equals(heldLeaseKey, targetKey, StringComparison.OrdinalIgnoreCase))
    {
        throw new RdsExecutionException(
            "EXPERT_ROBOT_CHANGED_DURING_VALIDATION",
            $"The robot for this request changed from '{heldLeaseKey}' to '{targetKey}' ...");
    }
}
```

> **UYARI:** Kira ortada değiştirilmez. Yeni hedefin kirasına geçmek, kiranın kapatmak için var olduğu pencerenin ta kendisini yeniden açardı. Doğru davranış: reddet, çağıran taze durumdan tekrar denesin.

Dispatch yolunda aynı değişmez `ROBOT_CHANGED_DURING_VALIDATION` koduyla, uzman (expert) yollarında `EXPERT_ROBOT_CHANGED_DURING_VALIDATION` koduyla uygulanır.


### 12.4 Kira alınamazsa

`TryAcquireRobotLeaseAsync` null dönerse operasyon `ROBOT_BUSY` ile reddedilir. Beklemez, kuyruğa girmez, zorlamaz. Dispatch yolunda bu `RDS_DISPATCH_DEFERRED` olarak denetlenir ve talep kuyrukta kalır.


### 12.5 Kimlik çözülemezse

`ResolveRobotLeaseKeyAsync` null dönerse — telemetri okunamadıysa veya alias belirsizse — güvenlik-kritik mutasyon için bu "kilit gerekmiyor" anlamına gelmez; fail-closed davranılır: uzman yollarında `EXPERT_CURRENT_STATE_UNAVAILABLE`, dispatch yolunda `ROBOT_IDENTITY_UNRESOLVED`.


## 13 Alarm Modeli

Alarm ayrıştırma projede tek bir yerde yapılır: `RdsRobotAlarmParser`. Daha önce iki kopya vardı ve native telemetri istemcisindeki kopya `alarms.fatals` ile `rbk_report.alarms.fatals` yollarını okuyordu — bu yollar SEER dokümantasyonunda hiç geçmez, dolayısıyla gerçek bir payload'da hiçbir şeyle eşleşmiyordu. Sonuç: robotlar daima sıfır alarm bildiriyordu ve "robotun bloklayan alarmı var" kapısı hiç çalışamıyordu.


### 13.1 Severity ve bloklama

| Severity | Bucket | Bloklar mı? |
|---|---|---|
| Fatal | fatals | Evet |
| Error | errors | Evet |
| Warning | warnings | Hayır |
| Notice | notices | Hayır |

> **UYARI:** Alarm listesinin boş olmaması bir arıza demek DEĞİLDİR. Karar daima `HasBlockingAlarm` üzerinden verilir; bir notice asla bloklayan bir alarm gibi davranmamalı veya görünmemelidir.


### 13.2 Dört dokümante zarf (envelope)

Bucket adları daima çoğuldur ve hem bir durum nesnesinin doğrudan üzerinde hem de bir `alarms` sarmalayıcısının altında görünür. Tek bir dokümante `/robotsStatus` yanıtı iki formu birden taşır, bu yüzden biri diğerinin lehine atılamaz.

| Zarf | Yol | Kaynak |
|---|---|---|
| Kök, sarmalı | alarms.{fatals,errors,warnings,notices} | SEER DOCX (çıkarım satırı L4052) |
| Kök, doğrudan | {fatals,errors,warnings,notices} — alarms ile kardeş | Aynı yanıt, L4079; RdsScriptMethods_en.md:1040-1058 |
| Robot, sarmalı | report[].rbk_report.alarms.{...} | DOCX L4236, L4427 |
| Robot, doğrudan | report[].rbk_report.{fatals,errors,...} | RdsScriptMethods_en.md:1135-1136, 1166, 1195 |

> **KANIT:** DOCUMENTATION-CONFIRMED — Sarmalı bir bucket JSON null olabilir (DOCX L4443: alarms:{errors:null, fatals:null, notices:null, warnings:null}). Bu yalnızca "alarm yok" demektir ve istisna fırlatmamalıdır.

> **NOT:** Önceki bir inceleme turunda `alarms` sarmalayıcısının dokümante olmadığı sonucuna varılmıştı; bu, `RDS DÖKÜMANLARI/` altındaki Markdown dosyalarında yapılan bir grep'e dayanıyordu. Bu bir arama hatasıydı, kanıt değil: depo kökündeki yetkili DOCX bunu açıkça dokümante eder.


### 13.3 İki giriş şekli

| Şekil | Örnek | Kod nerede |
|---|---|---|
| A — açık kod | { "code": 52201, "desc": "...", "times": 1, "timestamp": ... } | code özelliğinde |
| B — dinamik anahtarlı | { "55001": 1634020734, "desc": "...", "times": 1 } | Özellik ADI'nda; değer oluşum zaman damgası |

> **UYARI:** Şekil B'de `code` özelliği aramak her girdiyi sessizce düşürür. Parser, alarm kodu olmadığı bilinen kardeş alanları (code, desc, description, times, time, timestamp, level, severity) hariç tutarak kalan anahtarı kod olarak alır.


### 13.4 Fleet snapshot: robot alarmları ve kök alarmlar

`GetRobotsStatusAsync` tek bir `RdsCoreFleetStatus(Robots, GlobalAlarms)` döndürür. İkisinin birlikte dönmesi bilinçlidir: parser kök alarmları bir süredir anlıyordu, ancak her iki istemci de ona yalnız daha dar bir nesne veriyordu — native istemci bir `report[]` elemanı, read istemcisi bir `rbk_report` — dolayısıyla dokümante bir çekirdek düzeyi fatal hiçbir çağırana ulaşamıyordu ve dispatch tamamen sağlıklı bir filo görüyordu.

Kök alarmlar RDSCore/sahne düzeyindedir, robot düzeyinde değil: dokümante yanıtta `disable_paths`, `disable_points` ve `dynamic_obstacle` ile yan yana dururlar.

> **UYARI:** Kök alarmlar her robota kopyalanmaz. `desc` metni bir robotu adlandırsa bile ("2000-1, have no charge point in map.") yapısal olarak hiçbir şey onu tek bir robota atfetmez. Kopyalamak, dokümantasyonun yapmadığı bir atıf uydurmak ve tüm filoyu tek bir robotun şarj noktası sorununda mahsur bırakmak olurdu.

İki ayrı kapı vardır: `fleet.GlobalHasBlockingAlarm` → `RDSCORE_HAS_BLOCKING_ALARM` (dispatch reddi), robot düzeyinde `robot.HasBlockingAlarm` → o robota dispatch reddi ve kilit kurtarma uygunsuzluğu.


## 14 Harita Geçişi ve Kilit Kurtarma

Bir taşıma, robotun o an bulunduğundan farklı bir haritada çalışmayı gerektiriyorsa dispatch öncesi harita hazırlığı yapılır: `/lock` → `/switchMap` → `/unlock`. Bu üçlü ExecutionMutation sınıfındadır ve global kapı kapalıyken hiç başlamaz.


### 14.1 Çökme senaryosu

TKG `/lock` ile `/unlock` arasında çökerse robot kilitli kalır. Bir sonraki dispatch denemesi bunu kayıttan görür ve otomatik kurtarma yapıp yapamayacağına karar verir. Otomatik `/unlock`, TKG'nin kilitli bıraktığına inandığı bir robot üzerinde fiziksel bir mutasyondur; korelasyon kilidin **bizim** olduğunu kanıtlar, şu anda serbest bırakmanın **güvenli** olduğunu kanıtlamaz. Sahiplik ve güvenlik ayrı sorulardır ve ikisi de adaptör çağrısından ÖNCE cevaplanmalıdır.

> **NOT:** Daha önce bloklayan-alarm ve uyumsuz-sipariş kontrolleri bu metodun aşağısında duruyordu ve dolayısıyla ancak `/unlock` zaten gönderildikten SONRA çalışıyordu. Şimdi tüm koşullar önce değerlendirilir, adaptör çağrısı en sondadır.


### 14.2 Dokuz koşul (A–I)

| # | Koşul | Kod ifadesi |
|---|---|---|
| A | Bu talep için bir kurtarma kaydı var | hasUnreleasedLock |
| B | Kayıt bu robotu adlandırıyor | recordedVehicleMatches |
| C | Kayıt şu an hazırlanan haritayı hedefliyor | recordedMapMatches |
| D | Robot şu an kendini kilitli bildiriyor | robotReportsLocked |
| E | Canlı kilit parmak izi korele oluyor | CorrelatesWith |
| F | Robot şu an gerekli haritada | currentMapMatches |
| G | Aktif hiçbir sipariş yok | !hasActiveOrder |
| H | Bloklayan (fatal/error) alarm yok | !HasBlockingAlarm |
| I | İş durumu dokümante-boşta | !MayBeExecutingBusinessWork |

A/B/C/E `isLockCorrelated` içinde toplanır. Dokuzunun tamamı sağlanmazsa otomatik kurtarma yapılmaz.

> **NOT:** B ve C kontrolleri savunma derinliğidir: store zaten kaydı araca göre filtreler, ancak dönen kayıt otomatik bir `/unlock` yetkilendirmeden önce MEVCUT hazırlık bağlamına karşı yeniden doğrulanır. Kaydedilen robot paylaşılan kimlik üzerinden eşleştirilir, böylece bir uuid asla bir vehicle_id ile karşılaştırılmaz.


### 14.3 G koşulu: hiçbir aktif sipariş, kendi siparişimiz dahil

Herhangi bir terminal-olmayan sipariş otomatik kurtarmayı bloklar — bu talebin kendi ana siparişi ve kendi MAP_PREP geçiş siparişi dahil. Önceki bir tur bu ikisini "bu talebe ait" gerekçesiyle muaf tutmuştu; ancak sahiplik güvenlik değildir: hâlâ RUNNING veya SUSPENDED olan bir MAP_PREP siparişi, bu kilidin var olma sebebi olan harita hazırlığının hâlâ sürdüğü anlamına gelir ve kilidi altından çekmek tam da güvensiz durumdur. SEER, altından unlock yapmanın kanıtlanabilir şekilde güvenli olduğu hiçbir sipariş durumu dokümante etmez.


### 14.4 I koşulu: procBusiness belirsizliği

> **UYARI:** DOCUMENTATION AMBIGUITY — DOCX alan tablosu `procBusiness` alanını yalnızca "Kullanıcı tarafından gönderilen order'ı yürütüyor mu" olarak tanımlar; `RdsCustomProjectTutorial_en.md:350` boşta olmayı **birleşim** olarak verir: `dispatchable == true && procBusiness == false`. Hiçbiri tek başına `procBusiness == false`'un boşta demek olduğunu söylemez ve birleşim kilit kurtarma sırasında yazıldığı gibi kullanılamaz, çünkü kilit tutmanın kendisi `dispatchable`'ın false olması için bir sebeptir. SEER, çağıranın kendi kilidini kurtarması için hiçbir yüklem dokümante etmez.

```csharp
/// True for procBusiness == true and also for null/absent, so an unknown
/// business state fails closed.
public bool MayBeExecutingBusinessWork => ProcBusiness != false;
```

Özellik bilerek kanıtlayabildiği şeyle adlandırılmıştır ve yalnız bir **gerekli negatif koşul** olarak kullanılır; hiçbir zaman boşta olmanın yeterli kanıtı sayılmaz. Hiçbir şeyin çalışmadığına dair gerçek kanıt yanındaki sipariş-durum kontrolünden gelir.


### 14.5 Kilit parmak izi korelasyonu

`CorrelatesWith`, iki `lock_info` yapısının aynı kilidi tanımlayıp tanımlamadığına karar verir. Kural üç parçalıdır:

1. Her iki taraf da kilitli olmalı ve kullanılabilir bir parmak izi taşımalı (`HasUsableFingerprint`).
2. Mevcut olan her birincil tanımlayıcı (`TimeT`, `Ip`, `NickName`) karşı tarafta da bulunmalı ve eşleşmeli; biri eşleşmezse korelasyon reddedilir.
3. İkincil alanlar (`Port`, `Type`) yalnız her iki tarafta da varsa çelişki yaratabilir. En az bir birincil tanımlayıcı eşleşmiş olmalıdır — yani boş bir parmak izi hiçbir şeyle korele olmaz.


### 14.6 Sonuçlar ve denetim izleri

| Durum | Denetim eylemi | Sebep kodu |
|---|---|---|
| Robot kilitsiz ve gerekli haritada | MAP_PREP_LOCK_RELEASED | LOCK_ALREADY_RELEASED |
| Robot kilitsiz ama farklı haritada | MAP_PREP_BLOCKED | MAP_SWITCH_LOCK_RECOVERY_REQUIRED |
| Kilit bizim ama koşullar uygun değil | MAP_PREP_BLOCKED | LOCK_RECOVERY_UNSAFE |
| Kurtarma yapıldı ve RDSCore kabul etti | MAP_PREP_LOCK_RELEASED | LOCK_RECOVERY_SUCCESS |
| Kurtarma sonrası hâlâ kilitli/dispatch dışı | RDS_DISPATCH_BLOCKED | MAP_SWITCH_LOCK_RECOVERY_REQUIRED |

`LOCK_RECOVERY_UNSAFE` denetim kaydına ayrıca bloklayan sebep yazılır: `MAP_MISMATCH`, `ROBOT_NOT_LOCKED`, `ACTIVE_ORDER_ON_ROBOT`, `BLOCKING_ALARM`, `BUSINESS_STATE_UNKNOWN` veya `BUSINESS_BUSY`. Bu, devreye alma mühendisinin kurtarmanın neden durduğunu görebilmesi içindir.


## 15 Native Order Dispatch Akışı

`DispatchNativeOrderAsync` projenin en uzun ve en güvenlik-kritik metodudur. Sırası tesadüfi değildir: her adım bir öncekinin kanıtı üzerine kurulur ve fiziksel çağrı en sondadır.


### 15.1 Adım adım

| # | Adım | Başarısızlık kodu |
|---|---|---|
| 1 | Navigation profile çözümle (talep → varyant → makine) | NAVIGATION_PROFILE_UNRESOLVED |
| 2 | Profilden gerekli robot haritasını al | REQUIRED_MAP_UNRESOLVED |
| 3 | Aracı şablondan veya profilden çöz | VEHICLE_UNRESOLVED |
| 4 | Telemetriden robot kira anahtarını çöz | ROBOT_IDENTITY_UNRESOLVED |
| 5 | Robot kirasını al (session advisory lock) | ROBOT_BUSY |
| 6 | `/robotsStatus` oku (fleet snapshot) | ROBOT_OFFLINE |
| 7 | Çekirdek düzeyi bloklayan alarm kontrolü | RDSCORE_HAS_BLOCKING_ALARM |
| 8 | Alias'ı tek robota çöz | ROBOT_ALIAS_AMBIGUOUS |
| 9 | Kiranın hedefi kapsadığını doğrula (TOCTOU) | ROBOT_CHANGED_DURING_VALIDATION |
| 10 | Bağlantı durumu kontrolü (ConnectionStatus == 1) | ROBOT_OFFLINE |
| 11 | Serbest bırakılmamış harita kilidi kontrolü / kurtarma | MAP_SWITCH_LOCK_RECOVERY_REQUIRED / LOCK_RECOVERY_UNSAFE |
| 12 | Dispatch edilebilirlik kontrolü | ROBOT_NOT_DISPATCHABLE |
| 13 | Gerekiyorsa harita hazırlığı: /lock → /switchMap → /unlock | — |
| 14 | `/setOrder` gönder | — |

> **UYARI:** 6. adımda telemetri okuması başarısız olursa `ROBOT_OFFLINE` yazılır — ancak iptal (`OperationCanceledException`) bunun dışında tutulur. İptal asla bir robot arızası değildir: bir kapanış sağlıklı bir robota karşı ROBOT_OFFLINE olarak kaydedilmemelidir.


### 15.2 DispatchQueuedAsync — toplu çalışma

Worker her turda en fazla `Math.Clamp(limit, 1, 100)` kuyruktaki talebi alır ve her biri için ayrı bir `correlationId` üretir. Üç istisna türü ayrı ayrı yakalanır ve farklı denetim eylemleri yazar:

| İstisna | Denetim eylemi | Anlamı |
|---|---|---|
| RdsExecutionException | RDS_DISPATCH_BLOCKED | TKG kuralı reddetti; tekrar denemek yardımcı olmaz |
| RdsReadException | RDS_DISPATCH_DEFERRED | RDS okuma kanıtı yok; talep kuyrukta kalır |
| RdsCoreNativeOrderException | RDS_DISPATCH_DEFERRED | RDSCore geçici olarak erişilemez; talep kuyrukta kalır |

Ayrım önemlidir: *blocked* bir karardır, *deferred* bir bilgi eksikliğidir. İkisi aynı sayılsaydı geçici bir ağ kesintisi talepleri kalıcı olarak başarısız yapardı.


### 15.3 Savunma derinliği

`DispatchOneAsync` public ve doğrudan çağrılabilir olduğu için global mutasyon kapısı burada da yeniden kontrol edilir, yalnız `DispatchQueuedAsync` içinde değil. Kapı kapalıyken hiçbir kira alınmaz ve hiçbir RDS çağrısı yapılmaz; yalnız bir denetim kaydı yazılır.


### 15.4 Post-outcome audit

Fiziksel sonuç gerçekleştikten sonra yazılan denetim kaydı (`PostOutcomeAudit.RunAsync`) kendi 5 saniyelik sınırlı token'ı altında, best-effort olarak çalışır ve `OperationCanceledException` dahil tüm istisnaları yutar.

> **UYARI:** Denetim yazımı başarısız olduğu için fiziksel operasyon ASLA tekrar denenmez. Robot çoktan hareket etmiştir; denetim kaydının kaybı bir kayıt sorunudur, bir yürütme sorunu değil. Tersini yapmak çift hareket üretirdi.


## 16 Reconciliation, Worker ve Callback


### 16.1 Worker döngüsü

Arka plan servisi `Rds:WorkerIntervalSeconds` (varsayılan 3 sn) aralığıyla iki iş yapar: kuyruktaki talepleri dispatch etmeye çalışır ve devam eden talepleri RDS gerçeğiyle uzlaştırır. `Rds:Enabled=false` iken servis hiç çalışmaz.


### 16.2 ReconcileAsync

Uzlaştırma salt-okunur bir işlemdir ve global dispatch kapısından etkilenmez — bu bilinçlidir: kapı kapalıyken bile daha önce gönderilmiş bir siparişin sonucunu öğrenmek gerekir. RDS sipariş durumu okunur, TKG durum makinesine göre ilerletilir ve tamamlanan taşımalarda slot durumları uygulanır.


### 16.3 ApplyTaskStatusAsync

RDS görev durumundan TKG durumuna geçiş burada yapılır. Terminal RDS durumları TKG'de `Completed` veya `Failed` üretir; her geçiş `TransportStateMachine.EnsureTransition` üzerinden doğrulanır, dolayısıyla RDS'ten gelen beklenmedik bir durum illegal bir geçiş üretemez.


### 16.4 Callback yolu

`ProcessCallbackAsync`, RDS'in TKG'ye geri bildirim gönderdiği yolu işler (`Rds:CallbackUrl` ile yapılandırılır). Callback, uzlaştırmanın yerine geçmez; onu hızlandırır. Callback hiç gelmese bile periyodik uzlaştırma aynı sonuca varır.

> **NOT:** Bu tasarım bilinçlidir: callback ağ üzerinden gelen, kaybolabilen bir bildirimdir. Sistemin doğruluğu ona bağlı olsaydı tek bir kayıp paket taşımayı sonsuza dek asılı bırakırdı.


### 16.5 WorkSite tazeliği

`Rds:WorkSiteFreshnessSeconds` (varsayılan 300 sn) RDS WorkSite projeksiyonunun ne kadar süre taze sayılacağını belirler. `RefreshRelevantWorkSitesAsync` yalnız ilgili WorkSite'leri yeniler; tüm filoyu her turda taramaz.


### 16.6 SynchronizeLoadContentAsync

Slot yük durumu değiştiğinde RDS tarafındaki içerik değerini (`Rds:EmptyContentValue` / `Rds:FullContentValue`) senkronize eder. Bu değerler yapılandırmadan gelir ve varsayılanları **boştur**.

> **UYARI:** COMMISSIONING VALIDATION REQUIRED — `EmptyContentValue` ve `FullContentValue` sahaya özgü RDS içerik etiketleridir. Depoda boş bırakılmaları bilinçlidir: uydurulmuş bir değer yanlış WorkSite içeriği yazardı.


## 17 Kimlik Doğrulama ve Yetkilendirme


### 17.1 Üç kimlik sözleşmesi

| Kullanıcı | Kimlik bilgisi | Cihaz gerekir mi? |
|---|---|---|
| Admin | kullanıcı adı + parola | Hayır |
| Operatör (üretim) | sicil numarası (employeeNumber) + cihaz — PAROLA YOK | Evet |
| Operatör (eski/legacy) | kullanıcı adı + parola + cihaz | Evet |

> **NOT:** Legacy operatör kullanıcı adı girişi backend'de geçici olarak desteklenmeye devam eder. Üretim sözleşmesi sicil numarasıdır.


### 17.2 employee_no normalizasyonu

- Tip: string (sayı değil).
- Kırpılır (trim).
- Kimlik karşılaştırması büyük/küçük harf duyarsızdır.
- **Baştaki sıfırlar anlamlıdır** — `007` ile `7` farklı sicillerdir.
- Global olarak null olabilir (her kullanıcının sicili olmak zorunda değil).
- Dolu olduğunda normalize edilmiş biçimiyle benzersizdir.

Bu kurallar `src/TkgPda.Application/EmployeeNumber.cs` içinde tek yerde uygulanır. Sayıya çevirme yapılmaz; baştaki sıfırların korunması bunun sebebidir.


### 17.3 Roller ve politikalar

| Rol kodu | Anlamı |
|---|---|
| ADMIN | Tüm konfigürasyon ve teşhis |
| SUPERVISOR | Varyant yönetimi, uzman RDS operasyonları |
| LINE_OPERATOR | Hat operatörü — kasa iş akışları |
| WAREHOUSE_OPERATOR | Depo operatörü — hazırlık işaretleme |

| Politika | İzinli roller |
|---|---|
| operator | ADMIN, SUPERVISOR, LINE_OPERATOR, WAREHOUSE_OPERATOR |
| variant-manager | ADMIN, SUPERVISOR |
| administrator | ADMIN |
| warehouse-preparation | ADMIN, SUPERVISOR, WAREHOUSE_OPERATOR |

> **NOT:** WP-05A gereği bir talebi READY işaretlemek bir **depo hazırlık** eylemidir, sıradan bir hat operatörü taşıma eylemi değil. `LINE_OPERATOR` bu politikadan bilinçli olarak dışlanmıştır.


### 17.4 Cihaz bağlama (device binding)

Operatör token'ları cihaza bağlıdır. Her istek `X-Device-Code` başlığı taşır ve middleware üç şeyi doğrular:

1. Başlıktaki cihaz kodu ile token içindeki `device_code` iddiası birebir eşleşmeli — aksi halde `DEVICE_CONTEXT_MISMATCH` (HTTP 403).
2. Cihaz veritabanında (`audit.client_devices`) bulunmalı ve aktif olmalı — aksi halde `DEVICE_NOT_AUTHORIZED` (HTTP 403).
3. Doğrulanan cihaz kaydı istek bağlamına konur ve aşağı katmanlarda yeniden sorgulanmaz.


### 17.5 İstemci tipleri

| ClientType | Kim kullanır | Ek kısıt |
|---|---|---|
| DEVICE_BOUND | Operator PDA | X-Device-Code zorunlu |
| ADMIN_WEB | Admin Web | Token ADMIN rolü taşımalı, aksi halde ADMIN_WEB_ROLE_REQUIRED |


### 17.6 JWT yapılandırması

| Ayar | Varsayılan | Not |
|---|---|---|
| Authentication:Jwt:Issuer | tkg-pda |  |
| Authentication:Jwt:Audience | tkg-pda-clients |  |
| Authentication:Jwt:SigningKey | (yok) | `<JWT_SIGNING_KEY>` — en az 32 bayt olmalı, aksi halde başlangıçta hata |
| Authentication:Jwt:ExpiryMinutes | 60 |  |
| Authentication:Jwt:ClockSkewSeconds | 30 |  |

> **UYARI:** İmzalama anahtarı `appsettings.json` içinde **yoktur** ve olmamalıdır. Ortam değişkeni `Authentication__Jwt__SigningKey` ile verilir. `Validate()` 32 bayttan kısa anahtarı reddeder, böylece zayıf bir anahtarla üretime çıkmak imkânsızdır.


### 17.7 Standart başlıklar

| Başlık | Yön | Amaç |
|---|---|---|
| X-Device-Code | İstek | Cihaz bağlama |
| X-Correlation-ID | İstek/Yanıt | Uçtan uca iz sürme |
| Idempotency-Key | İstek | Tekrar güvenliği |
| Idempotency-Replayed | Yanıt | Bu yanıt mevcut bir kaydın tekrarı mı |


## 18 PostgreSQL Şeması

Şemanın tek gerçek kaynağı `db/migrations` altındaki SQL dosyalarıdır. EF Core migration'ı yoktur ve eklenmemelidir.

Toplam **31 tablo**, **5 şema** altında dağılır.

| Şema | Tablo | Amaç |
|---|---|---|
| auth | 4 | Kullanıcılar, roller, makine bazlı yetkiler |
| master | 17 | Ana veri: makineler, varyantlar, parçalar, layout'lar, navigasyon profilleri |
| ops | 5 | Çalışma zamanı durumu: slot doluluk/yük, taşıma talepleri, olaylar |
| integration | 3 | RDS entegrasyon durumu: WorkSite projeksiyonu, slot eşlemesi, görevler |
| audit | 2 | Denetim kayıtları ve istemci cihazları |


### 18.1 auth şeması

- `auth.roles`
- `auth.user_machine_permissions`
- `auth.user_roles`
- `auth.users`


### 18.2 master şeması

- `master.container_types`
- `master.layout_slot_parts`
- `master.layout_slots`
- `master.layouts`
- `master.machines`
- `master.navigation_profiles`
- `master.part_planning_metrics`
- `master.parts`
- `master.product_group_parts`
- `master.product_groups`
- `master.slot_group_members`
- `master.slot_groups`
- `master.task_templates`
- `master.task_types`
- `master.variant_layout_assignments`
- `master.variant_navigation_profile_assignments`
- `master.variants`


### 18.3 ops şeması

- `ops.machine_runtime_state`
- `ops.slot_runtime_state`
- `ops.transport_batches`
- `ops.transport_request_events`
- `ops.transport_requests`


### 18.4 integration şeması

- `integration.layout_slot_rds_mapping`
- `integration.rds_tasks`
- `integration.rds_worksites`


### 18.5 audit şeması

- `audit.audit_logs`
- `audit.client_devices`


### 18.6 Kritik kısıtlar

| Kısıt | Tablo | Ne korur |
|---|---|---|
| ck_slot_load_requires_occupancy | ops.slot_runtime_state | Dolu olmayan bir slot EMPTY/FULL yük durumu taşıyamaz |
| uq_layout_slot_code | master.layout_slots | (layout_id, code) benzersiz — layout klonlamanın eşleme anahtarı |
| uq_transport_idempotency | ops.transport_requests | Aynı idempotency anahtarı iki kayıt üretemez |
| PRIMARY KEY (layout_slot_id, part_id) | master.layout_slot_parts | Aynı parça bir slota iki kez bağlanamaz |

> **KANIT:** DATABASE-CONFIRMED — Tüm kısıtlar `db/migrations/` altındaki SQL dosyalarından okunmuştur.


### 18.7 Migration dosyaları

| Dosya | Getirdiği |
|---|---|
| 001_initial_schema.sql | 29 tablo; auth, master, ops, integration, audit şemalarının tamamı |
| 002_operator_employee_identity.sql | auth.users üzerinde employee_no ve normalize edilmiş benzersiz indeks |
| 003_transport_batch.sql | ops.transport_batches — çok parçalı toplu gönderim |
| 004_transport_request_readiness.sql | Talep hazırlık ekseni (TransportReadinessState) kolonları |
| 005_layout_slot_part_mapping.sql | master.layout_slot_parts — Parça ↔ Slot yetkili eşlemesi |

Tüm migration'lar `IF NOT EXISTS` konvansiyonunu izler ve idempotenttir; sıralı uygulanmaları gerekir.


## 19 Parça ↔ Slot Yönlendirme ve Layout Sürümleme


### 19.1 Neden ayrı bir ilişki tablosu?

Operatör hangi fiziksel slota dokunacağını elle seçmemelidir. "Bu parça hangi makine slotuna ait?" sorusunun yetkili cevabı `master.layout_slot_parts` tablosudur.

> **UYARI:** `master.layout_slots.metadata_json` içindeki `canonical_part_codes`, `part_association_unresolved` ve `confidence` alanları **çalışma zamanı yönlendirme kaynağı değildir**. Bunlar otomatik Excel çıkarımından gelen, kendi boşluklarını kendisi bildiren köken/veri-kalitesi kanıtıdır. Bir dispatch veya PART_CONFLICT kararı asla bu JSON'a dayanmaz.


### 19.2 Kardinalite

İlişki çoktan-çoğadır. Bir fiziksel slot meşru olarak birden çok müşteri parça kodunu tutabilir (ana + alternatifler); bir parça birden çok fiziksel slotta ve farklı varyanta özgü layout'larda farklı slotlarda görünebilir. "Bir parça → bir global slot" kısıtı **uygulanmaz**.


### 19.3 Kapsam sınırı

Bu tablo yalnız kimlik ilişkisini kaydeder (hangi parça hangi slota ait). Hiçbir yürütme/çalışma zamanı durumu taşımaz; doluluk ve yük yalnız `ops.slot_runtime_state` içinde kalır.


### 19.4 Layout sürüm klonlama

Bir layout'un yeni sürümü oluşturulduğunda üç şey sırayla kopyalanır ve son adım doğrulanır:

1. Yeni `master.layouts` satırı: aynı makine/kod, `max(version)+1`, durum `DRAFT`.
2. `master.layout_slots` satırları yeni layout'a kopyalanır — yeni slotlar **yeni UUID** alır.
3. `master.layout_slot_parts` ilişkileri, eski slot ile yeni slot `code` üzerinden eşleştirilerek kopyalanır.

```sql
INSERT INTO master.layout_slot_parts (layout_slot_id, part_id, created_at)
SELECT new_slot.id, lsp.part_id, now()
FROM master.layout_slots old_slot
JOIN master.layout_slot_parts lsp ON lsp.layout_slot_id = old_slot.id
JOIN master.layout_slots new_slot ON new_slot.layout_id = @new_id AND new_slot.code = old_slot.code
WHERE old_slot.layout_id = @source_id
```

`code` üzerinden eşleme, `uq_layout_slot_code UNIQUE (layout_id, code)` kısıtı sayesinde güvenlidir: bir layout içinde bir kod en fazla bir slota aittir, dolayısıyla JOIN çoğaltma üretemez.


### 19.5 İlişkisel kayıp koruması

```csharp
var copiedCount = await copyParts.ExecuteNonQueryAsync(cancellationToken);
// ... kaynak layout'taki gerçek ilişki sayısı okunur ...
if (copiedCount != expectedCount)
{
    throw new InvalidOperationException(
        $"Relational loss during layout version cloning: expected {expectedCount} layout_slot_parts, copied {copiedCount}.");
}
```

> **NOT:** Sayım doğrulaması aynı transaction içindedir. Bir ilişki sessizce düşerse klon commit edilmez; yeni sürüm hiç oluşmaz. Bu, "yeni layout sürümünde parçalar kayboldu" sınıfı hataların üretime çıkmasını engelleyen tek koruma katmanıdır.

Klonlama başarılı olursa `ADMIN_LAYOUT_VERSION_CREATED` denetim kaydı yazılır ve ardından transaction commit edilir.


## 20 HTTP API Referansı

API yüzeyinde toplam **84 uç nokta** kayıtlıdır. Aşağıdaki tablolar `.MapGet/.MapPost/.MapPut/.MapDelete/.MapPatch` kayıtlarından doğrudan çıkarılmıştır.


### 20.1 Operatör ve kimlik uçları (28)

| Metot | Yol | Handler |
|---|---|---|
| POST | /api/v1/auth/login | LoginAsync |
| GET | /api/v1/auth/me | CurrentUserAsync |
| GET | /api/v1/machines | ListMachinesAsync |
| GET | /api/v1/machines/{machineId:guid}/operational-context | OperationalContextAsync |
| GET | /api/v1/machines/{machineId:guid}/parts/{partId:guid}/compatible-variants | CompatibleVariantsAsync |
| GET | /api/v1/machines/{machineId:guid}/part-options | MachinePartOptionsAsync |
| POST | /api/v1/machines/{machineId:guid}/variant-selection | SwitchVariantAsync |
| POST | /api/v1/transport-requests | CreateTransportAsync |
| POST | /api/v1/transport-batches | CreateTransportBatchAsync |
| GET | /api/v1/machines/{machineId:guid}/container-positions | ContainerPositionsAsync |
| POST | /api/v1/machines/{machineId:guid}/container-actions | CreateContainerActionAsync |
| POST | /api/v1/machines/{machineId:guid}/container-actions/batch | CreateContainerActionBatchAsync |
| POST | /api/v1/machines/{machineId:guid}/container-actions/remove-all | RemoveAllContainersAsync |
| GET | /api/v1/transport-requests/queue | OperatorQueueAsync |
| GET | /api/v1/transport-requests/{requestId:guid} | GetTransportAsync |
| POST | /api/v1/transport-requests/{requestId:guid}/cancel | CancelTransportAsync |
| POST | /api/v1/transport-requests/{requestId:guid}/ready | MarkTransportReadyAsync |
| GET | /api/v1/warehouse/preparation-requests | ListWarehousePreparationRequestsAsync |
| PUT | /api/v1/machines/{machineId:guid}/slots/{slotId:guid}/load-state | UpdateLoadStateAsync |
| GET | /api/v1/rds/robot-alarm-summary | RobotAlarmSummaryAsync |
| GET | /api/v1/rds/robots | ListRobotsAsync |
| POST | /api/v1/rds/robots/pause | PauseRobotAsync |
| POST | /api/v1/rds/robots/resume | ResumeRobotAsync |
| POST | /api/v1/rds/robots/clear-errors | ClearRobotErrorsAsync |
| POST | /api/v1/rds/robots/dispatchable | SetRobotDispatchableAsync |
| POST | /api/v1/transport-requests/{requestId:guid}/priority | SetRequestPriorityAsync |
| POST | /api/v1/transport-requests/{requestId:guid}/manual-finish | ManualFinishRequestAsync |
| POST | /api/v1/transport-requests/{requestId:guid}/redo | RedoRequestAsync |


### 20.2 Admin uçları (55)

| Metot | Yol | Handler |
|---|---|---|
| POST | /api/v1/admin/auth/login | LoginAsync |
| GET | /api/v1/admin/roles | (inline lambda) |
| GET | /api/v1/admin/users | (inline lambda) |
| POST | /api/v1/admin/users | CreateUserAsync |
| PUT | /api/v1/admin/users/{userId:guid} | UpdateUserAsync |
| POST | /api/v1/admin/users/{userId:guid}/password-reset | ResetPasswordAsync |
| PUT | /api/v1/admin/users/{userId:guid}/roles | SetRolesAsync |
| GET | /api/v1/admin/devices | (inline lambda) |
| POST | /api/v1/admin/devices | CreateDeviceAsync |
| PUT | /api/v1/admin/devices/{deviceId:guid} | UpdateDeviceAsync |
| GET | /api/v1/admin/machines | (inline lambda) |
| PUT | /api/v1/admin/machines/{machineId:guid} | UpdateMachineAsync |
| PUT | /api/v1/admin/users/{userId:guid}/machine-permissions/{machineId:guid} | SetMachinePermissionAsync |
| DELETE | /api/v1/admin/users/{userId:guid}/machine-permissions/{machineId:guid} | RemoveMachinePermissionAsync |
| GET | /api/v1/admin/variants | (inline lambda) |
| POST | /api/v1/admin/variants | CreateVariantAsync |
| PUT | /api/v1/admin/variants/{variantId:guid} | UpdateVariantAsync |
| PUT | /api/v1/admin/variants/{variantId:guid}/default-layout | SetDefaultLayoutAsync |
| GET | /api/v1/admin/layouts | (inline lambda) |
| POST | /api/v1/admin/layouts | CreateLayoutAsync |
| POST | /api/v1/admin/layouts/{layoutId:guid}/versions | CreateLayoutVersionAsync |
| PUT | /api/v1/admin/layouts/{layoutId:guid} | UpdateLayoutAsync |
| POST | /api/v1/admin/layouts/{layoutId:guid}/publish | PublishLayoutAsync |
| POST | /api/v1/admin/layouts/{layoutId:guid}/archive | ArchiveLayoutAsync |
| GET | /api/v1/admin/layouts/{layoutId:guid}/slots | (inline lambda) |
| POST | /api/v1/admin/layouts/{layoutId:guid}/slots | CreateSlotAsync |
| PUT | /api/v1/admin/layouts/{layoutId:guid}/slots/{slotId:guid} | UpdateSlotAsync |
| GET | /api/v1/admin/layout-slots/{slotId:guid}/parts | (inline lambda) |
| PUT | /api/v1/admin/layout-slots/{slotId:guid}/parts/{partId:guid} | AssignSlotPartAsync |
| DELETE | /api/v1/admin/layout-slots/{slotId:guid}/parts/{partId:guid} | RemoveSlotPartAsync |
| GET | /api/v1/admin/parts | (inline lambda) |
| POST | /api/v1/admin/parts | CreatePartAsync |
| PUT | /api/v1/admin/parts/{partId:guid} | UpdatePartAsync |
| GET | /api/v1/admin/product-groups | (inline lambda) |
| POST | /api/v1/admin/product-groups | CreateProductGroupAsync |
| PUT | /api/v1/admin/product-groups/{groupId:guid} | UpdateProductGroupAsync |
| PUT | /api/v1/admin/product-groups/{groupId:guid}/parts/{partId:guid} | SetProductGroupPartAsync |
| DELETE | /api/v1/admin/product-groups/{groupId:guid}/parts/{partId:guid} | RemoveProductGroupPartAsync |
| GET | /api/v1/admin/containers | (inline lambda) |
| POST | /api/v1/admin/containers | CreateContainerAsync |
| PUT | /api/v1/admin/containers/{containerId:guid} | UpdateContainerAsync |
| GET | /api/v1/admin/data-issues | (inline lambda) |
| GET | /api/v1/admin/audit-logs | (inline lambda) |
| GET | /api/v1/admin/rds/status | RdsStatusAsync |
| GET | /api/v1/admin/rds/robots | ListRdsRobotsAsync |
| GET | /api/v1/admin/rds/alarms | ListRdsRobotAlarmsAsync |
| GET | /api/v1/admin/rds/navigation-profiles | (inline lambda) |
| POST | /api/v1/admin/rds/worksites/sync | SyncRdsWorkSitesAsync |
| GET | /api/v1/admin/rds/worksites | ListRdsWorkSitesAsync |
| GET | /api/v1/admin/rds/slot-mappings | (inline lambda) |
| PUT | /api/v1/admin/layout-slots/{slotId:guid}/rds-mapping | SetRdsMappingAsync |
| DELETE | /api/v1/admin/layout-slots/{slotId:guid}/rds-mapping | RemoveRdsMappingAsync |
| GET | /api/v1/admin/rds/task-templates | (inline lambda) |
| POST | /api/v1/admin/rds/task-templates/validate | ValidateRdsTaskTemplateAsync |
| POST | /api/v1/admin/rds/commissioning/capture | CaptureRdsCommissioningDiscoveryAsync |


### 20.3 RDS / dahili uçlar (1)

| Metot | Yol | Handler |
|---|---|---|
| POST | /api/integrations/rds/events | CallbackAsync |


### 20.4 Ortak yanıt sözleşmesi

Hatalar tek bir problem gövdesi ile döner: `code` (makine okunur), `message` (insan okunur), `traceId`. HTTP durumu hatanın sınıfına göre belirlenir.

| Durum | Anlamı | Örnek kod |
|---|---|---|
| 400 | İstek biçimi geçersiz | BATCH_ITEMS_REQUIRED |
| 401 | Kimlik doğrulanmadı | — |
| 403 | Yetki veya cihaz reddi | DEVICE_CONTEXT_MISMATCH, ADMIN_WEB_ROLE_REQUIRED |
| 404 | Kaynak yok | — |
| 409 | Çakışma / eşzamanlılık | ACTIVE_TRANSPORT_EXISTS |
| 422 | İş kuralı reddi | OPERATION_NOT_OPERATOR_SELECTABLE |
| 503 | Aşağı akış erişilemez | RDS okuma hataları |


### 20.5 Admin yazma uçlarında iyimser eşzamanlılık (ETag / If-Match)

Her Admin yazma ucu yanıtında bir `ETag` başlığı döner ve her mutasyon isteğinde `If-Match` başlığı **zorunludur**. Değer, kaydın sayısal eşzamanlılık jetonudur (`ConcurrencyToken`).

| Durum | Kod | HTTP | Anlamı |
|---|---|---|---|
| If-Match yok veya boş | IF_MATCH_REQUIRED | 428 | Bu mutasyon için başlık zorunludur |
| If-Match sayısal değil veya ≤ 0 | INVALID_CONCURRENCY_TOKEN | 400 | API'nin döndürdüğü jeton kullanılmalıdır |
| Jeton eskimiş | CONFIGURATION_CONFLICT | 409 | Kayıt başkası tarafından değiştirilmiş |

Layout alt kaynaklarında (slotlar) ayrıca `X-Layout-Version` başlığı, üst layout sürümünü doğrulamak için aynı ayrıştırıcıdan geçer. Böylece bir slot güncellemesi, altında bulunduğu layout sürümü değişmişken uygulanamaz.

> **NOT:** 428 seçimi bilinçlidir: eksik `If-Match` bir yetki veya doğrulama hatası değil, karşılanmamış bir ön koşuldur. İstemci jetonu okuyup isteği tekrar edebilir.


### 20.6 İptal ve istisna sıralaması

`ApiErrors` middleware'i istisnaları tek bir zincirde ele alır. Sıralama anlamlıdır ve **tek bir** iptal yolu vardır:

| Yakalanan | Sonuç |
|---|---|
| ApiException / WorkflowRuleException | Kodun taşıdığı HTTP durumu ve `code` |
| BadHttpRequestException | 400 INVALID_HTTP_REQUEST |
| JsonException | 400 INVALID_JSON |
| OperationCanceledException (RequestAborted iptal edilmişse) | Yanıt gövdesi yok; Information seviyesinde günlüklenir |
| PostgresException (unique/fk/check ihlali) | 409 CONFIGURATION_CONFLICT |
| NpgsqlException | 503 DATABASE_UNAVAILABLE |
| Exception | 500, Error seviyesinde günlüklenir |

> **NOT:** İstemci bağlantıyı kopardığında sunucu hatası üretilmez: istek yanıt gövdesi olmadan biter, çünkü istemcinin terk ettiği bir bağlantıya hiçbir şey yazılamaz. `RequestAborted` iptal edilmemişken gelen bir `OperationCanceledException` ise gerçek bir iç arızadır ve bilinçli olarak aşağıdaki 500'e düşer.


## 21 Operator PDA (Flutter)

Operatör uygulaması el terminalinde çalışır ve tek bir kurala göre tasarlanmıştır: **operatör fiziksel yönlendirme kararı vermez**. Ham RDS tanımlayıcıları (task id, WorkSite, AGV id, uuid) bu yüzeye hiç serialize edilmez.


### 21.1 Katmanlar

| Katman | Dizin | Sorumluluk |
|---|---|---|
| Uygulama kabuğu | lib/app | AppController (durum), router, tema |
| Çekirdek | lib/core | Ağ, kimlik, yapılandırma, platform, depolama |
| Veri | lib/data | API modelleri ve repository |
| Özellikler | lib/features | Ekranlar ve iş yüzeyleri |
| Paylaşılan | lib/shared | Sunum yardımcıları, geri bildirim bantları |


### 21.2 AppController

1790 satırlık tek bir `ChangeNotifier`; uygulamanın tüm durumunu tutar. Bu bilinçli bir seçimdir: operatör akışı doğrusaldır ve durumun tek bir yerde olması, iki ekranın çelişkili durum göstermesini imkânsız kılar.

Aşama makinesi `AppStage` enum'u ile ifade edilir. Başlıca metotlar:

| Metot | Ne yapar |
|---|---|
| initialize | Cihaz kodunu yükler, oturumu geri yükler, başlangıç aşamasını belirler |
| login / logout / handleUnauthorized | Kimlik yaşam döngüsü; 401 alındığında oturum belleği temizlenir |
| selectMachine / leaveMachine / refreshContext | Makine bağlamı ve operasyonel bağlam yenileme |
| switchVariant | Aktif varyantı değiştirir — aktif rezervasyon varsa sunucu reddeder |
| selectMainPartOption / selectEffectivePart / selectCompatibleVariant | Parça-öncelikli seçim akışı |
| enterBatchSelection / addPartToBatch / beginBatchConfiguration / submitBatch | Çok parçalı toplu gönderim akışı |
| submitContainerAction | WP-33 doğrudan kasa aksiyonu |
| removeAllContainers | Varyant kapsamlı toplu kaldırma |
| enterWarehousePreparation / markPreparationReady | Depo hazırlık akışı |
| retryRobotAlarmSummary | Robot alarm özetini yeniden okur |


### 21.3 Ekranlar

| Ekran | Dosya | Amaç |
|---|---|---|
| Cihaz kurulumu | features/device_setup/device_setup_screen.dart | Cihaz kodunu bir kez girer ve güvenli depolamaya yazar |
| Giriş | features/login/login_screen.dart | Sicil numarası (veya legacy kullanıcı adı) ile giriş |
| Makine listesi | features/machines/machine_list_screen.dart | Yetkili makineler |
| Operasyon | features/operation/operation_screen.dart | Ana iş ekranı |
| Parça-öncelikli | features/operation/part_first_screen.dart | Parçadan varyanta çözümleme |
| Çoklu parça batch | features/operation/multi_part_batch_screen.dart | Sepet ve toplu gönderim |
| Kasa paneli | features/operation/container_position_panel.dart | WP-33 kasa aksiyonları |
| Taşıma paneli | features/operation/transport_action_panel.dart | Taşıma aksiyonları |
| Operatör kuyruğu | features/tasks/operator_queue_screen.dart | Devam eden talepler ve iptal |
| Depo hazırlık | features/warehouse/warehouse_preparation_screen.dart | READY işaretleme |


### 21.4 WP-33 kasa paneli davranışı

`ContainerPositionPanel` yalnız backend'in mantıksal pozisyon projeksiyonunu tüketir ve **hiçbir zaman fiziksel slot tanımlayıcısı almaz**. Her pozisyon kartı iki buton taşır:

| Buton | Widget anahtarı | Etiket |
|---|---|---|
| Kasa Doldu | direct-action-REMOVE_FULL-{positionNumber} | {n}. Kasa Doldu |
| Kasa Boş | direct-action-BRING_EMPTY-{positionNumber} | {n}. Kasa Boş |

> **NOT:** Anahtar, WP-33 altında MANTIKSAL iş aksiyonunu adlandırır: (makine, varyant/layout, parça, mantıksal kasa numarası, aksiyon). Kasa numarasının anahtarın parçası olmasının sebebi budur; aynı ekranda birden çok kasa aynı aksiyonu sunabilir.

Buton etkinliği üç koşulun birleşimidir: `availability.enabled` (sunucu projeksiyonu), `controller.mutationGate.canMutate` (global mutasyon kapısı) ve `!submitting`. Devre dışıysa sebep hem tooltip'te hem de butonun altında görünür metin olarak gösterilir.

> **UYARI:** Sebep yalnız tooltip'te gösterilmez. Bir el terminalinde hover yoktur; tooltip tek başına erişilebilir bir açıklama değildir. Bu yüzden `disabledReason` ayrıca butonun içinde küçük punto ile basılır.


### 21.5 Durum rozeti ve animasyon

`_StateBadge` taşıma durumunu Türkçe etiketle gösterir ve yalnız `REQUESTED`, `QUEUED`, `IN_PROGRESS` durumlarında nabız animasyonu çalıştırır. `MediaQuery.disableAnimations` etkinse animasyon hiç kurulmaz; `Semantics` etiketi her iki durumda da aynı bilgiyi taşır.

| Durum | Türkçe etiket |
|---|---|
| READY | Hazır |
| WAITING_PREPARATION | Hazırlık Bekleniyor |
| REQUESTED | İstendi |
| QUEUED | Sırada |
| ROBOT_ASSIGNED | Robot atandı |
| IN_PROGRESS | Devam ediyor |
| COMPLETED | Tamamlandı |
| ERROR | Hata |
| (diğer) | Güncelleniyor |

> **NOT:** Bu etiketler operatöre gösterilen **sunum durumlarıdır**, `TransportRequestStatus` enum'u değil. İkisi kasıtlı olarak ayrıdır: sunum katmanı hazırlık eksenini ve robot atamasını tek bir rozette birleştirir.


### 21.6 SubmissionCoordinator — istemci tarafı idempotency

Bu küçük sınıf, çift gönderimi engelleyen istemci tarafı mekanizmadır ve mantığı incelikli olduğu için ayrıca açıklanmayı hak eder.

```dart
String keyFor(TransportCommand command) {
  if (_materialSignature != command.materialSignature) {
    _materialSignature = command.materialSignature;
    _idempotencyKey = _keyFactory();
    _ambiguous = false;
  }
  return _idempotencyKey!;
}
```

- Idempotency anahtarı komutun **maddi imzasına** bağlıdır. Operatör aynı işi tekrar denerse aynı anahtar kullanılır; farklı bir iş seçerse yeni anahtar üretilir.
- `_inFlight` aynı anda birden fazla gönderimi engeller ve `StateError` fırlatır.
- `AmbiguousMutationException` alındığında anahtar **temizlenmez**: sonuç bilinmiyorken yeni anahtar üretmek çift fiziksel hareket riski doğururdu. `hasAmbiguousRequest` true olur ve kullanıcı yenileme ile durumu netleştirir (`resolveByRefresh`).

> **UYARI:** Belirsiz mutasyon (ağ zaman aşımı gibi) sonrası otomatik yeniden gönderim YOKTUR. Bu bilinçlidir: bir kasa taşıma emri belirsiz durumdayken körlemesine tekrarlanamaz.


### 21.7 Güvenli depolama ve platform ayrımı

Cihaz kodu depolaması platforma göre koşullu import ile seçilir: `device_code_store_secure.dart` (mobil, secure storage), `device_code_store_web.dart` (web) ve bir stub. Aynı desen `platform_info` ve `runtime_config_loader` için de kullanılır. Bu, Flutter'ın koşullu import mekanizmasının standart uygulamasıdır ve web derlemesinin mobil-özel paketleri çekmesini önler.


### 21.8 Oturum belleği

`session_memory.dart` JWT'yi yalnız **bellekte** tutar; diske yazmaz. Uygulama kapandığında token kaybolur ve yeniden giriş gerekir. Cihaz kodu ise kalıcıdır — çünkü o bir sır değil, cihaz kimliğidir.


## 22 Admin Web (React)

Admin Web, konfigürasyonu ve teknik teşhisi yöneten React + TypeScript + Vite uygulamasıdır. Operatör iş akışı içermez.


### 22.1 Yapı

| Dizin | İçerik |
|---|---|
| src/api | Tip güvenli API istemcisi (`client.ts`), tipler (`types.ts`), context sağlayıcı |
| src/auth | AuthContext — token yalnız bellekte |
| src/components | AppShell, Toast, paylaşılan UI ilkelleri |
| src/pages | Sayfa bileşenleri |
| src/test | Vitest kurulum, render yardımcıları, fixture'lar |
| e2e | Playwright uçtan uca senaryoları |


### 22.2 Sayfalar

| Sayfa | Amaç |
|---|---|
| LoginPage | Kullanıcı adı + parola ile ADMIN girişi |
| DashboardPage | Sistem durumu özeti |
| MachinesPage | Makine ana verisi |
| CatalogPage | Varyant, parça, ürün grubu kataloğu |
| ContainersPage | Kasa tipleri |
| LayoutsPage | Layout listesi ve sürümleme |
| LayoutEditorPage | Slot düzenleyici; slot ↔ parça eşlemesi, RDS eşlemeleri |
| UsersPage | Kullanıcı, rol ve makine yetkileri |
| DevicesPage | İstemci cihazları |
| RdsIntegrationPage | RDS entegrasyon durumu, robot telemetrisi, uzman operasyonlar |
| AuditPage | Denetim kayıtları |
| DataQualityPage | Veri kalitesi ve köken raporları |
| NotFoundPage | 404 |


### 22.3 Durum yönetimi

Sunucu durumu TanStack Query ile yönetilir; yerel bileşen durumu React hook'ları ile. Yazma işlemleri `useMutation` ile yapılır ve başarıda ilgili sorgular geçersizleştirilir. Global bir Redux benzeri store yoktur.


### 22.4 Kimlik

> **UYARI:** Token **yalnız bellekte** tutulur; `localStorage` veya `sessionStorage` kullanılmaz. Pratik sonucu: tam sayfa yeniden yüklemesi (`page.goto`, F5) oturumu düşürür. Bu bilinçli bir güvenlik kararıdır, bir hata değil — E2E testleri bu davranışa göre yazılmıştır.


### 22.5 RdsIntegrationPage

Bu sayfa operatörden gizlenen teknik gerçeği gösterir: robot telemetrisi, alarm listeleri (severity ile), kilit durumu, RDSCore uç sağlığı ve uzman operasyonlar. Uzman operasyonlar global mutasyon kapısına tabidir ve kapı kapalıyken sunucu `RDS_DISPATCH_DISABLED` döner — arayüz bu kodu kullanıcıya anlaşılır bir mesaj olarak gösterir.

> **NOT:** Arayüzde bir butonun görünür olması hareketin izinli olduğu anlamına gelmez. Yetki kararı daima sunucudadır; istemci yalnız sonucu gösterir.


### 22.6 LayoutEditorPage

Projedeki en yoğun tek sayfa (966 satır). Sekiz farklı API ailesini birlikte kullanır: `layouts`, `slots`, `machines`, `containers`, `parts`, `rdsNavigationProfiles`, `rdsSlotMappings`, `rdsWorkSites`. Slot geometrisi (ui_x/ui_y/ui_width/ui_height) düzenlenebilir ve slot ↔ parça ilişkisi burada kurulur.


## 23 Konfigürasyon Referansı

Yapılandırma standart ASP.NET Core sırasını izler: `appsettings.json` → ortam değişkenleri (`__` ayırıcı). Sırlar dosyaya yazılmaz.


### 23.1 Rds bölümü

| Anahtar | Varsayılan | Anlamı |
|---|---|---|
| Rds:Enabled | true | RDS entegrasyonu aktif mi (okuma dahil) |
| Rds:DispatchEnabled | false | Fiziksel hareket mutasyonu izni |
| Rds:BaseUrl | http://127.0.0.1:8080 | RDS okuma/WindTask adresi |
| Rds:TimeoutSeconds | 5 | HTTP zaman aşımı |
| Rds:CallbackUrl | (boş) | RDS'in TKG'ye geri bildirim adresi |
| Rds:EmptyContentValue | (boş) | Boş kasa için RDS içerik etiketi — sahaya özgü |
| Rds:FullContentValue | (boş) | Dolu kasa için RDS içerik etiketi — sahaya özgü |
| Rds:AllowInsecureRemoteHttp | false | Uzak adrese düz HTTP izni |
| Rds:WorkerIntervalSeconds | 3 | Worker döngü aralığı |
| Rds:WorkSiteFreshnessSeconds | 300 | WorkSite projeksiyon tazelik penceresi |
| Rds:IntegrationKey | (yok) | `<INTEGRATION_KEY>` — yalnız ortam değişkeninden; uzunluk 32–512 karakter |
| Rds:CallbackKey | (yok) | `<CALLBACK_KEY>` — yalnız ortam değişkeninden; uzunluk 32–512 karakter |

> **NOT:** `RdsOptions` iki anahtarın uzunluğunu doğrular: 32 karakterden kısa veya 512 karakterden uzun bir anahtar reddedilir. Callback doğrulaması sabit zamanlı karşılaştırma (`FixedTimeEquals`) kullanır, böylece anahtar karşılaştırması zamanlama saldırısına açık değildir. Callback anahtarı yapılandırılmamışsa callback ucu hiç kabul etmez.


### 23.2 RdsCore bölümü

| Anahtar | Varsayılan | Anlamı |
|---|---|---|
| RdsCore:BaseUrl | http://127.0.0.1:8088 | RDSCore native order adresi |
| RdsCore:TimeoutSeconds | 5 | HTTP zaman aşımı |
| RdsCore:AllowInsecureRemoteHttp | false | Uzak adrese düz HTTP izni |
| RdsCoreExecution:Enabled | true | Native order motoru yapılandırıldı mı |
| RdsCoreExecution:Mode | RDSCORE_NATIVE_ORDER | Yürütme motoru seçimi |
| RdsCoreDiscovery:Enabled | true | Commissioning keşif istemcisi |

> **UYARI:** `RdsCoreExecution:Enabled=true` fiziksel hareket izni DEĞİLDİR. Yalnız hangi motorun yapılandırıldığını söyler; izin `Rds:DispatchEnabled` üzerindedir ve bileşim `AND`'dir.


### 23.3 Diğer bölümler

| Anahtar | Varsayılan | Anlamı |
|---|---|---|
| ConnectionStrings:TkgPda | (yok) | `<DB_CONNECTION>` — yalnız ortam değişkeninden |
| Cors:AllowedOrigins | 127.0.0.1:8085, :7357 ve localhost karşılıkları | Admin Web ve test kaynakları |
| Logging:LogLevel:Default | Information |  |
| Logging:LogLevel:Microsoft.AspNetCore | Warning |  |
| AllowedHosts | * |  |


### 23.4 Adres güvenliği

`AllowInsecureRemoteHttp=false` iken uzak (loopback olmayan) bir adrese düz HTTP ile bağlanmak reddedilir. Başlangıç günlüğü adresi `BaseAddressDisplaySafe` üzerinden yazar; bu, adresin kullanıcı bilgisi/kimlik parçası içermesi durumunda onu günlüğe düşürmemek içindir.

```text
RDS Integration: {Status}, RDS Dispatch: {Dispatch}, RDSCore: {Endpoint}
```

Bu satır `src/TkgPda.Api/Program.cs` içinde başlangıçta yazılır ve bir devreye alma mühendisinin kapının açık mı kapalı mı olduğunu tek bakışta görmesini sağlar.

> **UYARI:** Bu doküman hiçbir gerçek sır içermez. Bağlantı dizesi, JWT imzalama anahtarı, RDS entegrasyon anahtarı ve callback anahtarı yalnız `<DB_CONNECTION>`, `<JWT_SIGNING_KEY>`, `<INTEGRATION_KEY>`, `<CALLBACK_KEY>` yer tutucularıyla anılır.


## 24 Test Mimarisi

Testler dört ayrı yığında çalışır. Her yığın farklı bir soruyu cevaplar ve birbirinin yerine geçmez.

| Yığın | Konum | Dosya | Cevapladığı soru |
|---|---|---|---|
| xUnit — Domain | tests/TkgPda.Domain.Tests | 9 | İş kuralları doğru mu? |
| xUnit — Application | tests/TkgPda.Application.Tests | 16 | Orkestrasyon ve RDS politikaları doğru mu? |
| xUnit — Auth | tests/TkgPda.Auth.Tests | 7 | Kimlik ve yetki doğru mu? |
| xUnit — Api | tests/TkgPda.Api.Tests | 32 | HTTP sözleşmesi ve adaptörler doğru mu? |
| xUnit — Postgres | tests/TkgPda.Infrastructure.Postgres.Tests | 15 | SQL ve işlem sınırları doğru mu? |
| flutter_test | src/TkgPda.Pda/test | 12 | Operatör yüzeyi doğru mu? |
| Flutter integration_test | src/TkgPda.Pda/integration_test | 1 | Uçtan uca operatör akışı çalışıyor mu? |
| Vitest | src/TkgPda.AdminWeb/src/**/*.test.tsx | 6 | Admin bileşenleri doğru mu? |
| Playwright | src/TkgPda.AdminWeb/e2e | — | Gerçek tarayıcıda admin akışı çalışıyor mu? |


### 24.1 PostgreSQL testleri gerçek veritabanı ister

`TkgPda.Infrastructure.Postgres.Tests` ve API entegrasyon testleri sahte (in-memory) bir veritabanı kullanmaz. Gerekli ortam değişkenleri:

| Değişken | Amaç |
|---|---|
| TKG_PDA_API_TEST_CONNECTION_STRING | Test veritabanı bağlantısı — `<DB_CONNECTION>` |
| ConnectionStrings__TkgPda | Barındırılan `Program` aynı bağlantıyı ayrıca ister |
| Authentication__Jwt__SigningKey | Barındırılan host başlatma doğrulaması — `<JWT_SIGNING_KEY>` |

> **UYARI:** Yalnız `TKG_PDA_API_TEST_CONNECTION_STRING` verilerek koşulan API entegrasyon paketi, barındırılan host'un başlatma doğrulamasına takılır. Bu bir ürün hatası değil, bir ortam yapılandırma hatasıdır; belirti yanıltıcı olduğu için burada kayda geçirilmiştir.

> **UYARI:** Kirlenmiş bir test veritabanı da yanlış başarısızlık üretir: yarıda kesilen bir koşu `finally` temizliğini çalıştıramadan ölürse artık kayıtlar sonraki koşuyu düşürür. Doğru çözüm testi gevşetmek değil, gerçekten temiz bir veritabanı sağlamaktır.


### 24.2 Testlerin kuralı zayıflatmaması ilkesi

Bu projede birkaç test, ürün davranışını değil bir **hatayı** kodluyordu (örneğin REMOVE_EMPTY'nin operatöre açık olduğunu varsayan bir test). Bunlar gerçek sözleşmeye göre yeniden yazıldı; kural gevşetilmedi. Bir testin geçmesi için `find.byKey(...)` çağrısını `.first` veya `findsWidgets` ile değiştirmek bu projede kabul edilebilir bir düzeltme değildir.


### 24.3 E2E kontrol sayacı

Operatör E2E senaryosu, çalıştırdığı doğrulama sayısını sayar ve beklenen sayıdan sapma testi düşürür. Bu sayaç bilinçli olarak "kırılgan"dır: atlanan bir doğrulamanın sessizce geçmesini engellemek için vardır.


### 24.4 Bilinen test kırılganlığı

> **UYARI:** `PostgresLayoutCloningTests` içinde `SELECT id FROM auth.users LIMIT 1` sonucu koruma olmadan `Guid`'e çevrilir. Kullanıcısı olmayan bir veritabanında bu `NullReferenceException` üretir; kullanıcısı olan bir veritabanında test geçer. Bu bir **test kırılganlığıdır**, ürün hatası değil — klonlama kodu ayrıca doğrulanmıştır. Bu doküman salt-okunur olarak üretildiği için düzeltilmemiştir.


## 25 Betikler, Araçlar ve Yerel Yığın


### 25.1 tools/dev — yerel geliştirme yığını

| Betik | Ne yapar |
|---|---|
| start_local_stack.ps1 | PostgreSQL'i hazırlar, migration'ları ve seed'leri uygular, durum dosyasını yazar |
| stop_local_stack.ps1 | Yerel yığını durdurur |
| reset_local_database.ps1 | Veritabanını sıfırdan kurar |

Yığın durumu `%LOCALAPPDATA%\TKG_PDA_DEV\state\stack_state.json` altında tutulur; bağlantı dizesi buradan çözülür ve depoya hiç yazılmaz.


### 25.2 scripts/run_commissioning.ps1

Devreye alma için API host'unu **dispatch açık** olarak başlatan tek betik. Normal geliştirme bu betiği çalıştırmaz.

```text
Rds__Enabled          = true
Rds__DispatchEnabled  = true
```

| Parametre | Kaynak / varsayılan |
|---|---|
| -IntegrationKey | $env:Rds__IntegrationKey — `<INTEGRATION_KEY>` |
| -CallbackKey | $env:Rds__CallbackKey — `<CALLBACK_KEY>` |
| -CallbackUrl | $env:Rds__CallbackUrl |
| -ConnectionString | $env:ConnectionStrings__TkgPda, yoksa yerel yığın durum dosyasından — `<DB_CONNECTION>` |
| -JwtSigningKey | $env:Authentication__Jwt__SigningKey, yoksa 48 baytlık kriptografik rastgele üretilir |
| -RdsBaseUrl | $env:Rds__BaseUrl, yoksa http://127.0.0.1:8080 |
| -RdsCoreBaseUrl | $env:RdsCore__BaseUrl, yoksa http://127.0.0.1:8088 |
| -ApiPort | 5000 |
| -HostAddress | 0.0.0.0 |

- Sırları asla sabit kodlamaz; eksik zorunlu yapılandırmada hızlıca başarısız olur.
- Sırsız, güvenli bir başlangıç özeti basar.
- Üretimle **aynı** worker, aynı `RdsMutationPolicy` ve aynı native order yolunu kullanır — devreye alma için ayrı bir kod yolu yoktur.
- `dotnet run --no-launch-profile` ile çalışır, çünkü `launchSettings.json` git tarafından yok sayılır ve paylaşılan bir profil olamaz.

> **UYARI:** Bu betik gerçek robotu hareket ettirebilir. Yalnız yetkili bir devreye alma oturumunda, sahada güvenlik önlemleri alınmışken çalıştırılmalıdır. COMMISSIONING VALIDATION REQUIRED.

> **NOT:** Betikte bulunan geliştirme anahtarları **yer tutucu** niteliğindedir ve üretim sırrı değildir. Üretimde her anahtar ortam değişkeninden verilmelidir.


### 25.3 tools/phase_02a, tools/phase_02b

Müşteri kaynak verisinden (Excel) `data/generated/` ve `db/seeds/generated/` çıktılarının üretildiği dönüştürme araçları. Çıktıları üretilmiş kabul edilir ve elle düzenlenmez; kaynak veri değişirse yeniden üretilir.

> **UYARI:** Bu araçların ürettiği eşlemeler **deneme/simülasyon eşlemeleridir**. Üretim WorkSite etiketleri, çatal yükseklikleri, geometri ve LOAD/UNLOAD değerleri sahada doğrulanmadan üretim eşlemesi olarak sunulamaz.


## 26 Devreye Alma (Commissioning) Rehberi

> **UYARI:** Bu bölümdeki her adım fiziksel sonuç doğurabilir. Tümü COMMISSIONING VALIDATION REQUIRED olarak işaretlidir: kaynak koddan doğrulanamazlar, yalnız sahada doğrulanabilirler.


### 26.1 Ön koşullar

1. PostgreSQL erişilebilir ve tüm migration'lar uygulanmış.
2. Saha verisi (makineler, varyantlar, parçalar, layout'lar) yüklenmiş ve doğrulanmış.
3. NavigationProfile kayıtları gerçek robot haritası adı ve gerçek araç kimliği ile yayınlanmış.
4. `master.layout_slot_parts` eşlemesi saha ile doğrulanmış.
5. `integration.layout_slot_rds_mapping` gerçek WorkSite etiketleri ile doldurulmuş.
6. RDSCore erişilebilir; `/robotsStatus` gerçek robotları döndürüyor.
7. Saha güvenlik önlemleri alınmış; robot çalışma alanında personel yok.


### 26.2 Aşamalı açılış

| Aşama | Rds:Enabled | Rds:DispatchEnabled | Doğrulanan |
|---|---|---|---|
| 1 — Kapalı | false | false | TKG tek başına çalışıyor; talepler oluşuyor ve kuyrukta kalıyor |
| 2 — Salt okuma | true | false | Telemetri okunuyor, alarmlar görünüyor, WorkSite projeksiyonu tazeleniyor; hiçbir robot hareket etmiyor |
| 3 — Dispatch | true | true | Fiziksel hareket; her talep tek tek ve gözetim altında |

Aşama 2 atlanmamalıdır. Kimlik çözümleme, alarm ayrıştırma ve WorkSite eşlemesindeki hataların tamamı bu aşamada hareket olmadan görülebilir.


### 26.3 Aşama 2'de doğrulanacaklar

| Kontrol | Nasıl doğrulanır | Başarısızlık işareti |
|---|---|---|
| Robot kimliği | Admin Web RdsIntegrationPage robot listesi | uuid ve vehicle_id boş veya beklenenden farklı |
| Alias belirsizliği | Yapılandırılan araç kimliğini telemetri ile karşılaştır | ROBOT_ALIAS_AMBIGUOUS |
| Alarm ayrıştırma | Robotta bilerek bir uyarı oluştur, listede görünmeli | Alarm listesi daima boş |
| Harita adı | NavigationProfile RobotMapName ile robotun CurrentMap değerini karşılaştır | REQUIRED_MAP_UNRESOLVED veya sürekli harita hazırlığı |
| WorkSite eşlemesi | Slot ↔ WorkSite eşlemesini fiziksel konumla karşılaştır | Yanlış rafa gitme riski — dispatch açılmadan düzeltilmeli |


### 26.4 Aşama 3'e geçiş

`scripts/run_commissioning.ps1` ile başlatın. Başlangıç günlüğündeki şu satır kapının durumunu teyit eder:

```text
RDS Integration: Enabled, RDS Dispatch: Enabled, RDSCore: http://<host>:8088
```

> **UYARI:** Bu satırda `RDS Dispatch: Enabled` görülüyorsa sistem gerçek robot hareketi başlatabilir. Sahada bu satır görülmeden fiziksel test başlatılmamalı; görüldükten sonra ise alan boşaltılmadan talep gönderilmemelidir.


### 26.5 Doğrulanması gereken saha değerleri

| Değer | Nerede | Neden uydurulamaz |
|---|---|---|
| WorkSite etiketleri | integration.layout_slot_rds_mapping | Yanlış etiket robotu yanlış rafa gönderir |
| Çatal yükseklikleri | RDS görev şablonları | Yanlış yükseklik fiziksel çarpma riski |
| Geometri / park noktaları | RDSCore /getRobotParkPoints | Sahaya özgü koordinatlar |
| LOAD/UNLOAD değerleri | Rds:EmptyContentValue / FullContentValue | Yanlış içerik etiketi envanteri bozar |
| Robot eşlemesi | NavigationProfile.Vehicle | Yanlış robot yanlış hattı çalıştırır |
| Tanıma parametreleri | RDSCore çekirdek parametreleri | Sahaya özgü kalibrasyon |

> **UYARI:** Depoda bulunan hiçbir örnek değer üretim gerçeği değildir ve simülatör deneme eşlemesi nihai üretim eşlemesi olarak sunulamaz.


---


## 27 Dosya Dosya Kaynak Referansı

Bu bölüm envanterde DOCUMENTED olarak sınıflanan **300 dosyanın her biri** için ayrı bir alt bölüm içerir. Alt bölümler, kaynak ağacındaki gruplara göre düzenlenmiştir.

> **NOT:** Analiz derinliği her dosyada açıkça belirtilir. **Derin analiz**: gerçek uygulama mantığı taşıyan, elle yazılmış açıklama. **Yapısal**: dosyadan okunan gerçek tip/üye bilgisi ve konumundan türetilen rol. **Kısa**: küçük yapılandırma veya veri dosyası. Bu ayrım bilinçlidir; her dosya için elle yazılmış analiz iddia etmek dürüst olmazdı.


### 27.1 Domain katmanı (5)


#### 27.1.1 src/TkgPda.Domain/TkgPda.Domain.csproj

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild proje dosyası | Domain | 2 | Kısa |

**Rol.** Domain katmanı: saf iş kuralları. I/O içermez.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.1.2 src/TkgPda.Domain/TransportRules.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Domain | 181 | Derin analiz |

**Amaç.** Taşıma oluşturmanın saf iş kuralları: hangi operasyon hangi slot durumunda meşrudur, hangi kaynak/hedef kombinasyonu geçerlidir.

**Sorumluluklar.**

- Operasyon ↔ slot durumu uyumunu doğrulamak.
- Kaynak ve hedef slot rollerini kontrol etmek.
- Geçersiz kombinasyonları iş kuralı hatasıyla reddetmek.

**Tanımlanan tipler.** `class TransportRules`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Doğrulama metotları | Her biri tek bir kuralı kontrol eder ve ihlalde `WorkflowRuleException` fırlatır. I/O yapmaz, bu yüzden tamamı saf birim testi ile kapsanabilir. |

**Bağımlılıklar.** `WorkflowEnums`, `WorkflowModels`.

**Çağıranlar.** `ContainerActionPolicy`, `TransportWorkflowService`, Postgres store.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- UNKNOWN bir eksende varsa iş reddedilir.
- Kural teknik kolaylık için gevşetilmez.

**Hata yönetimi.** OCCUPIED_CONTAINER_REQUIRED, BUSINESS_RULE_NOT_CONFIGURED ve ilgili kod ailesi.

**Güvenlik.** Yanlış bir kural kabulü robotu boş bir slota veya dolu bir slota gönderirdi.

**Testler.** `tests/TkgPda.Domain.Tests`.


#### 27.1.3 src/TkgPda.Domain/TransportStateMachine.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Domain | 47 | Derin analiz |

**Amaç.** Taşıma talebi durum geçişlerinin tek yetkili tanımı. 47 satırdır ve bu kısalık kasıtlıdır: kural küçük ve okunabilir olmalıdır.

**Sorumluluklar.**

- Yasal geçiş kümesini tanımlamak.
- Geçişleri zorlamak.
- Rezervasyon aktifliğini ve iptal edilebilirliği türetmek.

**Tanımlanan tipler.** `class TransportStateMachine`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| LegalTransitions | Durumdan izinli hedef durumlara sözlük. Terminal durumların (Completed/Failed/Cancelled) çıkışı yoktur. |
| EnsureTransition | Yasal olmayan geçişi `ILLEGAL_STATUS_TRANSITION` ile reddeder. Durum değiştiren her yol buradan geçer. |
| IsReservationActive | Terminal olmayan durumlar rezervasyon tutar. Varyant değiştirme ve slot yeniden kullanımı bu tanıma bakar. |
| CanCancelBeforeExecution | Created, Validating, Queued için true — RDS'e hiç gidilmemiş talepler. |

**Bağımlılıklar.** `WorkflowEnums`.

**Çağıranlar.** `PostgresTransportWorkflowStore`, `RdsExecutionCoordinator`, operatör kuyruğu projeksiyonu.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- Durum yalnız `EnsureTransition` üzerinden değişir.
- RESERVED bir durum değildir; rezervasyon durumdan türetilir.

**Hata yönetimi.** ILLEGAL_STATUS_TRANSITION.

**Güvenlik.** RDS'ten gelen beklenmedik bir durum illegal geçiş üretemez.

**Testler.** `tests/TkgPda.Domain.Tests` — tam geçiş matrisi.


#### 27.1.4 src/TkgPda.Domain/WorkflowEnums.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Domain | 185 | Derin analiz |

**Amaç.** Domain'in donmuş enum'ları: taşıma durumu, hazırlık durumu, operasyon türü, doluluk ve yük eksenleri, slot rolleri.

**Sorumluluklar.**

- `TransportRequestStatus` sekiz değerini tanımlamak.
- `TransportReadinessState` üç değerini ayrı bir eksen olarak tanımlamak.
- `OccupancyState` ve `LoadState` eksenlerini ayrı tutmak.
- `TransportOperation` üç değerini tanımlamak.

**Tanımlanan tipler.** `enum TransportRequestStatus`, `enum TransportTaskType`, `enum TransportReadinessState`, `enum RequestOrigin`, `enum SlotRole`, `enum OccupancyState`, `enum LoadState`, `class WorkflowCode`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Ayrıştırma yardımcıları | Metin ↔ enum dönüşümleri; bilinmeyen değer sessizce varsayılana düşmez. |

**Bağımlılıklar.** Yok.

**Çağıranlar.** Tüm katmanlar.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- `WAITING_PREPARATION` bir durum değildir; ayrı ve dik bir eksendir.
- RESERVED asla bir occupancy değeri değildir.

**Hata yönetimi.** Yok.

**Güvenlik.** Yok.

**Testler.** `tests/TkgPda.Domain.Tests`.


#### 27.1.5 src/TkgPda.Domain/WorkflowModels.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Domain | 232 | Yapısal |

**Rol.** Domain katmanı: saf iş kuralları. I/O içermez.

**Tanımlanan tipler.** `class WorkflowRuleException`, `class WorkflowErrorCodes`, `record SlotState`, `record SlotSnapshot`, `record CreateTransportRequest`, `record TransportRequestSnapshot`, `class TransportReadinessRules`, `record CreateTransportResult`, `record CreateTransportBatchRequest`, `record TransportBatchResult`, `record CancelTransportResult`, `record LoadStateUpdateRequest`, `record LoadStateUpdateResult`, `record ExecutionOutcome`, `record VariantSelection`

**Tanımlanan üyeler.** `ValidateShape`, `IsMarkReadyNoOp`, `EnsureCanMarkReady`


### 27.2 Application katmanı (19)


#### 27.2.1 src/TkgPda.Application/AdminConfigurationService.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 352 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `class AdminRuleException`, `class AdminConfigurationService`

**Tanımlanan üyeler.** `ListRolesAsync`, `ListUsersAsync`, `CreateUserAsync`, `UpdateUserAsync`, `ResetPasswordAsync`, `SetUserRolesAsync`, `ListDevicesAsync`, `CreateDeviceAsync`, `UpdateDeviceAsync`, `ListMachinesAsync`, `UpdateMachineAsync`, `SetMachinePermissionAsync`, `RemoveMachinePermissionAsync`, `ListVariantsAsync`, `CreateVariantAsync`, `UpdateVariantAsync`, `SetDefaultLayoutAsync`, `ListLayoutsAsync`, `CreateLayoutAsync`, `CreateLayoutVersionAsync`, `UpdateLayoutAsync`, `PublishLayoutAsync`, `ArchiveLayoutAsync`, `ListSlotsAsync`, `CreateSlotAsync`, `UpdateSlotAsync`, `ListSlotPartsAsync`, `AssignSlotPartAsync`, `RemoveSlotPartAsync`, `ListPartsAsync`, `CreatePartAsync`, `UpdatePartAsync`, `ListProductGroupsAsync`, `CreateProductGroupAsync`, `UpdateProductGroupAsync`, `SetProductGroupPartAsync`, `RemoveProductGroupPartAsync`, `ListContainersAsync`, `CreateContainerAsync`, `UpdateContainerAsync`


#### 27.2.2 src/TkgPda.Application/AdminModels.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 292 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `class KnownRoleCodes`, `record AdminMutationContext`, `record AdminRoleRecord`, `record AdminUserRecord`, `record AdminDeviceRecord`, `record AdminMachineRecord`, `record AdminVariantRecord`, `record AdminLayoutRecord`, `record AdminSlotRecord`, `record AdminPartRecord`, `record AdminProductGroupPartRecord`, `record AdminSlotPartRecord`, `record AdminProductGroupRecord`, `record AdminContainerRecord`, `record AdminDataIssueRecord`, `record AdminAuditRecord`, `record CreateUserCommand`, `record UpdateUserCommand`, `record CreateDeviceCommand`, `record UpdateDeviceCommand`, `record UpdateMachineCommand`, `record CreateVariantCommand`, `record UpdateVariantCommand`, `record CreateLayoutCommand`, `record UpdateLayoutCommand`, `record CreateSlotCommand`, `record UpdateSlotCommand`, `record CreatePartCommand`, `record UpdatePartCommand`, `record CreateProductGroupCommand`, `record UpdateProductGroupCommand`, `record SetProductGroupPartCommand`, `record CreateContainerCommand`, `record UpdateContainerCommand`, `interface IPasswordHashService`


#### 27.2.3 src/TkgPda.Application/ApiModels.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 356 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `record AuthUserRecord`, `record DeviceRecord`, `record MachinePermissionRecord`, `record MachineSummaryRecord`, `record CompatibleVariantRecord`, `record MachineRelatedPartRecord`, `record MachineMainPartOptionRecord`, `record VariantLayoutOptionRecord`, `record RdsWorkSiteProjectionRecord`, `record SlotContextDataRecord`, `record PdaAvailableActionRecord`, `record PdaExecutionReadinessRecord`, `record SlotContextRecord`, `record PdaProductGroupPartRecord`, `record PdaProductGroupRecord`, `record PdaTaskTemplateRecord`, `record RdsTaskDataRecord`, `record ActiveTransportDataRecord`, `record PdaExecutionProjectionRecord`, `record ActiveTransportRequestRecord`, `record WarehousePreparationRequestRecord`, `record OperationalContextDataRecord`, `record OperationalContextRecord`, `record AuditEntry`, `enum MachineAccessKind`

**Proje içi bağımlılıklar.** `TkgPda.Domain`


#### 27.2.4 src/TkgPda.Application/ContainerActions.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 417 | Derin analiz |

**Amaç.** WP-33 kasa aksiyonlarının politika ve servis katmanı. Operatörün mantıksal niyetini domain operasyonuna çevirir.

**Sorumluluklar.**

- Tekil kasa aksiyonunu (FULL/EMPTY) domain operasyonuna çevirmek.
- Remove-All kapsamında her pozisyon için operasyon türetmek.
- Uygun olmayan durumları anlamlı iş kuralı hatalarıyla reddetmek.
- Aksiyon uygunluğunu (`enabled` + `disabledMessage`) operatör projeksiyonu için üretmek.

**Tanımlanan tipler.** `enum OperatorContainerAction`, `class OperatorContainerActionCode`, `record ContainerActionResolution`, `class ContainerActionResolutionPolicy`, `record ContainerPositionQuery`, `record ContainerActionCommand`, `record RemoveAllContainersCommand`, `record ContainerActionBatchItem`, `record ContainerActionBatchCommand`, `record ContainerActionAvailabilityRecord`, `record ContainerPositionRecord`, `record ContainerActionResult`, `record RemoveAllPositionIssue`, `record RemoveAllContainersResult`, `interface IContainerActionStore`, `class ContainerActionService`, `record OperatorQueueQuery`, `record OperatorQueueItemRecord`, `record OperatorQueuePageRecord`, `interface IOperatorTransportQueueStore`, `class OperatorTransportQueueService`, `record TransportEtaProjection`, `class TransportEtaService`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Resolve | Tekil aksiyon çözümü. OCCUPIED+EMPTY için yük durumunu FULL yapıp REMOVE_FULL; OCCUPIED+FULL için idempotent REMOVE_FULL; VACANT için `OCCUPIED_CONTAINER_REQUIRED`; UNKNOWN için `BUSINESS_RULE_NOT_CONFIGURED`. |
| ResolveRemoveAll | Varyant kapsamlı toplu kaldırma. OCCUPIED+FULL → REMOVE_FULL, OCCUPIED+EMPTY → REMOVE_EMPTY (yalnız burada, sunucu üretir), VACANT+UNKNOWN → null (atlanır), UNKNOWN → `REMOVE_ALL_POSITION_UNKNOWN` (tüm batch red), çelişki → `REMOVE_ALL_POSITION_CONFLICT` (tüm batch red). |
| Uygunluk projeksiyonu | Her pozisyon için `fullAction` ve `emptyAction` uygunluğunu ve devre dışıysa sebebini üretir. Bu, operatör ekranındaki buton durumunun tek kaynağıdır. |

**Bağımlılıklar.** `TransportRules`, `WorkflowEnums`, `ITransportWorkflowStore`.

**Çağıranlar.** `ApiEndpoints`, `PdaOperationalContextService`.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- Remove-All kapsamı varyant/layout'tur, seçili parça değil.
- Bir pozisyon UNKNOWN veya çelişkiliyse tüm batch reddedilir — kısmi uygulama yoktur.
- REMOVE_EMPTY yalnız Remove-All orkestrasyonunda üretilir.

**Hata yönetimi.** OCCUPIED_CONTAINER_REQUIRED, BUSINESS_RULE_NOT_CONFIGURED, REMOVE_ALL_POSITION_UNKNOWN, REMOVE_ALL_POSITION_CONFLICT.

**Güvenlik.** Yanlış operasyon türetmek robotu yanlış işle gönderirdi.

**Testler.** `tests/TkgPda.Application.Tests` — durum matrisi tam kapsanır.


#### 27.2.5 src/TkgPda.Application/EmployeeNumber.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 31 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `class EmployeeNumber`

**Tanımlanan üyeler.** `Canonicalize`, `NormalizeForLookup`


#### 27.2.6 src/TkgPda.Application/IAdminConfigurationStore.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 59 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `interface IAdminConfigurationStore`


#### 27.2.7 src/TkgPda.Application/IApiDataStore.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 79 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `interface IApiDataStore`

**Proje içi bağımlılıklar.** `TkgPda.Domain`


#### 27.2.8 src/TkgPda.Application/ITransportWorkflowStore.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 44 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `interface ITransportWorkflowStore`

**Proje içi bağımlılıklar.** `TkgPda.Domain`


#### 27.2.9 src/TkgPda.Application/PdaOperationalContextService.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 464 | Derin analiz |

**Amaç.** Operatör ekranının gördüğü her şeyi üreten projeksiyon servisi. Fiziksel gerçeği mantıksal iş yüzeyine çevirir.

**Sorumluluklar.**

- Makine, aktif varyant, layout ve slot durumlarını okumak.
- Fiziksel slotları mantıksal kasa pozisyonlarına (1. Kasa, 2. Kasa …) eşlemek.
- Her pozisyon için aksiyon uygunluğunu hesaplamak.
- Mutasyon kapısını ve devam eden taşıma durumlarını projeksiyona koymak.

**Tanımlanan tipler.** `record PdaActorContext`, `class PdaOperationalContextService`, `class PdaOperationalContextPolicy`, `class PdaExecutionProjector`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Bağlam üretimi | Slotları `display_order` ile sıralar ve pozisyon numaralarını buradan türetir. Operatör asla bir slot UUID'si görmez. |
| Aksiyon uygunluğu | `ContainerActionPolicy` sonucunu `enabled` + `disabledMessage` çiftine çevirir. |
| Taşıma durumu projeksiyonu | Devam eden talepleri sunum durumlarına (READY / REQUESTED / QUEUED / IN_PROGRESS …) çevirir ve kuyruk pozisyonunu ekler. |

**Bağımlılıklar.** `IApiDataStore`, `ContainerActionPolicy`, `IRdsReadClient`, `TransportStateMachine`.

**Çağıranlar.** `ApiEndpoints.OperationalContextAsync`.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- Ham RDS tanımlayıcısı projeksiyona hiç konmaz.
- Sunum durumları `TransportRequestStatus` ile birebir aynı değildir; hazırlık ekseni ve robot ataması burada birleştirilir.

**Hata yönetimi.** Veri eksikliğinde ilgili iş kuralı kodları.

**Güvenlik.** Bilgi sınırının uygulandığı yer: operatöre gitmemesi gereken her alan burada filtrelenir.

**Testler.** `tests/TkgPda.Application.Tests`.


#### 27.2.10 src/TkgPda.Application/RdsCommissioningDiscovery.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 464 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `record RdsCoreCaptureCommand`, `record RdsConnectivityProbe`, `record RdsDistanceProbe`, `record RdsCoreSourceSnapshot`, `record RdsCoreEndpointResult`, `record RdsCoreAlarmSnapshot`, `record RdsCoreRobotSnapshot`, `record RdsCoreBinTaskSnapshot`, `record RdsCoreWorkSiteSnapshot`, `record RdsCoreDeviceSnapshot`, `record RdsCoreChargeParameters`, `record RdsCoreProbeSnapshot`, `record RdsCoreSelectedParametersSnapshot`, `record RdsCoreNavigationSnapshot`, `record RdsCommissioningSnapshot`, `interface IRdsCoreDiscoveryClient`, `record RdsCommissioningSlotConfiguration`, `record RdsExpectedRobotMapping`, `record RdsRequiredBinTaskContract`, `record RdsCommissioningConfiguration`, `interface IRdsCommissioningConfigurationStore`, `enum RdsCommissioningFindingClass`, `record RdsCommissioningFinding`, `record RdsCommissioningCaptureResult`, `class RdsCommissioningDiscoveryService`, `class RdsCommissioningDiscoveryException`, `class RdsCommissioningCanonicalJson`, `class RdsCommissioningMarkdown`

**Tanımlanan üyeler.** `CaptureAsync`, `Compare`, `Canonicalize`, `WriteCanonical`, `Render`, `ToLabel`, `Escape`


#### 27.2.11 src/TkgPda.Application/RdsCoreNativeOrders.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 621 | Derin analiz |

**Amaç.** Native order modelinin tip sözleşmesi: sipariş, blok, telemetri, kilit bilgisi, filo anlık görüntüsü ve istemci arayüzü.

**Sorumluluklar.**

- `RdsCoreRobotTelemetry` ve türetilmiş güvenlik özelliklerini tanımlamak.
- `RdsCoreFleetStatus` ile robot ve çekirdek alarmlarını birlikte döndürmeyi zorlamak.
- `RdsCoreRobotLockInfo.CorrelatesWith` kilit parmak izi korelasyonunu tanımlamak.
- `IRdsCoreNativeOrderClient` arayüzünü sabitlemek.

**Tanımlanan tipler.** `enum RdsExecutionMode`, `record RdsCoreOrderBlock`, `record RdsCoreNavigationBlock`, `record RdsCoreOperationBlock`, `record RdsCoreScriptBlock`, `record RdsCoreBinTaskBlock`, `class RdsCoreNormalOrder`, `record RdsCoreDiagnostic`, `record RdsCoreOrderDiagnostics`, `record RdsCoreSetOrderResult`, `enum RdsCoreKnownOrderState`, `enum RdsCoreKnownBlockState`, `record RdsCoreOrderState`, `record RdsCoreBlockState`, `record RdsCoreBlockDetails`, `record RdsCoreOrderDetails`, `record RdsCoreOrderList`, `enum RdsCoreOrderSortField`, `enum RdsCoreOrderSortDirection`, `record RdsCoreOrderQuery`, `record RdsCoreTerminateResult`, `record RdsCoreSimpleResult`, `record RdsCoreRobotLockInfo`, `record RdsMapSwitchLockSnapshot`, `record RdsCoreRobotTelemetry`, `record RdsCoreFleetStatus`, `interface IRdsCoreNativeOrderClient`, `class RdsCoreNativeOrderException`, `enum RdsCoreReconciliationMeaning`, `record RdsCoreReconciliationDecision`, `class RdsCoreOrderReconciliationPolicy`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| CorrelatesWith | İki kilit bilgisinin aynı kilidi tanımlayıp tanımlamadığına karar verir. Her iki taraf kilitli ve kullanılabilir parmak izine sahip olmalı; mevcut olan her birincil tanımlayıcı (TimeT, Ip, NickName) karşı tarafta da bulunup eşleşmeli; ikincil alanlar (Port, Type) yalnız ikisinde de varsa çelişki yaratabilir; en az bir birincil eşleşme zorunludur. Boş parmak izi hiçbir şeyle korele olmaz. |
| HasBlockingAlarm | Yalnız fatal/error için true. Alarm listesinin dolu olması arıza demek değildir. |
| MayBeExecutingBusinessWork | `ProcBusiness != false`. Yani true veya null/absent iken true döner — bilinmeyen iş durumu fail-closed ele alınır. Bilinçli olarak kanıtlayabildiği şeyle adlandırılmıştır ve tek başına boşta olmanın kanıtı sayılmaz. |
| ResolvedIdentity | Kimlik yoksa `VehicleId`'yi vehicle uzayı kimliği olarak ele alır — okuyucunun ürettiği biçim budur. |
| GlobalHasBlockingAlarm | Çekirdek düzeyi fatal/error. RDSCore'un kendi durumunun dispatch için güvensiz olduğunu söyler. |

**Bağımlılıklar.** `RdsParsedAlarm`, `RdsRobotIdentity`.

**Çağıranlar.** `RdsCoreNativeOrderClient`, `RdsExecutionCoordinator`.

**Durum ve yan etki.** Değişmez record'lar.

**İş kuralları / değişmezler.**

- Kök (çekirdek) alarmlar robotlara kopyalanmaz; yapısal bir atıf yoktur.
- Batarya seviyesi null ise UNAVAILABLE demektir, %0 değil.
- Filo anlık görüntüsü robotları ve kök alarmları birlikte döndürür; ayrı istenemez.

**Hata yönetimi.** Yok (model katmanı).

**Güvenlik.** `MayBeExecutingBusinessWork` ve `CorrelatesWith` doğrudan kilit kurtarma güvenliğini belirler.

**Testler.** `tests/TkgPda.Application.Tests` ve `tests/TkgPda.Api.Tests/RdsCoreNativeOrderClientTests.cs`.


#### 27.2.12 src/TkgPda.Application/RdsExecution.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 2069 | Derin analiz |

**Amaç.** Projenin yürütme kalbi. Kuyruktaki taşıma taleplerini fiziksel RDS siparişlerine çeviren, sonuçlarını uzlaştıran, iptal eden ve uzman (expert) mutasyonlarını yürüten koordinatör burada yaşar. 2069 satırla en büyük tek üretim dosyasıdır ve büyüklüğü kasıtlıdır: fiziksel hareketi yetkilendiren her karar tek bir dosyada, birlikte okunabilir halde durur.

**Sorumluluklar.**

- Global fiziksel mutasyon kapısını (`IsDispatchEnabled`) tek noktada tanımlamak.
- Robot kimliğini telemetriden çözmek ve robot kirasını yönetmek.
- Native order dispatch akışının tamamını yürütmek.
- Harita hazırlığı ve kilit kurtarma kararlarını vermek.
- RDS sonuçlarını TKG durum makinesine uzlaştırmak.
- Uzman mutasyonlarını (manualFinished, redoFailedOrder, setPriority, robot bakımı) politikaya tabi kılmak.

**Tanımlanan tipler.** `enum RdsRobotMaintenanceAction`, `record RdsExpertActionResult`, `record RdsExecutionSettings`, `record RdsDispatchEndpoint`, `record RdsDispatchContext`, `record RdsTaskProjection`, `record RdsTaskTemplateSummary`, `record RdsMappedWorkSite`, `record RdsNavigationProfileSummary`, `interface IRdsExecutionLease`, `interface IRdsExecutionStore`, `record RdsValidateTransportRequest`, `interface IRobotTelemetryProvider`, `record RdsValidationResult`, `record RdsCreateTransportRequest`, `record RdsCreateTransportResult`, `record RdsStopTaskResult`, `record RdsContentUpdateResult`, `interface IRdsExecutionClient`, `class RdsExecutionException`, `record RdsDispatchAttemptResult`, `record RdsCancellationResult`, `record RdsLoadContentSyncResult`, `record RdsCallbackEvent`, `record RdsCallbackResult`, `class RdsTaskInputBinder`, `class RdsExecutionCoordinator`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| DispatchQueuedAsync | Worker girişi. Kapı kapalıysa boş liste döner ve hiçbir kira alınmaz. `ListQueuedRequestIdsAsync` ile en fazla `Clamp(limit,1,100)` talep alır, her biri için yeni bir `correlationId` üretir ve `DispatchOneAsync` çağırır. Üç istisna türünü ayrı ayrı yakalar: `RdsExecutionException` → RDS_DISPATCH_BLOCKED (karar), `RdsReadException` ve `RdsCoreNativeOrderException` → RDS_DISPATCH_DEFERRED (bilgi eksikliği). Bu ayrım olmasaydı geçici bir ağ kesintisi talepleri kalıcı olarak başarısız yapardı. |
| DispatchOneAsync | Tek talep dispatch'i. Public ve doğrudan çağrılabilir olduğu için global kapıyı savunma derinliği olarak yeniden kontrol eder; kapalıysa denetim kaydı yazıp döner, kira almaz, RDS'e dokunmaz. Talep kirasını (`tkg:rds:{id}`) alır ve motor seçimine göre native ya da WindTask yoluna dallanır. |
| DispatchNativeOrderAsync | Native order akışının tamamı. Navigation profile → gerekli harita → araç → robot kira anahtarı → robot kirası → fleet telemetrisi → çekirdek alarm kapısı → tekil robot çözümü → TOCTOU doğrulaması → bağlantı kontrolü → kilit kurtarma → dispatch edilebilirlik → harita hazırlığı → `/setOrder`. Her adımın kendi hata kodu vardır ve fiziksel çağrı en sondadır. |
| ReconcileAsync | Devam eden görevleri RDS gerçeğiyle uzlaştırır. Salt-okunur olduğu için global kapıdan etkilenmez; kapı kapalıyken bile daha önce gönderilmiş bir siparişin sonucu öğrenilmelidir. |
| ApplyTaskStatusAsync | RDS görev durumunu TKG durumuna çevirir. Her geçiş `TransportStateMachine.EnsureTransition` üzerinden yapılır, dolayısıyla RDS'ten gelen beklenmedik bir durum illegal bir geçiş üretemez. |
| ProcessCallbackAsync | RDS callback'ini işler. Uzlaştırmanın yerine geçmez, onu hızlandırır: callback hiç gelmese bile periyodik uzlaştırma aynı sonuca varır. |
| CancelAsync | İptal. Created/Validating/Queued için yereldir ve RDS'e hiç gidilmez. SentToRds/Running için `/terminate` çağrılır — bu SafetyStop sınıfındadır, dolayısıyla global kapı kapalıyken bile izinlidir. |
| ResolveRobotLeaseKeyAsync | Telemetriden robot kira anahtarını çözer. `RdsRobotResolver.Resolve` sonucu Unique değilse null döner; belirsiz bir alias sessizce bir makine seçmez. |
| WithRobotLeaseAsync | Uzman mutasyonlarını robot kirası altında çalıştıran sarmalayıcı. Kira anahtarı çözülemezse EXPERT_CURRENT_STATE_UNAVAILABLE, kira alınamazsa ROBOT_BUSY fırlatır. Tutulan anahtar iş fonksiyonuna verilir ki mutasyona uğrattığı robotun kiranın kapsadığı robot olduğunu kanıtlayabilsin. |
| EnsureLeaseCoversTarget | TOCTOU değişmezi. Tutulan kira anahtarı ile yetkili yeniden okumadan gelen hedefin kira anahtarı eşleşmezse EXPERT_ROBOT_CHANGED_DURING_VALIDATION fırlatır. Kira ortada değiştirilmez — bu, kapatılmak istenen pencerenin ta kendisini yeniden açardı. |
| EnsureMutationAllowed | Dispatch yolu dışındaki tek mutasyon kapısı uygulama noktası. Admin/Supervisor olmak bu kontrolün yerine geçmez. |
| RobotMaintenanceAsync | Filo kapsamlı bakım (clearRobotAllError, gotoSitePause/Resume, dispatchable). Açık ve boş olmayan bir araç listesi zorunludur; yayın (broadcast) semantiği yoktur. Supervisor için makine kapsamı burada uydurulmaz. |
| ManualFinishAsync | `/manualFinished`. MotionEnabling sınıfındadır. Mevcut yürütmeye bağlanır, kira altında çalışır ve TOCTOU doğrulaması yapar. |
| RedoFailedAsync | `/redoFailedOrder`. MotionEnabling. Aynı kira ve TOCTOU koruması altındadır. |
| SetRequestPriorityAsync | `/setPriority`. ExecutionMutation sınıfındadır: sıralamayı değiştirmek işin dispatch edilme zamanını değiştirir. |
| ResolveExpertTargetAsync | Talep kimliğinden uzman hedefini (talep + RDS görevi) çözer. |
| ResolveCurrentExecutionTargetAsync | Uzman mutasyonunu *o an çalışan* yürütmeye bağlar. Bu, geçmiş bir görevin üzerine mutasyon uygulanmasını engeller. |
| ReadFleetForMaintenanceAsync | Bakım için telemetri okur ve okuyamazsa fail-closed davranır. |
| SynchronizeLoadContentAsync | Slot yük durumunu RDS içerik değeriyle senkronlar. |
| RefreshRelevantWorkSitesAsync | Yalnız ilgili WorkSite'leri tazeler; her turda tüm filoyu taramaz. |

**Bağımlılıklar.** `IRdsExecutionStore` (Postgres), `IRdsCoreNativeOrderClient`, `IRdsExecutionClient`, `IRdsReadClient`, `RdsMutationPolicy`, `RdsRobotResolver`, `RdsRobotIdentity`, `TransportStateMachine`.

**Çağıranlar.** Arka plan worker'ı (dispatch + reconcile), `AdminEndpoints` (uzman operasyonlar), `ApiEndpoints` (iptal), `RdsIntegrationEndpoints` (callback).

**Durum ve yan etki.** Kendi içinde durum tutmaz. Tüm kalıcı durum store üzerinden PostgreSQL'dedir; kiralar advisory lock olarak veritabanında yaşar.

**İş kuralları / değişmezler.**

- Fiziksel mutasyon yalnız `Rds:DispatchEnabled=true` iken yapılır.
- Bir robot üzerinde aynı anda tek TKG operasyonu çalışır.
- Kira alındıktan sonra hedef robot değişirse operasyon reddedilir.
- Belirsiz alias asla bir makineye çözülmez.
- Çekirdek düzeyi bloklayan alarm dispatch'i durdurur.
- Denetim yazımı başarısız olduğu için fiziksel operasyon tekrar edilmez.

**Hata yönetimi.** RDS_DISPATCH_DISABLED, RDSCORE_HAS_BLOCKING_ALARM, ROBOT_OFFLINE, ROBOT_BUSY, ROBOT_NOT_DISPATCHABLE, ROBOT_ALIAS_AMBIGUOUS, ROBOT_IDENTITY_UNRESOLVED, ROBOT_CHANGED_DURING_VALIDATION, EXPERT_ROBOT_CHANGED_DURING_VALIDATION, EXPERT_CURRENT_STATE_UNAVAILABLE, MAP_SWITCH_LOCK_RECOVERY_REQUIRED, LOCK_RECOVERY_UNSAFE, NAVIGATION_PROFILE_UNRESOLVED, REQUIRED_MAP_UNRESOLVED, VEHICLE_UNRESOLVED, VEHICLES_REQUIRED.

**Güvenlik.** Fiziksel güvenliğin merkezi. Rol tabanlı yetki burada mutasyon kapısının yerine geçmez; iki kontrol bağımsız ve birlikte uygulanır.

**Testler.** `tests/TkgPda.Application.Tests` altındaki RDS yürütme testleri; kapı, kira, TOCTOU, alarm ve kilit kurtarma senaryoları ayrı ayrı kapsanır.


#### 27.2.13 src/TkgPda.Application/RdsIntegration.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 704 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `record RdsWorkSiteSnapshot`, `record RdsWindTaskQuery`, `record RdsWindTaskSnapshot`, `record RdsWindTaskPage`, `record RdsReadResult`, `record RdsRobotAlarmSnapshot`, `record RdsRobotStateSnapshot`, `record RdsRobotAlarmReport`, `interface IRdsReadClient`, `class RdsReadException`, `class RdsIntegrationRuleException`, `record RdsConnectionStatusRecord`, `record RdsNavigationProfileRecord`, `record RdsMirroredWorkSiteRecord`, `record RdsWorkSiteFilter`, `record RdsSlotMappingRecord`, `record RdsWorkSiteSyncRecord`, `record RdsRobotAlarmAdminRecord`, `record RdsActiveLockFingerprint`, `record RdsRobotAlarmCounts`, `record RdsRobotAdminRecord`, `record RdsRobotAlarmOperatorSummary`, `record OperatorRobotSummaryRecord`, `interface IRdsIntegrationStore`, `interface IRdsWorkSiteReconciler`, `class RdsIntegrationService`

**Tanımlanan üyeler.** `GetStatusAsync`, `ListNavigationProfilesAsync`, `ListWorkSitesAsync`, `ListSlotMappingsAsync`, `SyncWorkSitesAsync`, `SetSlotMappingAsync`, `RemoveSlotMappingAsync`, `ListRobotAlarmsForAdminAsync`, `ListRobotsForAdminAsync`, `ListRobotsForOperatorAsync`, `CountBySeverity`, `ProjectRobotForAdmin`, `GetRobotAlarmSummaryForOperatorAsync`


#### 27.2.14 src/TkgPda.Application/RdsMutationPolicy.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 69 | Derin analiz |

**Amaç.** Bir RDS operasyonunun fiziksel işe göre güvenlik sınıfını tanımlar. Global dispatch kapısının ne anlama geldiğinin **tek** tanımıdır; hiçbir adaptör veya uç nokta ikinci, çelişkili bir fiziksel izin kavramı taşıyamaz.

**Sorumluluklar.**

- Dört mutasyon sınıfını tanımlamak: ReadOnly, SafetyStop, MotionEnabling, ExecutionMutation.
- Kapı kararını tek bir saf fonksiyonda vermek.
- `/dispatchable` gibi gövdesine göre sınıf değiştiren operasyonları sınıflandırmak.
- Reddetme kodunu ve insan okunur mesajını sağlamak.

**Tanımlanan tipler.** `enum RdsMutationClass`, `class RdsMutationPolicy`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| IsAllowed | ReadOnly ve SafetyStop daima izinli; diğer iki sınıf `globalDispatchEnabled` değerine bağlı. Saf fonksiyon, yan etkisiz, test edilmesi önemsiz. |
| ClassifyDispatchable | `type` alanı `dispatchable` ise MotionEnabling, aksi halde SafetyStop. Robotu dispatch edilebilir yapmak hareketi mümkün kılar; dispatch dışına almak güvenliği artırır. |
| BlockedMessage | Reddin *neden* verildiğini açıklayan mesajı üretir: operasyonun fiziksel işi başlatacağı/sürdüreceği/ilerleteceği belirtilir. |

**Bağımlılıklar.** Yok. Bilinçli olarak bağımlılıksızdır; hiçbir I/O yapmaz.

**Çağıranlar.** `RdsExecutionCoordinator.EnsureMutationAllowed`, `AdminEndpoints` uzman operasyon yolları, RDS adaptörleri.

**Durum ve yan etki.** Durumsuz statik sınıf.

**İş kuralları / değişmezler.**

- Hareketi azaltan operasyonlar kapı kapalıyken bile izinlidir.
- Hareketi başlatan, sürdüren, yeniden etkinleştiren veya ilerleten her şey kapıya tabidir.
- Rol yetkisi kapıyı ikame etmez.

**Hata yönetimi.** `BlockedCode = "RDS_DISPATCH_DISABLED"`.

**Güvenlik.** Projenin en güvenlik-kritik tek dosyası. Buradaki bir sınıflandırma hatası doğrudan istenmeyen fiziksel harekete dönüşür.

**Testler.** `tests/TkgPda.Application.Tests` — her sınıf için kapı açık/kapalı matrisi ve `/dispatchable` tip varyasyonları.


#### 27.2.15 src/TkgPda.Application/RdsRobotAlarmParsing.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 186 | Derin analiz |

**Amaç.** Tek SEER alarm ayrıştırıcısı. Dört dokümante zarfı ve iki giriş şeklini destekler, severity'yi korur ve bloklama kararını severity üzerinden verdirir.

**Sorumluluklar.**

- `RdsAlarmSeverity` sıralamasını tanımlamak (Notice < Warning < Error < Fatal).
- Dört bucket'ı hem doğrudan hem `alarms` sarmalı formda okumak.
- Şekil A (`code`) ve Şekil B (dinamik anahtarlı) girişleri ayrıştırmak.
- Null bucket'ı istisna değil "alarm yok" olarak ele almak.

**Tanımlanan tipler.** `enum RdsAlarmSeverity`, `record RdsParsedAlarm`, `class RdsRobotAlarmParser`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Parse (container) | Verilen nesnedeki tüm dokümante bucket'ları hem doğrudan hem sarmalı formda okur; tek bir dokümante yanıt iki formu birden taşıdığı için biri diğerinin lehine atlanamaz. |
| IsBlocking (RdsParsedAlarm) | Yalnız Fatal ve Error true döner. Warning ve Notice raporlanır ama bloklamaz. |

**Bağımlılıklar.** `System.Text.Json`.

**Çağıranlar.** `RdsCoreNativeOrderClient`, `RdsReadClient`.

**Durum ve yan etki.** Durumsuz statik sınıf.

**İş kuralları / değişmezler.**

- Dokümante olmayan hiçbir zarf veya bucket yazımı uydurulmaz.
- Şekil B'de alarm kodu özellik ADIdır; bilinen kardeş alanlar hariç tutularak bulunur.
- Bir notice asla bloklayan alarm gibi davranmaz.

**Hata yönetimi.** Kendi kodu yoktur; sonuçları `RDSCORE_HAS_BLOCKING_ALARM` ve robot düzeyi dispatch reddine beslenir.

**Güvenlik.** Bozuk bir parser, alarmlı bir robota dispatch yapılmasına izin verirdi — bu tam olarak yaşanmış bir hatadır ve tek parser kuralı onun tekrarını engeller.

**Testler.** `tests/TkgPda.Application.Tests` — dört zarf, iki şekil, null bucket ve severity matrisleri.


#### 27.2.16 src/TkgPda.Application/RdsRobotIdentity.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 98 | Derin analiz |

**Amaç.** SEER'in iki kimlik uzayını (`uuid` ve `vehicle_id`) tek bir tipte modelleyerek, hangi uzayın hangi amaçla kullanılacağını yapısal olarak sabitler.

**Sorumluluklar.**

- İki kimlik uzayını birlikte taşımak.
- Amaç bazlı türetilmiş kimlikleri sağlamak: MutationTarget, DiscoveryKey, RobotLeaseKey.
- İki uzayda birden eşleme yapmak (`Matches`).
- Telemetri elemanından kimlik okumayı tek noktada toplamak (`FromRobotElement`).

**Tanımlanan tipler.** `record RdsRobotIdentity`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| MutationTarget | VehicleId ?? Uuid. Vehicle-kapsamlı RDSCore API'leri `vehicle_id` üzerinde sözleşir; `uuid` yalnız onu atlayan payload'lar için yedektir. |
| DiscoveryKey | Uuid ?? VehicleId. `/getRobotParkPoints` uuid anahtarlı istek alır, `/queryDist` uuid anahtarlı harita döner. |
| RobotLeaseKey | VehicleId ?? Uuid. Bugün MutationTarget ile aynı çözülür ama ayrı bir kavramdır: *hangi kira bu robotu kapsıyor?*. Ayrı tutulması, bir uç sözleşmesi değişirse kira alanının sessizce ikiye bölünmesini engeller. |
| Matches | Aday değer iki uzaydan birinde eşleşiyorsa true. Boş adaya asla genişlemez. |
| FromRobotElement | Bir `/robotsStatus` `report[]` elemanından her iki uzayı okur. Tüm telemetri okuyucuları bunu kullanır, böylece üç parser tekrar birbirinden ayrışamaz. |

**Bağımlılıklar.** `System.Text.Json` dışında yok.

**Çağıranlar.** `RdsRobotResolver`, `RdsExecutionCoordinator`, native order ve read istemcileri.

**Durum ve yan etki.** Değişmez (immutable) record.

**İş kuralları / değişmezler.**

- Görüntüleme kimliği (model/isim) bilinçli olarak burada modellenmez; bir gösterim metni asla mutasyon hedefi olamaz.
- Aynı robotun iki alias'ı asla iki farklı kira üretmemelidir.

**Hata yönetimi.** Kendi kodu yoktur; null `MutationTarget` çağıranlar tarafından fail-closed olarak yorumlanır.

**Güvenlik.** Yanlış kimlik uzayı seçimi geçmişte kilit korelasyonunun gerçek robotta sessizce başarısız olmasına yol açmıştı; bu tip o hatanın yapısal çözümüdür.

**Testler.** `tests/TkgPda.Application.Tests` — her türetilmiş özellik ve `Matches` için ayrı vakalar.


#### 27.2.17 src/TkgPda.Application/RdsRobotResolution.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 70 | Derin analiz |

**Amaç.** Bir alias'ın fiziksel bir robota çevrildiği **tek** yer. Belirsizliği yakalar ve tahmin etmeyi imkânsız kılar.

**Sorumluluklar.**

- NoMatch / Unique / Ambiguous üç sonuçlu çözümleme.
- Ayrıklığı tüm kimlik çifti üzerinden hesaplamak.
- Boş/whitespace alias'ı NoMatch olarak ele almak.

**Tanımlanan tipler.** `enum RdsRobotMatchKind`, `class RdsRobotResolver`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Resolve | Alias'a cevap veren robotları iki uzayda birden arar. Farklı kimlik çifti sayısı 1 ise Unique, değilse Ambiguous. Aynı robotun payload'da iki kez görünmesi hâlâ tek robottur. |

**Bağımlılıklar.** `RdsRobotIdentity`, `RdsCoreRobotTelemetry`.

**Çağıranlar.** `RdsExecutionCoordinator.ResolveRobotLeaseKeyAsync` ve `ResolveUniqueRobot`.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- Belirsizlikte daima fail-closed: hangi makinenin kastedildiğini tahmin etmenin güvenli bir yolu yoktur.
- Ayrıklık tek bir çözülmüş string üzerinden hesaplanamaz — bu, (uuid=X,vehicle=null) ile (uuid=null,vehicle=X) çiftlerini birleştirip aranan belirsizliği gizlerdi.

**Hata yönetimi.** Çağıran tarafında `ROBOT_ALIAS_AMBIGUOUS`.

**Güvenlik.** Önceki `FirstOrDefault` davranışı, payload sırasına göre farklı bir fiziksel makineyi hedefleyebiliyordu — hareket doğuran bir mutasyon için mümkün olan en kötü hata biçimi.

**Testler.** `tests/TkgPda.Application.Tests` — tek robot, çakışan iki robot, tekrar eden aynı robot ve boş alias vakaları.


#### 27.2.18 src/TkgPda.Application/TkgPda.Application.csproj

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild proje dosyası | Application | 6 | Kısa |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.2.19 src/TkgPda.Application/TransportWorkflowService.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Application | 112 | Yapısal |

**Rol.** Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez.

**Tanımlanan tipler.** `class TransportWorkflowService`

**Tanımlanan üyeler.** `CreateAsync`, `CreateBatchAsync`, `CancelAsync`, `MarkReadyAsync`, `RecordExecutionTransitionAsync`, `RecordExecutionOutcomeAsync`, `UpdateLoadStateAsync`, `SwitchVariantAsync`

**Proje içi bağımlılıklar.** `TkgPda.Domain`


### 27.3 Infrastructure — PostgreSQL (11)


#### 27.3.1 src/TkgPda.Infrastructure.Postgres/PostgresAdminConfigurationStore.Identity.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 442 | Yapısal |

**Rol.** PostgreSQL erişim katmanı: ham SQL, işlem sınırları, advisory lock.

**Tanımlanan tipler.** `class PostgresAdminConfigurationStore`

**Tanımlanan üyeler.** `CreateUserAsync`, `UpdateUserAsync`, `ResetPasswordAsync`, `SetUserRolesAsync`, `CreateDeviceAsync`, `UpdateDeviceAsync`, `UpdateMachineAsync`, `SetMachinePermissionAsync`, `RemoveMachinePermissionAsync`, `ReplaceRolesAsync`, `TouchUserAsync`, `ReadDeviceAsync`, `ReadMachineAsync`, `FindPermissionAsync`, `HasActiveTransportAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.3.2 src/TkgPda.Infrastructure.Postgres/PostgresAdminConfigurationStore.Layouts.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 690 | Derin analiz |

**Amaç.** Layout ve slot yönetiminin SQL implementasyonu; sürüm klonlamanın ilişkisel bütünlük koruması burada.

**Sorumluluklar.**

- Layout CRUD ve sürümleme.
- Slot CRUD ve geometri.
- Slot ↔ parça ilişkisi yönetimi.
- Klonlamada ilişkisel kaybı tespit etmek.

**Tanımlanan tipler.** `class PostgresAdminConfigurationStore`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| CloneLayoutAsync | Yeni layout (`max(version)+1`, DRAFT) → slotlar (yeni UUID, aynı `code`) → `layout_slot_parts` ilişkileri (`code` üzerinden JOIN). Kopyalanan satır sayısı beklenenle karşılaştırılır; eşit değilse `InvalidOperationException` fırlatılır ve transaction geri alınır. |
| PublishLayoutAsync | DRAFT → PUBLISHED geçişi. |
| Slot eşleme metotları | Slot ↔ parça ilişkilerini kurar ve kaldırır. |

**Bağımlılıklar.** `Npgsql`.

**Çağıranlar.** `AdminConfigurationService` → `AdminEndpoints`.

**Durum ve yan etki.** Veritabanı.

**İş kuralları / değişmezler.**

- `code` üzerinden eşleme `uq_layout_slot_code UNIQUE (layout_id, code)` sayesinde tekildir; JOIN çoğaltma üretemez.
- İlişkisel kayıp tespit edilirse klon commit edilmez.

**Hata yönetimi.** "Relational loss during layout version cloning" — ilişkisel bütünlük ihlali.

**Güvenlik.** Sessizce parça ilişkisi kaybeden bir klon, robotu yanlış slota yönlendirebilirdi.

**Testler.** `tests/TkgPda.Infrastructure.Postgres.Tests/PostgresLayoutCloningTests.cs` — gerçek veritabanı ile.


#### 27.3.3 src/TkgPda.Infrastructure.Postgres/PostgresAdminConfigurationStore.MasterData.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 493 | Yapısal |

**Rol.** PostgreSQL erişim katmanı: ham SQL, işlem sınırları, advisory lock.

**Tanımlanan tipler.** `class PostgresAdminConfigurationStore`

**Tanımlanan üyeler.** `CreatePartAsync`, `UpdatePartAsync`, `ListSlotPartsAsync`, `AssignSlotPartAsync`, `RemoveSlotPartAsync`, `CreateProductGroupAsync`, `UpdateProductGroupAsync`, `SetProductGroupPartAsync`, `RemoveProductGroupPartAsync`, `CreateContainerAsync`, `UpdateContainerAsync`, `ReadPartAsync`, `ReadContainerAsync`, `TouchProductGroupAsync`, `ExistsAsync`, `AddContainerParameters`, `AddNullableInteger`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.3.4 src/TkgPda.Infrastructure.Postgres/PostgresAdminConfigurationStore.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 485 | Yapısal |

**Rol.** PostgreSQL erişim katmanı: ham SQL, işlem sınırları, advisory lock.

**Tanımlanan tipler.** `class PostgresAdminConfigurationStore`

**Tanımlanan üyeler.** `ListRolesAsync`, `ListUsersAsync`, `ListDevicesAsync`, `ListMachinesAsync`, `ListVariantsAsync`, `ListLayoutsAsync`, `ListSlotsAsync`, `ListPartsAsync`, `ListProductGroupsAsync`, `ListContainersAsync`, `ListDataIssuesAsync`, `ListAuditAsync`, `ReadUserAsync`, `ReadProductGroupAsync`, `ReadDevice`, `ReadMachine`, `ReadVariant`, `ReadLayout`, `ReadSlot`, `ReadPart`, `ReadContainer`, `InsertAuditAsync`, `NotFound`, `Conflict`, `ParseJson`, `NullableJson`, `NullableGuid`, `NullableString`, `NullableDecimal`, `NullableInt`, `AddNullableUuid`, `AddNullableText`, `AddNullableJson`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.3.5 src/TkgPda.Infrastructure.Postgres/PostgresApiDataStore.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 997 | Yapısal |

**Rol.** PostgreSQL erişim katmanı: ham SQL, işlem sınırları, advisory lock.

**Tanımlanan tipler.** `class PostgresApiDataStore`

**Tanımlanan üyeler.** `FindUserByUsernameAsync`, `FindUserByEmployeeNumberAsync`, `FindAuthUserAsync`, `FindDeviceAsync`, `MarkLoginSucceededAsync`, `FindMachinePermissionAsync`, `ListVisibleMachinesAsync`, `ListWarehousePreparationRequestsAsync`, `ListCompatibleVariantsAsync`, `ListMachinePartOptionsAsync`, `GetOperationalContextDataAsync`, `ReadProductGroupsAsync`, `ReadActiveRequestsAsync`, `ReadTaskTemplatesAsync`, `FindTransportRequestAsync`, `WriteAuditAsync`, `ListAsync`, `BusinessAction`, `QueueState`, `IsReadyAsync`, `ReadPermission`, `NullableGuid`, `NullableString`, `NullableDecimal`, `NullableInt32`, `NullableDateTimeOffset`, `AddNullableUuid`, `AddNullableText`, `NullableJsonElement`, `AddNullableJson`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`


#### 27.3.6 src/TkgPda.Infrastructure.Postgres/PostgresRdsCommissioningConfigurationStore.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 50 | Yapısal |

**Rol.** PostgreSQL erişim katmanı: ham SQL, işlem sınırları, advisory lock.

**Tanımlanan tipler.** `class PostgresRdsCommissioningConfigurationStore`

**Tanımlanan üyeler.** `LoadAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.3.7 src/TkgPda.Infrastructure.Postgres/PostgresRdsExecutionStore.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 782 | Derin analiz |

**Amaç.** RDS yürütmesinin kalıcılık katmanı: kuyruk okuma, görev kayıtları, denetim yazımı ve iki ayrı advisory kira implementasyonu.

**Sorumluluklar.**

- Kuyruktaki talep kimliklerini listelemek.
- Talep kirasını (`tkg:rds:`) ve robot kirasını (`tkg:robot:`) sağlamak.
- `integration.rds_tasks` kayıtlarını yönetmek.
- Yürütme denetim kayıtlarını yazmak.
- Serbest bırakılmamış harita geçiş kilidi kaydını okumak.

**Tanımlanan tipler.** `class PostgresRdsExecutionStore`, `class PostgresRdsAdvisoryLease`, `class PostgresRdsExecutionLease`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| TryAcquireLeaseAsync | Talep kirası. `SELECT pg_try_advisory_lock(hashtextextended(@key,0))`, anahtar `tkg:rds:{requestId:D}`. |
| TryAcquireRobotLeaseAsync | Robot kirası. Ayrılmış bir bağlantıda SESSION düzeyinde tutulur; işlem düzeyinde olsaydı commit anında — fiziksel mutasyondan önce — serbest kalırdı. Anahtar `tkg:robot:{key}` biçimindedir ve küçük harfe indirgenir, böylece bir robot anahtarı bir talep anahtarıyla çarpışamaz. |
| WriteExecutionAuditAsync | Eylem + sebep kodu + mesaj + korelasyon kimliği yazar. |
| GetUnreleasedMapSwitchLockAsync | Araca göre filtrelenmiş kurtarma kaydını döndürür; çağıran ayrıca mevcut bağlama karşı yeniden doğrular. |
| ListQueuedRequestIdsAsync | Dispatch için kuyruk okuma. |

**Bağımlılıklar.** `Npgsql`.

**Çağıranlar.** `RdsExecutionCoordinator`.

**Durum ve yan etki.** Veritabanı; kira nesneleri kendi bağlantılarını tutar ve `DisposeAsync` ile bırakır.

**İş kuralları / değişmezler.**

- Kira ad alanı öneki anahtar dizesinin içindedir.
- Kira serbest bırakma `await using` üzerinden her yolda garantidir.

**Hata yönetimi.** `NpgsqlException` yukarıya taşınır ve 503'e çevrilir.

**Güvenlik.** Kira alanlarının ayrılması ve session ömrü, robot serileştirmesinin doğruluğunun temelidir.

**Testler.** `tests/TkgPda.Infrastructure.Postgres.Tests` — gerçek PostgreSQL ile eşzamanlılık senaryoları.


#### 27.3.8 src/TkgPda.Infrastructure.Postgres/PostgresRdsIntegrationStore.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 471 | Yapısal |

**Rol.** PostgreSQL erişim katmanı: ham SQL, işlem sınırları, advisory lock.

**Tanımlanan tipler.** `class PostgresRdsIntegrationStore`, `record SlotIdentity`, `record WorkSiteIdentity`

**Tanımlanan üyeler.** `ListNavigationProfilesAsync`, `ListWorkSitesAsync`, `ListSlotMappingsAsync`, `SetSlotMappingAsync`, `RemoveSlotMappingAsync`, `ReadSlotForUpdateAsync`, `ReadWorkSiteForUpdateAsync`, `ReadExistingMappingAsync`, `InsertAuditAsync`, `ReadMapping`, `Normalize`, `Text`, `Int`, `Uuid`, `Timestamp`, `AddText`, `AddInt`, `AddUuid`, `AddJson`, `GetActiveTkgLockFingerprintsAsync`, `ReadCapturedLockInfo`, `ReadString`, `ReadInt`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.3.9 src/TkgPda.Infrastructure.Postgres/PostgresTransportWorkflowStore.ContainerActions.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 911 | Derin analiz |

**Amaç.** Kasa aksiyonlarının SQL implementasyonu: tekil aksiyon, batch ve Remove-All akışları.

**Sorumluluklar.**

- Kasa aksiyonunu tek transaction içinde uygulamak.
- Boş kasa tahsisini `FOR UPDATE ... SKIP LOCKED` ile yapmak.
- Yük durumu geçişini (EMPTY→FULL) atomik uygulamak.
- Batch bütünlüğünü korumak: kısmi uygulama yok.

**Tanımlanan tipler.** `class PostgresTransportWorkflowStore`, `record ActivePositionRequest`, `record PositionRow`, `record VariantPositionRow`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Tekil aksiyon | Advisory lock → slot satırı `FOR UPDATE` → gerekiyorsa yük güncelleme → talep INSERT → olay INSERT, tek transaction. |
| Boş kasa tahsisi | `EMPTY_STORAGE` rolündeki OCCUPIED+EMPTY slotlardan `FOR UPDATE SKIP LOCKED` ile bir tanesi seçilir. `SKIP LOCKED` olmadan iki eşzamanlı BRING_EMPTY aynı tampon kasayı seçerdi. |
| Remove-All | Tüm pozisyonlar için türetilen operasyonlar tek transaction içinde yazılır; biri reddedilirse hiçbiri yazılmaz. |

**Bağımlılıklar.** `Npgsql`, `ContainerActionPolicy`, `TransportRules`.

**Çağıranlar.** `ContainerActionService`.

**Durum ve yan etki.** Veritabanı.

**İş kuralları / değişmezler.**

- Batch ya tamamen uygulanır ya hiç uygulanmaz.
- Yük durumu değişimi ile talep oluşturma aynı transaction içindedir; ikisi ayrılamaz.

**Hata yönetimi.** REMOVE_ALL_POSITION_UNKNOWN, REMOVE_ALL_POSITION_CONFLICT, OCCUPIED_CONTAINER_REQUIRED, BATCH_* ailesi.

**Güvenlik.** Kısmi batch uygulaması, operatörün gördüğü durumla fiziksel gerçeği ayırırdı.

**Testler.** `tests/TkgPda.Infrastructure.Postgres.Tests`.


#### 27.3.10 src/TkgPda.Infrastructure.Postgres/PostgresTransportWorkflowStore.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Infrastructure | 1478 | Derin analiz |

**Amaç.** Taşıma iş akışının SQL implementasyonu. İşlem sınırları, idempotency, rezervasyon koruması ve durum geçişlerinin kalıcılığı burada.

**Sorumluluklar.**

- Taşıma taleplerini oluşturmak ve durumlarını ilerletmek.
- Idempotency'yi advisory lock + benzersiz indeks ile garanti etmek.
- Aktif rezervasyon varken varyant değişimini engellemek.
- Olayları `ops.transport_request_events` tablosuna yazmak.

**Tanımlanan tipler.** `class PostgresTransportWorkflowStore`, `record LockedWorkflowRequest`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Talep oluşturma | `pg_advisory_xact_lock(hashtextextended(idempotency_key,0))` ile aynı anahtarlı eşzamanlı istekleri serileştirir; mevcut kayıt varsa onu döndürür ve `IdempotentReplay` işaretler. |
| Durum güncelleme | `TransportStateMachine.EnsureTransition` ile doğrulanan geçişi yazar ve olayı kaydeder. |
| Varyant değiştirme | Aktif rezervasyon varsa `ACTIVE_TRANSPORT_EXISTS` fırlatır. |
| Kuyruk sorgusu | Operatör kuyruğunu ve `CanCancel` alanını üretir. |

**Bağımlılıklar.** `Npgsql`, `TransportStateMachine`, `TransportRules`.

**Çağıranlar.** `ApiEndpoints`, `TransportWorkflowService`, `ContainerActionService`.

**Durum ve yan etki.** Veritabanı; kendi içinde durum tutmaz.

**İş kuralları / değişmezler.**

- Idempotency iki katmanla korunur: advisory lock yarışı serileştirir, benzersiz indeks son savunmadır.
- Durum yalnız durum makinesi üzerinden değişir.

**Hata yönetimi.** ACTIVE_TRANSPORT_EXISTS, ILLEGAL_STATUS_TRANSITION ve ilgili kodlar.

**Güvenlik.** Çift kayıt = çift fiziksel hareket. Bu dosya o riski kapatır.

**Testler.** `tests/TkgPda.Infrastructure.Postgres.Tests` — gerçek PostgreSQL gerektirir.


#### 27.3.11 src/TkgPda.Infrastructure.Postgres/TkgPda.Infrastructure.Postgres.csproj

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild proje dosyası | Infrastructure | 10 | Kısa |

**Rol.** PostgreSQL erişim katmanı: ham SQL, işlem sınırları, advisory lock.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


### 27.4 API katmanı (16)


#### 27.4.1 src/TkgPda.Api/AdminContracts.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 118 | Yapısal |

**Rol.** API katmanı: HTTP yüzeyi, güvenlik ve RDS adaptörleri.

**Tanımlanan tipler.** `record AdminLoginResponse`, `record CreateAdminUserRequest`, `record UpdateAdminUserRequest`, `record ResetPasswordRequest`, `record SetRolesRequest`, `record CreateAdminDeviceRequest`, `record UpdateAdminDeviceRequest`, `record UpdateAdminMachineRequest`, `record SetMachinePermissionRequest`, `record CreateAdminVariantRequest`, `record UpdateAdminVariantRequest`, `record SetDefaultLayoutRequest`, `record CreateAdminLayoutRequest`, `record UpdateAdminLayoutRequest`, `record CreateAdminSlotRequest`, `record UpdateAdminSlotRequest`, `record CreateAdminPartRequest`, `record UpdateAdminPartRequest`, `record CreateAdminProductGroupRequest`, `record UpdateAdminProductGroupRequest`, `record SetAdminProductGroupPartRequest`, `record CreateAdminContainerRequest`, `record UpdateAdminContainerRequest`, `record SyncRdsWorkSitesRequest`, `record SetRdsWorkSiteMappingRequest`, `record ValidateRdsTaskTemplateRequest`


#### 27.4.2 src/TkgPda.Api/AdminEndpoints.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 571 | Derin analiz |

**Amaç.** Admin HTTP yüzeyi: ana veri yönetimi, layout sürümleme, kullanıcı/cihaz yönetimi, RDS entegrasyon teşhisi ve uzman robot operasyonları.

**Sorumluluklar.**

- Admin uçlarını `administrator` / `variant-manager` politikalarına bağlamak.
- Konfigürasyon CRUD'unu `AdminConfigurationService`'e delege etmek.
- Uzman RDS operasyonlarını koordinatöre yönlendirmek — kendi mutasyon kararını vermeden.

**Tanımlanan tipler.** `class AdminEndpoints`, `record RdsCommissioningCaptureApiRequest`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Layout uçları | Liste, oku, oluştur, güncelle, sürüm klonla, yayınla. |
| Slot uçları | Slot CRUD, geometri, slot ↔ parça eşlemesi. |
| Ana veri uçları | Makine, varyant, parça, ürün grubu, kasa tipi. |
| Kimlik uçları | Kullanıcı, rol, makine yetkisi, cihaz. |
| RDS teşhis uçları | WorkSite projeksiyonu, robot telemetrisi, izin verilen operasyon listesi, entegrasyon durumu. |
| Uzman operasyon uçları | manualFinished, redoFailedOrder, setPriority, robot bakımı. Hepsi `RdsExecutionCoordinator` üzerinden geçer ve mutasyon kapısına tabidir. |

**Bağımlılıklar.** `AdminConfigurationService`, `RdsExecutionCoordinator`, `RdsIntegrationService`, `ApiSecurity`.

**Çağıranlar.** Admin Web `client.ts`.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- Admin olmak fiziksel mutasyon kapısını açmaz.

**Hata yönetimi.** ADMIN_WEB_ROLE_REQUIRED, CONFIGURATION_CONFLICT, RDS_DISPATCH_DISABLED ve koordinatörün ürettiği tüm kodlar.

**Güvenlik.** Uzman operasyonlar burada kendi izin kararını vermez; karar tek yerde, politikadadır.

**Testler.** `tests/TkgPda.Api.Tests`.


#### 27.4.3 src/TkgPda.Api/ApiContracts.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 166 | Yapısal |

**Rol.** API katmanı: HTTP yüzeyi, güvenlik ve RDS adaptörleri.

**Tanımlanan tipler.** `record LoginRequest`, `record LoginResponse`, `record AuthenticatedUserResponse`, `record DeviceResponse`, `record CreateTransportHttpRequest`, `record CreateTransportBatchItemHttpRequest`, `record CreateTransportBatchHttpRequest`, `record TransportBatchHttpResponse`, `record CreateContainerActionHttpRequest`, `record RemoveAllContainersHttpRequest`, `record CreateContainerActionBatchItemHttpRequest`, `record CreateContainerActionBatchHttpRequest`, `record ContainerActionHttpResponse`, `record RemoveAllContainerItemHttpResponse`, `record RemoveAllContainersHttpResponse`, `record VariantSwitchHttpRequest`, `record UpdateLoadStateHttpRequest`, `record LoadContentSyncHttpResult`, `record UpdateLoadStateHttpResponse`, `record TransportHttpResponse`

**Tanımlanan üyeler.** `From`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.4.4 src/TkgPda.Api/ApiEndpoints.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 1184 | Derin analiz |

**Amaç.** Operatör HTTP yüzeyi. Kimlik, makine listesi, operasyonel bağlam, taşıma oluşturma, kasa aksiyonları, kuyruk, iptal ve depo hazırlık uçları burada kayıtlıdır.

**Sorumluluklar.**

- Operatör uçlarını kaydetmek ve politikalara bağlamak.
- Girdi doğrulaması ve idempotency başlığı yönetimi.
- Operatöre kapalı operasyonları elemek.
- Uygulama servislerine delege etmek.

**Tanımlanan tipler.** `class ApiEndpoints`, `record RobotVehiclesApiRequest`, `record SetDispatchableApiRequest`, `record SetPriorityApiRequest`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| LoginAsync | Sicil numarası veya legacy kullanıcı adı ile giriş. İkisi birden verilirse `AMBIGUOUS_LOGIN_IDENTITY`. |
| CurrentUserAsync | Token'daki kimliği ve rolleri döndürür. |
| ListMachinesAsync | Kullanıcının yetkili olduğu makineler. |
| OperationalContextAsync | Makine için operatör projeksiyonu: pozisyonlar, aksiyon uygunlukları, mutasyon kapısı. |
| CreateTransportAsync | Genel taşıma oluşturma. `EnsureTransportOperationAllowed` ile operatöre kapalı operasyonları eler. |
| CreateContainerActionAsync | WP-33 tekil kasa aksiyonu. |
| RemoveAllContainersAsync | Varyant kapsamlı toplu kaldırma. |
| OperatorQueueAsync | Devam eden talepler; `CanCancel` alanı `CanCancelBeforeExecution` üzerinden hesaplanır. |
| CancelTransportAsync | İptal; koordinatöre delege eder. |
| MarkTransportReadyAsync | Depo hazırlık; warehouse-preparation politikası. |
| EnsureTransportOperationAllowed | REMOVE_EMPTY gibi operatöre kapalı operasyonları her rol için reddeder → `OPERATION_NOT_OPERATOR_SELECTABLE` (HTTP 422). |

**Bağımlılıklar.** `PdaOperationalContextService`, `ContainerActionService`, `ITransportWorkflowStore`, `RdsExecutionCoordinator`, `ApiSecurity`.

**Çağıranlar.** Flutter `PdaRepository`.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- Operatöre ham RDS tanımlayıcısı serialize edilmez.
- REMOVE_EMPTY operatör tarafından seçilemez; yalnız sunucu orkestrasyonu üretir.

**Hata yönetimi.** OPERATION_NOT_OPERATOR_SELECTABLE, AMBIGUOUS_LOGIN_IDENTITY, BATCH_* ailesi, OCCUPIED_CONTAINER_REQUIRED, BUSINESS_RULE_NOT_CONFIGURED.

**Güvenlik.** Her uç bir politikaya bağlıdır; politikasız uç yoktur.

**Testler.** `tests/TkgPda.Api.Tests` — uç bazlı sözleşme testleri.


#### 27.4.5 src/TkgPda.Api/ApiErrors.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 191 | Derin analiz |

**Amaç.** Tüm API istisnalarını tek bir zincirde HTTP yanıtlarına çeviren middleware ve problem gövdesi yazıcısı.

**Sorumluluklar.**

- İş kuralı ve API istisnalarını kod + durum ile yanıtlamak.
- Tek bir iptal yolu tanımlamak.
- PostgreSQL kısıt ihlallerini 409'a, erişilemezliği 503'e çevirmek.
- Beklenmeyen istisnaları 500 olarak günlüklemek.

**Tanımlanan tipler.** `class ApiException`, `class ApiExceptionMiddleware`, `class ApiErrorWriter`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| InvokeAsync | İstisna zinciri. Sıra anlamlıdır: ApiException/WorkflowRuleException → BadHttpRequest → JsonException → OperationCanceledException (yalnız RequestAborted iptal edilmişse) → PostgresException (unique/fk/check) → NpgsqlException → Exception. |
| WriteAsync (ApiErrorWriter) | Problem gövdesini yazar: code, message, traceId. |

**Bağımlılıklar.** `Npgsql`, ASP.NET Core.

**Çağıranlar.** Program.cs pipeline'ı.

**Durum ve yan etki.** Durumsuz middleware.

**İş kuralları / değişmezler.**

- İstemci bağlantıyı kopardığında sunucu hatası üretilmez ve yanıt gövdesi yazılmaz.
- `RequestAborted` iptal edilmemişken gelen bir `OperationCanceledException` gerçek bir iç arızadır ve 500'e düşer.

**Hata yönetimi.** INVALID_HTTP_REQUEST, INVALID_JSON, CONFIGURATION_CONFLICT, DATABASE_UNAVAILABLE.

**Güvenlik.** Yanıt gövdesi yığın izi taşımaz; ayrıntı yalnız sunucu günlüğüne yazılır.

**Testler.** `tests/TkgPda.Api.Tests` — her dal için ayrı vaka.


#### 27.4.6 src/TkgPda.Api/ApiSecurity.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 331 | Derin analiz |

**Amaç.** Kimlik, roller, politikalar, JWT üretimi/doğrulaması ve cihaz bağlama middleware'i.

**Sorumluluklar.**

- Rol kodlarını ve politika üyeliklerini tanımlamak.
- JWT seçeneklerini doğrulamak ve token üretmek.
- Cihaz bağlamayı zorlamak (`X-Device-Code`).
- İstemci tipi kısıtlarını uygulamak.

**Tanımlanan tipler.** `class RoleCodes`, `class PolicyNames`, `class ApiClaimNames`, `class ClientTypes`, `record JwtOptions`, `record AccessTokenResult`, `class JwtTokenService`, `class Pbkdf2PasswordHasher`, `class PrincipalExtensions`, `class MachineAccessService`, `class DeviceContextMiddleware`, `class ApiHeaders`, `class ApiContextKeys`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| JwtOptions.Validate | Issuer/audience zorunlu; imzalama anahtarı en az 32 bayt olmalı. Zayıf anahtarla başlatma yapısal olarak imkânsızdır. |
| Token üretimi | Kullanıcı rollerini `ClaimTypes.Role` olarak, cihaz kodunu `device_code` iddiası olarak ekler. |
| Cihaz middleware'i | Başlıktaki cihaz kodu ile token iddiasını karşılaştırır (`DEVICE_CONTEXT_MISMATCH`), ardından cihazın kayıtlı ve aktif olduğunu doğrular (`DEVICE_NOT_AUTHORIZED`), sonra cihazı istek bağlamına koyar. |
| IsAdministrator | ADMIN rol kontrolü kısayolu. |

**Bağımlılıklar.** `Microsoft.IdentityModel.Tokens`, `System.IdentityModel.Tokens.Jwt`, `IApiDataStore`.

**Çağıranlar.** `Program.cs`, tüm uç noktalar.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- WP-05A: READY işaretleme bir depo hazırlık eylemidir; LINE_OPERATOR bilinçli olarak dışlanmıştır.
- Admin Web token'ı ADMIN rolü taşımalıdır.

**Hata yönetimi.** DEVICE_CONTEXT_MISMATCH, DEVICE_NOT_AUTHORIZED, ADMIN_WEB_ROLE_REQUIRED, AMBIGUOUS_LOGIN_IDENTITY.

**Güvenlik.** Bilgi güvenliğinin merkezi. İmzalama anahtarı yapılandırmadan gelir ve depoda bulunmaz — `<JWT_SIGNING_KEY>`.

**Testler.** `tests/TkgPda.Auth.Tests`, `tests/TkgPda.Api.Tests`.


#### 27.4.7 src/TkgPda.Api/PostOutcomeAudit.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 52 | Derin analiz |

**Amaç.** Fiziksel sonuç gerçekleştikten sonra yazılan denetim kaydını, çağıranın iptal edilmiş olabileceği koşullarda bile en iyi çabayla yazan küçük yardımcı.

**Sorumluluklar.**

- Kendi 5 saniyelik sınırlı token'ı altında çalışmak.
- `OperationCanceledException` dahil tüm istisnaları yutmak.
- Çağıranın akışını asla etkilememek.

**Tanımlanan tipler.** `class PostOutcomeAudit`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| RunAsync | Verilen denetim yazma işini kendi bounded token'ı ile çalıştırır. Hiçbir istisna dışarı çıkmaz; başarısızlık sessizce yutulur. |

**Bağımlılıklar.** Yok (delegate alır).

**Çağıranlar.** `RdsExecutionCoordinator` uzman ve dispatch yolları.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- Denetim yazımı başarısız olduğu için fiziksel operasyon ASLA tekrar denenmez.

**Hata yönetimi.** Yok — bilinçli olarak hiçbir hata yaymaz.

**Güvenlik.** Robot çoktan hareket etmişken denetim hatasının operasyonu tekrarlatması çift hareket üretirdi. Bu dosya o riski yapısal olarak kaldırır.

**Testler.** `tests/TkgPda.Api.Tests` — iptal edilmiş token ve fırlatan denetim yazıcı senaryoları.


#### 27.4.8 src/TkgPda.Api/Program.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 279 | Derin analiz |

**Amaç.** Uygulama kompozisyon kökü: yapılandırma bağlama, DI kaydı, kimlik doğrulama/yetkilendirme kurulumu, middleware sırası, uç nokta kaydı ve arka plan worker'ı.

**Sorumluluklar.**

- Yapılandırmayı seçenek tiplerine bağlamak ve doğrulamak.
- Store ve adaptör implementasyonlarını kaydetmek.
- Middleware sırasını kurmak (hata → kimlik → cihaz → uçlar).
- RDS worker'ını `Rds:Enabled` durumuna göre kaydetmek.
- Başlangıçta güvenli bir durum özeti günlüklemek.

**Tanımlanan tipler.** `class Program`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Başlangıç günlüğü | `RDS Integration: {Status}, RDS Dispatch: {Dispatch}, RDSCore: {Endpoint}` satırı. Devreye alma mühendisinin kapının durumunu tek bakışta görmesi için vardır ve adresi `BaseAddressDisplaySafe` üzerinden yazar. |

**Bağımlılıklar.** Tüm katmanlar.

**Çağıranlar.** Host.

**Durum ve yan etki.** Kompozisyon kökü.

**İş kuralları / değişmezler.**

- Sırlar yapılandırma dosyasından değil ortamdan gelir.
- `Rds:Enabled=false` iken worker hiç kaydedilmez.

**Hata yönetimi.** Başlangıç doğrulama hataları (eksik bağlantı dizesi, kısa imzalama anahtarı).

**Güvenlik.** Günlüklenen hiçbir alan sır içermez.

**Testler.** `tests/TkgPda.Api.Tests` barındırılan host testleri.


#### 27.4.9 src/TkgPda.Api/RdsCommissioningDiscoveryClient.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 574 | Yapısal |

**Rol.** API katmanı: HTTP yüzeyi, güvenlik ve RDS adaptörleri.

**Tanımlanan tipler.** `record RdsCoreDiscoveryOptions`, `record RdsCoreDiscoveryOperationDescriptor`, `class RdsCoreDiscoveryClient`, `enum Operation`, `record Spec`, `record CallResult`

**Tanımlanan üyeler.** `FromConfiguration`, `Validate`, `CaptureAsync`, `TryCallAsync`, `ParseRobots`, `ParseAlarmBuckets`, `ParseBins`, `ParseDevices`, `MergeParkPoints`, `ParseChargeParameters`, `ParseIdArray`, `ToProbe`, `BusinessCode`, `ValidateCommand`, `BindPathToken`, `ValidateValues`, `ValidateValue`, `Sanitize`, `WriteSanitized`, `IsSecretName`, `Object`, `Property`, `Text`, `Boolean`, `Integer`, `Double`, `Strings`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.4.10 src/TkgPda.Api/RdsCoreNativeOrderClient.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 914 | Derin analiz |

**Amaç.** RDSCore native order HTTP adaptörü. İzin verilen operasyonların kayıt tablosunu tutar, istekleri kurar, yanıtları ayrıştırır ve telemetriyi tiplere çevirir.

**Sorumluluklar.**

- Operasyon kayıt tablosu (`Registry`): metot, yol ve mutasyon bayrağı.
- `/robotsStatus` yanıtını `RdsCoreFleetStatus`'a çevirmek — robotlar ve kök alarmlar birlikte.
- Mutasyon operasyonlarını `MutationEnabled` ile kapılamak.
- Zaman aşımı, güvensiz uzak HTTP ve hata kodu haritalamasını uygulamak.

**Tanımlanan tipler.** `record RdsCoreExecutionOptions`, `class RdsCoreConfiguration`, `record RdsCoreNativeOrderOperationDescriptor`, `class RdsCoreNativeOrderClient`, `enum Operation`, `record Spec`, `record RdsCoreHttpResponse`, `class RdsCoreRobotTelemetryProvider`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| SetOrderAsync | `/setOrder`. Fiziksel hareketi başlatan çağrı. |
| GetRobotsStatusAsync | `/robotsStatus`. Robot listesini ve kök alarmları tek anlık görüntüde döndürür. `procBusiness` yoksa null bırakılır — UNKNOWN, boşta değil. |
| TerminateAsync | `/terminate`. SafetyStop. |
| LockAsync / UnlockAsync / SwitchMapAsync | Harita hazırlığı üçlüsü. ExecutionMutation. |
| ManualFinishedAsync / RedoFailedOrderAsync / ClearRobotAllErrorAsync / GotoSiteResumeAsync | MotionEnabling operasyonları. |
| GotoSitePauseAsync / DispatchableAsync(undispatchable) | SafetyStop operasyonları. |
| GetOrderDetailsAsync / GetOrdersAsync / GetOrderDetailsByExternalIdAsync / GetOrderDetailsByBlockIdAsync / GetBlockDetailsByIdAsync | Salt-okunur sipariş sorguları. |
| AllowedOperations | Kayıt tablosunu dışarıya açar; Admin Web teşhis sayfası hangi operasyonların var olduğunu buradan öğrenir. |

**Bağımlılıklar.** `HttpClient`, `RdsRobotAlarmParser`, `RdsRobotIdentity`, `RdsCoreExecutionOptions`.

**Çağıranlar.** `RdsExecutionCoordinator`, `AdminEndpoints` teşhis uçları.

**Durum ve yan etki.** Durumsuz; `HttpClient` DI ile verilir.

**İş kuralları / değişmezler.**

- Kayıt tablosunda olmayan hiçbir RDSCore yolu çağrılamaz.
- Telemetri okuma daima paylaşılan kimlik ve alarm parser'ını kullanır.

**Hata yönetimi.** `RdsCoreNativeOrderException` ve taşıdığı kodlar.

**Güvenlik.** `BaseAddressDisplaySafe` günlüklemede adres bileşenlerinin sızmasını engeller; `AllowInsecureRemoteHttp=false` uzak düz HTTP'yi reddeder.

**Testler.** `tests/TkgPda.Api.Tests/RdsCoreNativeOrderClientTests.cs`.


#### 27.4.11 src/TkgPda.Api/RdsExecutionClient.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 284 | Yapısal |

**Rol.** API katmanı: HTTP yüzeyi, güvenlik ve RDS adaptörleri.

**Tanımlanan tipler.** `class RdsExecutionClient`, `class RdsExecutionWorker`

**Tanımlanan üyeler.** `ValidateTransportAsync`, `CreateTransportAsync`, `StopTaskAsync`, `UpdateContentAsync`, `PostScriptAsync`, `PostAsync`, `Data`, `UnwrapJson`, `NormalizeEnvelope`, `Text`, `Number`, `Boolean`, `ExecuteAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.4.12 src/TkgPda.Api/RdsHttpReadTransport.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 34 | Yapısal |

**Rol.** API katmanı: HTTP yüzeyi, güvenlik ve RDS adaptörleri.

**Tanımlanan tipler.** `record RdsHttpJsonResponse`, `class RdsHttpReadTransport`

**Tanımlanan üyeler.** `Dispose`, `SendAsync`


#### 27.4.13 src/TkgPda.Api/RdsIntegrationEndpoints.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 55 | Derin analiz |

**Amaç.** RDS'in TKG'ye geri bildirim gönderdiği callback ucu. Küçük ama güvenlik-kritiktir.

**Sorumluluklar.**

- Callback anahtarını doğrulamak.
- Doğrulanan olayı koordinatöre iletmek.

**Tanımlanan tipler.** `record RdsCallbackHttpRequest`, `class RdsIntegrationEndpoints`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Callback handler | Yapılandırılmış bir `CallbackKey` yoksa hiçbir isteği kabul etmez (fail-closed). Anahtar karşılaştırması `FixedTimeEquals` ile sabit zamanlıdır. |

**Bağımlılıklar.** `RdsOptions`, `RdsExecutionCoordinator`.

**Çağıranlar.** RDS (dışarıdan).

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- Anahtar yapılandırılmamışsa uç kapalıdır.
- Callback uzlaştırmanın yerine geçmez; onu hızlandırır.

**Hata yönetimi.** Yetkisiz callback reddi.

**Güvenlik.** Sabit zamanlı karşılaştırma, anahtarın zamanlama saldırısıyla çıkarılmasını engeller.

**Testler.** `tests/TkgPda.Api.Tests`.


#### 27.4.14 src/TkgPda.Api/RdsReadClient.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | API | 556 | Yapısal |

**Rol.** API katmanı: HTTP yüzeyi, güvenlik ve RDS adaptörleri.

**Tanımlanan tipler.** `record RdsOptions`, `class RdsReadClient`

**Tanımlanan üyeler.** `FromConfiguration`, `Validate`, `GetWorkSitesAsync`, `QueryWindTasksAsync`, `GetRobotAlarmsAsync`, `AppendAlarms`, `UndispatchableReasonText`, `ReadAsync`, `MarkSuccess`, `Text`, `Number`, `TryNumber`, `Double`, `Bool`, `ReadLockInfo`, `TimeToken`, `Add`, `Failure`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.4.15 src/TkgPda.Api/TkgPda.Api.csproj

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild proje dosyası | API | 11 | Kısa |

**Rol.** API katmanı: HTTP yüzeyi, güvenlik ve RDS adaptörleri.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.4.16 src/TkgPda.Api/appsettings.json

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JSON yapılandırma | API | 49 | Yapısal |

**Rol.** API katmanı: HTTP yüzeyi, güvenlik ve RDS adaptörleri.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


### 27.5 Operator PDA — Flutter (57)


#### 27.5.1 src/TkgPda.Pda/.gitignore

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dosya | Operator PDA | 46 | Yapısal |

**Rol.** Flutter proje yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.5.2 src/TkgPda.Pda/README.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Operator PDA | 26 | Yapısal |

**Rol.** Flutter proje yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.5.3 src/TkgPda.Pda/analysis_options.yaml

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| YAML yapılandırma | Operator PDA | 31 | Yapısal |

**Rol.** Flutter proje yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.5.4 src/TkgPda.Pda/integration_test/operator_web_e2e_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 499 | Yapısal |

**Rol.** Uçtan uca operatör senaryosu; gerçek API'ye karşı çalışır.

**Tanımlanan üyeler.** `check`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `package:flutter_test/flutter_test.dart`, `package:integration_test/integration_test.dart`, `package:tkg_pda/app/app.dart`, `package:tkg_pda/app/app_controller.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/network/api_client.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `package:tkg_pda/core/storage/device_code_store_web.dart`, `package:tkg_pda/data/models/pda_models.dart`, `package:tkg_pda/data/repositories/pda_repository.dart`, `package:web/web.dart`


#### 27.5.5 src/TkgPda.Pda/lib/app/app.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 53 | Yapısal |

**Rol.** Uygulama kabuğu: yönlendirme, tema ve kök widget.

**Tanımlanan tipler.** `class TkgPdaApp`, `class _TkgPdaAppState`

**Tanımlanan üyeler.** `initState`, `didChangeAppLifecycleState`, `dispose`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `app_controller.dart`, `router.dart`, `theme.dart`


#### 27.5.6 src/TkgPda.Pda/lib/app/app_controller.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 1790 | Derin analiz |

**Amaç.** Operatör uygulamasının tüm durumunu tutan tek `ChangeNotifier`. 1790 satırdır ve tek olması kasıtlıdır: operatör akışı doğrusaldır, durumun tek yerde olması iki ekranın çelişkili bilgi göstermesini imkânsız kılar.

**Sorumluluklar.**

- Aşama makinesini (`AppStage`) yönetmek.
- Kimlik yaşam döngüsünü yürütmek.
- Makine/varyant/parça seçim akışını yönetmek.
- Kasa aksiyonlarını ve batch gönderimini koordine etmek.
- Depo hazırlık akışını yürütmek.
- Hata ve yükleme durumlarını arayüze taşımak.

**Tanımlanan tipler.** `enum AppStage`, `class EffectivePartSelection`, `class BatchCartItem`, `class AppController`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| initialize | Cihaz kodunu yükler, oturumu geri yükler, başlangıç aşamasını belirler. |
| login / logout / handleUnauthorized | 401 alındığında oturum belleği temizlenir ve giriş ekranına dönülür. |
| selectMachine / refreshContext | Operasyonel bağlamı yükler ve tüm panelleri besler. |
| switchVariant | Varyant değiştirir; aktif rezervasyon varsa sunucu 409 ile reddeder ve mesaj gösterilir. |
| submitContainerAction | WP-33 doğrudan gönderim. Onay diyaloğu yoktur. |
| removeAllContainers | Varyant kapsamlı toplu kaldırma; gönderim sırasında buton devre dışıdır. |
| enterBatchSelection / addPartToBatch / beginBatchConfiguration / confirmBatchItemCommand / submitBatch | Çok parçalı sepet akışı; her kalem ayrı yapılandırılır ve tek batch olarak gönderilir. |
| skipInvalidBatchItem / reconfigureBatchItem / retryAmbiguousBatch | Batch içindeki hatalı kalemlerin ele alınması; belirsiz batch körlemesine tekrarlanmaz. |
| enterWarehousePreparation / markPreparationReady | Depo hazırlık akışı. |
| retryRobotAlarmSummary | Alarm bandını yeniden yükler. |
| isSubmittingContainerAction / operationInProgress | Arayüzün buton durumlarını hesaplaması için gönderim bayrakları. |

**Bağımlılıklar.** `PdaRepository`, `SubmissionCoordinator`, `SessionMemory`, `DeviceCodeStore`, `ApiClient`.

**Çağıranlar.** Tüm ekranlar.

**Durum ve yan etki.** Uygulamanın tek durum sahibi. `notifyListeners` ile arayüzü besler.

**İş kuralları / değişmezler.**

- Fiziksel slot kimliği hiçbir zaman istemcide bulunmaz.
- Aynı anda tek gönderim; `SubmissionCoordinator` bunu zorlar.

**Hata yönetimi.** `ApiProblem` kodları Türkçe mesajlara çevrilerek gösterilir.

**Güvenlik.** JWT yalnız bellekte; diske yazılmaz.

**Testler.** `src/TkgPda.Pda/test/widget/*` ve `test/unit/*`.


#### 27.5.7 src/TkgPda.Pda/lib/app/router.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 63 | Yapısal |

**Rol.** Uygulama kabuğu: yönlendirme, tema ve kök widget.

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `package:go_router/go_router.dart`, `../features/device_setup/device_setup_screen.dart`, `../features/login/login_screen.dart`, `../features/machines/machine_list_screen.dart`, `../features/operation/operation_screen.dart`, `../features/tasks/operator_queue_screen.dart`, `../features/warehouse/warehouse_preparation_screen.dart`, `app_controller.dart`


#### 27.5.8 src/TkgPda.Pda/lib/app/theme.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 57 | Yapısal |

**Rol.** Uygulama kabuğu: yönlendirme, tema ve kök widget.

**Proje içi bağımlılıklar.** `package:flutter/material.dart`


#### 27.5.9 src/TkgPda.Pda/lib/core/auth/session_memory.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 32 | Yapısal |

**Rol.** Oturum belleği. JWT yalnız bellekte tutulur, diske yazılmaz.

**Tanımlanan tipler.** `class SessionMemory`

**Tanımlanan üyeler.** `establish`, `clear`

**Proje içi bağımlılıklar.** `../../data/models/pda_models.dart`


#### 27.5.10 src/TkgPda.Pda/lib/core/config/app_config.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 41 | Yapısal |

**Rol.** Çalışma zamanı yapılandırma yükleyicisi; web ve yerel platformlar için koşullu import ile ayrışır.

**Tanımlanan tipler.** `class AppConfig`

**Proje içi bağımlılıklar.** `../platform/platform_info.dart`


#### 27.5.11 src/TkgPda.Pda/lib/core/config/runtime_config_loader.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 17 | Kısa |

**Rol.** Çalışma zamanı yapılandırma yükleyicisi; web ve yerel platformlar için koşullu import ile ayrışır.

**Tanımlanan tipler.** `class RuntimeAppConfig`

**Tanımlanan üyeler.** `load`

**Proje içi bağımlılıklar.** `runtime_config_loader_stub.dart`


#### 27.5.12 src/TkgPda.Pda/lib/core/config/runtime_config_loader_stub.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 12 | Kısa |

**Rol.** Çalışma zamanı yapılandırma yükleyicisi; web ve yerel platformlar için koşullu import ile ayrışır.

**Tanımlanan tipler.** `class NoopRuntimeConfigLoader`

**Tanımlanan üyeler.** `load`

**Proje içi bağımlılıklar.** `runtime_config_loader.dart`


#### 27.5.13 src/TkgPda.Pda/lib/core/config/runtime_config_loader_web.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 48 | Yapısal |

**Rol.** Çalışma zamanı yapılandırma yükleyicisi; web ve yerel platformlar için koşullu import ile ayrışır.

**Tanımlanan tipler.** `class WebRuntimeConfigLoader`

**Tanımlanan üyeler.** `load`

**Proje içi bağımlılıklar.** `package:http/http.dart`, `runtime_config_loader.dart`


#### 27.5.14 src/TkgPda.Pda/lib/core/errors/api_problem.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 66 | Yapısal |

**Rol.** Sunucu problem gövdesinin Dart tarafı temsili ve kullanıcıya gösterilecek mesaj üretimi.

**Tanımlanan tipler.** `class ApiProblem`, `class ConnectivityException`, `class AmbiguousMutationException`

**Tanımlanan üyeler.** `toString`


#### 27.5.15 src/TkgPda.Pda/lib/core/network/api_client.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 154 | Yapısal |

**Rol.** Operatör uygulamasının HTTP taşıma katmanı: JWT ve cihaz başlığı ekleme, zaman aşımı, hata gövdesi ayrıştırma.

**Tanımlanan tipler.** `class ApiResponse`, `class ApiClient`

**Tanımlanan üyeler.** `get`, `post`, `put`, `_send`, `close`

**Proje içi bağımlılıklar.** `package:http/http.dart`, `package:uuid/uuid.dart`, `../errors/api_problem.dart`, `../../data/models/pda_models.dart`


#### 27.5.16 src/TkgPda.Pda/lib/core/platform/platform_info.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 14 | Kısa |

**Rol.** Platform tespiti; koşullu import ile web ve IO ayrılır.

**Tanımlanan tipler.** `class PlatformInfo`

**Proje içi bağımlılıklar.** `platform_info_stub.dart`


#### 27.5.17 src/TkgPda.Pda/lib/core/platform/platform_info_io.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 4 | Kısa |

**Rol.** Platform tespiti; koşullu import ile web ve IO ayrılır.

**Proje içi bağımlılıklar.** `platform_info.dart`


#### 27.5.18 src/TkgPda.Pda/lib/core/platform/platform_info_stub.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 4 | Kısa |

**Rol.** Platform tespiti; koşullu import ile web ve IO ayrılır.

**Proje içi bağımlılıklar.** `platform_info.dart`


#### 27.5.19 src/TkgPda.Pda/lib/core/platform/platform_info_web.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 10 | Kısa |

**Rol.** Platform tespiti; koşullu import ile web ve IO ayrılır.

**Proje içi bağımlılıklar.** `platform_info.dart`


#### 27.5.20 src/TkgPda.Pda/lib/core/storage/device_code_store.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 55 | Yapısal |

**Rol.** Cihaz kodu kalıcı depolaması. Cihaz kodu bir sır değil, cihaz kimliğidir; bu yüzden kalıcıdır.

**Tanımlanan tipler.** `class PersistentDeviceCodeStore`, `class MemoryDeviceCodeStore`

**Tanımlanan üyeler.** `read`, `write`, `clear`, `remove`


#### 27.5.21 src/TkgPda.Pda/lib/core/storage/device_code_store_factory.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 9 | Kısa |

**Rol.** Cihaz kodu kalıcı depolaması. Cihaz kodu bir sır değil, cihaz kimliğidir; bu yüzden kalıcıdır.

**Proje içi bağımlılıklar.** `device_code_store.dart`, `device_code_store_factory_stub.dart`


#### 27.5.22 src/TkgPda.Pda/lib/core/storage/device_code_store_factory_secure.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 5 | Kısa |

**Rol.** Cihaz kodu kalıcı depolaması. Cihaz kodu bir sır değil, cihaz kimliğidir; bu yüzden kalıcıdır.

**Proje içi bağımlılıklar.** `device_code_store.dart`, `device_code_store_secure.dart`


#### 27.5.23 src/TkgPda.Pda/lib/core/storage/device_code_store_factory_stub.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 4 | Kısa |

**Rol.** Cihaz kodu kalıcı depolaması. Cihaz kodu bir sır değil, cihaz kimliğidir; bu yüzden kalıcıdır.

**Proje içi bağımlılıklar.** `device_code_store.dart`


#### 27.5.24 src/TkgPda.Pda/lib/core/storage/device_code_store_factory_web.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 5 | Kısa |

**Rol.** Cihaz kodu kalıcı depolaması. Cihaz kodu bir sır değil, cihaz kimliğidir; bu yüzden kalıcıdır.

**Proje içi bağımlılıklar.** `device_code_store.dart`, `device_code_store_web.dart`


#### 27.5.25 src/TkgPda.Pda/lib/core/storage/device_code_store_secure.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 27 | Yapısal |

**Rol.** Cihaz kodu kalıcı depolaması. Cihaz kodu bir sır değil, cihaz kimliğidir; bu yüzden kalıcıdır.

**Tanımlanan tipler.** `class SecureDeviceCodeStore`

**Tanımlanan üyeler.** `read`, `write`, `clear`

**Proje içi bağımlılıklar.** `package:flutter_secure_storage/flutter_secure_storage.dart`, `device_code_store.dart`


#### 27.5.26 src/TkgPda.Pda/lib/core/storage/device_code_store_web.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 21 | Kısa |

**Rol.** Cihaz kodu kalıcı depolaması. Cihaz kodu bir sır değil, cihaz kimliğidir; bu yüzden kalıcıdır.

**Tanımlanan tipler.** `class WebDeviceCodeStore`, `class WebLocalStorage`

**Tanımlanan üyeler.** `remove`, `write`

**Proje içi bağımlılıklar.** `package:web/web.dart`, `device_code_store.dart`


#### 27.5.27 src/TkgPda.Pda/lib/data/models/pda_models.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 1117 | Yapısal |

**Rol.** API sözleşmesinin Dart tarafı modelleri; JSON ayrıştırma burada yapılır.

**Tanımlanan tipler.** `class AuthenticatedUser`, `class PdaDevice`, `class LoginSession`, `class MachinePermissions`, `class MachineSummary`, `class VariantLayoutOption`, `class MachineRelatedPart`, `class MachineMainPartOption`, `class CompatibleVariant`, `class ContainerActionAvailability`, `class ContainerPosition`, `class ContainerActionResult`, `class RemoveAllResult`, `class ContainerActionBatchItemCommand`, `class OperatorQueueItem`, `class OperatorQueuePage`, `class ExecutionReadiness`, `class AvailableAction`, `class LayoutSlot`, `class ProductGroupPart`, `class ProductGroup`, `class TaskTemplate`, `class ExecutionProjection`, `class ActiveTransportRequest`, `class OperationalContext`, `class TransportCommand`, `class TransportResult`, `class WarehousePreparationRequest`, `class RobotAlarmSummary`, `class TransportBatchItemCommand`, `class TransportBatchResult`, `class RobotTelemetry`, `class PendingOperation`, `enum MutationGateReason`, `class MutationGate`

**Tanımlanan üyeler.** `toString`


#### 27.5.28 src/TkgPda.Pda/lib/data/repositories/pda_repository.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 385 | Derin analiz |

**Amaç.** Operatör uygulamasının tek HTTP erişim noktası. Her API çağrısı burada tiplenir.

**Sorumluluklar.**

- API uçlarını tip güvenli metotlara sarmak.
- Idempotency başlığını iletmek.
- Hata gövdelerini `ApiProblem`'e çevirmek.

**Tanımlanan tipler.** `class PdaRepository`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| login / me | Kimlik uçları. |
| operationalContext / switchVariant | Bağlam ve varyant. |
| createTransport / createTransportBatch | Genel taşıma oluşturma. |
| createContainerAction / createContainerActionBatch / removeAllContainers | WP-33 kasa aksiyonları. |
| operatorQueue / transport / cancel | Kuyruk ve iptal. |
| markTransportReady | Depo hazırlık. |
| robotAlarmSummary | Operatöre gösterilen alarm bandı verisi. |
| pauseRobot / resumeRobot / clearRobotErrors / setRequestPriority | Yetkili roller için uzman operasyonlar; sunucu tarafında mutasyon kapısına tabidir. |

**Bağımlılıklar.** `ApiClient`, `pda_models.dart`, `api_problem.dart`.

**Çağıranlar.** `AppController`.

**Durum ve yan etki.** Durumsuz.

**İş kuralları / değişmezler.**

- İstemci hiçbir zaman RDS ile doğrudan konuşmaz.

**Hata yönetimi.** `ApiProblem` üzerinden sunucu kodları.

**Güvenlik.** Tüm istekler JWT ve `X-Device-Code` taşır.

**Testler.** `src/TkgPda.Pda/test/support/fakes.dart` ile sahtelenir; widget testleri bunu kullanır.


#### 27.5.29 src/TkgPda.Pda/lib/features/device_setup/device_setup_screen.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 126 | Yapısal |

**Rol.** Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez.

**Tanımlanan tipler.** `class DeviceSetupScreen`, `class _DeviceSetupScreenState`

**Tanımlanan üyeler.** `initState`, `dispose`, `_save`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../app/app_controller.dart`


#### 27.5.30 src/TkgPda.Pda/lib/features/login/login_screen.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 151 | Yapısal |

**Rol.** Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez.

**Tanımlanan tipler.** `class LoginScreen`, `class _LoginScreenState`

**Tanımlanan üyeler.** `dispose`, `_login`, `_confirmDeviceChange`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../app/app_controller.dart`, `../../shared/widgets/feedback_banners.dart`


#### 27.5.31 src/TkgPda.Pda/lib/features/machines/machine_list_screen.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 188 | Yapısal |

**Rol.** Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez.

**Tanımlanan tipler.** `class MachineListScreen`, `class _MachineCard`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../app/app_controller.dart`, `../../data/models/pda_models.dart`, `../../shared/widgets/feedback_banners.dart`


#### 27.5.32 src/TkgPda.Pda/lib/features/operation/container_position_panel.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 430 | Derin analiz |

**Amaç.** WP-33 iş aksiyon yüzeyi. Yalnız backend'in mantıksal pozisyon projeksiyonunu tüketir ve hiçbir zaman fiziksel slot tanımlayıcısı almaz.

**Sorumluluklar.**

- Yatay kaydırmalı pozisyon kartı listesi çizmek.
- Her pozisyon için iki iş butonu sunmak.
- Devre dışı sebebini görünür metin olarak göstermek.
- Varyant kapsamlı "Tüm Kasaları Kaldır" aksiyonunu sunmak.
- Taşıma durumunu erişilebilir bir rozetle göstermek.

**Tanımlanan tipler.** `class ContainerPositionPanel`, `class _PositionCard`, `class _BusinessActionButton`, `class _StateBadge`, `class _StateBadgeState`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| build (ContainerPositionPanel) | Yükleniyor, hata, boş ve dolu durumlarını ayrı ayrı ele alır. Boş durumda `no-container-positions` anahtarlı açıklayıcı metin gösterilir. |
| _PositionCard.build | Pozisyon etiketi (birden çok pozisyon varsa `n / toplam` ile), durum rozeti ve iki iş butonu. |
| _BusinessActionButton.build | Etkinlik üç koşulun birleşimidir: `availability.enabled`, `mutationGate.canMutate`, `!submitting`. Devre dışıysa sebep hem tooltip'te hem buton içinde küçük punto ile basılır — bir el terminalinde hover olmadığı için tooltip tek başına yeterli değildir. |
| _StateBadgeState._syncAnimation | Yalnız REQUESTED, QUEUED, IN_PROGRESS için nabız animasyonu kurar; diğer durumlarda controller dispose edilir. `MediaQuery.disableAnimations` etkinse animasyon hiç kurulmaz ve `Semantics` etiketi her iki yolda da aynı bilgiyi taşır. |
| _stateLabel | Sunum durumlarını Türkçe etiketlere çevirir; bilinmeyen değer "Güncelleniyor" olur. |

**Bağımlılıklar.** `AppController`, `pda_models.dart`.

**Çağıranlar.** `operation_screen.dart`.

**Durum ve yan etki.** Durumsuz widget; durum `AppController`'dadır. Yalnız `_StateBadge` kendi animasyon controller'ını tutar ve `dispose` eder.

**İş kuralları / değişmezler.**

- Buton anahtarı MANTIKSAL iş aksiyonunu adlandırır ve kasa numarasını içerir: `direct-action-{ACTION}-{positionNumber}`.
- Onay diyaloğu yoktur; WP-33 doğrudan gönderim ister.
- "Tüm Kasaları Kaldır" yalnız batch yapılandırma modunda değilken gösterilir.

**Hata yönetimi.** Sunucu tarafı sebepler `availability.disabledMessage` ile gelir ve olduğu gibi gösterilir.

**Güvenlik.** Fiziksel slot kimliği bu widget'a hiç ulaşmaz.

**Testler.** `src/TkgPda.Pda/test/widget/wp33_operator_ux_test.dart`.


#### 27.5.33 src/TkgPda.Pda/lib/features/operation/multi_part_batch_screen.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 424 | Yapısal |

**Rol.** Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez.

**Tanımlanan tipler.** `class MultiPartBatchWorkspace`, `class _BatchPartBrowser`, `class _BatchPartBrowserState`, `class _CartSummary`, `class _AddPartsCard`, `class _AddRelatedPartChips`, `class _AddPartChip`, `class _BatchReview`

**Tanımlanan üyeler.** `_cartCountFor`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../app/app_controller.dart`, `../../data/models/pda_models.dart`, `../../shared/presentation.dart`


#### 27.5.34 src/TkgPda.Pda/lib/features/operation/operation_screen.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 480 | Yapısal |

**Rol.** Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez.

**Tanımlanan tipler.** `class OperationScreen`, `class _ActiveTransportIndicator`, `class _PartFirstWithBatchSkip`, `class _BatchConfiguringBanner`, `class _BatchProgressDots`, `class _AmbiguousSubmissionBanner`, `class _AmbiguousBatchBanner`, `class BatchCartTray`, `class _BatchCartTrayState`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../app/app_controller.dart`, `../../shared/widgets/feedback_banners.dart`, `container_position_panel.dart`, `part_first_screen.dart`


#### 27.5.35 src/TkgPda.Pda/lib/features/operation/part_first_screen.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 612 | Yapısal |

**Rol.** Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez.

**Tanımlanan tipler.** `class PartFirstSelector`, `class _PartFirstSelectorState`, `class _SectionLabel`, `class _MainPartChips`, `class _MainPartChip`, `class _EffectivePartCard`, `class _RelatedPartChips`, `class _RelatedPartChip`, `class _CompatibleVariantCard`, `class _RetryState`

**Tanımlanan üyeler.** `dispose`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../app/app_controller.dart`, `../../data/models/pda_models.dart`, `transport_action_panel.dart`


#### 27.5.36 src/TkgPda.Pda/lib/features/operation/transport_action_panel.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 145 | Yapısal |

**Rol.** Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez.

**Tanımlanan tipler.** `class OperatorActiveTransportList`, `class _TransportCard`

**Tanımlanan üyeler.** `_confirmCancel`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../app/app_controller.dart`, `../../data/models/pda_models.dart`, `../../shared/presentation.dart`


#### 27.5.37 src/TkgPda.Pda/lib/features/tasks/operator_queue_screen.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 450 | Yapısal |

**Rol.** Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez.

**Tanımlanan tipler.** `class OperatorQueueScreen`, `class _RobotStatusBar`, `class _RobotCard`, `class _QueueBody`, `class _QueueCard`

**Tanımlanan üyeler.** `_confirmCancel`, `two`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../app/app_controller.dart`, `../../data/models/pda_models.dart`, `../../shared/widgets/feedback_banners.dart`


#### 27.5.38 src/TkgPda.Pda/lib/features/transport/submission_coordinator.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 153 | Derin analiz |

**Amaç.** İstemci tarafı çift gönderim koruması ve idempotency anahtarı yaşam döngüsü.

**Sorumluluklar.**

- Maddi imzaya bağlı idempotency anahtarı üretmek.
- Aynı anda birden fazla gönderimi engellemek.
- Belirsiz mutasyon sonrası anahtarı korumak.

**Tanımlanan tipler.** `class SubmissionCoordinator`, `class BatchSubmissionCoordinator`

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| keyFor | Komutun maddi imzası değiştiyse yeni anahtar üretir, değişmediyse mevcut anahtarı döndürür. Böylece aynı iş tekrar denendiğinde sunucu tarafı idempotency devreye girer. |
| submit | `_inFlight` ise `StateError` fırlatır. Başarıda `clear()` çağırır. `AmbiguousMutationException` yakalanırsa `_ambiguous` işaretlenir ve **anahtar temizlenmez** — sonuç bilinmiyorken yeni anahtar üretmek çift fiziksel hareket riski doğururdu. |
| resolveByRefresh | Kullanıcı durumu yenileyerek belirsizliği çözdüğünde anahtarı temizler. |
| clear | İmza, anahtar ve belirsizlik bayrağını sıfırlar. |

**Bağımlılıklar.** `uuid`, `ApiProblem`, `pda_models.dart`.

**Çağıranlar.** `AppController.submitContainerAction` ve batch gönderim yolları.

**Durum ve yan etki.** Maddi imza, bekleyen anahtar, uçuş ve belirsizlik bayrakları.

**İş kuralları / değişmezler.**

- Belirsiz mutasyon sonrası otomatik yeniden gönderim yoktur.
- Aynı anda yalnız bir taşıma isteği gönderilebilir.

**Hata yönetimi.** `StateError` (eşzamanlı gönderim), `AmbiguousMutationException` (yeniden fırlatılır).

**Güvenlik.** Bir kasa taşıma emri belirsiz durumdayken körlemesine tekrarlanmaz.

**Testler.** `src/TkgPda.Pda/test/unit/submission_presentation_test.dart`.


#### 27.5.39 src/TkgPda.Pda/lib/features/warehouse/warehouse_preparation_screen.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 305 | Yapısal |

**Rol.** Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez.

**Tanımlanan tipler.** `class WarehousePreparationScreen`, `class _Body`, `class _RequestCard`, `class _InfoRow`

**Tanımlanan üyeler.** `_confirmReady`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../app/app_controller.dart`, `../../data/models/pda_models.dart`, `../../shared/presentation.dart`, `../../shared/widgets/feedback_banners.dart`


#### 27.5.40 src/TkgPda.Pda/lib/main.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 37 | Yapısal |

**Rol.** Flutter giriş noktası; yapılandırmayı yükler ve kök widget'ı çalıştırır.

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `app/app.dart`, `app/app_controller.dart`, `core/auth/session_memory.dart`, `core/config/app_config.dart`, `core/config/runtime_config_loader.dart`, `core/network/api_client.dart`, `core/platform/platform_info.dart`, `core/storage/device_code_store_factory.dart`, `data/repositories/pda_repository.dart`


#### 27.5.41 src/TkgPda.Pda/lib/shared/presentation.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 102 | Yapısal |

**Rol.** Ekranlar arasında paylaşılan sunum yardımcıları ve geri bildirim bileşenleri.

**Proje içi bağımlılıklar.** `package:flutter/material.dart`


#### 27.5.42 src/TkgPda.Pda/lib/shared/widgets/feedback_banners.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 199 | Yapısal |

**Rol.** Ekranlar arasında paylaşılan sunum yardımcıları ve geri bildirim bileşenleri.

**Tanımlanan tipler.** `class OfflineBanner`, `class ProblemBanner`, `class RobotAlarmBanner`, `class NoticeBanner`, `class OperatorUserBanner`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `../../core/errors/api_problem.dart`, `../../data/models/pda_models.dart`


#### 27.5.43 src/TkgPda.Pda/pubspec.yaml

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| YAML yapılandırma | Operator PDA | 98 | Yapısal |

**Rol.** Flutter proje yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.5.44 src/TkgPda.Pda/test/support/fakes.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 994 | Yapısal |

**Rol.** Test sahteleri (fake repository) — üretim kodunda karşılığı yoktur.

**Tanımlanan tipler.** `class FakePdaRepository`

**Tanımlanan üyeler.** `login`, `me`, `operationalContext`, `switchVariant`, `createTransport`, `createTransportBatch`, `createContainerAction`, `createContainerActionBatch`, `removeAllContainers`, `operatorQueue`, `transport`, `cancel`, `markTransportReady`, `robotAlarmSummary`, `pauseRobot`, `resumeRobot`, `clearRobotErrors`, `setRequestPriority`

**Proje içi bağımlılıklar.** `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/data/models/pda_models.dart`, `package:tkg_pda/data/repositories/pda_repository.dart`


#### 27.5.45 src/TkgPda.Pda/test/unit/api_problem_presentation_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 34 | Yapısal |

**Rol.** Flutter birim testi; iş mantığı ve sunum dönüşümlerini widget ağacı olmadan doğrular.

**Proje içi bağımlılıklar.** `package:flutter_test/flutter_test.dart`, `package:tkg_pda/core/errors/api_problem.dart`


#### 27.5.46 src/TkgPda.Pda/test/unit/operator_integrity_phase1_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 386 | Yapısal |

**Rol.** Flutter birim testi; iş mantığı ve sunum dönüşümlerini widget ağacı olmadan doğrular.

**Tanımlanan üyeler.** `setupController`

**Proje içi bağımlılıklar.** `package:flutter_test/flutter_test.dart`, `package:tkg_pda/app/app_controller.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `package:tkg_pda/data/models/pda_models.dart`, `../support/fakes.dart`


#### 27.5.47 src/TkgPda.Pda/test/unit/security_network_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 352 | Yapısal |

**Rol.** Flutter birim testi; iş mantığı ve sunum dönüşümlerini widget ağacı olmadan doğrular.

**Tanımlanan tipler.** `class _MapStorage`

**Tanımlanan üyeler.** `remove`, `write`

**Proje içi bağımlılıklar.** `package:flutter_test/flutter_test.dart`, `package:flutter_secure_storage/flutter_secure_storage.dart`, `package:http/http.dart`, `package:http/testing.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/config/app_config.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/network/api_client.dart`, `package:tkg_pda/core/platform/platform_info.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `package:tkg_pda/core/storage/device_code_store_secure.dart`, `package:tkg_pda/data/models/pda_models.dart`, `package:tkg_pda/data/repositories/pda_repository.dart`, `../support/fakes.dart`


#### 27.5.48 src/TkgPda.Pda/test/unit/submission_presentation_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 163 | Yapısal |

**Rol.** Flutter birim testi; iş mantığı ve sunum dönüşümlerini widget ağacı olmadan doğrular.

**Proje içi bağımlılıklar.** `package:flutter_test/flutter_test.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/data/models/pda_models.dart`, `package:tkg_pda/features/transport/submission_coordinator.dart`, `package:tkg_pda/shared/presentation.dart`


#### 27.5.49 src/TkgPda.Pda/test/widget/multi_part_batch_flow_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 319 | Yapısal |

**Rol.** Flutter widget testi; gerçek widget ağacında operatör akışını doğrular.

**Tanımlanan tipler.** `class _Harness`

**Tanımlanan üyeler.** `dispose`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `package:flutter_test/flutter_test.dart`, `package:tkg_pda/app/app.dart`, `package:tkg_pda/app/app_controller.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `../support/fakes.dart`


#### 27.5.50 src/TkgPda.Pda/test/widget/operator_usability_phase3_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 231 | Yapısal |

**Rol.** Flutter widget testi; gerçek widget ağacında operatör akışını doğrular.

**Tanımlanan tipler.** `class _Harness`

**Tanımlanan üyeler.** `dispose`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `package:flutter_test/flutter_test.dart`, `package:tkg_pda/app/app.dart`, `package:tkg_pda/app/app_controller.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `package:tkg_pda/data/models/pda_models.dart`, `../support/fakes.dart`


#### 27.5.51 src/TkgPda.Pda/test/widget/part_first_flow_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 556 | Yapısal |

**Rol.** Flutter widget testi; gerçek widget ağacında operatör akışını doğrular.

**Tanımlanan tipler.** `class _Harness`

**Tanımlanan üyeler.** `dispose`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `package:flutter_test/flutter_test.dart`, `package:tkg_pda/app/app.dart`, `package:tkg_pda/app/app_controller.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `package:tkg_pda/data/models/pda_models.dart`, `../support/fakes.dart`


#### 27.5.52 src/TkgPda.Pda/test/widget/pda_flow_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 774 | Yapısal |

**Rol.** Flutter widget testi; gerçek widget ağacında operatör akışını doğrular.

**Tanımlanan tipler.** `class _Harness`

**Tanımlanan üyeler.** `dispose`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `package:flutter_test/flutter_test.dart`, `package:tkg_pda/app/app.dart`, `package:tkg_pda/app/app_controller.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `package:tkg_pda/data/models/pda_models.dart`, `../support/fakes.dart`


#### 27.5.53 src/TkgPda.Pda/test/widget/robot_alarm_banner_flow_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 139 | Yapısal |

**Rol.** Flutter widget testi; gerçek widget ağacında operatör akışını doğrular.

**Tanımlanan tipler.** `class _Harness`

**Tanımlanan üyeler.** `dispose`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `package:flutter_test/flutter_test.dart`, `package:tkg_pda/app/app.dart`, `package:tkg_pda/app/app_controller.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `package:tkg_pda/data/models/pda_models.dart`, `../support/fakes.dart`


#### 27.5.54 src/TkgPda.Pda/test/widget/warehouse_preparation_flow_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 263 | Yapısal |

**Rol.** Flutter widget testi; gerçek widget ağacında operatör akışını doğrular.

**Tanımlanan tipler.** `class _Harness`

**Tanımlanan üyeler.** `dispose`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `package:flutter_test/flutter_test.dart`, `package:tkg_pda/app/app.dart`, `package:tkg_pda/app/app_controller.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `package:tkg_pda/data/models/pda_models.dart`, `../support/fakes.dart`


#### 27.5.55 src/TkgPda.Pda/test/widget/wp33_operator_ux_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 372 | Yapısal |

**Rol.** Flutter widget testi; gerçek widget ağacında operatör akışını doğrular.

**Tanımlanan tipler.** `class _Harness`

**Tanımlanan üyeler.** `dispose`

**Proje içi bağımlılıklar.** `package:flutter/material.dart`, `package:flutter_test/flutter_test.dart`, `package:tkg_pda/app/app.dart`, `package:tkg_pda/app/app_controller.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/storage/device_code_store.dart`, `package:tkg_pda/data/models/pda_models.dart`, `../support/fakes.dart`


#### 27.5.56 src/TkgPda.Pda/test_driver/integration_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 4 | Kısa |

**Rol.** Flutter proje yapılandırması.

**Proje içi bağımlılıklar.** `package:integration_test/integration_test_driver.dart`


#### 27.5.57 src/TkgPda.Pda/tool/phase_07b/real_backend_contract_test.dart

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dart kaynak | Operator PDA | 353 | Yapısal |

**Rol.** Flutter proje yapılandırması.

**Tanımlanan tipler.** `class _ClientContext`

**Proje içi bağımlılıklar.** `package:flutter_test/flutter_test.dart`, `package:tkg_pda/core/auth/session_memory.dart`, `package:tkg_pda/core/errors/api_problem.dart`, `package:tkg_pda/core/network/api_client.dart`, `package:tkg_pda/data/models/pda_models.dart`, `package:tkg_pda/data/repositories/pda_repository.dart`


### 27.6 Admin Web — React (50)


#### 27.6.1 src/TkgPda.AdminWeb/.gitignore

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dosya | Admin Web | 33 | Yapısal |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.2 src/TkgPda.AdminWeb/.oxlintrc.json

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JSON yapılandırma | Admin Web | 9 | Kısa |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.3 src/TkgPda.AdminWeb/README.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Admin Web | 21 | Kısa |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.4 src/TkgPda.AdminWeb/e2e/admin-acceptance.spec.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 122 | Yapısal |

**Rol.** Playwright uçtan uca senaryosu; gerçek tarayıcı ve gerçek API kullanır.

**Proje içi bağımlılıklar.** `./support`


#### 27.6.5 src/TkgPda.AdminWeb/e2e/auth-security.spec.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 101 | Yapısal |

**Rol.** Playwright uçtan uca senaryosu; gerçek tarayıcı ve gerçek API kullanır.

**Proje içi bağımlılıklar.** `./support`


#### 27.6.6 src/TkgPda.AdminWeb/e2e/global-setup.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 143 | Yapısal |

**Rol.** Playwright uçtan uca senaryosu; gerçek tarayıcı ve gerçek API kullanır.

**Tanımlanan üyeler.** `run`, `applySqlFile`, `body`

**Kullandığı API aileleri.** `api.unref`


#### 27.6.7 src/TkgPda.AdminWeb/e2e/global-teardown.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 22 | Kısa |

**Rol.** Playwright uçtan uca senaryosu; gerçek tarayıcı ve gerçek API kullanır.

**Proje içi bağımlılıklar.** `./harness`


#### 27.6.8 src/TkgPda.AdminWeb/e2e/harness.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 131 | Yapısal |

**Rol.** Playwright uçtan uca senaryosu; gerçek tarayıcı ve gerçek API kullanır.

**Tanımlanan tipler.** `RuntimeInfo`

**Tanımlanan üyeler.** `adminWebRoot`, `repoRoot`, `API_URL`, `PG_PORT`, `PG_DATABASE`, `PG_BIN`, `TEMP_DIR_NAME`, `TEMP_ROOT`, `RUNTIME_FILE`, `ADMIN_USERNAME`, `createRuntimeSecrets`, `buildConnectionString`, `pgTool`, `psqlArgs`, `psql`, `hashPassword`, `writeRuntime`, `readRuntime`, `removeRuntime`, `ensureLogFile`, `removeTempRoot`, `stopPostgres`, `killProcessTree`, `sleep`, `assertPortAvailable`


#### 27.6.9 src/TkgPda.AdminWeb/e2e/layout-acceptance.spec.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 209 | Yapısal |

**Rol.** Playwright uçtan uca senaryosu; gerçek tarayıcı ve gerçek API kullanır.

**Tanımlanan üyeler.** `slotName`, `currentLayout`, `currentSlot`, `style`

**Proje içi bağımlılıklar.** `./harness`, `./support`


#### 27.6.10 src/TkgPda.AdminWeb/e2e/support.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 61 | Yapısal |

**Rol.** Playwright uçtan uca senaryosu; gerçek tarayıcı ve gerçek API kullanır.

**Tanımlanan üyeler.** `uniqueCode`, `loginAsAdmin`, `loginAdminApi`, `dumpWebStorage`, `indexedDbDatabases`, `runtime`, `body`, `read`

**Proje içi bağımlılıklar.** `./harness`


#### 27.6.11 src/TkgPda.AdminWeb/index.html

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| HTML | Admin Web | 15 | Kısa |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.12 src/TkgPda.AdminWeb/package.json

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JSON yapılandırma | Admin Web | 39 | Yapısal |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.13 src/TkgPda.AdminWeb/playwright.config.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 33 | Yapısal |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.14 src/TkgPda.AdminWeb/src/App.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 47 | Yapısal |

**Rol.** Admin Web uygulama kökü.

**Tanımlanan üyeler.** `ProtectedLayout`

**Proje içi bağımlılıklar.** `./auth/AuthContext`, `./components/AppShell`, `./pages/AuditPage`, `./pages/CatalogPage`, `./pages/ContainersPage`, `./pages/DashboardPage`, `./pages/DataQualityPage`, `./pages/DevicesPage`, `./pages/LayoutEditorPage`, `./pages/LayoutsPage`, `./pages/LoginPage`, `./pages/MachinesPage`, `./pages/NotFoundPage`, `./pages/RdsIntegrationPage`, `./pages/UsersPage`


#### 27.6.15 src/TkgPda.AdminWeb/src/api/ApiContext.test.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 24 | Kısa |

**Rol.** Admin Web API istemcisi ve tip sözleşmesi. Her uç burada tiplenir.

**Tanımlanan üyeler.** `LogoutProbe`

**Proje içi bağımlılıklar.** `../auth/AuthContext`, `../test/fixtures`, `./ApiContext`


#### 27.6.16 src/TkgPda.AdminWeb/src/api/ApiContext.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 28 | Yapısal |

**Rol.** Admin Web API istemcisi ve tip sözleşmesi. Her uç burada tiplenir.

**Tanımlanan üyeler.** `ApiProvider`, `useAdminApi`

**Kullandığı React hook'ları.** `useEffect`, `useMemo`, `useRef`

**Proje içi bağımlılıklar.** `./client`, `../auth/AuthContext`


#### 27.6.17 src/TkgPda.AdminWeb/src/api/client.test.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 48 | Yapısal |

**Rol.** Admin Web API istemcisi ve tip sözleşmesi. Her uç burada tiplenir.

**Proje içi bağımlılıklar.** `./client`, `../test/fixtures`, `../test/render`


#### 27.6.18 src/TkgPda.AdminWeb/src/api/client.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 221 | Yapısal |

**Rol.** Admin Web API istemcisi ve tip sözleşmesi. Her uç burada tiplenir.

**Tanımlanan tipler.** `ApiProblem`, `ApiResult`, `AdminApi`

**Tanımlanan üyeler.** `loginAdmin`, `createAdminApi`


#### 27.6.19 src/TkgPda.AdminWeb/src/api/types.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 395 | Yapısal |

**Rol.** Admin Web API istemcisi ve tip sözleşmesi. Her uç burada tiplenir.

**Tanımlanan tipler.** `RoleCode`, `AuthenticatedUser`, `AdminSession`, `Role`, `MachinePermission`, `AdminUser`, `Device`, `Machine`, `Variant`, `LayoutStatus`, `Layout`, `SlotRole`, `ContainerCategory`, `PartRelationType`, `Slot`, `SlotPartRecord`, `Part`, `ProductGroupPart`, `ProductGroup`, `ContainerType`, `DataIssue`, `AuditRecord`, `ProblemDetails`, `JsonObject`, `UserDraft`, `DeviceDraft`, `SlotDraft`, `RdsConnectionStatus`, `RdsNavigationProfile`, `RdsWorkSite`, `RobotConnectivity`, `RobotDispatch`, `RobotMotion`, `RobotExecution`, `RobotControl`, `RdsRobotAlarmCounts`, `RdsRobotDetail`, `RdsRobotAlarm`, `RdsSlotMapping`, `RdsSyncResult`

**Tanımlanan üyeler.** `ACTIVE_ROBOT_EXECUTIONS`, `isRobotExecutionActive`


#### 27.6.20 src/TkgPda.AdminWeb/src/auth/AuthContext.test.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 93 | Yapısal |

**Rol.** Admin kimlik bağlamı. Token yalnız bellekte tutulur; sayfa yenilemesi oturumu düşürür.

**Tanımlanan üyeler.** `Probe`, `serializedStores`, `dump`

**Kullandığı React hook'ları.** `useState`

**Proje içi bağımlılıklar.** `./AuthContext`, `../test/render`, `../test/fixtures`


#### 27.6.21 src/TkgPda.AdminWeb/src/auth/AuthContext.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 65 | Yapısal |

**Rol.** Admin kimlik bağlamı. Token yalnız bellekte tutulur; sayfa yenilemesi oturumu düşürür.

**Tanımlanan üyeler.** `AuthProvider`, `useAuth`, `isUsableSession`

**Kullandığı React hook'ları.** `useCallback`, `useEffect`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/client`, `../api/types`


#### 27.6.22 src/TkgPda.AdminWeb/src/components/AppShell.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 62 | Yapısal |

**Rol.** Paylaşılan arayüz bileşenleri: kabuk, bildirim, temel UI ilkelleri.

**Tanımlanan üyeler.** `AppShell`

**Kullandığı React hook'ları.** `useState`

**Proje içi bağımlılıklar.** `../auth/AuthContext`


#### 27.6.23 src/TkgPda.AdminWeb/src/components/Toast.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 57 | Yapısal |

**Rol.** Paylaşılan arayüz bileşenleri: kabuk, bildirim, temel UI ilkelleri.

**Tanımlanan üyeler.** `ToastProvider`, `useToast`

**Kullandığı React hook'ları.** `useCallback`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/client`


#### 27.6.24 src/TkgPda.AdminWeb/src/components/ui.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 104 | Yapısal |

**Rol.** Paylaşılan arayüz bileşenleri: kabuk, bildirim, temel UI ilkelleri.

**Tanımlanan üyeler.** `Button`, `IconButton`, `PageHeader`, `Panel`, `PanelHeader`, `StatusPill`, `Field`, `TextInput`, `SelectInput`, `SearchInput`, `Check`, `Modal`, `LoadingState`, `EmptyState`, `ErrorState`, `TableFrame`, `Definition`, `formatDate`, `compactNumber`

**Proje içi bağımlılıklar.** `../api/client`


#### 27.6.25 src/TkgPda.AdminWeb/src/index.css

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Stil sayfası | Admin Web | 432 | Yapısal |

**Rol.** Admin Web uygulama kökü.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.26 src/TkgPda.AdminWeb/src/main.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 31 | Yapısal |

**Rol.** Admin Web uygulama kökü.

**Proje içi bağımlılıklar.** `./App`, `./api/ApiContext`, `./auth/AuthContext`, `./components/Toast`


#### 27.6.27 src/TkgPda.AdminWeb/src/pages/AuditPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 22 | Kısa |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `AuditPage`, `AuditRows`

**Kullandığı API aileleri.** `api.audit`

**Kullandığı React hook'ları.** `useQuery`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../components/ui`


#### 27.6.28 src/TkgPda.AdminWeb/src/pages/CatalogPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 65 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `CatalogPage`, `refresh`, `execute`, `variantLabel`, `PartDialog`, `GroupDialog`, `submit`, `RelationDialog`

**Kullandığı API aileleri.** `api.parts`, `api.productGroups`, `api.variants`, `api.containers`, `api.removeProductGroupPart`, `api.createPart`, `api.updatePart`, `api.createProductGroup`, `api.updateProductGroup`, `api.setProductGroupPart`

**Kullandığı React hook'ları.** `useQuery`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../api/types`, `../components/Toast`, `../components/ui`


#### 27.6.29 src/TkgPda.AdminWeb/src/pages/ContainersPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 33 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `ContainersPage`, `execute`, `ContainerDialog`, `numeric`, `submit`

**Kullandığı API aileleri.** `api.containers`, `api.createContainer`, `api.updateContainer`

**Kullandığı React hook'ları.** `useQuery`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../api/types`, `../components/Toast`, `../components/ui`


#### 27.6.30 src/TkgPda.AdminWeb/src/pages/DashboardPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 62 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `DashboardPage`

**Kullandığı API aileleri.** `api.users`, `api.devices`, `api.machines`, `api.layouts`, `api.parts`, `api.containers`, `api.dataIssues`, `api.audit`, `api.rdsRobots`

**Kullandığı React hook'ları.** `useQuery`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../components/ui`


#### 27.6.31 src/TkgPda.AdminWeb/src/pages/DataQualityPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 22 | Kısa |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `DataQualityPage`, `count`

**Kullandığı API aileleri.** `api.dataIssues`

**Kullandığı React hook'ları.** `useQuery`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../components/ui`


#### 27.6.32 src/TkgPda.AdminWeb/src/pages/DevicesPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 39 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `DevicesPage`, `execute`, `DeviceDialog`, `submit`

**Kullandığı API aileleri.** `api.devices`, `api.createDevice`, `api.updateDevice`

**Kullandığı React hook'ları.** `useQuery`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../api/types`, `../components/Toast`, `../components/ui`


#### 27.6.33 src/TkgPda.AdminWeb/src/pages/LayoutEditorPage.test.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 154 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `mockEditorApi`

**Proje içi bağımlılıklar.** `./LayoutEditorPage`, `../test/fixtures`, `../test/render`


#### 27.6.34 src/TkgPda.AdminWeb/src/pages/LayoutEditorPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 966 | Derin analiz |

**Amaç.** Projedeki en yoğun tek arayüz sayfası (966 satır). Layout slot geometrisi, slot ↔ parça eşlemesi ve RDS eşlemeleri burada yönetilir.

**Sorumluluklar.**

- Slot listesini ve geometrisini düzenlemek.
- Slot ↔ parça ilişkisini kurmak ve kaldırmak.
- RDS navigasyon profili, slot eşlemesi ve WorkSite verilerini göstermek.
- Değişiklikleri optimistik olmayan biçimde kaydedip sorguları geçersizleştirmek.

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Sekiz API ailesi kullanımı | `layouts`, `slots`, `machines`, `containers`, `parts`, `rdsNavigationProfiles`, `rdsSlotMappings`, `rdsWorkSites`. Bu sayfa, konfigürasyonun farklı alanlarının kesiştiği tek yerdir. |
| Slot düzenleme | ui_x / ui_y / ui_width / ui_height alanları ile yerleşim; kaydetme sonrası ilgili sorgular yeniden çekilir. |
| Parça eşleme | `master.layout_slot_parts` ilişkisini kurar — çalışma zamanı yönlendirmesinin yetkili kaynağı. |

**Bağımlılıklar.** TanStack Query, `api/client.ts`, `components/ui.tsx`, `components/Toast.tsx`.

**Çağıranlar.** `App.tsx` router.

**Durum ve yan etki.** Sunucu durumu TanStack Query'de; form durumu yerel hook'larda.

**İş kuralları / değişmezler.**

- `metadata_json` içindeki köken bilgisi burada gösterilebilir ama yönlendirme kararı vermez.

**Hata yönetimi.** 409 CONFIGURATION_CONFLICT eşzamanlı düzenlemede gösterilir.

**Güvenlik.** administrator politikası gerektirir.

**Testler.** `src/TkgPda.AdminWeb/src/pages/LayoutEditorPage.test.tsx`.


#### 27.6.35 src/TkgPda.AdminWeb/src/pages/LayoutsPage.test.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 103 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Proje içi bağımlılıklar.** `./LayoutsPage`, `../test/fixtures`, `../test/render`


#### 27.6.36 src/TkgPda.AdminWeb/src/pages/LayoutsPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 289 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `LayoutsPage`, `refresh`, `execute`, `machineName`, `CreateLayoutDialog`, `submit`, `RenameDialog`, `LifecycleDialog`

**Kullandığı API aileleri.** `api.layouts`, `api.machines`, `api.createLayout`, `api.updateLayout`, `api.publishLayout`, `api.archiveLayout`, `api.cloneLayout`, `api.slots`, `api.rdsSlotMappings`, `api.slotParts`

**Kullandığı React hook'ları.** `useQuery`, `useEffect`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../api/types`, `../components/Toast`, `../components/ui`


#### 27.6.37 src/TkgPda.AdminWeb/src/pages/LoginPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 64 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `LoginPage`, `submit`, `from`

**Kullandığı React hook'ları.** `useState`, `useNavigate`

**Proje içi bağımlılıklar.** `../api/client`, `../auth/AuthContext`, `../components/ui`


#### 27.6.38 src/TkgPda.AdminWeb/src/pages/MachinesPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 52 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `MachinesPage`, `publishedLayouts`, `refresh`, `execute`, `MachineDialog`, `VariantDialog`, `submit`

**Kullandığı API aileleri.** `api.machines`, `api.variants`, `api.layouts`, `api.setDefaultLayout`, `api.createVariant`, `api.updateVariant`, `api.updateMachine`

**Kullandığı React hook'ları.** `useQuery`, `useEffect`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../api/types`, `../components/Toast`, `../components/ui`


#### 27.6.39 src/TkgPda.AdminWeb/src/pages/NotFoundPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 7 | Kısa |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `NotFoundPage`


#### 27.6.40 src/TkgPda.AdminWeb/src/pages/RdsIntegrationPage.test.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 364 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `mockRdsApi`

**Proje içi bağımlılıklar.** `./RdsIntegrationPage`, `../test/render`


#### 27.6.41 src/TkgPda.AdminWeb/src/pages/RdsIntegrationPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 341 | Derin analiz |

**Amaç.** RDS entegrasyonunun teknik teşhis yüzeyi: robot telemetrisi, alarmlar, kilit durumu, izin verilen operasyonlar ve uzman mutasyonlar.

**Sorumluluklar.**

- Filo telemetrisini severity ayrımıyla göstermek.
- Çekirdek düzeyi alarmları robot alarmlarından ayrı sunmak.
- Uzman operasyonları tetiklemek ve sonuçlarını göstermek.
- Mutasyon kapısı kapalıyken sebebi anlaşılır biçimde iletmek.

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Telemetri tablosu | Robot başına kimlik, harita, bağlantı, dispatch edilebilirlik, batarya (null ise UNAVAILABLE), alarm listesi ve kilit durumu. |
| Uzman aksiyonlar | manualFinished, redoFailedOrder, setPriority, robot bakımı. Hepsi sunucudaki mutasyon kapısına tabidir. |

**Bağımlılıklar.** TanStack Query, `api/client.ts`.

**Çağıranlar.** `App.tsx` router.

**Durum ve yan etki.** Sunucu durumu sorgularda.

**İş kuralları / değişmezler.**

- Arayüzde bir butonun görünür olması hareketin izinli olduğu anlamına gelmez; karar sunucudadır.
- Warning/notice alarmlar bloklayan alarmlardan görsel olarak ayrılır.

**Hata yönetimi.** RDS_DISPATCH_DISABLED kullanıcıya açıklayıcı mesaj olarak gösterilir.

**Güvenlik.** administrator/variant-manager politikası gerektirir.

**Testler.** `src/TkgPda.AdminWeb/src/pages/RdsIntegrationPage.test.tsx`.


#### 27.6.42 src/TkgPda.AdminWeb/src/pages/UsersPage.test.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 36 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `mockUsersApi`

**Proje içi bağımlılıklar.** `./UsersPage`, `../test/fixtures`, `../test/render`


#### 27.6.43 src/TkgPda.AdminWeb/src/pages/UsersPage.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 95 | Yapısal |

**Rol.** Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir.

**Tanımlanan üyeler.** `UsersPage`, `selected`, `refresh`, `execute`, `RoleChecks`, `CreateUserDialog`, `submit`, `EditUserDialog`, `PasswordDialog`, `PermissionDialog`

**Kullandığı API aileleri.** `api.users`, `api.roles`, `api.machines`, `api.createUser`, `api.updateUser`, `api.setRoles`, `api.resetPassword`, `api.removePermission`, `api.setPermission`

**Kullandığı React hook'ları.** `useQuery`, `useMemo`, `useState`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../api/types`, `../components/Toast`, `../components/ui`


#### 27.6.44 src/TkgPda.AdminWeb/src/test/fixtures.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 29 | Yapısal |

**Rol.** Vitest kurulum ve render yardımcıları.

**Tanımlanan üyeler.** `session`, `roles`, `machines`, `users`, `devices`, `variants`, `layouts`, `containers`, `slots`, `issues`

**Proje içi bağımlılıklar.** `../api/types`


#### 27.6.45 src/TkgPda.AdminWeb/src/test/render.tsx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript React bileşeni | Admin Web | 19 | Kısa |

**Rol.** Vitest kurulum ve render yardımcıları.

**Tanımlanan üyeler.** `jsonResponse`, `renderAuthenticated`, `Providers`

**Proje içi bağımlılıklar.** `../api/ApiContext`, `../auth/AuthContext`, `../components/Toast`, `./fixtures`


#### 27.6.46 src/TkgPda.AdminWeb/src/test/setup.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 12 | Kısa |

**Rol.** Vitest kurulum ve render yardımcıları.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.47 src/TkgPda.AdminWeb/tsconfig.app.json

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JSON yapılandırma | Admin Web | 27 | Yapısal |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.48 src/TkgPda.AdminWeb/tsconfig.json

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JSON yapılandırma | Admin Web | 8 | Kısa |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.49 src/TkgPda.AdminWeb/tsconfig.node.json

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JSON yapılandırma | Admin Web | 24 | Kısa |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.6.50 src/TkgPda.AdminWeb/vite.config.ts

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| TypeScript kaynak | Admin Web | 32 | Yapısal |

**Rol.** Admin Web proje/araç yapılandırması.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


### 27.7 Veritabanı — migration ve seed (10)


#### 27.7.1 db/migrations/001_initial_schema.sql

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| SQL | Veritabanı | 556 | Derin analiz |

**Amaç.** Şemanın temeli. Beş şema ve 29 tablo ile birlikte kritik kısıtları kurar.

**Sorumluluklar.**

- auth, master, ops, integration, audit şemalarını oluşturmak.
- Ana veri, çalışma zamanı ve entegrasyon tablolarını tanımlamak.
- Doluluk/yük tutarlılığını CHECK kısıtı ile korumak.
- Idempotency benzersizliğini indeksle zorlamak.

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| ck_slot_load_requires_occupancy | Dolu olmayan bir slotun EMPTY/FULL yük durumu taşımasını engeller. İki eksenin bağımsızlığı burada sınırlanır: yük ancak doluluk varsa anlamlıdır. |
| uq_layout_slot_code | (layout_id, code) benzersiz. Layout klonlamanın `code` üzerinden eşleme yapabilmesinin sebebi budur. |
| uq_transport_idempotency | Aynı idempotency anahtarıyla iki taşıma kaydını imkânsız kılar. |

**Bağımlılıklar.** PostgreSQL 18.

**Çağıranlar.** `tools/dev/start_local_stack.ps1`, devreye alma kurulumu.

**Durum ve yan etki.** Şema.

**İş kuralları / değişmezler.**

- Şemanın tek gerçek kaynağı budur; EF migration üretilmez.

**Hata yönetimi.** Kısıt ihlalleri API'de 409 CONFIGURATION_CONFLICT'e çevrilir.

**Güvenlik.** Yetki tabloları (`auth.*`) ve denetim tabloları (`audit.*`) burada tanımlanır.

**Testler.** `tests/TkgPda.Infrastructure.Postgres.Tests` bu şemaya karşı çalışır.


#### 27.7.2 db/migrations/002_operator_employee_identity.sql

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| SQL | Veritabanı | 38 | Yapısal |

**Rol.** Şema migration'ı. Şemanın tek gerçek kaynağı; sıralı ve idempotent uygulanır.

**Oluşturulan veritabanı nesneleri.** `UNIQUE INDEX uq_users_employee_no_normalized`


#### 27.7.3 db/migrations/003_transport_batch.sql

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| SQL | Veritabanı | 64 | Yapısal |

**Rol.** Şema migration'ı. Şemanın tek gerçek kaynağı; sıralı ve idempotent uygulanır.

**Oluşturulan veritabanı nesneleri.** `TABLE ops.transport_batches`, `UNIQUE INDEX uq_transport_batch_idempotency`, `INDEX ix_transport_batch_machine_created`, `INDEX ix_transport_requests_batch`


#### 27.7.4 db/migrations/004_transport_request_readiness.sql

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| SQL | Veritabanı | 58 | Yapısal |

**Rol.** Şema migration'ı. Şemanın tek gerçek kaynağı; sıralı ve idempotent uygulanır.

**Oluşturulan veritabanı nesneleri.** `INDEX ix_transport_requests_readiness`


#### 27.7.5 db/migrations/005_layout_slot_part_mapping.sql

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| SQL | Veritabanı | 47 | Derin analiz |

**Amaç.** Parça ↔ Slot yönlendirmesinin yetkili tablosunu ekler. Operatörün slot koduna elle dokunmasını gereksiz kılar.

**Sorumluluklar.**

- `master.layout_slot_parts` tablosunu oluşturmak.
- Ters arama indeksini eklemek.

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Tablo tanımı | (layout_slot_id, part_id) birincil anahtar; her iki yabancı anahtar `ON DELETE CASCADE`. |
| ix_layout_slot_parts_part_id | "Bu parça hangi slotlara uygun?" — otomatik makine-slot çözümlemesinin gerçekten sorguladığı yön. |

**Bağımlılıklar.** 001 migration.

**Çağıranlar.** Şema kurulumu.

**Durum ve yan etki.** Şema.

**İş kuralları / değişmezler.**

- Çoktan-çoğa. Bir slot birden çok parça kodu tutabilir; bir parça birden çok slotta olabilir. "Bir parça → bir global slot" kısıtı uygulanmaz.
- Yalnız kimlik ilişkisi kaydedilir; hiçbir çalışma zamanı durumu taşımaz.
- `layout_slots.metadata_json` içindeki çıkarım kanıtı bu tablonun yerine geçmez.

**Hata yönetimi.** Yok.

**Güvenlik.** Yanlış eşleme robotu yanlış rafa gönderir — bu yüzden ilişki elle küratörlüdür, otomatik çıkarım değil.

**Testler.** `tests/TkgPda.Infrastructure.Postgres.Tests` klonlama testleri bu tabloyu kullanır.


#### 27.7.6 db/seeds/README.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Veritabanı | 81 | Yapısal |

**Rol.** Seed verisi.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.7.7 db/seeds/generated/002a_customer_textual_master_data.sql

| Tür | Katman | Satır | Analiz derinliği | Üretim |
|---|---|---|---|---|
| SQL | Veritabanı | 682 | Yapısal | ÜRETİLMİŞ — elle düzenlenmez |

**Rol.** Müşteri kaynak verisinden ÜRETİLMİŞ seed SQL. Gerçek veritabanlarına karşı çalıştırıldığı için dokümante edilir; elle düzenlenmez, kaynak veri değişirse yeniden üretilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.7.8 db/seeds/generated/002b_customer_physical_layouts.sql

| Tür | Katman | Satır | Analiz derinliği | Üretim |
|---|---|---|---|---|
| SQL | Veritabanı | 323 | Yapısal | ÜRETİLMİŞ — elle düzenlenmez |

**Rol.** Müşteri kaynak verisinden ÜRETİLMİŞ seed SQL. Gerçek veritabanlarına karşı çalıştırıldığı için dokümante edilir; elle düzenlenmez, kaynak veri değişirse yeniden üretilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.7.9 db/seeds/generated/002c_customer_layout_slot_part_assignments.sql

| Tür | Katman | Satır | Analiz derinliği | Üretim |
|---|---|---|---|---|
| SQL | Veritabanı | 172 | Yapısal | ÜRETİLMİŞ — elle düzenlenmez |

**Rol.** Müşteri kaynak verisinden ÜRETİLMİŞ seed SQL. Gerçek veritabanlarına karşı çalıştırıldığı için dokümante edilir; elle düzenlenmez, kaynak veri değişirse yeniden üretilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.7.10 db/seeds/generated/002d_simulation_rds_trial_mappings.sql

| Tür | Katman | Satır | Analiz derinliği | Üretim |
|---|---|---|---|---|
| SQL | Veritabanı | 190 | Yapısal | ÜRETİLMİŞ — elle düzenlenmez |

**Rol.** Müşteri kaynak verisinden ÜRETİLMİŞ seed SQL. Gerçek veritabanlarına karşı çalıştırıldığı için dokümante edilir; elle düzenlenmez, kaynak veri değişirse yeniden üretilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


### 27.8 Testler (77)


#### 27.8.1 tests/TkgPda.Api.Tests/AdminConfigurationApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 772 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class AdminConfigurationApiTests`

**Tanımlanan üyeler.** `Admin_web_login_needs_no_device_and_exposes_admin_surface`, `Admin_login_with_wrong_password_is_rejected`, `Non_admin_cannot_receive_admin_web_token`, `Admin_web_and_device_bound_tokens_cannot_cross_mutation_boundaries`, `User_provisioning_assigns_exact_roles_hashes_password_and_audits`, `Admin_login_does_not_accept_employee_number`, `Admin_user_create_trims_and_preserves_employee_number`, `Admin_duplicate_normalized_employee_number_is_rejected_cleanly`, `Admin_allows_multiple_users_without_employee_number`, `Admin_accepts_leading_zero_distinct_employee_numbers`, `Password_reset_invalidates_old_password_and_never_audits_hash_material`, `Stale_if_match_returns_explicit_concurrency_conflict`, `Missing_if_match_is_rejected_before_mutation`, `Device_provisioning_and_archive_control_pda_access`, `Machine_permissions_are_administered_against_user_version`, `Warehouse_operator_is_limited_to_own_bring_empty_requests`, `Layout_draft_slots_publish_and_default_assignment_follow_lifecycle`, `Published_layout_is_immutable_and_new_version_copies_slots`, `Publishing_new_layout_version_archives_previous_and_migrates_default_assignment`, `Draft_slot_update_requires_slot_and_parent_layout_tokens`, `Invalid_slot_geometry_is_not_persisted`, `Part_product_group_and_relation_management_preserves_nullable_evidence`, `Unknown_container_footprint_remains_null_and_is_visible_as_data_issue`, `Active_transport_blocks_replacement_layout_publication`, `Self_deactivation_and_self_admin_removal_are_blocked`, `Audit_history_exposes_actor_correlation_and_before_after_without_hard_delete_surface`, `Slot_part_mapping_supports_list_assign_and_remove_with_audit`, `Slot_part_assignment_rejects_unknown_slot_or_inactive_part`, `CreateUserAsync`, `CreatePartAsync`, `CreateVariantAsync`, `CreateLayoutAsync`, `CreatePublishableLayoutAsync`, `SendAsync`, `Json`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`, `TkgPda.Domain`


#### 27.8.2 tests/TkgPda.Api.Tests/ApiCancellationMiddlewareTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 61 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class ApiCancellationMiddlewareTests`

**Tanımlanan üyeler.** `Context`, `A_client_disconnect_is_not_reported_as_a_server_error`, `An_internal_cancellation_is_still_a_server_error`

**Proje içi bağımlılıklar.** `TkgPda.Api`


#### 27.8.3 tests/TkgPda.Api.Tests/ApiTestFixture.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 542 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class ApiTestFixture`, `record TestIdentity`, `record LayoutContext`, `class TestRdsReadClient`, `class TestRdsCoreDiscoveryClient`

**Tanımlanan üyeler.** `Client`, `ConfigureWebHost`, `ResetAsync`, `SeedIdentityAsync`, `LoginClientAsync`, `AdminLoginClientAsync`, `ConfigureMachineAsync`, `EnsureDefaultBufferSlotsAsync`, `EnsureLayoutSlotAsync`, `ContextAsync`, `LogisticsSlotsAsync`, `PartForSlotAsync`, `SetSlotAsync`, `SendTransportAsync`, `ErrorCodeAsync`, `ExecuteAsync`, `QueryGuidsAsync`, `AddParameters`, `Reset`, `GetWorkSitesAsync`, `QueryWindTasksAsync`, `GetRobotAlarmsAsync`, `CaptureAsync`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.4 tests/TkgPda.Api.Tests/AssemblyInfo.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 4 | Kısa |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.5 tests/TkgPda.Api.Tests/AuthenticatedApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 610 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class AuthenticatedApiTests`

**Tanımlanan üyeler.** `Health_endpoints_are_anonymous_and_database_ready`, `Login_returns_bound_jwt_exact_roles_and_current_user`, `Invalid_credentials_return_generic_error_and_are_audited`, `Login_rate_limit_returns_stable_problem_code`, `Inactive_device_cannot_login`, `PostLoginAsync`, `Employee_number_login_succeeds_and_returns_device_bound_session`, `Employee_number_login_is_case_insensitive_and_trims_whitespace`, `Employee_number_login_preserves_significant_leading_zeros`, `Employee_number_login_unknown_number_is_generic_credentials_error`, `Employee_number_login_succeeds_without_a_password`, `Employee_number_login_does_not_verify_a_supplied_password`, `Username_login_without_a_password_is_rejected`, `Employee_number_login_inactive_user_is_rejected`, `Employee_number_login_requires_authorized_device`, `Login_with_both_username_and_employee_number_is_ambiguous`, `Login_with_no_identity_is_rejected`, `Login_with_blank_employee_number_and_no_username_is_rejected`, `Legacy_username_operator_login_remains_supported`, `Protected_endpoints_require_authentication`, `Token_is_rejected_when_device_header_changes`, `Machine_permission_filters_list_and_blocks_context`, `Admin_sees_all_nine_machine_masters_without_explicit_rows`, `Operational_context_exposes_runtime_variants_geometry_and_non_transport_machine_region`, `Transport_http_idempotency_returns_created_replay_and_conflict`, `Operate_permission_is_required_even_for_operator_role`, `Queued_transport_cancels_locally_and_dispatched_transport_reports_pending_Rds_confirmation`, `Variant_switch_requires_supervisor_role_and_machine_permission`, `Authorized_variant_switch_uses_workflow_guard`, `Active_transport_blocks_variant_switch_over_http`, `Concurrent_http_requests_for_same_slot_create_one_reservation`, `Concurrent_same_key_http_retry_returns_one_created_and_one_replay`, `Execution_state_mutation_routes_are_not_exposed`, `Bring`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.6 tests/TkgPda.Api.Tests/CompatibleVariantsApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 300 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class CompatibleVariantsApiTests`, `record Scene`

**Tanımlanan üyeler.** `Customer_seed_main_part_resolves_in_its_parent_context`, `Main_part_resolves_its_variant`, `Related_part_resolves_its_variant`, `Alt_part_resolves_its_variant`, `Main_part_used_by_multiple_variants_returns_all_contextual_variants`, `Child_relationship_does_not_escape_the_selected_main_part_context`, `Duplicated_child_under_the_selected_main_returns_only_that_relationship`, `Same_child_under_another_main_preserves_that_product_group`, `Part_with_no_mapping_returns_an_empty_list`, `Missing_main_part_context_is_rejected_explicitly`, `Unauthorized_machine_is_rejected`, `Authorized_machine_returns_the_compatible_variants`, `Ordering_is_deterministic_by_display_order`, `QueryAsync`, `SeedSceneAsync`, `InsertVariantAsync`, `InsertPartAsync`, `InsertProductGroupAsync`, `InsertGroupPartAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.7 tests/TkgPda.Api.Tests/ContainerActionsApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 458 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class ContainerActionsApiTests`, `record MappedPosition`

**Tanımlanan üyeler.** `Container_positions_reject_another_machines_variant_and_layout_context`, `Kasa_doldu_atomically_marks_full_and_creates_remove_full`, `Kasa_doldu_on_full_is_idempotent`, `Kasa_doldu_rejects_unproven_container_state`, `Kasa_doldu_rolls_back_load_when_request_creation_fails`, `Kasa_bos_from_vacant_creates_bring_empty_but_occupied_is_rejected`, `Remove_all_uses_entire_layout_and_is_atomic_on_unknown`, `Remove_all_rejects_the_whole_batch_when_any_position_is_reserved`, `Concurrent_container_actions_create_only_one_reservation`, `Container_action_batch_rejects_same_key_with_a_different_payload_or_endpoint_kind`, `Global_queue_is_cross_user_cross_machine_and_hides_technical_identifiers`, `SeedMappedPositionAsync`, `SetPositionAsync`, `MakeLayoutVacantAsync`, `SendActionAsync`, `SendRemoveAllAsync`, `SendActionBatchAsync`

**Proje içi bağımlılıklar.** `TkgPda.Api`


#### 27.8.8 tests/TkgPda.Api.Tests/ExpertActionSafetyApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 190 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class ExpertActionSafetyApiTests`

**Tanımlanan üyeler.** `Manual_finish_rejects_an_unknown_request_instead_of_acting_on_caller_supplied_vehicles`, `Redo_rejects_an_unknown_request`, `Set_priority_rejects_an_unknown_request`, `Line_and_warehouse_operators_are_forbidden_from_changing_priority`, `Line_operator_is_forbidden_from_manual_finish_and_redo`, `Expert_actions_enforce_machine_authorization_on_a_real_request`, `Set_priority_rejects_an_out_of_range_value_on_a_real_request`, `Expert_action_is_refused_while_the_global_dispatch_gate_is_closed`, `CreateRemoveFullAsync`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.9 tests/TkgPda.Api.Tests/LiveRdsPostgresSyncTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 84 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class LiveRdsPostgresSyncTests`, `class LiveFactory`, `class LiveRdsFactAttribute`

**Tanımlanan üyeler.** `Actual_read_worksites_sync_idempotently_to_temporary_postgres`, `ExecuteAsync`, `CreateClient`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Infrastructure.Postgres`


#### 27.8.10 tests/TkgPda.Api.Tests/MachinePartOptionsApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 233 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class MachinePartOptionsApiTests`, `record Scene`

**Tanımlanan üyeler.** `Machine_returns_its_main_part_options`, `Related_parts_appear_under_the_correct_main_part`, `Alt_parts_appear_under_the_correct_main_part`, `Relationships_across_multiple_variants_are_unioned`, `Duplicate_relationships_across_variants_are_deduplicated`, `Another_machines_data_does_not_leak`, `Unauthorized_machine_is_rejected`, `Main_part_with_no_related_parts_returns_an_empty_list`, `Ordering_is_deterministic`, `QueryAsync`, `SeedSceneAsync`, `InsertVariantAsync`, `InsertPartAsync`, `InsertProductGroupAsync`, `InsertGroupPartAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.11 tests/TkgPda.Api.Tests/OperatorInformationBoundaryApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 402 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class OperatorInformationBoundaryApiTests`

**Tanımlanan üyeler.** `AssertNoForbiddenFields`, `Operator_robot_payload_contains_only_business_fields`, `Operator_robot_fault_flag_ignores_notices_and_warnings`, `Operator_queue_payload_does_not_carry_the_raw_agv_identifier`, `Operational_context_is_business_safe_for_every_pda_role`, `Operational_context_never_exposes_the_configured_rds_task_label`, `Operator_cannot_choose_physical_routing_by_injecting_slot_ids`, `Load_state_response_does_not_expose_the_rds_worksite_site_id`, `Raw_rds_order_passthrough_is_no_longer_reachable_by_an_operator`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.12 tests/TkgPda.Api.Tests/OperatorOperationAuthorityApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 119 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class OperatorOperationAuthorityApiTests`

**Tanımlanan üyeler.** `Direct_remove_empty_is_rejected_before_any_transport_is_created`, `Direct_remove_empty_is_rejected_inside_a_transport_batch`, `Operational_context_never_offers_remove_empty_as_a_selectable_action`

**Proje içi bağımlılıklar.** `TkgPda.Api`


#### 27.8.13 tests/TkgPda.Api.Tests/OperatorWebCorsTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 47 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class OperatorWebCorsTests`

**Tanımlanan üyeler.** `Configured_operator_origin_receives_only_the_required_cors_contract`, `Unconfigured_origin_receives_no_cors_authorization`


#### 27.8.14 tests/TkgPda.Api.Tests/PdaRdsContractReadinessApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 282 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class PdaRdsContractReadinessApiTests`

**Tanımlanan üyeler.** `Active_variant_product_groups_expose_main_and_active_child_parts_with_null_evidence_preserved`, `Available_actions_are_role_permission_state_and_machine_region_sensitive`, `Active_request_projection_sets_is_mine_filters_history_and_blocks_duplicate_slot_action`, `Persisted_rds_task_and_worksite_are_projected_while_unmapped_slot_remains_honestly_unmapped`, `GetContextAsync`, `AssertEnabled`, `AssertNotOffered`, `AssertDisabled`, `Bring`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.15 tests/TkgPda.Api.Tests/Phase08bExecutionApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 154 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class Phase08bExecutionApiTests`

**Tanımlanan üyeler.** `Device_bound_operator_reports_load_state_without_changing_occupancy`, `Load_state_endpoint_rejects_unknown_warehouse_and_active_transport_mutations`, `Callback_key_is_required_and_unknown_task_is_ignored_safely`, `Authenticated_task_done_callback_completes_known_request_idempotently`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.16 tests/TkgPda.Api.Tests/PostOutcomeAuditTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 109 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class PostOutcomeAuditTests`

**Tanımlanan üyeler.** `Throwing`, `An_audit_cancellation_is_swallowed_so_the_caller_can_rethrow_the_physical_failure`, `A_task_cancellation_is_also_swallowed`, `Every_audit_failure_is_swallowed_and_reported`, `AuditFailures`, `The_audit_is_attempted_exactly_once_and_is_never_retried`, `The_audit_write_gets_its_own_live_token`, `A_successful_audit_reports_no_failure`, `A_failing_failure_logger_still_cannot_escape`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.17 tests/TkgPda.Api.Tests/RdsAdminApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 167 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class RdsAdminApiTests`, `record Setup`

**Tanımlanan üyeler.** `InitializeAsync`, `DisposeAsync`, `Commissioning_capture_is_admin_only_explicit_and_disabled_by_default`, `Rds_diagnostics_are_admin_web_only_and_use_safe_endpoint_display`, `Explicit_profile_sync_is_idempotent_and_filterable`, `Mapping_is_manual_audited_and_unmapping_is_supported`, `Machine_region_and_logical_worksite_mapping_are_rejected`, `SetupAsync`, `Site`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.18 tests/TkgPda.Api.Tests/RdsCommissioningDiscoveryClientTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 260 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class RdsCommissioningDiscoveryClientTests`, `enum FailureKind`, `class Factory`, `class RoutingHandler`, `record ObservedRequest`

**Tanımlanan üyeler.** `Default_inventory_parses_documented_core_shapes_without_business_state_inference`, `Http_200_with_nonzero_core_code_is_business_failure`, `Http_failures_are_captured_without_aborting_inventory`, `Transport_and_json_failures_are_isolated`, `Optional_fields_and_unknown_future_fields_are_tolerated`, `Registry_is_closed_read_discovery_only_and_has_no_arbitrary_request_surface`, `Explicit_typed_probes_use_only_registered_endpoints_and_sanitize_results`, `Disabled_discovery_fails_closed_with_zero_http_calls`, `Base_url_is_independent_strict_and_diagnostic_origin_is_sanitized`, `Client`, `DefaultResponse`, `Json`, `CreateClient`, `SendAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.19 tests/TkgPda.Api.Tests/RdsCoreNativeOrderClientTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 777 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class RdsCoreNativeOrderClientTests`, `class Factory`, `class LiveFactory`, `class RecordingHandler`, `record ObservedRequest`

**Tanımlanan üyeler.** `Navigation_order_serializes_all_supported_fields_with_sealed_deterministic_contract`, `Operation_script_and_binTask_shapes_are_serialized_without_invented_fields`, `SetOrder_distinguishes_http_core_execution_and_duplicate_semantics`, `SetOrder_preserves_errors_warnings_and_notices`, `Successful_malformed_JSON_is_rejected`, `Order_details_parses_known_and_unknown_states_without_losing_raw`, `Block_details_parses_known_unknown_and_missing_optional_fields`, `Order_list_parses_envelope_paging_and_unknown_fields`, `Lookups_use_closed_escaped_paths_and_external_or_block_lookup_returns_arrays`, `Terminate_can_only_target_one_nonempty_order_and_never_disables_vehicle`, `Disabled_or_non_native_mode_fails_closed_before_any_HTTP_call`, `Configuration_requires_explicit_native_enable_and_shares_only_explicit_core_origin`, `Operation_registry_is_closed_and_excludes_recovery_or_arbitrary_request_surfaces`, `Set_priority_serializes_id_and_priority_to_post_setPriority`, `Clear_robot_all_error_serializes_vehicles_to_post_clearRobotAllError`, `Goto_site_pause_and_resume_serialize_vehicles_to_correct_endpoints`, `Manual_finished_and_redo_failed_order_serialize_vehicles_to_correct_endpoints`, `Set_dispatchable_validates_type_and_serializes_to_post_dispatchable`, `Lock_and_unlock_serialize_vehicles_to_correct_endpoints`, `Switch_map_serializes_vehicle_and_map_to_post_switchMap`, `RootAlarmPayload`, `Root_wrapped_fatal_reaches_the_caller_as_a_blocking_global_alarm`, `Root_direct_error_reaches_the_caller_as_a_blocking_global_alarm`, `Root_warning_and_notice_are_reported_but_not_blocking`, `A_robot_fatal_still_blocks_on_the_robot_and_not_globally`, `Root_and_robot_alarms_are_both_preserved_and_kept_apart`, `A_malformed_report_does_not_discard_a_root_fatal`, `Get_robots_status_parses_live_report_telemetry_for_Sfl_Cbd15`, `Live_rdscore_telemetry_can_be_queried_if_server_is_running`, `NavigationOrder`, `Navigation`, `Block`, `Element`, `Remaining_time_provider_keys_by_the_shared_vehicle_space_identity`, `Client`, `Json`, `CreateClient`, `Single`, `SendAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.20 tests/TkgPda.Api.Tests/RdsDispatchConfigurationTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 199 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class RdsDispatchConfigurationTests`

**Tanımlanan üyeler.** `FromSettings`, `The_shipped_default_keeps_dispatch_closed`, `Rds_disabled_permits_no_execution_at_all`, `Dispatch_without_integration_is_rejected_at_startup`, `Enabled_with_dispatch_closed_allows_reads_but_refuses_execution`, `Enabled_with_dispatch_open_permits_the_normal_execution_path`, `The_runtime_environment_alone_can_open_the_gate`, `No_simulation_switch_participates_in_dispatch_permission`, `RdsMutationPolicy_is_the_single_authority`, `Dispatchable_classification_follows_the_direction_of_motion`, `BaseAddressDisplaySafe_formats_clean_origin_without_leakage`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.21 tests/TkgPda.Api.Tests/RdsExecutionClientTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 153 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class RdsExecutionClientTests`, `class Factory`, `class RecordingHandler`

**Tanımlanan üyeler.** `Dispatch_refuses_insecure_remote_http_and_short_secrets`, `Stop_task_sends_release_site_and_immutable_record_identity`, `Script_response_can_be_direct_or_wrapped_json_without_losing_replay_proof`, `Wind_cancellation_stays_available_when_the_global_dispatch_gate_is_closed`, `Wind_task_creation_is_blocked_when_the_global_dispatch_gate_is_closed`, `Wind_content_update_is_blocked_when_the_global_dispatch_gate_is_closed`, `Wind_cancellation_is_also_available_with_the_gate_open`, `GatedClient`, `Client`, `CreateClient`, `SendAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.22 tests/TkgPda.Api.Tests/RdsReadClientTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 420 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class RdsReadClientTests`, `class Factory`, `class RecordingHandler`

**Tanımlanan üyeler.** `Worksite_response_preserves_external_evidence`, `Http_200_business_failure_is_rejected_with_context`, `Empty_wind_task_page_is_a_valid_read_result`, `Robot_alarms_flatten_across_severity_buckets_with_correct_fields`, `Robot_alarms_are_deterministic_across_multiple_robots`, `Empty_robot_alarm_snapshot_is_a_valid_result`, `Malformed_alarm_item_without_code_is_skipped_safely`, `Robot_alarm_read_failure_surfaces_as_rds_unavailable_not_empty`, `Disabled_client_does_not_send_http`, `Robot_telemetry_fields_are_parsed_accurately`, `Both_seer_identity_spaces_are_preserved_when_uuid_differs_from_vehicle_id`, `Absent_connectivity_and_lock_telemetry_stay_unknown_rather_than_online_and_unlocked`, `Full_seer_lock_fingerprint_is_captured_for_correlation`, `Both_documented_seer_alarm_shapes_are_parsed_with_their_bucket_severity`, `Malformed_and_duplicate_alarm_entries_do_not_break_the_fleet_read`, `Read_only_configuration_still_refuses_insecure_remote_http`, `Client`, `CreateClient`, `SendAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.23 tests/TkgPda.Api.Tests/RdsRobotAlarmApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 120 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class RdsRobotAlarmApiTests`

**Tanımlanan üyeler.** `InitializeAsync`, `DisposeAsync`, `Admin_alarm_endpoint_returns_detailed_projection`, `Non_admin_is_forbidden_from_the_admin_alarm_endpoint`, `Admin_alarm_endpoint_surfaces_rds_unavailable_instead_of_empty_list`, `Operator_summary_reports_no_active_alarm_when_snapshot_is_empty`, `Operator_summary_reports_highest_severity_without_leaking_diagnostic_detail`, `Operator_summary_surfaces_rds_unavailable_instead_of_empty_summary`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.24 tests/TkgPda.Api.Tests/TkgPda.Api.Tests.csproj

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild proje dosyası | Test | 21 | Kısa |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.25 tests/TkgPda.Api.Tests/TransportBatchApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 314 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class TransportBatchApiTests`

**Tanımlanan üyeler.** `Batch_with_two_items_creates_one_batch_with_two_child_requests`, `Child_request_uuids_are_distinct`, `Every_child_retains_its_own_idempotency_key`, `Every_child_keeps_its_own_operation_part_source_destination`, `Batch_id_links_children`, `Standalone_existing_request_still_has_null_batch_id`, `Same_batch_idempotency_key_replay_creates_no_duplicates`, `One_invalid_child_creates_zero_batch_and_zero_child_requests`, `Unauthorized_machine_is_rejected`, `Mixed_machine_context_across_items_is_rejected`, `No_rds_task_is_created_directly_by_the_batch_api`, `Bring`, `PostBatchAsync`, `CreateBatchAsync`

**Proje içi bağımlılıklar.** `TkgPda.Api`


#### 27.8.26 tests/TkgPda.Api.Tests/TransportReadinessApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 273 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class TransportReadinessApiTests`

**Tanımlanan üyeler.** `Omitted_requires_preparation_creates_a_not_required_request`, `Requires_preparation_false_creates_a_not_required_request`, `Requires_preparation_true_creates_a_waiting_preparation_request`, `Mark_ready_transitions_waiting_preparation_to_ready_and_is_idempotent`, `Line_operator_is_forbidden_from_marking_a_request_ready`, `Warehouse_operator_can_mark_a_request_ready`, `Supervisor_can_mark_a_request_ready`, `Mark_ready_on_a_not_required_request_is_rejected`, `Mark_ready_on_a_terminal_request_is_rejected`, `Batch_children_independently_preserve_their_own_readiness`, `Batch_atomicity_is_intact_when_one_item_is_invalid_regardless_of_mixed_readiness`, `Bring`, `BatchItem`

**Proje içi bağımlılıklar.** `TkgPda.Api`


#### 27.8.27 tests/TkgPda.Api.Tests/WarehousePreparationApiTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 210 | Yapısal |

**Rol.** API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir.

**Tanımlanan tipler.** `class WarehousePreparationApiTests`

**Tanımlanan üyeler.** `Warehouse_operator_sees_waiting_preparation_requests`, `Supervisor_sees_waiting_preparation_requests`, `Line_operator_is_forbidden_from_listing_preparation_requests`, `Ready_and_not_required_requests_are_excluded`, `Results_are_ordered_deterministically_oldest_first`, `A_machine_without_permission_is_not_leaked`, `CreateWaitingRequestAsync`, `Bring`, `ReadAsync`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.28 tests/TkgPda.Application.Tests/ContainerActionResolutionPolicyTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 186 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class ContainerActionResolutionPolicyTests`

**Tanımlanan üyeler.** `Full_on_a_vacant_position_reports_that_a_container_is_required`, `Full_on_an_occupied_position_with_unknown_load_still_reports_insufficient_state`, `Unknown_occupancy_still_reports_insufficient_state_for_both_actions`, `Legal_db_states_keep_their_established_resolutions`, `Operator_container_action_parser_rejects_missing_or_unsupported_values`, `Kasa_doldu_from_occupied_empty_marks_full_then_removes_full`, `Kasa_doldu_from_occupied_full_reuses_remove_full_without_state_rewrite`, `Kasa_doldu_fails_closed_without_an_occupied_container`, `Kasa_bos_from_vacant_unknown_brings_empty`, `Kasa_bos_rejects_occupied_positions`, `Remove_all_selects_operation_from_tkg_business_state`, `Remove_all_ignores_vacant_unknown`, `Remove_all_blocks_unknown_position`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`


#### 27.8.29 tests/TkgPda.Application.Tests/PdaOperationalContextPolicyTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 294 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class PdaOperationalContextPolicyTests`

**Tanımlanan üyeler.** `Machine_region_exposes_no_enabled_transport_action`, `Vacant_unknown_enables_bring_empty_for_authorized_line_operator`, `Remove_empty_is_never_offered_as_a_selectable_action`, `Occupied_full_enables_remove_full_for_authorized_operator`, `Warehouse_operator_receives_only_bring_empty_capability`, `Active_request_blocks_every_second_action_for_the_slot`, `Admin_web_context_never_receives_pda_transport_capabilities`, `Execution_projection_maps_tkg_and_persisted_rds_state_without_claiming_motion`, `Terminal_tkg_business_state_takes_precedence_over_stale_rds_state`, `Known_failure_is_mapped_to_the_operator_safe_contract_for_every_role`, `Unrecognised_failure_code_does_not_fall_back_to_raw_rds_text`, `Core_traffic_warning_projects_waiting_not_failed`, `Missing_rds_mapping_is_safe_and_explicit_without_invented_site_id`, `Persisted_rds_mapping_is_projected_exactly_without_invention`, `Mapped_unavailable_rds_evidence_is_not_execution_ready`, `Action`, `Actor`, `Slot`, `Data`, `Request`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`


#### 27.8.30 tests/TkgPda.Application.Tests/RdsCommissioningDiscoveryTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 111 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class RdsCommissioningDiscoveryTests`

**Tanımlanan üyeler.** `Canonical_snapshot_and_report_are_deterministic`, `Comparison_keeps_customer_gaps_separate_from_confirmed_rds_blockers`, `Explicit_expected_robot_and_bintask_contracts_become_blockers_when_absent`, `Machine_regions_are_not_compared_as_transport_slots`, `Configuration`, `Snapshot`, `Json`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.31 tests/TkgPda.Application.Tests/RdsCoreNativeOrderTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 112 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class RdsCoreNativeOrderTests`

**Tanımlanan üyeler.** `Normal_order_uses_request_identity_deterministic_blocks_and_is_always_sealed`, `Normal_order_rejects_non_deterministic_ids_and_missing_locations`, `Typed_JSON_argument_fields_reject_non_object_values`, `Reconciliation_classifies_every_RDS_order_state_without_mutating_domain`, `Finished_from_queued_uses_only_legal_existing_transitions`, `Suspended_unknown_and_terminal_conflicts_never_generate_mutations`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`


#### 27.8.32 tests/TkgPda.Application.Tests/RdsExecutionCoordinatorNativeOrderTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 2145 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class RdsExecutionCoordinatorNativeOrderTests`, `record NativeTestFixture`, `class FakeNativeExecutionLease`, `class FakeNativeStore`, `class FakeNativeWorkflowStore`, `class FakeNativeExecutionClient`, `class FakeNativeReadClient`, `class FakeNativeWorkSiteReconciler`, `class FakeNativeOrderClient`, `class RobotLeaseRegistry`, `class Handle`

**Tanımlanan üyeler.** `WaitingPreparation_readiness_state_never_dispatches_native_order`, `Duplicate_worker_attempt_aborts_when_lease_cannot_be_acquired`, `Native_dispatch_uses_deterministic_order_and_block_identities`, `Ambiguous_setOrder_network_timeout_recovers_via_details_query`, `Finished_native_order_reconciles_to_Completed`, `Stopped_native_order_reconciles_to_Cancelled_when_cancellation_requested`, `Failed_native_order_reconciles_to_Failed_safely`, `Unknown_RDS_state_does_not_mutate_request`, `Native_and_Wind_mode_are_mutually_exclusive`, `Repeated_cancel_is_idempotent`, `Same_map_bypasses_switch_map_and_dispatches_transport`, `Different_map_orchestrates_navigation_to_transition_station_locks_switches_unlocks_and_dispatches_transport`, `Different_map_when_already_at_transition_station_skips_preliminary_navigation`, `Fail_closed_when_NavigationProfile_unresolved`, `Fail_closed_when_required_map_unresolved`, `Fail_closed_when_vehicle_unresolved`, `Fail_closed_when_robot_offline`, `Fail_closed_when_robot_not_dispatchable`, `Fail_closed_when_robot_has_blocking_alarm`, `Fail_closed_when_robot_has_incompatible_order`, `Fail_closed_when_switch_transition_station_unresolved`, `Fail_closed_when_switchMap_fails`, `Fail_closed_when_switchMap_verification_fails`, `Fail_closed_when_post_switch_alarm_detected`, `Restart_recovery_when_map_already_switched_resumes_without_duplicate_switch`, `ExecutionMode_missing_fails_closed_with_EXECUTION_MODE_UNRESOLVED`, `ExecutionMode_invalid_fails_closed_with_EXECUTION_MODE_INVALID`, `Unmapped_variant_fails_closed_with_NAVIGATION_PROFILE_UNRESOLVED`, `Wrong_machine_or_different_variant_never_resolves_profile`, `Switch_transition_unconfirmed_when_robot_station_does_not_match_transition_station`, `Switch_transition_unconfirmed_when_robot_has_alarm_at_transition_station`, `Crash_recovery_scenario_A_lock_acquired_before_switchMap_crashed_fails_closed`, `Crash_race_A_TKG_lock_fingerprint_matches_after_restart_safe_recovery_behavior`, `Lock_recovery_is_blocked_when_the_record_names_a_different_robot`, `ArrangeCorrelatedRecovery`, `TkgLock`, `Lock_recovery_is_blocked_by_this_requests_own_active_main_order`, `Lock_recovery_is_blocked_by_this_requests_own_active_map_prep_order`, `Lock_recovery_continues_when_the_robots_own_order_is_terminal`, `Lock_recovery_does_not_treat_procBusiness_false_as_proof_of_idle`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`


#### 27.8.33 tests/TkgPda.Application.Tests/RdsExecutionCoordinatorTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 391 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class RdsExecutionCoordinatorTests`, `record TestFixture`, `class Lease`, `class FakeExecutionStore`, `class FakeExecutionClient`, `class FakeReadClient`, `class FakeWorkSiteReconciler`, `class FakeWorkflowStore`

**Tanımlanan üyeler.** `Binder_resolves_whitelisted_sources_deterministically`, `Binder_rejects_missing_required_value`, `Binder_rejects_unknown_source_and_duplicate_param`, `Dispatch_precreates_identity_and_marks_sent_only_after_query_proof`, `Ambiguous_create_reconciles_same_identity_without_second_create`, `Missing_query_proof_keeps_request_queued`, `Unsafe_endpoint_blocks_dispatch`, `Unmapped_stale_and_locked_required_worksites_block_dispatch`, `An_auto_allocated_empty_container_buffer_slot_that_is_unmapped_cannot_be_dispatched`, `Missing_template_binding_keeps_other_dispatch_cycles_recoverable`, `Reconciliation_maps_running_and_finished`, `Suspended_status_remains_nonterminal_and_termination_without_cancel_intent_fails`, `Reconciliation_maps_abnormal_terminal_states_to_failed`, `Terminal_callback_worksite_refresh_remains_durable_for_the_next_worker_cycle`, `Duplicate_callback_is_harmless_and_unknown_task_is_ignored`, `Confirmed_stop_uses_same_task_identity_and_cancels_only_after_query_proof`, `Content_sync_not_configured_never_calls_Rds`, `Configured_content_sync_sends_exact_value_without_occupancy_or_filled_semantics`, `Fixture`, `Context`, `Endpoint`, `Request`, `WindTask`, `Projection`, `Bindings`, `Json`, `ListQueuedRequestIdsAsync`, `ListActiveTasksAsync`, `ListPendingWorkSiteRefreshRequestIdsAsync`, `TryAcquireRequestLeaseAsync`, `TryAcquireRobotLeaseAsync`, `GetDispatchContextAsync`, `FindRequestAsync`, `FindTaskByRequestAsync`, `FindTaskByRecordIdAsync`, `EnsureTaskRecordAsync`, `RecordDispatchResponseAsync`, `RecordTaskStatusAsync`, `MarkCancellationRequestedAsync`, `WriteExecutionAuditAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`


#### 27.8.34 tests/TkgPda.Application.Tests/RdsRobotAlarmParserTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 387 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class RdsRobotAlarmParserTests`

**Tanımlanan üyeler.** `ParseRobot`, `ParseContainer`, `Core_level_envelope_with_explicit_code_is_parsed_with_severity`, `Core_level_envelope_with_dynamically_keyed_code_is_parsed_with_severity`, `Robot_level_rbk_report_envelope_with_explicit_code_is_parsed`, `Robot_level_rbk_report_envelope_with_dynamically_keyed_code_is_parsed`, `Each_documented_bucket_maps_to_its_own_severity`, `Malformed_entries_are_skipped_without_dropping_valid_alarms`, `Empty_absent_or_unusable_buckets_yield_no_alarms`, `Root_alarms_wrapper_parses_fatal_with_shape_a`, `Root_alarms_wrapper_parses_error_with_shape_b`, `Root_alarms_wrapper_keeps_non_blocking_severities`, `Rbk_report_alarms_wrapper_parses_every_severity`, `Rbk_report_alarms_wrapper_tolerates_null_buckets`, `Wrapper_and_direct_buckets_coexist_exactly_as_documented`, `A_malformed_entry_in_the_wrapper_does_not_discard_a_valid_fatal`, `A_duplicate_across_wrapper_and_direct_bucket_is_reported_once`, `The_same_alarm_in_two_envelopes_is_reported_once`, `The_same_code_at_two_severities_stays_two_alarms`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.35 tests/TkgPda.Application.Tests/RdsTelemetryDomainBoundaryTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 399 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class RdsTelemetryDomainBoundaryTests`, `class StubRdsReadClient`, `class StubRdsIntegrationStore`

**Tanımlanan üyeler.** `Rds_reported_payload_loaded_telemetry_never_mutates_or_influences_tkg_load_state`, `Active_native_order_may_have_current_map_differ_from_pre_dispatch_map_without_invariant_violation`, `Online_and_undispatchable_is_representable_across_orthogonal_dimensions`, `Online_and_blocked_and_running_is_representable_across_orthogonal_dimensions`, `LockedRobot`, `ProjectAsync`, `Case_A_full_matching_fingerprint_is_labeled_tkg_correlated`, `Case_B_different_nickname_is_not_tkg_correlated`, `Case_C_captured_fingerprint_missing_is_not_tkg_correlated`, `Case_D_current_fingerprint_missing_is_not_tkg_correlated`, `Case_E_stale_unreleased_audit_with_external_lock_is_not_tkg_correlated`, `Case_F_correlation_works_when_uuid_differs_from_vehicle_id`, `Case_G_absent_lock_telemetry_is_unknown_not_unlocked`, `Observed_unlocked_robot_is_reported_unlocked`, `Missing_connectivity_dispatch_and_battery_telemetry_stay_unknown`, `Notice_alarms_are_counted_separately_from_errors`, `Cancellation_during_lock_fingerprint_lookup_is_not_swallowed`, `Non_cancellation_fingerprint_failure_fails_closed_without_crashing_admin`, `GetWorkSitesAsync`, `QueryWindTasksAsync`, `GetRobotAlarmsAsync`, `ListNavigationProfilesAsync`, `ListWorkSitesAsync`, `ListSlotMappingsAsync`, `SetSlotMappingAsync`, `RemoveSlotMappingAsync`, `GetActiveTkgLockFingerprintsAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`


#### 27.8.36 tests/TkgPda.Application.Tests/TkgPda.Application.Tests.csproj

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild proje dosyası | Test | 19 | Kısa |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.37 tests/TkgPda.Application.Tests/TransportEtaServiceTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 156 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class TransportEtaServiceTests`

**Tanımlanan üyeler.** `Terminal_requests_return_unavailable_eta`, `Terminal_rds_statuses_return_unavailable_eta`, `Explicit_remaining_time_in_last_status_json_is_used`, `Running_request_calculates_remaining_time_from_started_at`, `Running_request_exceeding_cycle_time_preserves_floor`, `Queued_requests_project_eta_proportionally_to_position`, `From_reliable_remaining_time_handles_valid_and_invalid_values`, `Waiting_preparation_yields_unavailable_eta`

**Proje içi bağımlılıklar.** `TkgPda.Domain`


#### 27.8.38 tests/TkgPda.Application.Tests/TransportWorkflowServiceTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 179 | Yapısal |

**Rol.** Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi.

**Tanımlanan tipler.** `class TransportWorkflowServiceTests`, `class FakeStore`

**Tanımlanan üyeler.** `Valid_create_is_delegated_to_store`, `Blank_idempotency_key_is_rejected_before_store`, `Bring_empty_accepts_an_unresolved_warehouse_source`, `Removal_accepts_an_unresolved_machine_side_source`, `Cancellation_is_delegated`, `Empty_cancellation_id_is_rejected`, `Unsupported_execution_transition_is_rejected`, `Variant_switch_is_delegated`, `Command`, `CreateAndQueueAsync`, `MarkReadyAsync`, `CreateBatchAndQueueAsync`, `CancelAsync`, `RecordExecutionTransitionAsync`, `RecordExecutionOutcomeAsync`, `UpdateLoadStateAsync`, `SwitchVariantAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`


#### 27.8.39 tests/TkgPda.Auth.Tests/AuthenticationTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 171 | Yapısal |

**Rol.** Kimlik ve yetki testi.

**Tanımlanan tipler.** `class PasswordHashTests`, `class JwtTokenTests`, `class FixedTimeProvider`

**Tanımlanan üyeler.** `Hash_uses_random_salt`, `Correct_password_verifies`, `Wrong_password_is_rejected`, `Malformed_hash_is_rejected_without_throwing`, `Hash_records_the_fixed_iteration_policy`, `Token_contains_exact_roles_and_device_binding`, `Admin_web_token_has_explicit_client_type_without_device_claims`, `Admin_web_token_validates_with_the_same_short_lived_security_parameters`, `Password_hasher_implements_the_application_hashing_port`, `Token_expiry_is_exactly_configured_lifetime`, `Token_validates_with_expected_issuer_audience_and_key`, `Wrong_audience_is_rejected`, `User`, `Device`, `AdminUser`, `GetUtcNow`

**Proje içi bağımlılıklar.** `TkgPda.Api`, `TkgPda.Application`


#### 27.8.40 tests/TkgPda.Auth.Tests/TkgPda.Auth.Tests.csproj

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild proje dosyası | Test | 19 | Kısa |

**Rol.** Kimlik ve yetki testi.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.41 tests/TkgPda.Domain.Tests/TkgPda.Domain.Tests.csproj

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild proje dosyası | Test | 18 | Kısa |

**Rol.** Domain birim testi; iş kurallarını I/O olmadan doğrular.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.42 tests/TkgPda.Domain.Tests/TransportDomainTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 245 | Yapısal |

**Rol.** Domain birim testi; iş kurallarını I/O olmadan doğrular.

**Tanımlanan tipler.** `class TransportDomainTests`

**Tanımlanan üyeler.** `Sql_transport_statuses_are_complete_and_deterministic`, `Legal_transition_table_is_accepted`, `Terminal_states_have_no_outgoing_transition`, `Queued_cannot_skip_directly_to_completed`, `Operation_accepts_its_required_initial_state`, `Operation_rejects_an_incompatible_initial_state`, `Machine_region_can_never_be_a_transport_endpoint`, `Empty_storage_can_never_be_the_machine_side_endpoint`, `Unknown_part_and_container_associations_do_not_block`, `Explicit_part_conflict_blocks`, `Explicit_container_conflict_blocks`, `Cancellation_restores_the_deterministic_state`, `Completion_applies_the_final_slot_state`, `Request`, `Slot`

**Proje içi bağımlılıklar.** `TkgPda.Domain`


#### 27.8.43 tests/TkgPda.Domain.Tests/TransportEmptyContainerBufferDomainTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 170 | Yapısal |

**Rol.** Domain birim testi; iş kurallarını I/O olmadan doğrular.

**Tanımlanan tipler.** `class TransportEmptyContainerBufferDomainTests`

**Tanımlanan üyeler.** `Only_empty_container_operations_require_a_buffer_slot`, `Bring_empty_buffer_slot_is_its_source`, `Remove_empty_buffer_slot_is_its_destination`, `Remove_full_never_has_a_buffer_slot`, `Expected_buffer_state_matches_the_operation`, `Buffer_completion_state_is_the_mirror_image_of_the_machine_side`, `A_correctly_configured_and_staged_buffer_slot_is_accepted`, `A_slot_that_is_not_configured_as_EMPTY_STORAGE_is_rejected`, `A_buffer_slot_in_the_wrong_state_is_rejected`, `A_configured_buffer_slot_still_enforces_part_associations`, `Request`, `BufferSlot`

**Proje içi bağımlılıklar.** `TkgPda.Domain`


#### 27.8.44 tests/TkgPda.Domain.Tests/TransportReadinessDomainTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 133 | Yapısal |

**Rol.** Domain birim testi; iş kurallarını I/O olmadan doğrular.

**Tanımlanan tipler.** `class TransportReadinessDomainTests`

**Tanımlanan üyeler.** `Omitted_requires_preparation_defaults_to_not_required`, `Requires_preparation_false_yields_not_required`, `Requires_preparation_true_yields_waiting_preparation`, `Mark_ready_is_allowed_from_waiting_preparation_on_a_non_terminal_request`, `Mark_ready_is_rejected_when_readiness_is_not_required`, `Mark_ready_is_rejected_on_a_terminal_request`, `Repeated_mark_ready_on_an_already_ready_request_is_a_no_op`, `Ready_cannot_be_reached_again_through_ensure_can_mark_ready_once_already_ready`, `Sql_readiness_states_are_complete_and_deterministic`, `Command`

**Proje içi bağımlılıklar.** `TkgPda.Domain`


#### 27.8.45 tests/TkgPda.Infrastructure.Postgres.Tests/AssemblyInfo.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 4 | Kısa |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.46 tests/TkgPda.Infrastructure.Postgres.Tests/PostgresEmployeeNumberIdentityTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 133 | Yapısal |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

**Tanımlanan tipler.** `class PostgresEmployeeNumberIdentityTests`

**Tanımlanan üyeler.** `Migration_creates_the_normalized_employee_number_unique_index`, `Employee_number_uniqueness_is_case_insensitive`, `Employee_number_uniqueness_ignores_surrounding_whitespace`, `Leading_zero_employee_numbers_remain_distinct`, `Multiple_null_employee_numbers_are_allowed`, `Blank_employee_numbers_are_exempt_from_uniqueness`, `Store_resolves_user_by_normalized_employee_number`, `ResetAsync`, `InsertUserAsync`, `ExecuteAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Infrastructure.Postgres`


#### 27.8.47 tests/TkgPda.Infrastructure.Postgres.Tests/PostgresEmptyContainerBufferTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 493 | Yapısal |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

**Tanımlanan tipler.** `class PostgresEmptyContainerBufferTests`, `record LayoutContext`

**Tanımlanan üyeler.** `Bring_empty_ignores_a_same_state_slot_that_is_not_configured_as_EMPTY_STORAGE`, `Bring_empty_source_must_be_OCCUPIED_and_EMPTY`, `Remove_empty_ignores_a_same_state_slot_that_is_not_configured_as_EMPTY_STORAGE`, `Remove_empty_destination_must_be_VACANT`, `Bring_empty_completion_updates_both_the_buffer_and_the_machine_slot`, `Remove_empty_completion_updates_both_the_machine_and_the_buffer_slot`, `Remove_full_never_uses_the_empty_container_buffer`, `Missing_buffer_configuration_fails_cleanly_instead_of_guessing`, `Missing_worksite_mapping_on_the_allocated_buffer_is_reported_cleanly_at_dispatch_resolution`, `An_active_reservation_prevents_a_second_request_from_claiming_the_same_buffer_slot`, `Multiple_eligible_buffer_slots_resolve_deterministically_by_display_order_then_code`, `Existing_standalone_transport_flows_remain_valid`, `ResetAsync`, `ConfigureAsync`, `ContextAsync`, `LogisticsSlotAsync`, `LogisticsSlotsAsync`, `CreateSlotAsync`, `SetSlotAsync`, `Command`, `CaptureRuleAsync`, `ExecuteAsync`, `AddParameters`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`, `TkgPda.Infrastructure.Postgres`


#### 27.8.48 tests/TkgPda.Infrastructure.Postgres.Tests/PostgresFactAttribute.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 15 | Kısa |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

**Tanımlanan tipler.** `class PostgresFactAttribute`


#### 27.8.49 tests/TkgPda.Infrastructure.Postgres.Tests/PostgresLayoutCloningTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 115 | Yapısal |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

**Tanımlanan tipler.** `class PostgresLayoutCloningTests`

**Tanımlanan üyeler.** `CreateLayoutVersionAsync_preserves_authoritative_layout_slot_parts_relationships`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.50 tests/TkgPda.Infrastructure.Postgres.Tests/PostgresRdsCommissioningConfigurationStoreTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 28 | Yapısal |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

**Tanımlanan tipler.** `class PostgresRdsCommissioningConfigurationStoreTests`

**Tanımlanan üyeler.** `Baseline_configuration_is_read_without_inventing_robot_or_bintask_contracts`


#### 27.8.51 tests/TkgPda.Infrastructure.Postgres.Tests/PostgresRdsReadinessTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 137 | Yapısal |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

**Tanımlanan tipler.** `class PostgresRdsReadinessTests`

**Tanımlanan üyeler.** `Read_fixture_sync_mapping_audit_and_operational_projection_are_safe_and_idempotent`, `Site`, `ExecuteAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`


#### 27.8.52 tests/TkgPda.Infrastructure.Postgres.Tests/PostgresTransportReadinessTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 406 | Yapısal |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

**Tanımlanan tipler.** `class PostgresTransportReadinessTests`, `record LayoutContext`

**Tanımlanan üyeler.** `Requires_preparation_omitted_or_false_creates_a_not_required_request`, `Requires_preparation_true_creates_a_waiting_preparation_request`, `Waiting_preparation_requests_are_excluded_from_the_dispatch_queue`, `A_higher_precedence_waiting_preparation_request_does_not_block_a_lower_precedence_eligible_request`, `Eligible_requests_preserve_priority_ordering_then_fifo_ordering`, `Mark_ready_transitions_waiting_preparation_to_ready_and_sets_ready_at_once`, `Not_required_requests_still_follow_the_existing_workflow_and_reject_mark_ready`, `A_terminal_request_cannot_be_marked_ready`, `ResetAsync`, `ConfigureAsync`, `EnsureDefaultBufferSlotsAsync`, `ContextAsync`, `LogisticsSlotAsync`, `LogisticsSlotsAsync`, `SetSlotAsync`, `Command`, `CaptureRuleAsync`, `ExecuteAsync`, `AddParameters`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`, `TkgPda.Infrastructure.Postgres`


#### 27.8.53 tests/TkgPda.Infrastructure.Postgres.Tests/PostgresTransportWorkflowTests.cs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| C# kaynak | Test | 1330 | Yapısal |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

**Tanımlanan tipler.** `class PostgresTransportWorkflowTests`, `record LayoutContext`, `record Attempt`

**Tanımlanan üyeler.** `Bring_empty_auto_allocates_a_configured_buffer_source_without_changing_any_slot_state`, `Bring_empty_success_changes_destination_to_occupied_empty`, `Remove_full_cancellation_restores_occupied_full_and_preserves_known_values`, `Remove_empty_success_changes_source_to_vacant_unknown`, `Bring_and_remove_empty_cancellation_restore_their_expected_states`, `Load_state_reporting_changes_only_load_state_and_is_idempotent`, `Load_state_reporting_rejects_vacant_machine_and_busy_slots`, `Rds_task_identity_is_precreated_exactly_once_with_request_uuid`, `Rds_dispatch_context_resolves_explicit_template_binding_and_physical_mapping`, `Endpoint_resolution_REMOVE_EMPTY_resolves_strictly_to_EMPTY_STORAGE_never_DROP`, `Navigation_profile_fails_closed_and_never_leaks_unmapped_or_other_variants`, `HasUnreleasedMapSwitchLock_tracks_lock_lifecycle_in_audit_logs`, `Machine_region_is_rejected`, `Layout_machine_regions_are_distinct_from_customer_machine_master_records`, `Slot_from_another_machine_is_rejected`, `Request_context_must_match_active_variant_and_layout`, `Sequential_idempotent_retry_returns_the_same_request`, `Reusing_an_idempotency_key_for_a_different_payload_is_rejected`, `Concurrent_requests_for_the_same_slot_create_only_one_reservation`, `Concurrent_retry_with_same_key_returns_one_logical_request`, `Explicit_container_conflict_is_rejected_but_null_association_is_allowed`, `Source_unresolved_associations_accept_existing_master_part_and_container_without_guessing`, `Explicit_part_conflict_from_curated_slot_mapping_is_rejected`, `Active_reservation_blocks_variant_switch_until_cancelled`, `Cancellation_after_dispatch_uses_the_stable_deferred_error`, `Cancellation_while_running_uses_the_stable_deferred_error`, `Concurrent_cancellation_and_dispatch_do_not_deadlock`, `Bring_empty_auto_resolves_the_part_mapped_vacant_machine_slot`, `Automatic_slot_resolution_replay_with_the_same_key_is_idempotent`, `Automatic_machine_slot_resolution_never_uses_EMPTY_STORAGE`, `Remove_full_auto_resolves_the_part_mapped_occupied_full_machine_slot`, `Remove_empty_auto_resolves_the_part_mapped_occupied_empty_machine_slot`, `Auto_resolution_never_selects_a_slot_mapped_to_a_different_part`, `Auto_resolution_never_selects_a_slot_outside_the_active_layout`, `Auto_resolution_never_selects_a_disabled_slot`, `Auto_resolution_never_selects_a_slot_with_an_active_reservation`, `Auto_resolution_is_deterministic_when_multiple_slots_are_eligible`, `Concurrent_auto_resolution_for_the_same_part_claims_only_one_of_two_eligible_slots`, `No_eligible_slot_reports_a_friendly_message_per_operation_without_a_raw_code`, `ResetAsync`

**Proje içi bağımlılıklar.** `TkgPda.Application`, `TkgPda.Domain`, `TkgPda.Infrastructure.Postgres`


#### 27.8.54 tests/TkgPda.Infrastructure.Postgres.Tests/TkgPda.Infrastructure.Postgres.Tests.csproj

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild proje dosyası | Test | 20 | Kısa |

**Rol.** PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.55 tests/db/001_schema_smoke_test.sql

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| SQL | Test | 509 | Yapısal |

**Rol.** Veritabanı doğrulama betiği.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.56 tests/phase_02a/expected_counts.json

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JSON yapılandırma | Test | 10 | Kısa |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.57 tests/phase_02a/run_postgres_integration.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 150 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `Port`


#### 27.8.58 tests/phase_02a/test_pipeline.py

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Python betiği | Test | 135 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.59 tests/phase_02b/expected_counts.json

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JSON yapılandırma | Test | 19 | Kısa |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.60 tests/phase_02b/run_postgres_integration.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 241 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `Port`


#### 27.8.61 tests/phase_02b/test_pipeline.py

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Python betiği | Test | 216 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.62 tests/phase_03/run_postgres_integration.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 93 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `Port`


#### 27.8.63 tests/phase_04/run_postgres_api_integration.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 99 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `Port`


#### 27.8.64 tests/phase_05/run_postgres_admin_integration.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 100 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `Port`


#### 27.8.65 tests/phase_07a/run_postgres_pda_rds_integration.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 100 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `Port`


#### 27.8.66 tests/phase_07b/run_real_api_contract.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 252 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `PgPort`, `ApiPort`


#### 27.8.67 tests/phase_08a/run_live_rds_postgres_sync.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 53 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `Port`


#### 27.8.68 tests/phase_08a/run_live_rds_readonly.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 65 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.69 tests/phase_08a/run_postgres_rds_readiness_integration.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 83 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `Port`


#### 27.8.70 tests/phase_08b/run_postgres_rds_execution_integration.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 120 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `Port`


#### 27.8.71 tests/phase_08c/run_live_rds_simulator_e2e.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 520 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `PostgresPort`, `ApiPort`, `RdsBaseUrl`, `CommissioningPhase`, `TaskLabel`, `FromParameter`, `ToParameter`, `FromValue`, `ToValue`, `SceneConfirmationVariable`, `TerminalTimeoutSeconds`


#### 27.8.72 tests/phase_09/run_web_operator_e2e.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 408 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `PgPort`, `ApiPort`, `ChromePort`, `EdgePort`, `Port`


#### 27.8.73 tests/phase_09/web_contract.test.mjs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JavaScript modülü | Test | 62 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.74 tests/phase_09/webdriver_edge_proxy.mjs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JavaScript modülü | Test | 90 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.75 tests/phase_10a/rds_dynamic_task_contract.test.mjs

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JavaScript modülü | Test | 134 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.8.76 tests/phase_10a/run_live_dynamic_rds_e2e.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 102 | Yapısal |

**Rol.** Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı.

**Parametreler.** `PgBin`, `PostgresPort`, `ApiPort`, `RdsBaseUrl`


#### 27.8.77 tests/wp30/run_postgres_admin_discovery.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Test | 109 | Yapısal |

**Rol.** WP-30 doğrulama kaydı.

**Parametreler.** `PgBin`, `Port`


### 27.9 Araçlar ve betikler (11)


#### 27.9.1 scripts/run_commissioning.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Araç/Betik | 116 | Derin analiz |

**Amaç.** API host'unu devreye alma modunda, fiziksel dispatch açık olarak başlatan tek betik.

**Sorumluluklar.**

- `Rds__Enabled=true` ve `Rds__DispatchEnabled=true` ortamını kurmak.
- Bağlantı dizesini yerel yığın durum dosyasından çözmek.
- Eksik zorunlu yapılandırmada hızlı başarısız olmak.
- Sırsız bir başlangıç özeti basmak.

**Metotlar ve mantık.**

| Metot | Ne yapar ve neden |
|---|---|
| Parametre çözümü | Her sır önce ortam değişkeninden, sonra parametreden alınır. JWT anahtarı hiç verilmezse 48 baytlık kriptografik rastgele üretilir. |
| Bağlantı dizesi çözümü | `%LOCALAPPDATA%\TKG_PDA_DEV\state\stack_state.json` dosyasından okunur; depoya hiç yazılmaz. |
| Başlatma | `dotnet run --no-launch-profile` — `launchSettings.json` git tarafından yok sayıldığı için paylaşılan bir profil olamaz. |

**Bağımlılıklar.** PowerShell 7+, .NET SDK, çalışan PostgreSQL.

**Çağıranlar.** Devreye alma mühendisi (elle).

**Durum ve yan etki.** Ortam değişkenleri.

**İş kuralları / değişmezler.**

- Üretimle aynı worker, aynı `RdsMutationPolicy` ve aynı native order yolu kullanılır — devreye alma için ayrı kod yolu yoktur.
- Normal geliştirme bu betiği çalıştırmaz.

**Hata yönetimi.** Eksik yapılandırmada hızlı hata.

**Güvenlik.** Sırları asla sabit kodlamaz ve günlüğe basmaz. COMMISSIONING VALIDATION REQUIRED — bu betik gerçek robotu hareket ettirebilir.

**Testler.** Otomatik testi yoktur; sahada elle doğrulanır.


#### 27.9.2 tools/dev/README.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Araç/Betik | 122 | Yapısal |

**Rol.** Yerel geliştirme yığını betiği. Durum dosyası `%LOCALAPPDATA%` altında tutulur; depoya sır yazılmaz.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.9.3 tools/dev/reset_local_database.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Araç/Betik | 79 | Yapısal |

**Rol.** Yerel geliştirme yığını betiği. Durum dosyası `%LOCALAPPDATA%` altında tutulur; depoya sır yazılmaz.

**Parametreler.** `PgBin`, `Force`


#### 27.9.4 tools/dev/start_local_stack.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Araç/Betik | 831 | Yapısal |

**Rol.** Yerel geliştirme yığını betiği. Durum dosyası `%LOCALAPPDATA%` altında tutulur; depoya sır yazılmaz.

**Parametreler.** `PgBin`, `PostgresPort`, `Database`, `ApiPort`, `AdminPort`, `OperatorPort`, `HostAddress`, `RdsMode`, `RdsBaseUrl`, `IntegrationKey`, `CallbackKey`, `CallbackUrl`, `Password`, `NoOperator`, `NoAdmin`, `EnableSimulationTrialMappings`, `Wait`, `Description`


#### 27.9.5 tools/dev/stop_local_stack.ps1

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| PowerShell betiği | Araç/Betik | 243 | Yapısal |

**Rol.** Yerel geliştirme yığını betiği. Durum dosyası `%LOCALAPPDATA%` altında tutulur; depoya sır yazılmaz.

**Parametreler.** `PgBin`, `ApiPort`, `AdminPort`, `OperatorPort`, `KeepPostgres`, `Description`


#### 27.9.6 tools/phase_02a/__init__.py

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Python betiği | Araç/Betik | 2 | Kısa |

**Rol.** Müşteri kaynak verisi dönüştürme aracı. Çıktıları üretilmiş kabul edilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.9.7 tools/phase_02a/pipeline.py

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Python betiği | Araç/Betik | 1413 | Yapısal |

**Rol.** Müşteri kaynak verisi dönüştürme aracı. Çıktıları üretilmiş kabul edilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.9.8 tools/phase_02a/requirements.txt

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Metin dosyası | Araç/Betik | 2 | Kısa |

**Rol.** Müşteri kaynak verisi dönüştürme aracı. Çıktıları üretilmiş kabul edilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.9.9 tools/phase_02b/__init__.py

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Python betiği | Araç/Betik | 2 | Kısa |

**Rol.** Müşteri kaynak verisi dönüştürme aracı. Çıktıları üretilmiş kabul edilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.9.10 tools/phase_02b/pipeline.py

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Python betiği | Araç/Betik | 1584 | Yapısal |

**Rol.** Müşteri kaynak verisi dönüştürme aracı. Çıktıları üretilmiş kabul edilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.9.11 tools/phase_02b/requirements.txt

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Metin dosyası | Araç/Betik | 3 | Kısa |

**Rol.** Müşteri kaynak verisi dönüştürme aracı. Çıktıları üretilmiş kabul edilir.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


### 27.10 RDS varlıkları (4)


#### 27.10.1 rds/scripts/README.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | RDS varlığı | 26 | Yapısal |

**Rol.** RDS tarafı yardımcı betik. Üretim eşlemesi değildir; sahada doğrulanmalıdır.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.10.2 rds/scripts/test/tkg_integration.test.js

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JavaScript | RDS varlığı | 118 | Yapısal |

**Rol.** RDS tarafı yardımcı betik. Üretim eşlemesi değildir; sahada doğrulanmalıdır.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.10.3 rds/scripts/tkg_integration.js

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JavaScript | RDS varlığı | 172 | Yapısal |

**Rol.** RDS tarafı yardımcı betik. Üretim eşlemesi değildir; sahada doğrulanmalıdır.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.10.4 rds/tasks/commissioning/README.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | RDS varlığı | 64 | Yapısal |

**Rol.** RDS görev şablonu örneği. Üretim geometrisi değildir; COMMISSIONING VALIDATION REQUIRED.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


### 27.11 Dokümanlar (32)


#### 27.11.1 docs/CUSTOMER_WP01A_OPERATOR_EMPLOYEE_IDENTITY.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 165 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.2 docs/OPERATOR_WEB_DEPLOYMENT.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 138 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.3 docs/PHASE_01_NOTES.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 101 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.4 docs/PHASE_02A_NOTES.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 173 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.5 docs/PHASE_02B_NOTES.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 234 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.6 docs/PHASE_03_NOTES.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 202 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.7 docs/PHASE_04_NOTES.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 219 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.8 docs/PHASE_05_ADMIN_CONFIGURATION_API.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 272 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.9 docs/PHASE_06_ADMIN_WEB.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 181 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.10 docs/PHASE_07A_PDA_RDS_CONTRACT_READINESS.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 111 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.11 docs/PHASE_07B_FLUTTER_PDA.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 137 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.12 docs/PHASE_08A_LIVE_RDS_READINESS.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 180 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.13 docs/PHASE_08B_RDS_EXECUTION_INTEGRATION.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 206 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.14 docs/PHASE_08C_RDS_SIMULATOR_COMMISSIONING.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 151 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.15 docs/PHASE_09_MULTIPLATFORM_OPERATOR.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 154 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.16 docs/PHASE_10A_DYNAMIC_RDS_WINDTASK.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 180 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.17 docs/PROJECT_BASELINE.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 253 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.18 docs/RDS_PDA_INTEGRATION_SPEC.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 178 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.19 docs/README.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 52 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.20 docs/TKG_PDA_DATABASE_SPEC_v0.1.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 274 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.21 docs/WP_33_OPERATOR_UX_V2_SHARED_QUEUE.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 55 | Yapısal |

**Rol.** Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.22 docs/commissioning/CUSTOMER_ACCEPTANCE_AUDIT.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 147 | Yapısal |

**Rol.** Devreye alma dokümanı; saha prosedürleri ve kanıt kayıtları.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.23 docs/commissioning/CUSTOMER_DATA_REQUEST.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 119 | Yapısal |

**Rol.** Devreye alma dokümanı; saha prosedürleri ve kanıt kayıtları.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.24 docs/commissioning/CUSTOMER_SOURCE_RECONCILIATION.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 115 | Yapısal |

**Rol.** Devreye alma dokümanı; saha prosedürleri ve kanıt kayıtları.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.25 docs/commissioning/PRODUCTION_COMMISSIONING_CHECKLIST.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 163 | Yapısal |

**Rol.** Devreye alma dokümanı; saha prosedürleri ve kanıt kayıtları.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.26 docs/commissioning/RDS_COMMISSIONING_DISCOVERY.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 120 | Yapısal |

**Rol.** Devreye alma dokümanı; saha prosedürleri ve kanıt kayıtları.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.27 docs/commissioning/RDS_COMMISSIONING_SNAPSHOT_SCHEMA.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 98 | Yapısal |

**Rol.** Devreye alma dokümanı; saha prosedürleri ve kanıt kayıtları.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.28 docs/commissioning/RDS_FIRST_SNAPSHOT_RUNBOOK.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 244 | Yapısal |

**Rol.** Devreye alma dokümanı; saha prosedürleri ve kanıt kayıtları.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.29 docs/database/ERD.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 405 | Yapısal |

**Rol.** Veritabanı dokümanı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.30 docs/integration/RDSCORE_NATIVE_ORDER_CONTRACT.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 203 | Yapısal |

**Rol.** Entegrasyon dokümanı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.31 docs/references/customer/README.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 32 | Yapısal |

**Rol.** Referans dokümanı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.11.32 docs/references/rds/RdsApi_en.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Doküman | 1849 | Yapısal |

**Rol.** Referans dokümanı.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


### 27.12 Kök seviyesi dosyalar (8)


#### 27.12.1 .gitattributes

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dosya | Kök | 11 | Kısa |

**Rol.** Proje dosyası.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.12.2 .gitignore

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Dosya | Kök | 73 | Yapısal |

**Rol.** Proje dosyası.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.12.3 AGENTS.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Kök | 94 | Yapısal |

**Rol.** Proje dosyası.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.12.4 CLAUDE.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Kök | 103 | Yapısal |

**Rol.** Proje dosyası.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.12.5 Directory.Build.props

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| MSBuild özellik dosyası | Kök | 11 | Kısa |

**Rol.** Proje dosyası.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.12.6 README.md

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Markdown doküman | Kök | 149 | Yapısal |

**Rol.** Proje dosyası.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.12.7 TKG.Pda.slnx

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| Çözüm dosyası | Kök | 16 | Kısa |

**Rol.** Proje dosyası.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


#### 27.12.8 global.json

| Tür | Katman | Satır | Analiz derinliği |
|---|---|---|---|
| JSON yapılandırma | Kök | 7 | Kısa |

**Rol.** Proje dosyası.

Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir.


---


## 28 Çağrı Grafikleri

Aşağıdaki on iki grafik sistemin ana yollarını uçtan uca gösterir. Her düğüm gerçek bir tip veya metottur; hiçbiri şematik değildir.


### 28.1 A — Operatör girişi

```text
LoginScreen
  -> AppController.login
    -> PdaRepository.login
      -> POST /api/v1/auth/login              (X-Device-Code)
        -> ApiEndpoints.LoginAsync
          -> EmployeeNumber.Normalize          (sicil kimliği)
          -> IApiDataStore.FindUser...
          -> JwtTokenFactory.Create            (roller + device_code iddiası)
        <- { accessToken, user, roles }
    -> SessionMemory.set                       (yalnız bellek)
  -> AppStage.machineSelection
```


### 28.2 B — Operasyonel bağlam yükleme

```text
OperationScreen
  -> AppController.refreshContext
    -> PdaRepository.operationalContext
      -> GET /api/v1/machines/{id}/operational-context
        -> PdaOperationalContextService
          -> IApiDataStore  (makine, varyant, layout, slot durumları)
          -> ContainerActionPolicy  (her pozisyon icin fullAction/emptyAction)
          -> RdsReadClient          (WorkSite tazeliği, salt okuma)
        <- OperationalContext { positions[], mutationGate, activeVariantCode }
  -> ContainerPositionPanel yeniden çizilir
```


### 28.3 C — WP-33 kasa aksiyonu (tekil)

```text
_BusinessActionButton (key: direct-action-REMOVE_FULL-2)
  -> AppController.submitContainerAction
    -> SubmissionCoordinator.keyFor        (maddi imza -> idempotency key)
    -> PdaRepository.createContainerAction
      -> POST /api/v1/.../container-actions   (Idempotency-Key)
        -> ApiEndpoints  -> EnsureTransportOperationAllowed
        -> ContainerActionService
          -> ContainerActionPolicy.Resolve      (occupancy/load -> operasyon)
          -> PostgresTransportWorkflowStore.CreateContainerActionAsync
             BEGIN
               pg_advisory_xact_lock(idempotency_key)
               UPDATE ops.slot_runtime_state    (EMPTY -> FULL, gerekiyorsa)
               INSERT ops.transport_requests    (status = Queued)
               INSERT ops.transport_request_events
             COMMIT
        <- TransportResult { id, status, idempotentReplay }
  -> SubmissionCoordinator.clear
```


### 28.4 D — Tüm kasaları kaldır (batch)

```text
ContainerPositionPanel (key: remove-all-containers)
  -> AppController.removeAllContainers
    -> PdaRepository.removeAllContainers
      -> POST /api/v1/.../containers/remove-all
        -> ContainerActionPolicy.ResolveRemoveAll   (her pozisyon icin)
             OCCUPIED+FULL   -> REMOVE_FULL
             OCCUPIED+EMPTY  -> REMOVE_EMPTY        (yalniz sunucu üretir)
             VACANT+UNKNOWN  -> null (atlanir)
             UNKNOWN         -> REMOVE_ALL_POSITION_UNKNOWN  (tüm batch red)
             çelişki         -> REMOVE_ALL_POSITION_CONFLICT (tüm batch red)
        -> tek transaction icinde tüm talepler
```


### 28.5 E — Dispatch worker döngüsü

```text
RdsBackgroundWorker (her Rds:WorkerIntervalSeconds)
  -> RdsExecutionCoordinator.DispatchQueuedAsync(limit)
       if (!IsDispatchEnabled) return []
    -> IRdsExecutionStore.ListQueuedRequestIdsAsync
    -> for each id:
         DispatchOneAsync(id, correlationId)
         catch RdsExecutionException        -> audit RDS_DISPATCH_BLOCKED
         catch RdsReadException             -> audit RDS_DISPATCH_DEFERRED
         catch RdsCoreNativeOrderException  -> audit RDS_DISPATCH_DEFERRED
```


### 28.6 F — Native order dispatch

```text
DispatchOneAsync
  -> (kapı yeniden kontrol) IsDispatchEnabled
  -> TryAcquireLeaseAsync("tkg:rds:{requestId}")     talep kirasi
  -> DispatchNativeOrderAsync
    -> GetNavigationProfileSummaryAsync        -> requiredMap, vehicle
    -> ResolveRobotLeaseKeyAsync(vehicle)
         -> nativeOrderClient.GetRobotsStatusAsync
         -> RdsRobotResolver.Resolve   (NoMatch/Unique/Ambiguous)
    -> TryAcquireRobotLeaseAsync("tkg:robot:{key}")   robot kirasi
    -> GetRobotsStatusAsync                    -> RdsCoreFleetStatus
    -> fleet.GlobalHasBlockingAlarm ?          -> RDSCORE_HAS_BLOCKING_ALARM
    -> ResolveUniqueRobot
    -> (TOCTOU) dispatchLeaseKey == robot.RobotLeaseKey ?
    -> robot.ConnectionStatus == 1 ?
    -> GetUnreleasedMapSwitchLockAsync         -> kilit kurtarma (A-I)
    -> robot.Dispatchable ?
    -> [harita farkli ise]  LockAsync -> SwitchMapAsync -> UnlockAsync
    -> SetOrderAsync                            FIZIKSEL HAREKET
    -> PostOutcomeAudit.RunAsync                (best-effort, 5 sn)
  <- await using: robot kirasi serbest, talep kirasi serbest
```


### 28.7 G — Uzman mutasyon (ManualFinish örneği)

```text
AdminEndpoints  (administrator/variant-manager politikasi)
  -> RdsExecutionCoordinator.ManualFinishAsync
    -> EnsureMutationAllowed(MotionEnabling, "manualFinished")
         kapali ise -> RDS_DISPATCH_DISABLED
    -> ResolveCurrentExecutionTargetAsync   (mevcut yürütmeye bağlanma)
    -> WithRobotLeaseAsync(alias, ...)
         -> ResolveRobotLeaseKeyAsync  (null ise EXPERT_CURRENT_STATE_UNAVAILABLE)
         -> TryAcquireRobotLeaseAsync  (null ise ROBOT_BUSY)
         -> leasedWork(heldLeaseKey):
              -> GetRobotsStatusAsync            yetkili yeniden okuma
              -> EnsureLeaseCoversTarget(...)    EXPERT_ROBOT_CHANGED_DURING_VALIDATION
              -> ManualFinishedAsync             FIZIKSEL ETKI
              -> PostOutcomeAudit.RunAsync
```


### 28.8 H — İptal

```text
OperatorQueueScreen / AdminEndpoints
  -> RdsExecutionCoordinator.CancelAsync(requestId)
    -> durum Created/Validating/Queued ?
         evet -> yerel iptal, RDS'e hic gidilmez
         hayir (SentToRds/Running) ->
              TerminateAsync   (SafetyStop -> kapi kapali olsa bile izinli)
    -> TransportStateMachine.EnsureTransition(-> Cancelled)
    -> rezervasyon serbest
```


### 28.9 I — Uzlaştırma (reconciliation)

```text
RdsBackgroundWorker
  -> RdsExecutionCoordinator.ReconcileAsync(limit)
    -> ListInFlightTasksAsync
    -> nativeOrderClient.GetOrderDetailsAsync / GetOrdersAsync
    -> ApplyTaskStatusAsync
         -> TransportStateMachine.EnsureTransition
         -> tamamlandiysa slot durumlari uygulanir
    -> RefreshRelevantWorkSitesAsync   (Rds:WorkSiteFreshnessSeconds)
```


### 28.10 J — RDS callback

```text
RDS  -> POST (Rds:CallbackUrl)
  -> RdsIntegrationEndpoints
    -> CallbackKey yapilandirilmis mi ?   degilse reddet
    -> FixedTimeEquals(supplied, CallbackKey)   sabit zamanli karsilastirma
    -> RdsExecutionCoordinator.ProcessCallbackAsync
      -> ApplyTaskStatusAsync   (uzlastirma ile ayni yol)
```


### 28.11 K — Layout sürüm klonlama

```text
LayoutsPage / LayoutEditorPage
  -> AdminEndpoints  (administrator)
    -> AdminConfigurationService
      -> PostgresAdminConfigurationStore.CloneLayoutAsync
         BEGIN
           INSERT master.layouts        (version = max+1, status DRAFT)
           INSERT master.layout_slots   (yeni UUID'ler, ayni code)
           INSERT master.layout_slot_parts
                 JOIN old_slot.code = new_slot.code
           SELECT count(*) beklenen ilişki sayisi
           if (copied != expected) -> InvalidOperationException  ROLLBACK
           INSERT audit  ADMIN_LAYOUT_VERSION_CREATED
         COMMIT
```


### 28.12 L — Robot alarm özeti (operatör görünümü)

```text
OperationScreen banner
  -> AppController.retryRobotAlarmSummary
    -> PdaRepository.robotAlarmSummary
      -> GET /api/v1/.../robot-alarm-summary
        -> RdsReadClient / RdsCoreNativeOrderClient
          -> RdsRobotAlarmParser  (4 zarf, 2 giriş şekli)
          -> RdsCoreFleetStatus { Robots, GlobalAlarms }
        <- yalniz IsBlocking olanlar operatöre bant olarak gösterilir
```


## 29 Veritabanı Akış Diyagramları


### 29.1 Taşıma talebi oluşturma — işlem sınırı

```text
BEGIN TRANSACTION
  |
  +-- pg_advisory_xact_lock(hashtextextended(idempotency_key,0))
  |     amaç: ayni anahtarla es zamanli iki istek serilestirilir
  |
  +-- SELECT ops.transport_requests WHERE idempotency_key = ?
  |     varsa -> mevcut kaydi dondur, IdempotentReplay = true, COMMIT
  |
  +-- SELECT ops.slot_runtime_state ... FOR UPDATE
  |     hedef slot satiri kilitlenir
  |
  +-- [gerekiyorsa] UPDATE ops.slot_runtime_state SET load_state = 'FULL'
  |
  +-- INSERT ops.transport_requests (status = 'Queued', idempotency_key)
  |     uq_transport_idempotency benzersiz indeksi son savunma hatti
  |
  +-- INSERT ops.transport_request_events (event = 'CREATED')
  |
COMMIT
```

Advisory lock ile benzersiz indeks birlikte kullanılır: kilit yarışı serileştirir, indeks ise kilit hiç alınmamış bir yol kalsa bile çift kaydı imkânsız kılar.


### 29.2 Boş kasa tahsisi — SKIP LOCKED

```text
BEGIN TRANSACTION
  |
  +-- SELECT s.id
  |     FROM master.layout_slots s
  |     JOIN ops.slot_runtime_state r ON r.slot_id = s.id
  |    WHERE s.slot_role = 'EMPTY_STORAGE'
  |      AND r.occupancy_state = 'OCCUPIED'
  |      AND r.load_state = 'EMPTY'
  |    ORDER BY s.display_order
  |    FOR UPDATE SKIP LOCKED
  |    LIMIT 1
  |     -> baska bir islem tarafindan kilitli satirlar atlanir,
  |        böylece iki es zamanli BRING_EMPTY ayni tampon kasayi secmez
  |
  +-- INSERT ops.transport_requests (source_slot_id = secilen)
  |
COMMIT
```

> **NOT:** `SKIP LOCKED` olmadan iki eşzamanlı BRING_EMPTY talebi aynı tampon slotu seçer ve ikincisi ya bekler ya da çakışır. `SKIP LOCKED` ile ikinci talep bir sonraki uygun tampon slota geçer.


### 29.3 Robot kirası — ayrı bağlantı, session ömrü

```text
[Baglanti A: is transaction'i]        [Baglanti B: kira]
   BEGIN                                 SELECT pg_try_advisory_lock(hash)
   ...                                     -> true  = kira alindi
   COMMIT   <-- burada xact kilidi          -> false = ROBOT_BUSY
              serbest kalirdi
                                          ... FIZIKSEL RDS MUTASYONU ...
                                          SELECT pg_advisory_unlock(hash)
                                          (await using -> finally)
```

Kira iş transaction'ından bağımsız bir bağlantıda tutulur; bu, mutasyonun tamamlanmasından önce serbest kalmasını engeller.


### 29.4 Varyant değiştirme — rezervasyon koruması

```text
UPDATE ops.machine_runtime_state SET active_variant_id = ?
  |
  +-- ÖNCE: aktif rezervasyon var mi ?
  |     SELECT 1 FROM ops.transport_requests
  |      WHERE machine_id = ? AND status IN (Created, Validating, Queued,
  |                                          SentToRds, Running)
  |     varsa -> ACTIVE_TRANSPORT_EXISTS (HTTP 409)
  |
  +-- yoksa -> varyant degistirilir ve denetim kaydi yazilir
```

Aktif rezervasyon `TransportStateMachine.IsReservationActive` tanımına göre belirlenir; terminal durumlar (Completed/Failed/Cancelled) rezervasyon tutmaz.


### 29.5 Layout klonlama — ilişkisel bütünlük

```text
BEGIN
  INSERT master.layouts        -> new_id, version = max+1, DRAFT
  INSERT master.layout_slots   -> yeni UUID'ler, code korunur
  INSERT master.layout_slot_parts
        JOIN ON new_slot.code = old_slot.code    (uq_layout_slot_code sayesinde tekil)
        -> copiedCount
  SELECT count(*) kaynak iliskileri              -> expectedCount
  IF copiedCount <> expectedCount -> THROW  ==> ROLLBACK
  INSERT audit.audit_logs  ADMIN_LAYOUT_VERSION_CREATED
COMMIT
```


## 30 Kod Referansı: Hata ve Sonuç Kodları

Aşağıdaki **205 kod** `docs/generator/extract_facts.py` tarafından kaynak koddan çıkarılmıştır. Hiçbiri uydurulmamıştır; her satırın `Kaynak` sütunu kodun geçtiği dosyayı gösterir.

> **NOT:** Tabloda yalnız hata kodları değil, çağıranın dallanabildiği **sonuç ve denetim sebep kodları** da vardır (örneğin `MAP_PREP_DISPATCHED`, `RDS_EVENT_APPLIED`). Tür sütunu hangisi olduğunu söyler. Bir kod birden çok dosyada geçebilir; tablo ilk tanım dosyasını gösterir.


### 30.1 İstisna ile fırlatılan kodlar (142)

Bu kodlar bir istisna ile fırlatılır ve API katmanında bir HTTP durumu ve problem gövdesine dönüşür.

| Kod | Tür | Mesaj | Kaynak |
|---|---|---|---|
| ACTIVE_TRANSPORT_EXISTS | WorkflowRuleException | Variant changes are blocked while a transport reservation is active. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| ADMIN_WEB_ROLE_REQUIRED | ApiException | Admin Web tokens require the ADMIN role. | Api / ApiSecurity.cs |
| AMBIGUOUS_LOGIN_IDENTITY | ApiException | Provide either a username or an employee number, not both. | Api / ApiEndpoints.cs |
| BATCH_IDEMPOTENCY_KEY_REQUIRED | WorkflowRuleException | A batch idempotency key is required. | Domain / WorkflowModels.cs |
| BATCH_IDEMPOTENCY_KEY_TOO_LONG | WorkflowRuleException | The batch idempotency key cannot exceed 200 characters. | Domain / WorkflowModels.cs |
| BATCH_ITEMS_REQUIRED | ApiException | Grup en az bir kasa işlemi içermelidir. | Api / ApiEndpoints.cs |
| BATCH_ITEM_IDEMPOTENCY_KEY_DUPLICATED | WorkflowRuleException | Each batch item must use a distinct idempotency key. | Domain / WorkflowModels.cs |
| BATCH_MACHINE_MISMATCH | WorkflowRuleException | Every batch item must target the batch's machine. | Domain / WorkflowModels.cs |
| BUSINESS_RULE_NOT_CONFIGURED | WorkflowRuleException | Kasa konumunun mevcut durumu güvenli bir işlem seçmek için yeterli değildir. | Application / ContainerActions.cs |
| CANCELLATION_NOT_ALLOWED | WorkflowRuleException | The transport request cannot be cancelled from its current state. | Application / RdsExecution.cs |
| CANCELLATION_NOT_ALLOWED_FOR_ROLE | ApiException | WAREHOUSE_OPERATOR may cancel only BRING_EMPTY requests created by the same user. | Api / ApiEndpoints.cs |
| CONCURRENCY_TOKEN_REQUIRED | AdminRuleException | A positive concurrency token is required. | Application / AdminConfigurationService.cs |
| CONTAINER_ACTION_INVALID | WorkflowRuleException | Container action must be FULL or EMPTY. | Application / ContainerActions.cs |
| CONTAINER_BATCH_DUPLICATE_POSITION | WorkflowRuleException | Aynı fiziksel kasa konumu grup içinde birden fazla kez seçilemez. | Infrastructure.Postgres / PostgresTransportWorkflowStore.ContainerActions.cs |
| CONTAINER_BATCH_ITEM_INVALID | WorkflowRuleException | Every batch item requires product group, part and positive position identifiers. | Application / ContainerActions.cs |
| CONTAINER_CONFLICT | WorkflowRuleException | The requested container conflicts with the slot's source-backed container mapping. | Domain / TransportRules.cs |
| CONTAINER_POSITION_INVALID | WorkflowRuleException | Container position must be a positive number and priority cannot be negative. | Application / ContainerActions.cs |
| CONTAINER_POSITION_NOT_FOUND | WorkflowRuleException | Seçilen kasa konumu artık mevcut değildir. | Infrastructure.Postgres / PostgresTransportWorkflowStore.ContainerActions.cs |
| CONTAINER_POSITION_OCCUPIED | WorkflowRuleException | Bu konumda zaten bir kasa bulunuyor. | Application / ContainerActions.cs |
| CONTAINER_STATE_CONFLICT | WorkflowRuleException | Kasa konumunun mevcut durumu bu işlemle uyumlu değildir. | Application / ContainerActions.cs |
| CROSS_MACHINE_SLOT | WorkflowRuleException | Every supplied slot must belong to the request's active machine layout. | Domain / TransportRules.cs |
| DESTINATION_REQUIRED | WorkflowRuleException | BRING_EMPTY requires a machine destination slot. | Domain / WorkflowModels.cs |
| DEVICE_CONTEXT_MISMATCH | ApiException | The request device does not match the access token. | Api / ApiSecurity.cs |
| DEVICE_CONTEXT_REQUIRED | ApiException | Login requires an X-Device-Code header. | Api / ApiEndpoints.cs |
| DEVICE_NOT_AUTHORIZED | ApiException | The client device is unknown, inactive or not a PDA. | Api / ApiEndpoints.cs |
| EMPTY_BUFFER_SLOT_NOT_CONFIGURED | WorkflowRuleException | Slot {bufferSlot.Id} is not a configured EMPTY_STORAGE buffer slot. | Domain / TransportRules.cs |
| EMPTY_BUFFER_STATE_MISMATCH | WorkflowRuleException | {request.TaskType.ToDatabase()} buffer slot must be {expected.Occupancy}/{expected.Load} but was {bufferSlot.State.Occupancy}/{bufferSlot.State.Load}. | Domain / TransportRules.cs |
| EMPTY_BUFFER_UNAVAILABLE | WorkflowRuleException | No configured EMPTY_STORAGE buffer slot is currently {expected.Occupancy.ToDatabase()}/{expected.Load.ToDatabase()} and available on this layout. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| EMPTY_STORAGE_MACHINE_SLOT_FORBIDDEN | WorkflowRuleException | EMPTY_STORAGE is reserved for backend buffer allocation and cannot be used as the machine-side transport slot. | Domain / TransportRules.cs |
| EXPERT_CURRENT_STATE_UNAVAILABLE | RdsExecutionException | Current RDS state for order '{task.TaskRecordId}' could not be read, so {operation} is refused. | Application / RdsExecution.cs |
| EXPERT_ORDER_NOT_CURRENTLY_EXECUTING | RdsExecutionException |  | Application / RdsExecution.cs |
| EXPERT_ORDER_STATE_INCOMPATIBLE | RdsExecutionException | Order '{task.TaskRecordId}' is {details.State.Known.ToString().ToUpperInvariant()} and robot '{vehicle}' reports no error condition. {operation} re... | Application / RdsExecution.cs |
| EXPERT_ROBOT_CHANGED_DURING_VALIDATION | RdsExecutionException | The robot for this request changed from '{heldLeaseKey}' to '{targetKey}' after the lease was taken, so {operation} is refused. Retry from current ... | Application / RdsExecution.cs |
| EXPERT_ROBOT_MISMATCH | RdsExecutionException | Order '{task.TaskRecordId}' is currently executing on a different robot than the one recorded for this request, so {operation} is refused. | Application / RdsExecution.cs |
| IDEMPOTENCY_KEY_REQUIRED | ApiException | Transport creation requires an Idempotency-Key header. | Api / ApiEndpoints.cs |
| IDEMPOTENCY_KEY_REUSED | WorkflowRuleException | The batch idempotency key belongs to a different container-action payload. | Infrastructure.Postgres / PostgresTransportWorkflowStore.ContainerActions.cs |
| IDEMPOTENCY_KEY_TOO_LONG | WorkflowRuleException | The idempotency key cannot exceed 200 characters. | Application / ContainerActions.cs |
| ID_REQUIRED | AdminRuleException | A non-empty entity ID is required. | Application / AdminConfigurationService.cs |
| IF_MATCH_REQUIRED | ApiException | {header} is required for this mutation. | Api / AdminEndpoints.cs |
| ILLEGAL_STATUS_TRANSITION | WorkflowRuleException | Transport request cannot transition from {current.ToDatabase()} to {next.ToDatabase()}. | Domain / TransportStateMachine.cs |
| INVALID_CLIENT_CONTEXT | ApiException | The access token has no supported client context. | Api / ApiSecurity.cs |
| INVALID_CONCURRENCY_TOKEN | ApiException | {header} must contain the numeric concurrency token returned by the API. | Api / AdminEndpoints.cs |
| INVALID_CONTAINER_DIMENSIONS | AdminRuleException | Known container dimensions and capacity must be positive; unknown values remain NULL. | Application / AdminConfigurationService.cs |
| INVALID_CREDENTIALS | ApiException | The username or password is invalid. | Api / AdminEndpoints.cs |
| INVALID_ENUM_VALUE | AdminRuleException | Unsupported {field} value. | Application / AdminConfigurationService.cs |
| INVALID_FIELD | AdminRuleException | {field} is required and must contain at most {maxLength} characters. | Application / AdminConfigurationService.cs |
| INVALID_JSON_OBJECT | AdminRuleException | {field} must be a JSON object. | Application / AdminConfigurationService.cs |
| INVALID_LOGIN_REQUEST | ApiException | Username and password are required and must fit the supported limits. | Api / AdminEndpoints.cs |
| INVALID_OPERATION | ApiException | Operation must be BRING_EMPTY, REMOVE_FULL or REMOVE_EMPTY. | Api / ApiEndpoints.cs |
| INVALID_PRIORITY | WorkflowRuleException | Priority cannot be negative. | Domain / WorkflowModels.cs |
| INVALID_SLOT_GEOMETRY | AdminRuleException | Slot geometry must be fully NULL or form a positive rectangle inside normalized 0.0-1.0 bounds. | Application / AdminConfigurationService.cs |
| INVALID_TOKEN_SUBJECT | ApiException | The access token has no valid user subject. | Api / ApiSecurity.cs |
| LAYOUT_SLOT_NOT_FOUND | RdsIntegrationRuleException | The layout slot does not exist. | Infrastructure.Postgres / PostgresRdsIntegrationStore.cs |
| LOAD_STATE_NOT_SELECTABLE | ApiException | LoadState must be EMPTY or FULL. | Api / ApiEndpoints.cs |
| MACHINE_ACCESS_DENIED | ApiException | The user does not have the required permission for this machine. | Api / ApiSecurity.cs |
| MACHINE_NOT_FOUND | ApiException | The requested machine does not exist or is inactive. | Api / ApiEndpoints.cs |
| MACHINE_REGION_FORBIDDEN | RdsExecutionException | Display-only MACHINE regions can never reach RDS dispatch. | Application / RdsExecution.cs |
| MACHINE_REGION_MAPPING_FORBIDDEN | RdsIntegrationRuleException | Machine regions cannot be mapped as RDS transport WorkSites. | Infrastructure.Postgres / PostgresRdsIntegrationStore.cs |
| MAIN_PART_CONTEXT_REQUIRED | ApiException | Uyumlu varyant sorgusu için ana parça bağlamı gereklidir. | Api / ApiEndpoints.cs |
| NATIVE_EXECUTION_UNAVAILABLE | RdsExecutionException | RDSCore native execution is not configured, so expert recovery is unavailable. | Application / RdsExecution.cs |
| NAVIGATION_CONTEXT_MISMATCH | WorkflowRuleException | A supplied navigation profile must match the active profile. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| NAVIGATION_PROFILE_CONFLICT | WorkflowRuleException | The supplied navigation profile is not a published assignment for this variant. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| NAVIGATION_PROFILE_NOT_FOUND | RdsIntegrationRuleException | The selected navigation profile does not exist. | Infrastructure.Postgres / PostgresRdsIntegrationStore.cs |
| NAVIGATION_PROFILE_REQUIRED | RdsIntegrationRuleException | An explicit navigation profile is required. | Application / RdsIntegration.cs |
| OCCUPIED_CONTAINER_REQUIRED | WorkflowRuleException | Dolu bildirimi için konumda bir kasa bulunmalıdır. | Application / ContainerActions.cs |
| OCCUPIED_SLOT_REQUIRED | WorkflowRuleException | Load state can be reported only for an occupied slot. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| OPERATION_NOT_ALLOWED_FOR_ROLE | ApiException | Rolünüz tüm kasaları kaldırma işlemini başlatmaya yetkili değildir. | Api / ApiEndpoints.cs |
| OPERATION_NOT_OPERATOR_SELECTABLE | ApiException | REMOVE_EMPTY is produced by the Remove-All workflow and cannot be requested directly. | Api / ApiEndpoints.cs |
| PART_CONFLICT | WorkflowRuleException | Seçilen parça konumun tanımlı parça eşlemesiyle uyumlu değil. | Domain / TransportRules.cs |
| PART_PRODUCT_GROUP_CONFLICT | WorkflowRuleException | Seçilen parça ürün grubu ve varyantla uyumlu değil. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| PART_REQUIRED_FOR_AUTOMATIC_SLOT_RESOLUTION | WorkflowRuleException | Otomatik konum seçimi için geçerli bir parça gereklidir. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| PART_VARIANT_CONFLICT | WorkflowRuleException | Seçilen parça aktif varyanta ait değil. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| PRIORITY_OUT_OF_RANGE | RdsExecutionException | Priority must be between {MinRdsPriority} and {MaxRdsPriority}. | Application / RdsExecution.cs |
| PRODUCT_GROUP_REQUIRED | WorkflowRuleException | A product group is required. | Application / ContainerActions.cs |
| QUEUE_PAGE_INVALID | WorkflowRuleException | Queue offset must be non-negative and limit must be between 1 and 100. | Application / ContainerActions.cs |
| RDSCORE_BUSINESS_FAILURE | RdsCoreNativeOrderException | RDSCore rejected the read request with code {code}. | Api / RdsCoreNativeOrderClient.cs |
| RDSCORE_DISCOVERY_CONFIRMATION_REQUIRED | ApiException | An explicit read-only discovery confirmation is required. | Api / AdminEndpoints.cs |
| RDSCORE_DISCOVERY_DISABLED | RdsCommissioningDiscoveryException | RDSCore discovery is disabled or has no explicit BaseURL. | Api / RdsCommissioningDiscoveryClient.cs |
| RDSCORE_EXECUTION_DISABLED | RdsCoreNativeOrderException | RDSCore native-order execution is disabled or is not the selected mode. | Api / RdsCoreNativeOrderClient.cs |
| RDSCORE_HTTP_FAILURE | RdsCoreNativeOrderException | RDSCore returned HTTP {response.StatusCode}. | Api / RdsCoreNativeOrderClient.cs |
| RDSCORE_INVALID_JSON | RdsCoreNativeOrderException | RDSCore returned invalid JSON. | Api / RdsCoreNativeOrderClient.cs |
| RDSCORE_PROBE_INVALID | RdsCommissioningDiscoveryException | A {name} value is empty or outside the supported format. | Api / RdsCommissioningDiscoveryClient.cs |
| RDSCORE_PROBE_LIMIT_EXCEEDED | RdsCommissioningDiscoveryException | At most 100 {name} values are supported. | Api / RdsCommissioningDiscoveryClient.cs |
| RDSCORE_READ_DISABLED | RdsCoreNativeOrderException | RDSCore is disabled or has no BaseAddress configured. | Api / RdsCoreNativeOrderClient.cs |
| RDSCORE_TIMEOUT | RdsCoreNativeOrderException | RDSCore native-order request timed out. | Api / RdsCoreNativeOrderClient.cs |
| RDSCORE_UNAVAILABLE | RdsCoreNativeOrderException | RDSCore native-order endpoint is unavailable. | Api / RdsCoreNativeOrderClient.cs |
| RDS_BUSINESS_FAILURE | RdsExecutionException | RDS rejected the mutation request. | Api / RdsExecutionClient.cs |
| RDS_CALLBACK_DISABLED | ApiException | RDS callback processing is not configured. | Api / RdsIntegrationEndpoints.cs |
| RDS_CALLBACK_UNAUTHORIZED | ApiException | The RDS callback key is invalid. | Api / RdsIntegrationEndpoints.cs |
| RDS_CONTENT_UPDATE_REJECTED | RdsExecutionException | RDS rejected the controlled content update. | Api / RdsExecutionClient.cs |
| RDS_DISABLED | RdsExecutionException | RDS integration is disabled. | Api / RdsExecutionClient.cs |
| RDS_DISPATCH_CONTEXT_NOT_FOUND | RdsExecutionException | The queued transport request no longer has a dispatch context. | Application / RdsExecution.cs |
| RDS_DUPLICATE_TASK_IDENTITY | RdsExecutionException | RDS returned more than one task for the immutable TKG taskRecordId. | Application / RdsExecution.cs |
| RDS_EVENT_INVALID | ApiException | A supported callback event type is required. | Api / RdsIntegrationEndpoints.cs |
| RDS_HTTP_FAILURE | RdsExecutionException | RDS returned HTTP {(int)response.StatusCode}. | Api / RdsExecutionClient.cs |
| RDS_INVALID_JSON | RdsExecutionException | RDS returned invalid JSON. | Api / RdsExecutionClient.cs |
| RDS_STOP_REJECTED | RdsExecutionException | RDS rejected the stop-task request. | Api / RdsExecutionClient.cs |
| RDS_SYNC_IN_PROGRESS | RdsIntegrationRuleException | A WorkSite synchronization is already running. | Application / RdsIntegration.cs |
| RDS_TASK_BINDINGS_REQUIRED | RdsExecutionException | The task template must define an rdsInputBindings array. | Application / RdsExecution.cs |
| RDS_TASK_BINDING_DUPLICATE | RdsExecutionException | RDS input parameter '{param}' is configured more than once. | Application / RdsExecution.cs |
| RDS_TASK_BINDING_INVALID | RdsExecutionException | Every RDS input binding must be an object. | Application / RdsExecution.cs |
| RDS_TASK_IDENTITY_CONFLICT | RdsExecutionException | The request already owns a different RDS task identity or label. | Infrastructure.Postgres / PostgresRdsExecutionStore.cs |
| RDS_TASK_INPUT_UNRESOLVED | RdsExecutionException | Required RDS input '{param}' could not be resolved from {source}. | Application / RdsExecution.cs |
| RDS_TASK_LABEL_REQUIRED | ApiException | The task template has no RDS task label. | Api / AdminEndpoints.cs |
| RDS_TASK_NOT_FOUND | RdsExecutionException | The request has no dispatched RDS task, so there is nothing to recover. | Application / RdsExecution.cs |
| RDS_TASK_TEMPLATE_NOT_FOUND | RdsExecutionException | No active task template matches the request variant, product group and operation. | Infrastructure.Postgres / PostgresRdsExecutionStore.cs |
| RDS_TIMEOUT | RdsExecutionException | RDS mutation timed out. | Api / RdsExecutionClient.cs |
| RDS_UNAVAILABLE | RdsExecutionException | RDS mutation endpoint is unavailable. | Api / RdsExecutionClient.cs |
| RDS_VEHICLE_UNASSIGNED | RdsExecutionException | RDS has not assigned a robot to this request yet, so no exact vehicle can be targeted. | Application / RdsExecution.cs |
| RDS_WORKSITE_MACHINE_CONFLICT | RdsIntegrationRuleException | The RDS WorkSite navigation profile belongs to another machine. | Infrastructure.Postgres / PostgresRdsIntegrationStore.cs |
| RDS_WORKSITE_NOT_FOUND | RdsIntegrationRuleException | The mirrored RDS WorkSite does not exist. | Infrastructure.Postgres / PostgresRdsIntegrationStore.cs |
| READINESS_NOT_APPLICABLE | WorkflowRuleException | Only a WAITING_PREPARATION request can be marked ready (current readiness: {readiness.ToDatabase()}). | Domain / WorkflowModels.cs |
| REMOVE_ALL_NOTHING_TO_REMOVE | WorkflowRuleException | Aktif varyantta kaldırılacak dolu bir kasa bulunmuyor. | Infrastructure.Postgres / PostgresTransportWorkflowStore.ContainerActions.cs |
| REMOVE_ALL_NOT_READY | WorkflowRuleException |  | Infrastructure.Postgres / PostgresTransportWorkflowStore.ContainerActions.cs |
| REMOVE_ALL_POSITION_CONFLICT | WorkflowRuleException | En az bir kasa konumunun doluluk/yük durumu çelişkilidir. | Application / ContainerActions.cs |
| REMOVE_ALL_POSITION_UNKNOWN | WorkflowRuleException | En az bir kasa konumunun doluluk durumu bilinmiyor. | Application / ContainerActions.cs |
| REQUEST_ALREADY_TERMINAL | RdsExecutionException | The transport request is already {request.Status.ToDatabase()}; expert recovery does not apply. | Application / RdsExecution.cs |
| REQUEST_ID_REQUIRED | WorkflowRuleException | A transport request ID is required. | Application / TransportWorkflowService.cs |
| REQUEST_LOCK_SCOPE_CHANGED | WorkflowRuleException | Transport request lock-scope fields changed while acquiring the workflow lock hierarchy. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| REQUEST_NOT_FOUND | ApiException | The transport request does not exist. | Api / ApiEndpoints.cs |
| REQUEST_TERMINAL | WorkflowRuleException | A {executionStatus.ToDatabase()} transport request's readiness cannot be changed. | Domain / WorkflowModels.cs |
| ROBOT_ALIAS_AMBIGUOUS | RdsExecutionException | '{alias}' matches more than one physical robot, so {operation} is refused. | Application / RdsExecution.cs |
| ROBOT_BUSY | RdsExecutionException | Another TKG operation is currently acting on robot '{leaseKey}', so {operation} is refused. Retry once it completes. | Application / RdsExecution.cs |
| ROLE_REQUIRED | AdminRuleException | At least one role is required. | Application / AdminConfigurationService.cs |
| RUNTIME_CONTEXT_MISMATCH | WorkflowRuleException | Kasa konumları yalnız makinenin etkin varyant ve yerleşim bağlamında görüntülenebilir. | Infrastructure.Postgres / PostgresTransportWorkflowStore.ContainerActions.cs |
| RUNTIME_STATE_NOT_CONFIGURED | WorkflowRuleException | Machine runtime state must exist before changing workflow state. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| SAME_SOURCE_DESTINATION | WorkflowRuleException | Source and destination slots must be different. | Domain / WorkflowModels.cs |
| SLOT_ALREADY_RESERVED | WorkflowRuleException | A supplied slot already has an active transport reservation. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| SLOT_DISABLED | WorkflowRuleException | Slot {slot.Id} is disabled. | Domain / TransportRules.cs |
| SLOT_NOT_FOUND | WorkflowRuleException | Layout slot {slotId} does not exist. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| SLOT_NOT_IN_ACTIVE_LAYOUT | WorkflowRuleException | The slot must belong to the machine's active layout. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| SLOT_STATE_MISMATCH | WorkflowRuleException | {request.TaskType.ToDatabase()} cannot reserve slot state {reservedSlot.State.Occupancy}/{reservedSlot.State.Load}. | Domain / TransportRules.cs |
| SOURCE_REQUIRED | WorkflowRuleException | {TaskType.ToDatabase()} requires a machine source slot. | Domain / WorkflowModels.cs |
| TASK_TEMPLATE_NOT_FOUND | ApiException | The task template does not exist. | Api / AdminEndpoints.cs |
| TASK_TYPE_NOT_FOUND | WorkflowRuleException | The requested logistics task type is not configured. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| UNKNOWN_ROLE | AdminRuleException | Only the four seeded system roles may be assigned. | Application / AdminConfigurationService.cs |
| UNSUPPORTED_EXECUTION_OUTCOME | WorkflowRuleException | Only executor-confirmed failure and cancellation outcomes are supported. | Application / TransportWorkflowService.cs |
| UNSUPPORTED_EXECUTION_UPDATE | WorkflowRuleException | Phase 03 only records dispatch, running and successful completion transitions. | Application / TransportWorkflowService.cs |
| UNSUPPORTED_MAINTENANCE_ACTION | RdsExecutionException | Unsupported robot maintenance action. | Application / RdsExecution.cs |
| VARIANT_LAYOUT_CONFLICT | WorkflowRuleException | The published layout is not assigned to this machine variant. | Infrastructure.Postgres / PostgresTransportWorkflowStore.cs |
| VEHICLES_REQUIRED | RdsExecutionException | At least one explicit vehicle identifier is required. | Application / RdsExecution.cs |
| WEAK_PASSWORD | AdminRuleException | Passwords must contain between 12 and 256 characters. | Application / AdminConfigurationService.cs |


### 30.2 Sonuç ve denetim sebep kodları (63)

Bu kodlar istisna fırlatmaz. Sonuç nesnelerinde, denetim kayıtlarında veya sabitlerde taşınır; çağıran bunlara göre dallanır ve devreye alma mühendisi denetim izinde bunları okur.

| Kod | Tür | Mesaj | Kaynak |
|---|---|---|---|
| CANCELLATION_AFTER_EXECUTION_DEFERRED | constant |  | Domain / WorkflowModels.cs |
| EXECUTION_MODE_INVALID | audit reason | execution audit reason | Application / RdsExecution.cs |
| EXECUTION_MODE_UNRESOLVED | audit reason | execution audit reason | Application / RdsExecution.cs |
| LOCK_ACQUIRED | audit reason | execution audit reason | Application / RdsExecution.cs |
| LOCK_ALREADY_RELEASED | audit reason | execution audit reason | Application / RdsExecution.cs |
| LOCK_FAILED | audit reason | execution audit reason | Application / RdsExecution.cs |
| LOCK_RECOVERY_SUCCESS | audit reason | execution audit reason | Application / RdsExecution.cs |
| LOCK_RECOVERY_UNSAFE | audit reason | execution audit reason | Application / RdsExecution.cs |
| MAP_PREP_DISPATCHED | result code |  | Application / RdsExecution.cs |
| MAP_PREP_RUNNING | result code |  | Application / RdsExecution.cs |
| MAP_PREP_SET_ORDER_REJECTED | result code |  | Application / RdsExecution.cs |
| MAP_SWITCH_LOCK_RECOVERY_REQUIRED | constant | execution audit reason | Application / RdsExecution.cs |
| NATIVE_DISPATCH_DISABLED | audit reason | execution audit reason | Application / RdsExecution.cs |
| NAVIGATION_PROFILE_NOT_CONFIGURED | audit reason | execution audit reason | Application / RdsExecution.cs |
| NAVIGATION_PROFILE_UNRESOLVED | audit reason | execution audit reason | Application / RdsExecution.cs |
| NOT_CONFIGURED | result code |  | Application / RdsExecution.cs |
| NOT_MAPPED | result code |  | Application / RdsExecution.cs |
| NOT_READY | result code |  | Application / RdsExecution.cs |
| POST_SWITCH_ALARM | audit reason | execution audit reason | Application / RdsExecution.cs |
| POST_SWITCH_TELEMETRY_FAILED | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDSCORE_HAS_BLOCKING_ALARM | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDSCORE_NOT_FOUND | result code |  | Api / RdsCommissioningDiscoveryClient.cs |
| RDS_CANCELLATION_PENDING | constant |  | Domain / WorkflowModels.cs |
| RDS_CREATE_REQUEST | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDS_DISPATCH_DISABLED | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDS_DISPATCH_ENDPOINT_UNRESOLVED | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDS_EVENT_APPLIED | result code |  | Application / RdsExecution.cs |
| RDS_EVENT_IGNORED | result code |  | Application / RdsExecution.cs |
| RDS_NATIVE_ORDER_OBSERVED | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDS_SET_ORDER | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDS_SET_ORDER_REJECTED | result code |  | Application / RdsExecution.cs |
| RDS_TASK_ID_UNRESOLVED | result code |  | Application / RdsExecution.cs |
| RDS_TASK_NOT_YET_OBSERVED | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDS_TASK_OBSERVED | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDS_TASK_TEMPLATE_NOT_READY | result code |  | Application / RdsExecution.cs |
| RDS_TASK_UNKNOWN | result code |  | Application / RdsExecution.cs |
| RDS_WORKSITE_DISABLED | result code | The {name} WorkSite is disabled. | Application / PdaOperationalContextService.cs |
| RDS_WORKSITE_EVENT_ACCEPTED | result code |  | Application / RdsExecution.cs |
| RDS_WORKSITE_EVIDENCE_REFRESHED | audit reason | execution audit reason | Application / RdsExecution.cs |
| RDS_WORKSITE_LOCKED | result code | The {name} WorkSite is locked. | Application / PdaOperationalContextService.cs |
| RDS_WORKSITE_NOT_MAPPED | result code | The {name} endpoint has no explicit WorkSite mapping. | Application / PdaOperationalContextService.cs |
| RDS_WORKSITE_NOT_PHYSICAL | result code | The {name} WorkSite is not physical. | Application / PdaOperationalContextService.cs |
| RDS_WORKSITE_STALE | result code | The {name} WorkSite evidence is stale. | Application / PdaOperationalContextService.cs |
| RDS_WORKSITE_SYNC_FAILED | result code | The {name} WorkSite reports syncFailed. | Application / PdaOperationalContextService.cs |
| READINESS_WAITING_PREPARATION | result code |  | Application / RdsExecution.cs |
| REQUEST_NOT_QUEUED | result code |  | Application / RdsExecution.cs |
| REQUIRED_MAP_UNRESOLVED | audit reason | execution audit reason | Application / RdsExecution.cs |
| ROBOT_BUSY_INCOMPATIBLE_ORDER | audit reason | execution audit reason | Application / RdsExecution.cs |
| ROBOT_CHANGED_DURING_VALIDATION | audit reason | execution audit reason | Application / RdsExecution.cs |
| ROBOT_HAS_BLOCKING_ALARM | audit reason | execution audit reason | Application / RdsExecution.cs |
| ROBOT_IDENTITY_UNRESOLVED | audit reason | execution audit reason | Application / RdsExecution.cs |
| ROBOT_NOT_DISPATCHABLE | audit reason | execution audit reason | Application / RdsExecution.cs |
| ROBOT_OFFLINE | audit reason | execution audit reason | Application / RdsExecution.cs |
| SLOT_BUSY | result code | Load state cannot change while an active transport uses the slot. | Infrastructure.Postgres / PostgresTransportWorkflowStore.ContainerActions.cs |
| SWITCHMAP_FAILED | audit reason | execution audit reason | Application / RdsExecution.cs |
| SWITCHMAP_REJECTED | audit reason | execution audit reason | Application / RdsExecution.cs |
| SWITCHMAP_SUCCESS | audit reason | execution audit reason | Application / RdsExecution.cs |
| SWITCHMAP_VERIFICATION_FAILED | audit reason | execution audit reason | Application / RdsExecution.cs |
| SWITCH_TRANSITION_NOT_CONFIRMED | audit reason | execution audit reason | Application / RdsExecution.cs |
| SWITCH_TRANSITION_UNRESOLVED | audit reason | execution audit reason | Application / RdsExecution.cs |
| TRANSITION_NAVIGATION | audit reason | execution audit reason | Application / RdsExecution.cs |
| UNLOCK_SUCCESS | audit reason | execution audit reason | Application / RdsExecution.cs |
| VEHICLE_UNRESOLVED | audit reason | execution audit reason | Application / RdsExecution.cs |


### 30.3 Güvenlik açısından kritik kodlar

Aşağıdaki kodlar fiziksel güvenlikle doğrudan ilgilidir. Bunlardan biri görüldüğünde sistem bilinçli olarak fail-closed davranmıştır.

| Kod | Kodda mevcut | Anlamı |
|---|---|---|
| RDS_DISPATCH_DISABLED | Evet | Global mutasyon kapısı kapalı; hareket doğuran operasyon reddedildi. |
| RDSCORE_HAS_BLOCKING_ALARM | Evet | RDSCore çekirdek düzeyinde fatal/error alarm bildiriyor. |
| ROBOT_ALIAS_AMBIGUOUS | Evet | Alias birden çok fiziksel robota çözülüyor; tahmin edilmedi. |
| ROBOT_BUSY | Evet | Robot kirası başka bir TKG operasyonunda. |
| EXPERT_ROBOT_CHANGED_DURING_VALIDATION | Evet | Kira alındıktan sonra hedef robot değişti (TOCTOU). |
| ROBOT_CHANGED_DURING_VALIDATION | Evet | Dispatch yolunda aynı TOCTOU değişmezi. |
| EXPERT_CURRENT_STATE_UNAVAILABLE | Evet | Telemetri okunamadı; güvenlik-kritik mutasyon için fail-closed. |
| MAP_SWITCH_LOCK_RECOVERY_REQUIRED | Evet | Çökmüş bir harita geçişinden kalan kilit; elle kurtarma gerekli. |
| LOCK_RECOVERY_UNSAFE | Evet | Kilit bizim ama çalışma zamanı durumu serbest bırakmayı yasaklıyor. |
| OPERATION_NOT_OPERATOR_SELECTABLE | Evet | REMOVE_EMPTY gibi operatöre kapalı bir operasyon istendi. |


## 31 Troubleshooting Karar Ağaçları

Her ağaç gözlemlenen belirtiyle başlar ve kaynak koddan doğrulanabilir bir sebebe iner. Tahmin adımı yoktur.


### 31.1 1 — Talep oluşuyor ama robot hiç hareket etmiyor

```text
Talep ops.transport_requests icinde Queued'da mi ?
 |-- HAYIR (Created/Validating) -> dogrulama tamamlanmamis; olaylara bak
 |
 EVET
 |
 Baslangic günlügünde "RDS Dispatch: Disabled" mi ?
 |-- EVET -> Rds:DispatchEnabled = false. BEKLENEN DAVRANIS.
 |           Devreye alma icin scripts/run_commissioning.ps1 gerekir.
 |
 HAYIR
 |
 Denetim kaydinda RDS_DISPATCH_BLOCKED var mi ?
 |-- EVET -> reason_code'a bak:
 |            RDSCORE_HAS_BLOCKING_ALARM  -> RDSCore sahne alarmi, once onu coz
 |            ROBOT_OFFLINE               -> ConnectionStatus != 1
 |            ROBOT_NOT_DISPATCHABLE      -> robot dispatch disi birakilmis
 |            NAVIGATION_PROFILE_UNRESOLVED -> profil yayinlanmamis
 |            REQUIRED_MAP_UNRESOLVED     -> profilde harita adi yok
 |            VEHICLE_UNRESOLVED          -> profilde/sablonda arac yok
 |
 HAYIR
 |
 Denetim kaydinda RDS_DISPATCH_DEFERRED var mi ?
 |-- EVET -> gecici; RDSCore erisilemez veya robot kirasi mesgul
 |            ROBOT_BUSY / ROBOT_IDENTITY_UNRESOLVED
 |
 HAYIR -> Worker hic calisiyor mu ? Rds:Enabled kontrol et.
```


### 31.2 2 — ROBOT_ALIAS_AMBIGUOUS

```text
/robotsStatus ciktisini al
 |
 Yapilandirilmis arac kimligi (NavigationProfile.Vehicle) kac robotta gecıyor ?
 |
 |-- 1 robot, hem uuid hem vehicle_id ayni -> bu hata cikmamali; telemetriyi
 |     yeniden oku, cakisan bir eski kayit olabilir
 |
 |-- 2+ farkli robot (biri uuid, digeri vehicle_id olarak tasiyor)
 |     -> GERCEK BELIRSIZLIK. Sistem dogru davraniyor.
 |     -> Cozum: NavigationProfile.Vehicle degerini benzersiz olan kimlik
 |        uzayina tasi (tercihen vehicle_id) veya RDSCore tarafinda cakisan
 |        kimligi degistir.
 |
 UYARI: Bu hatayi "ilk eslesen robotu sec" diyerek gecmek, yanlis fiziksel
 makineyi hareket ettirir. Kod bunu bilerek reddeder.
```


### 31.3 3 — Robot alarmda ama TKG alarm göstermiyor

```text
Alarm hangi severity ?
 |-- warning / notice -> BEKLENEN. Bunlar bloklamaz ve bant olarak gosterilmez.
 |
 fatal / error
 |
 /robotsStatus ham cevabini al ve alarm zarfini kontrol et
 |-- alarms.{fatals,errors}          -> kok, sarmali     (destekleniyor)
 |-- {fatals,errors} kokte           -> kok, dogrudan    (destekleniyor)
 |-- report[].rbk_report.alarms.{..} -> robot, sarmali   (destekleniyor)
 |-- report[].rbk_report.{fatals,..} -> robot, dogrudan  (destekleniyor)
 |-- bunlarin disinda bir yol        -> DOKUMANTE DEGIL
 |     -> uydurma yol EKLEME. Once SEER dokumantasyonundan dogrula.
 |
 Giris sekli ?
 |-- { "code": 52201, ... }      -> Sekil A
 |-- { "55001": 1634020734, ...} -> Sekil B (kod = ozellik ADI)
 |
 Kok alarm mi robot alarmi mi ?
 |-- Kok alarm robota kopyalanmaz. Robot listesinde gorunmemesi DOGRUDUR;
     RDSCORE_HAS_BLOCKING_ALARM olarak dispatch'i bloklar.
```


### 31.4 4 — MAP_SWITCH_LOCK_RECOVERY_REQUIRED

```text
Denetim kayitlarinda LOCK_RECOVERY_UNSAFE var mi ?
 |
 EVET -> blocking_reason alanina bak:
 |        MAP_MISMATCH          -> robot gerekli haritada degil
 |        ROBOT_NOT_LOCKED      -> robot kilitli bildirmiyor; kayit eski olabilir
 |        ACTIVE_ORDER_ON_ROBOT -> terminal olmayan siparis var (kendi MAP_PREP
 |                                 siparisimiz dahil). Once siparisi bitir.
 |        BLOCKING_ALARM        -> once alarmi coz
 |        BUSINESS_STATE_UNKNOWN-> procBusiness yok; fail-closed. Telemetriyi kontrol et.
 |        BUSINESS_BUSY         -> robot is yapiyor
 |
 HAYIR -> kilit korele olmuyor demektir (A/B/C/E kosullarindan biri saglanmiyor)
 |        - kayit farkli bir robotu adlandiriyor
 |        - kayit farkli bir haritayi hedefliyor
 |        - lock_info parmak izi eslesmiyor (TimeT / Ip / NickName)
 |
 HER IKI DURUMDA: Otomatik kurtarma yapilmaz. Elle kurtarma bir devreye alma
 muhendisi tarafindan, robot fiziksel olarak dogrulandiktan sonra yapilmalidir.
 COMMISSIONING VALIDATION REQUIRED.
```


### 31.5 5 — "N. Kasa Boş" butonu devre dışı

```text
Buton altinda gosterilen sebep metnini oku (tooltip'e guvenme)
 |
 "Islem kilitli" -> mutationGate.canMutate = false
 |    -> makine baglaminda global bir kisit var (or. aktif rezervasyon,
 |       yetkisiz rol, veya sunucu tarafi kapi)
 |
 diger sebep -> availability.disabledMessage sunucudan geliyor
 |    Slot durumu nedir ?
 |    |-- OCCUPIED       -> pozisyonda zaten kasa var; BRING_EMPTY anlamsiz
 |    |-- UNKNOWN        -> BUSINESS_RULE_NOT_CONFIGURED; veri eksik
 |    |-- VACANT+UNKNOWN -> etkin olmali; degilse tampon slot yok demektir
 |         -> EMPTY_STORAGE rolunde OCCUPIED+EMPTY slot var mi ? kontrol et
```


### 31.6 6 — Admin Web'de oturum sürekli düşüyor

```text
Sayfa yenilendi mi (F5 / page.goto) ?
 |-- EVET -> BEKLENEN DAVRANIS. Token yalniz bellekte tutulur; localStorage
 |           veya sessionStorage kullanilmaz. Bu bilincli bir guvenlik karari.
 |
 HAYIR
 |
 Token suresi doldu mu ? (Authentication:Jwt:ExpiryMinutes, varsayilan 60)
 |-- EVET -> yeniden giris normal
 |
 HAYIR -> 403 ADMIN_WEB_ROLE_REQUIRED mi aliniyor ?
          -> kullanicida ADMIN rolu yok
```


### 31.7 7 — Operatör 403 DEVICE_CONTEXT_MISMATCH alıyor

```text
X-Device-Code basligi ile token icindeki device_code iddiasi ayni mi ?
 |-- HAYIR -> cihaz kodu degistirildi ama yeniden giris yapilmadi.
 |            Cozum: cikis yap, yeniden giris yap.
 |
 EVET -> DEVICE_NOT_AUTHORIZED mi ?
         -> audit.client_devices icinde kayit var mi ve is_active = true mi ?
         -> Admin Web > DevicesPage uzerinden cihazi aktive et
```


### 31.8 8 — Layout klonlama başarısız: "Relational loss"

```text
InvalidOperationException: expected N layout_slot_parts, copied M
 |
 Bu bir VERI BUTUNLUGU korumasidir; klon commit EDILMEDI.
 |
 Sebep: eski slot code'u ile yeni slot code'u eslesmedi
 |-- Kaynak layout'ta ayni code'a sahip iki slot olamaz (uq_layout_slot_code)
 |-- Ancak bir slot code'u NULL veya bos ise JOIN eslesmez
 |
 Kontrol:
   SELECT code, count(*) FROM master.layout_slots
    WHERE layout_id = '<kaynak>' GROUP BY code HAVING count(*) > 1;
   SELECT id FROM master.layout_slots
    WHERE layout_id = '<kaynak>' AND (code IS NULL OR btrim(code) = '');
```


## 32 Güvenlik Modeli


### 32.1 İki ayrı güvenlik alanı

| Alan | Riski | Ana kontrol |
|---|---|---|
| Bilgi güvenliği | Yetkisiz veri erişimi | JWT + rol politikaları + cihaz bağlama |
| Fiziksel güvenlik | İstenmeyen robot hareketi | RdsMutationPolicy + dispatch kapısı + robot kirası |

İkisi ayrı ele alınır çünkü tehdit modelleri farklıdır: bilgi güvenliğinde en kötü sonuç veri sızıntısı, fiziksel güvenlikte ise insan yaralanmasıdır. Bu yüzden fiziksel tarafta "yetkili kullanıcı" hiçbir zaman tek başına yeterli bir kontrol sayılmaz.


### 32.2 Sır yönetimi

| Sır | Yer tutucu | Nereden gelir |
|---|---|---|
| Veritabanı bağlantı dizesi | <DB_CONNECTION> | ConnectionStrings__TkgPda ortam değişkeni |
| JWT imzalama anahtarı | <JWT_SIGNING_KEY> | Authentication__Jwt__SigningKey ortam değişkeni |
| RDS entegrasyon anahtarı | <INTEGRATION_KEY> | Rds__IntegrationKey ortam değişkeni |
| RDS callback anahtarı | <CALLBACK_KEY> | Rds__CallbackKey ortam değişkeni |

> **UYARI:** Hiçbiri `appsettings.json` içinde bulunmaz ve depoya işlenmez. Bu doküman da hiçbir gerçek sır içermez; tüm örnekler yukarıdaki yer tutucularla yazılmıştır.


### 32.3 Doğrulamalar

- JWT imzalama anahtarı 32 bayttan kısaysa uygulama başlamaz.
- RDS entegrasyon ve callback anahtarları 32–512 karakter aralığında olmalıdır.
- Callback anahtarı karşılaştırması sabit zamanlıdır (`FixedTimeEquals`) — zamanlama saldırısına kapalı.
- Callback anahtarı yapılandırılmamışsa callback ucu hiçbir isteği kabul etmez (fail-closed).
- `AllowInsecureRemoteHttp=false` iken uzak bir adrese düz HTTP reddedilir.


### 32.4 Günlükleme hijyeni

Başlangıç günlüğü RDS adreslerini `BaseAddressDisplaySafe` üzerinden yazar; adres bir kullanıcı bilgisi bileşeni taşıyorsa günlüğe düşmez. Denetim kayıtları sır içermez; yalnız kod, sebep ve korelasyon kimliği taşır.


### 32.5 Operatör yüzeyi bilgi sınırı

Operatör PDA'sına ham RDS tanımlayıcıları hiç gönderilmez: task id, WorkSite etiketi, AGV kimliği, robot uuid'i. Bu hem güvenlik hem de iş sözleşmesi kararıdır — operatör fiziksel yönlendirme kararı vermediği için bu bilgilere ihtiyacı yoktur ve bunların ekranda olması yanlış bir yetki hissi yaratır.


### 32.6 Fail-closed listesi

Aşağıdaki durumların **hepsinde** sistem reddeder; hiçbirinde tahmin etmez.

- Robot kimliği telemetriden çözülemiyor.
- Alias birden çok fiziksel robota çözülüyor.
- Robot kirası alınamıyor.
- Kira alındıktan sonra hedef robot değişmiş.
- `procBusiness` alanı yok veya true.
- Kilit parmak izi korele olmuyor.
- Slot durumu UNKNOWN.
- Remove-All kapsamında bir pozisyon UNKNOWN veya çelişkili.
- RDSCore çekirdek düzeyinde bloklayan alarm bildiriyor.
- Dokümantasyon güvenlik-kritik bir ön koşulda belirsiz.


## 33 Sözlük

| Terim | Açıklama |
|---|---|
| AGV | Automated Guided Vehicle — otonom taşıyıcı araç. |
| Alias | Bir robotu adlandıran, uuid veya vehicle_id uzayında olabilen serbest metin. |
| BRING_EMPTY | Boş kasa getirme taşıma operasyonu. |
| CommissioningDependent | Yalnız sahada fiziksel doğrulama ile teyit edilebilen değer veya davranış. |
| DiscoveryKey | Keşif uçlarının beklediği kimlik: Uuid ?? VehicleId. |
| DispatchEnabled | Fiziksel hareket mutasyonu izni. Rds:Enabled ile karıştırılmamalıdır. |
| ETag | HTTP koşullu istek etiketi; eşzamanlı düzenleme çakışmalarını yakalar. |
| ExecutionMutation | Yürütme kimliğini/sırasını değiştiren, kapıya tabi mutasyon sınıfı. |
| Fail-closed | Kanıt eksik veya belirsizken reddetme davranışı. |
| Idempotency | Aynı anahtarla tekrarlanan isteğin yeni etki üretmemesi. |
| LoadState | Bir kasanın içeriği: EMPTY / FULL / UNKNOWN. |
| MotionEnabling | Fiziksel işi başlatan/ilerleten, kapıya tabi mutasyon sınıfı. |
| MutationTarget | RDSCore'a gönderilecek hedef kimlik: VehicleId ?? Uuid. |
| Native Order | RDSCore'un /setOrder tabanlı yerel sipariş modeli. |
| NavigationProfile | Varyant/makine için robot haritası ve araç eşlemesi. |
| OccupancyState | Slotta kasa var mı: VACANT / OCCUPIED / UNKNOWN. |
| procBusiness | SEER telemetri alanı: robot kullanıcı siparişi yürütüyor mu. |
| RDS | SEER Robot Dispatch System — robot filo yönetim sistemi. |
| RDSCore | RDS'in native order HTTP API'sini sunan çekirdek bileşeni. |
| ReadinessState | Talebin hazırlık ekseni: NotRequired / WaitingPreparation / Ready. |
| REMOVE_EMPTY | Boş kasa kaldırma; yalnız sunucu tarafı orkestrasyonda üretilir. |
| REMOVE_FULL | Dolu kasa kaldırma taşıma operasyonu. |
| RobotLeaseKey | Bir fiziksel robotu kapsayan TKG kirasının anahtarı. |
| SafetyStop | Hareketi azaltan, kapı kapalıyken bile izinli mutasyon sınıfı. |
| SEER | RDS/RDSCore'u üreten satıcı. |
| Slot | Layout üzerindeki fiziksel kasa konumu. |
| SlotRole | Slotun işlevi, örn. EMPTY_STORAGE tampon rolü. |
| TOCTOU | Time-of-check to time-of-use — kontrol ile kullanım arasındaki yarış penceresi. |
| TransportRequest | Bir taşıma işinin TKG'deki kaydı. |
| UUID | Evrensel benzersiz tanımlayıcı. |
| Variant | Bir makinede üretilen ürün konfigürasyonu. Product Group ile aynı şey DEĞİLDİR. |
| WindTask | RDS'in alternatif görev modeli; native order'dan ayrıdır. |
| WorkSite | RDS tarafındaki fiziksel çalışma noktası tanımı. |
| WP-33 | Operatör kasa aksiyonları iş paketi. |


## 34 Kapsam Doğrulaması

Bu doküman, kapsamının denetlenebilir olması için makine okunur bir envanterle birlikte üretilmiştir: `docs/source_inventory.txt`. Envanterdeki her takip edilen dosya tam olarak bir kategoriye aittir.

| Kategori | Adet | Anlamı |
|---|---|---|
| DOCUMENTED | 300 | Bölüm 27'de kendi alt bölümü olan dosyalar |
| GENERATED | 20 | Araç çıktısı; elle düzenlenmez |
| DEPENDENCY | 2 | Üçüncü taraf bağımlılık / lock dosyası |
| BUILD_OUTPUT | 0 | Derleyici/paketleyici çıktısı |
| IRRELEVANT_ARTIFACT | 56 | İkili varlık, platform iskelesi, satıcı dokümanı |
| TOPLAM | 378 | git ls-files ile takip edilen tüm dosyalar |


### 34.1 Sonuç

| Ölçüm | Değer |
|---|---|
| Bulunan ilgili kaynak dosya | 300 |
| Dokümante edilen dosya bölümü | 300 |
| Kapsam | 100% |

> **KANIT:** Envanterde DOCUMENTED olarak sınıflanan her dosyanın bölüm 27 içinde kendi alt bölümü vardır. Eksik dosya yoktur.


### 34.2 Kategori kararlarının gerekçesi

- `db/seeds/generated/*.sql` üretilmiş olmasına rağmen DOCUMENTED sayılır: gerçek veritabanlarına karşı çalıştırılır, dolayısıyla açıklanmalı ve üretilmiş olduğu belirtilmelidir.
- `.gitignore` ve `.gitattributes` DOCUMENTED sayılır: `launchSettings.json` dosyasının git tarafından yok sayılması, paylaşılan bir devreye alma başlatma profilinin neden olamayacağının sebebidir — bu davranışsal bir gerçektir.
- Flutter platform iskelesi (`android/`, `ios/`, `windows/`, `web/` vb.) IRRELEVANT_ARTIFACT'tir: var olmak zorundadır ama hiçbir TKG iş mantığı taşımaz.
- `RDS DÖKÜMANLARI/` git tarafından takip edilmez ve dokunulmamıştır; envanterde yer almaz.

> **NOT:** Envanter `git ls-files` çıktısından üretilir. Bu dokümanın kendisi ve onu üreten `docs/generator/` betikleri üretim anında henüz takip edilmediği için envanterde görünmez; bu, kapsam hesabını etkilemez çünkü kapsam üretim öncesi kaynak ağacını ölçer.


### 34.3 Nasıl yeniden üretilir

1. `python docs/generator/inventory.py` — envanteri ve yapısal gerçekleri yeniden çıkarır.
2. `python docs/generator/build_doc.py` — `.md` ve `.pdf` çıktılarını yeniden üretir ve kapsamı doğrular.
3. Kapsam %100 değilse `build_doc.py` sıfırdan farklı bir çıkış kodu döner ve eksik dosyaları listeler.


## 35 Tam Dosya İndeksi

Takip edilen tüm dosyaların kategorileriyle birlikte tam listesi. Bu tablo `docs/source_inventory.txt` ile birebir aynı içeriktir.


### 35.1 DOCUMENTED (300)

| # | Dosya |
|---|---|
| 1 | .gitattributes |
| 2 | .gitignore |
| 3 | AGENTS.md |
| 4 | CLAUDE.md |
| 5 | Directory.Build.props |
| 6 | README.md |
| 7 | TKG.Pda.slnx |
| 8 | db/migrations/001_initial_schema.sql |
| 9 | db/migrations/002_operator_employee_identity.sql |
| 10 | db/migrations/003_transport_batch.sql |
| 11 | db/migrations/004_transport_request_readiness.sql |
| 12 | db/migrations/005_layout_slot_part_mapping.sql |
| 13 | db/seeds/README.md |
| 14 | db/seeds/generated/002a_customer_textual_master_data.sql |
| 15 | db/seeds/generated/002b_customer_physical_layouts.sql |
| 16 | db/seeds/generated/002c_customer_layout_slot_part_assignments.sql |
| 17 | db/seeds/generated/002d_simulation_rds_trial_mappings.sql |
| 18 | docs/CUSTOMER_WP01A_OPERATOR_EMPLOYEE_IDENTITY.md |
| 19 | docs/OPERATOR_WEB_DEPLOYMENT.md |
| 20 | docs/PHASE_01_NOTES.md |
| 21 | docs/PHASE_02A_NOTES.md |
| 22 | docs/PHASE_02B_NOTES.md |
| 23 | docs/PHASE_03_NOTES.md |
| 24 | docs/PHASE_04_NOTES.md |
| 25 | docs/PHASE_05_ADMIN_CONFIGURATION_API.md |
| 26 | docs/PHASE_06_ADMIN_WEB.md |
| 27 | docs/PHASE_07A_PDA_RDS_CONTRACT_READINESS.md |
| 28 | docs/PHASE_07B_FLUTTER_PDA.md |
| 29 | docs/PHASE_08A_LIVE_RDS_READINESS.md |
| 30 | docs/PHASE_08B_RDS_EXECUTION_INTEGRATION.md |
| 31 | docs/PHASE_08C_RDS_SIMULATOR_COMMISSIONING.md |
| 32 | docs/PHASE_09_MULTIPLATFORM_OPERATOR.md |
| 33 | docs/PHASE_10A_DYNAMIC_RDS_WINDTASK.md |
| 34 | docs/PROJECT_BASELINE.md |
| 35 | docs/RDS_PDA_INTEGRATION_SPEC.md |
| 36 | docs/README.md |
| 37 | docs/TKG_PDA_DATABASE_SPEC_v0.1.md |
| 38 | docs/WP_33_OPERATOR_UX_V2_SHARED_QUEUE.md |
| 39 | docs/commissioning/CUSTOMER_ACCEPTANCE_AUDIT.md |
| 40 | docs/commissioning/CUSTOMER_DATA_REQUEST.md |
| 41 | docs/commissioning/CUSTOMER_SOURCE_RECONCILIATION.md |
| 42 | docs/commissioning/PRODUCTION_COMMISSIONING_CHECKLIST.md |
| 43 | docs/commissioning/RDS_COMMISSIONING_DISCOVERY.md |
| 44 | docs/commissioning/RDS_COMMISSIONING_SNAPSHOT_SCHEMA.md |
| 45 | docs/commissioning/RDS_FIRST_SNAPSHOT_RUNBOOK.md |
| 46 | docs/database/ERD.md |
| 47 | docs/integration/RDSCORE_NATIVE_ORDER_CONTRACT.md |
| 48 | docs/references/customer/README.md |
| 49 | docs/references/rds/RdsApi_en.md |
| 50 | global.json |
| 51 | rds/scripts/README.md |
| 52 | rds/scripts/test/tkg_integration.test.js |
| 53 | rds/scripts/tkg_integration.js |
| 54 | rds/tasks/commissioning/README.md |
| 55 | scripts/run_commissioning.ps1 |
| 56 | src/TkgPda.AdminWeb/.gitignore |
| 57 | src/TkgPda.AdminWeb/.oxlintrc.json |
| 58 | src/TkgPda.AdminWeb/README.md |
| 59 | src/TkgPda.AdminWeb/e2e/admin-acceptance.spec.ts |
| 60 | src/TkgPda.AdminWeb/e2e/auth-security.spec.ts |
| 61 | src/TkgPda.AdminWeb/e2e/global-setup.ts |
| 62 | src/TkgPda.AdminWeb/e2e/global-teardown.ts |
| 63 | src/TkgPda.AdminWeb/e2e/harness.ts |
| 64 | src/TkgPda.AdminWeb/e2e/layout-acceptance.spec.ts |
| 65 | src/TkgPda.AdminWeb/e2e/support.ts |
| 66 | src/TkgPda.AdminWeb/index.html |
| 67 | src/TkgPda.AdminWeb/package.json |
| 68 | src/TkgPda.AdminWeb/playwright.config.ts |
| 69 | src/TkgPda.AdminWeb/src/App.tsx |
| 70 | src/TkgPda.AdminWeb/src/api/ApiContext.test.tsx |
| 71 | src/TkgPda.AdminWeb/src/api/ApiContext.tsx |
| 72 | src/TkgPda.AdminWeb/src/api/client.test.ts |
| 73 | src/TkgPda.AdminWeb/src/api/client.ts |
| 74 | src/TkgPda.AdminWeb/src/api/types.ts |
| 75 | src/TkgPda.AdminWeb/src/auth/AuthContext.test.tsx |
| 76 | src/TkgPda.AdminWeb/src/auth/AuthContext.tsx |
| 77 | src/TkgPda.AdminWeb/src/components/AppShell.tsx |
| 78 | src/TkgPda.AdminWeb/src/components/Toast.tsx |
| 79 | src/TkgPda.AdminWeb/src/components/ui.tsx |
| 80 | src/TkgPda.AdminWeb/src/index.css |
| 81 | src/TkgPda.AdminWeb/src/main.tsx |
| 82 | src/TkgPda.AdminWeb/src/pages/AuditPage.tsx |
| 83 | src/TkgPda.AdminWeb/src/pages/CatalogPage.tsx |
| 84 | src/TkgPda.AdminWeb/src/pages/ContainersPage.tsx |
| 85 | src/TkgPda.AdminWeb/src/pages/DashboardPage.tsx |
| 86 | src/TkgPda.AdminWeb/src/pages/DataQualityPage.tsx |
| 87 | src/TkgPda.AdminWeb/src/pages/DevicesPage.tsx |
| 88 | src/TkgPda.AdminWeb/src/pages/LayoutEditorPage.test.tsx |
| 89 | src/TkgPda.AdminWeb/src/pages/LayoutEditorPage.tsx |
| 90 | src/TkgPda.AdminWeb/src/pages/LayoutsPage.test.tsx |
| 91 | src/TkgPda.AdminWeb/src/pages/LayoutsPage.tsx |
| 92 | src/TkgPda.AdminWeb/src/pages/LoginPage.tsx |
| 93 | src/TkgPda.AdminWeb/src/pages/MachinesPage.tsx |
| 94 | src/TkgPda.AdminWeb/src/pages/NotFoundPage.tsx |
| 95 | src/TkgPda.AdminWeb/src/pages/RdsIntegrationPage.test.tsx |
| 96 | src/TkgPda.AdminWeb/src/pages/RdsIntegrationPage.tsx |
| 97 | src/TkgPda.AdminWeb/src/pages/UsersPage.test.tsx |
| 98 | src/TkgPda.AdminWeb/src/pages/UsersPage.tsx |
| 99 | src/TkgPda.AdminWeb/src/test/fixtures.ts |
| 100 | src/TkgPda.AdminWeb/src/test/render.tsx |
| 101 | src/TkgPda.AdminWeb/src/test/setup.ts |
| 102 | src/TkgPda.AdminWeb/tsconfig.app.json |
| 103 | src/TkgPda.AdminWeb/tsconfig.json |
| 104 | src/TkgPda.AdminWeb/tsconfig.node.json |
| 105 | src/TkgPda.AdminWeb/vite.config.ts |
| 106 | src/TkgPda.Api/AdminContracts.cs |
| 107 | src/TkgPda.Api/AdminEndpoints.cs |
| 108 | src/TkgPda.Api/ApiContracts.cs |
| 109 | src/TkgPda.Api/ApiEndpoints.cs |
| 110 | src/TkgPda.Api/ApiErrors.cs |
| 111 | src/TkgPda.Api/ApiSecurity.cs |
| 112 | src/TkgPda.Api/PostOutcomeAudit.cs |
| 113 | src/TkgPda.Api/Program.cs |
| 114 | src/TkgPda.Api/RdsCommissioningDiscoveryClient.cs |
| 115 | src/TkgPda.Api/RdsCoreNativeOrderClient.cs |
| 116 | src/TkgPda.Api/RdsExecutionClient.cs |
| 117 | src/TkgPda.Api/RdsHttpReadTransport.cs |
| 118 | src/TkgPda.Api/RdsIntegrationEndpoints.cs |
| 119 | src/TkgPda.Api/RdsReadClient.cs |
| 120 | src/TkgPda.Api/TkgPda.Api.csproj |
| 121 | src/TkgPda.Api/appsettings.json |
| 122 | src/TkgPda.Application/AdminConfigurationService.cs |
| 123 | src/TkgPda.Application/AdminModels.cs |
| 124 | src/TkgPda.Application/ApiModels.cs |
| 125 | src/TkgPda.Application/ContainerActions.cs |
| 126 | src/TkgPda.Application/EmployeeNumber.cs |
| 127 | src/TkgPda.Application/IAdminConfigurationStore.cs |
| 128 | src/TkgPda.Application/IApiDataStore.cs |
| 129 | src/TkgPda.Application/ITransportWorkflowStore.cs |
| 130 | src/TkgPda.Application/PdaOperationalContextService.cs |
| 131 | src/TkgPda.Application/RdsCommissioningDiscovery.cs |
| 132 | src/TkgPda.Application/RdsCoreNativeOrders.cs |
| 133 | src/TkgPda.Application/RdsExecution.cs |
| 134 | src/TkgPda.Application/RdsIntegration.cs |
| 135 | src/TkgPda.Application/RdsMutationPolicy.cs |
| 136 | src/TkgPda.Application/RdsRobotAlarmParsing.cs |
| 137 | src/TkgPda.Application/RdsRobotIdentity.cs |
| 138 | src/TkgPda.Application/RdsRobotResolution.cs |
| 139 | src/TkgPda.Application/TkgPda.Application.csproj |
| 140 | src/TkgPda.Application/TransportWorkflowService.cs |
| 141 | src/TkgPda.Domain/TkgPda.Domain.csproj |
| 142 | src/TkgPda.Domain/TransportRules.cs |
| 143 | src/TkgPda.Domain/TransportStateMachine.cs |
| 144 | src/TkgPda.Domain/WorkflowEnums.cs |
| 145 | src/TkgPda.Domain/WorkflowModels.cs |
| 146 | src/TkgPda.Infrastructure.Postgres/PostgresAdminConfigurationStore.Identity.cs |
| 147 | src/TkgPda.Infrastructure.Postgres/PostgresAdminConfigurationStore.Layouts.cs |
| 148 | src/TkgPda.Infrastructure.Postgres/PostgresAdminConfigurationStore.MasterData.cs |
| 149 | src/TkgPda.Infrastructure.Postgres/PostgresAdminConfigurationStore.cs |
| 150 | src/TkgPda.Infrastructure.Postgres/PostgresApiDataStore.cs |
| 151 | src/TkgPda.Infrastructure.Postgres/PostgresRdsCommissioningConfigurationStore.cs |
| 152 | src/TkgPda.Infrastructure.Postgres/PostgresRdsExecutionStore.cs |
| 153 | src/TkgPda.Infrastructure.Postgres/PostgresRdsIntegrationStore.cs |
| 154 | src/TkgPda.Infrastructure.Postgres/PostgresTransportWorkflowStore.ContainerActions.cs |
| 155 | src/TkgPda.Infrastructure.Postgres/PostgresTransportWorkflowStore.cs |
| 156 | src/TkgPda.Infrastructure.Postgres/TkgPda.Infrastructure.Postgres.csproj |
| 157 | src/TkgPda.Pda/.gitignore |
| 158 | src/TkgPda.Pda/README.md |
| 159 | src/TkgPda.Pda/analysis_options.yaml |
| 160 | src/TkgPda.Pda/integration_test/operator_web_e2e_test.dart |
| 161 | src/TkgPda.Pda/lib/app/app.dart |
| 162 | src/TkgPda.Pda/lib/app/app_controller.dart |
| 163 | src/TkgPda.Pda/lib/app/router.dart |
| 164 | src/TkgPda.Pda/lib/app/theme.dart |
| 165 | src/TkgPda.Pda/lib/core/auth/session_memory.dart |
| 166 | src/TkgPda.Pda/lib/core/config/app_config.dart |
| 167 | src/TkgPda.Pda/lib/core/config/runtime_config_loader.dart |
| 168 | src/TkgPda.Pda/lib/core/config/runtime_config_loader_stub.dart |
| 169 | src/TkgPda.Pda/lib/core/config/runtime_config_loader_web.dart |
| 170 | src/TkgPda.Pda/lib/core/errors/api_problem.dart |
| 171 | src/TkgPda.Pda/lib/core/network/api_client.dart |
| 172 | src/TkgPda.Pda/lib/core/platform/platform_info.dart |
| 173 | src/TkgPda.Pda/lib/core/platform/platform_info_io.dart |
| 174 | src/TkgPda.Pda/lib/core/platform/platform_info_stub.dart |
| 175 | src/TkgPda.Pda/lib/core/platform/platform_info_web.dart |
| 176 | src/TkgPda.Pda/lib/core/storage/device_code_store.dart |
| 177 | src/TkgPda.Pda/lib/core/storage/device_code_store_factory.dart |
| 178 | src/TkgPda.Pda/lib/core/storage/device_code_store_factory_secure.dart |
| 179 | src/TkgPda.Pda/lib/core/storage/device_code_store_factory_stub.dart |
| 180 | src/TkgPda.Pda/lib/core/storage/device_code_store_factory_web.dart |
| 181 | src/TkgPda.Pda/lib/core/storage/device_code_store_secure.dart |
| 182 | src/TkgPda.Pda/lib/core/storage/device_code_store_web.dart |
| 183 | src/TkgPda.Pda/lib/data/models/pda_models.dart |
| 184 | src/TkgPda.Pda/lib/data/repositories/pda_repository.dart |
| 185 | src/TkgPda.Pda/lib/features/device_setup/device_setup_screen.dart |
| 186 | src/TkgPda.Pda/lib/features/login/login_screen.dart |
| 187 | src/TkgPda.Pda/lib/features/machines/machine_list_screen.dart |
| 188 | src/TkgPda.Pda/lib/features/operation/container_position_panel.dart |
| 189 | src/TkgPda.Pda/lib/features/operation/multi_part_batch_screen.dart |
| 190 | src/TkgPda.Pda/lib/features/operation/operation_screen.dart |
| 191 | src/TkgPda.Pda/lib/features/operation/part_first_screen.dart |
| 192 | src/TkgPda.Pda/lib/features/operation/transport_action_panel.dart |
| 193 | src/TkgPda.Pda/lib/features/tasks/operator_queue_screen.dart |
| 194 | src/TkgPda.Pda/lib/features/transport/submission_coordinator.dart |
| 195 | src/TkgPda.Pda/lib/features/warehouse/warehouse_preparation_screen.dart |
| 196 | src/TkgPda.Pda/lib/main.dart |
| 197 | src/TkgPda.Pda/lib/shared/presentation.dart |
| 198 | src/TkgPda.Pda/lib/shared/widgets/feedback_banners.dart |
| 199 | src/TkgPda.Pda/pubspec.yaml |
| 200 | src/TkgPda.Pda/test/support/fakes.dart |
| 201 | src/TkgPda.Pda/test/unit/api_problem_presentation_test.dart |
| 202 | src/TkgPda.Pda/test/unit/operator_integrity_phase1_test.dart |
| 203 | src/TkgPda.Pda/test/unit/security_network_test.dart |
| 204 | src/TkgPda.Pda/test/unit/submission_presentation_test.dart |
| 205 | src/TkgPda.Pda/test/widget/multi_part_batch_flow_test.dart |
| 206 | src/TkgPda.Pda/test/widget/operator_usability_phase3_test.dart |
| 207 | src/TkgPda.Pda/test/widget/part_first_flow_test.dart |
| 208 | src/TkgPda.Pda/test/widget/pda_flow_test.dart |
| 209 | src/TkgPda.Pda/test/widget/robot_alarm_banner_flow_test.dart |
| 210 | src/TkgPda.Pda/test/widget/warehouse_preparation_flow_test.dart |
| 211 | src/TkgPda.Pda/test/widget/wp33_operator_ux_test.dart |
| 212 | src/TkgPda.Pda/test_driver/integration_test.dart |
| 213 | src/TkgPda.Pda/tool/phase_07b/real_backend_contract_test.dart |
| 214 | tests/TkgPda.Api.Tests/AdminConfigurationApiTests.cs |
| 215 | tests/TkgPda.Api.Tests/ApiCancellationMiddlewareTests.cs |
| 216 | tests/TkgPda.Api.Tests/ApiTestFixture.cs |
| 217 | tests/TkgPda.Api.Tests/AssemblyInfo.cs |
| 218 | tests/TkgPda.Api.Tests/AuthenticatedApiTests.cs |
| 219 | tests/TkgPda.Api.Tests/CompatibleVariantsApiTests.cs |
| 220 | tests/TkgPda.Api.Tests/ContainerActionsApiTests.cs |
| 221 | tests/TkgPda.Api.Tests/ExpertActionSafetyApiTests.cs |
| 222 | tests/TkgPda.Api.Tests/LiveRdsPostgresSyncTests.cs |
| 223 | tests/TkgPda.Api.Tests/MachinePartOptionsApiTests.cs |
| 224 | tests/TkgPda.Api.Tests/OperatorInformationBoundaryApiTests.cs |
| 225 | tests/TkgPda.Api.Tests/OperatorOperationAuthorityApiTests.cs |
| 226 | tests/TkgPda.Api.Tests/OperatorWebCorsTests.cs |
| 227 | tests/TkgPda.Api.Tests/PdaRdsContractReadinessApiTests.cs |
| 228 | tests/TkgPda.Api.Tests/Phase08bExecutionApiTests.cs |
| 229 | tests/TkgPda.Api.Tests/PostOutcomeAuditTests.cs |
| 230 | tests/TkgPda.Api.Tests/RdsAdminApiTests.cs |
| 231 | tests/TkgPda.Api.Tests/RdsCommissioningDiscoveryClientTests.cs |
| 232 | tests/TkgPda.Api.Tests/RdsCoreNativeOrderClientTests.cs |
| 233 | tests/TkgPda.Api.Tests/RdsDispatchConfigurationTests.cs |
| 234 | tests/TkgPda.Api.Tests/RdsExecutionClientTests.cs |
| 235 | tests/TkgPda.Api.Tests/RdsReadClientTests.cs |
| 236 | tests/TkgPda.Api.Tests/RdsRobotAlarmApiTests.cs |
| 237 | tests/TkgPda.Api.Tests/TkgPda.Api.Tests.csproj |
| 238 | tests/TkgPda.Api.Tests/TransportBatchApiTests.cs |
| 239 | tests/TkgPda.Api.Tests/TransportReadinessApiTests.cs |
| 240 | tests/TkgPda.Api.Tests/WarehousePreparationApiTests.cs |
| 241 | tests/TkgPda.Application.Tests/ContainerActionResolutionPolicyTests.cs |
| 242 | tests/TkgPda.Application.Tests/PdaOperationalContextPolicyTests.cs |
| 243 | tests/TkgPda.Application.Tests/RdsCommissioningDiscoveryTests.cs |
| 244 | tests/TkgPda.Application.Tests/RdsCoreNativeOrderTests.cs |
| 245 | tests/TkgPda.Application.Tests/RdsExecutionCoordinatorNativeOrderTests.cs |
| 246 | tests/TkgPda.Application.Tests/RdsExecutionCoordinatorTests.cs |
| 247 | tests/TkgPda.Application.Tests/RdsRobotAlarmParserTests.cs |
| 248 | tests/TkgPda.Application.Tests/RdsTelemetryDomainBoundaryTests.cs |
| 249 | tests/TkgPda.Application.Tests/TkgPda.Application.Tests.csproj |
| 250 | tests/TkgPda.Application.Tests/TransportEtaServiceTests.cs |
| 251 | tests/TkgPda.Application.Tests/TransportWorkflowServiceTests.cs |
| 252 | tests/TkgPda.Auth.Tests/AuthenticationTests.cs |
| 253 | tests/TkgPda.Auth.Tests/TkgPda.Auth.Tests.csproj |
| 254 | tests/TkgPda.Domain.Tests/TkgPda.Domain.Tests.csproj |
| 255 | tests/TkgPda.Domain.Tests/TransportDomainTests.cs |
| 256 | tests/TkgPda.Domain.Tests/TransportEmptyContainerBufferDomainTests.cs |
| 257 | tests/TkgPda.Domain.Tests/TransportReadinessDomainTests.cs |
| 258 | tests/TkgPda.Infrastructure.Postgres.Tests/AssemblyInfo.cs |
| 259 | tests/TkgPda.Infrastructure.Postgres.Tests/PostgresEmployeeNumberIdentityTests.cs |
| 260 | tests/TkgPda.Infrastructure.Postgres.Tests/PostgresEmptyContainerBufferTests.cs |
| 261 | tests/TkgPda.Infrastructure.Postgres.Tests/PostgresFactAttribute.cs |
| 262 | tests/TkgPda.Infrastructure.Postgres.Tests/PostgresLayoutCloningTests.cs |
| 263 | tests/TkgPda.Infrastructure.Postgres.Tests/PostgresRdsCommissioningConfigurationStoreTests.cs |
| 264 | tests/TkgPda.Infrastructure.Postgres.Tests/PostgresRdsReadinessTests.cs |
| 265 | tests/TkgPda.Infrastructure.Postgres.Tests/PostgresTransportReadinessTests.cs |
| 266 | tests/TkgPda.Infrastructure.Postgres.Tests/PostgresTransportWorkflowTests.cs |
| 267 | tests/TkgPda.Infrastructure.Postgres.Tests/TkgPda.Infrastructure.Postgres.Tests.csproj |
| 268 | tests/db/001_schema_smoke_test.sql |
| 269 | tests/phase_02a/expected_counts.json |
| 270 | tests/phase_02a/run_postgres_integration.ps1 |
| 271 | tests/phase_02a/test_pipeline.py |
| 272 | tests/phase_02b/expected_counts.json |
| 273 | tests/phase_02b/run_postgres_integration.ps1 |
| 274 | tests/phase_02b/test_pipeline.py |
| 275 | tests/phase_03/run_postgres_integration.ps1 |
| 276 | tests/phase_04/run_postgres_api_integration.ps1 |
| 277 | tests/phase_05/run_postgres_admin_integration.ps1 |
| 278 | tests/phase_07a/run_postgres_pda_rds_integration.ps1 |
| 279 | tests/phase_07b/run_real_api_contract.ps1 |
| 280 | tests/phase_08a/run_live_rds_postgres_sync.ps1 |
| 281 | tests/phase_08a/run_live_rds_readonly.ps1 |
| 282 | tests/phase_08a/run_postgres_rds_readiness_integration.ps1 |
| 283 | tests/phase_08b/run_postgres_rds_execution_integration.ps1 |
| 284 | tests/phase_08c/run_live_rds_simulator_e2e.ps1 |
| 285 | tests/phase_09/run_web_operator_e2e.ps1 |
| 286 | tests/phase_09/web_contract.test.mjs |
| 287 | tests/phase_09/webdriver_edge_proxy.mjs |
| 288 | tests/phase_10a/rds_dynamic_task_contract.test.mjs |
| 289 | tests/phase_10a/run_live_dynamic_rds_e2e.ps1 |
| 290 | tests/wp30/run_postgres_admin_discovery.ps1 |
| 291 | tools/dev/README.md |
| 292 | tools/dev/reset_local_database.ps1 |
| 293 | tools/dev/start_local_stack.ps1 |
| 294 | tools/dev/stop_local_stack.ps1 |
| 295 | tools/phase_02a/__init__.py |
| 296 | tools/phase_02a/pipeline.py |
| 297 | tools/phase_02a/requirements.txt |
| 298 | tools/phase_02b/__init__.py |
| 299 | tools/phase_02b/pipeline.py |
| 300 | tools/phase_02b/requirements.txt |


### 35.2 GENERATED (20)

| # | Dosya |
|---|---|
| 1 | data/generated/phase_02a/container_types.csv |
| 2 | data/generated/phase_02a/data_issues.csv |
| 3 | data/generated/phase_02a/data_issues.json |
| 4 | data/generated/phase_02a/normalized_customer_data.json |
| 5 | data/generated/phase_02a/parts.csv |
| 6 | data/generated/phase_02a/planning_metrics.csv |
| 7 | data/generated/phase_02a/product_group_parts.csv |
| 8 | data/generated/phase_02a/product_groups.csv |
| 9 | data/generated/phase_02a/source_manifest.json |
| 10 | data/generated/phase_02a/summary.json |
| 11 | data/generated/phase_02b/data_issues.csv |
| 12 | data/generated/phase_02b/data_issues.json |
| 13 | data/generated/phase_02b/excluded_candidates.csv |
| 14 | data/generated/phase_02b/layout_slot_part_assignments.csv |
| 15 | data/generated/phase_02b/layout_slots.csv |
| 16 | data/generated/phase_02b/layouts.csv |
| 17 | data/generated/phase_02b/normalized_physical_layouts.json |
| 18 | data/generated/phase_02b/source_manifest.json |
| 19 | data/generated/phase_02b/summary.json |
| 20 | data/generated/phase_02b/variant_layout_assignments.csv |


### 35.3 DEPENDENCY (2)

| # | Dosya |
|---|---|
| 1 | src/TkgPda.AdminWeb/package-lock.json |
| 2 | src/TkgPda.Pda/pubspec.lock |


### 35.4 IRRELEVANT_ARTIFACT (56)

| # | Dosya |
|---|---|
| 1 | data/generated/phase_02b/review/412b-customer-physical-v1.png |
| 2 | data/generated/phase_02b/review/412b-customer-physical-v2.png |
| 3 | data/generated/phase_02b/review/412b-customer-physical-v3.png |
| 4 | data/generated/phase_02b/review/gar-customer-physical-v1.png |
| 5 | data/generated/phase_02b/review/gar-customer-physical-v2.png |
| 6 | data/generated/phase_02b/review/gar-customer-physical-v3.png |
| 7 | data/generated/phase_02b/review/pah-01-a-customer-physical-v1.png |
| 8 | data/generated/phase_02b/review/pah-01-a-customer-physical-v2.png |
| 9 | data/generated/phase_02b/review/pah-02-a-customer-physical-v1.png |
| 10 | data/generated/phase_02b/review/pah-02-a-customer-physical-v2.png |
| 11 | data/generated/phase_02b/review/sealer-customer-physical-shared-v1-v2.png |
| 12 | data/generated/phase_02b/review/sh-01-a-customer-physical-v1.png |
| 13 | data/generated/phase_02b/review/sh-01-a-customer-physical-v2.png |
| 14 | data/generated/phase_02b/review/sh-01-a-customer-physical-v3.png |
| 15 | data/generated/phase_02b/review/sh-01-a-customer-physical-v4.png |
| 16 | data/generated/phase_02b/review/sh-01-a-customer-physical-v5.png |
| 17 | data/generated/phase_02b/review/sh-01-b-customer-physical-v1.png |
| 18 | data/generated/phase_02b/review/sh-01-b-customer-physical-v2.png |
| 19 | data/generated/phase_02b/review/sh-01-b-customer-physical-v3.png |
| 20 | data/generated/phase_02b/review/sh-01-p1-sh-01-p2-customer-physical-shared-v1-v2.png |
| 21 | data/generated/phase_02b/review/sh-02-p1-sh-02-p2-customer-physical-shared-v1-v2.png |
| 22 | rds/tasks/commissioning/TKG_SIM_DYNAMIC_MOVE.task |
| 23 | rds/tasks/commissioning/TKG_SIM_MOVE.task |
| 24 | src/TkgPda.Pda/.metadata |
| 25 | src/TkgPda.Pda/android/.gitignore |
| 26 | src/TkgPda.Pda/android/app/build.gradle.kts |
| 27 | src/TkgPda.Pda/android/app/src/debug/AndroidManifest.xml |
| 28 | src/TkgPda.Pda/android/app/src/main/AndroidManifest.xml |
| 29 | src/TkgPda.Pda/android/app/src/main/kotlin/com/logirobotic/tkg_pda/MainActivity.kt |
| 30 | src/TkgPda.Pda/android/app/src/main/res/drawable-v21/launch_background.xml |
| 31 | src/TkgPda.Pda/android/app/src/main/res/drawable/launch_background.xml |
| 32 | src/TkgPda.Pda/android/app/src/main/res/mipmap-hdpi/ic_launcher.png |
| 33 | src/TkgPda.Pda/android/app/src/main/res/mipmap-mdpi/ic_launcher.png |
| 34 | src/TkgPda.Pda/android/app/src/main/res/mipmap-xhdpi/ic_launcher.png |
| 35 | src/TkgPda.Pda/android/app/src/main/res/mipmap-xxhdpi/ic_launcher.png |
| 36 | src/TkgPda.Pda/android/app/src/main/res/mipmap-xxxhdpi/ic_launcher.png |
| 37 | src/TkgPda.Pda/android/app/src/main/res/values-night/styles.xml |
| 38 | src/TkgPda.Pda/android/app/src/main/res/values/styles.xml |
| 39 | src/TkgPda.Pda/android/app/src/profile/AndroidManifest.xml |
| 40 | src/TkgPda.Pda/android/build.gradle.kts |
| 41 | src/TkgPda.Pda/android/gradle.properties |
| 42 | src/TkgPda.Pda/android/gradle/wrapper/gradle-wrapper.properties |
| 43 | src/TkgPda.Pda/android/settings.gradle.kts |
| 44 | src/TkgPda.Pda/web/favicon.png |
| 45 | src/TkgPda.Pda/web/flutter_bootstrap.js |
| 46 | src/TkgPda.Pda/web/font-fallbacks/README.md |
| 47 | src/TkgPda.Pda/web/font-fallbacks/notosanssymbols/v43/rP2up3q65FkAtHfwd-eIS2brbDN6gxP34F9jRRCe4W3gfQ8gb_VFRkzrbQ.woff2 |
| 48 | src/TkgPda.Pda/web/font-fallbacks/roboto/v32/KFOmCnqEu92Fr1Me4GZLCzYlKw.woff2 |
| 49 | src/TkgPda.Pda/web/icons/Icon-192.png |
| 50 | src/TkgPda.Pda/web/icons/Icon-512.png |
| 51 | src/TkgPda.Pda/web/icons/Icon-maskable-192.png |
| 52 | src/TkgPda.Pda/web/icons/Icon-maskable-512.png |
| 53 | src/TkgPda.Pda/web/index.html |
| 54 | src/TkgPda.Pda/web/manifest.json |
| 55 | src/TkgPda.Pda/web/operator-config.json |
| 56 | src/TkgPda.Pda/web/operator_service_worker.js |
