"""Authored narrative sections of the TKG_PDA technical reference (Turkish).

Every statement here is traceable to the source tree at the documented commit.
Where something cannot be proven from source it is explicitly labelled
COMMISSIONING VALIDATION REQUIRED or UNVERIFIED.
"""

H, P, UL, OL, T, C, PRE, N, PB = "h", "p", "ul", "ol", "table", "code", "pre", "note", "pagebreak"


def section_about(meta):
    b = []
    b.append((H, 1, "Doküman Hakkında"))
    b.append((P, "Bu doküman TKG_PDA projesinin tamamını, mimariden tek tek kaynak dosyalara kadar açıklayan teknik referanstır. Amacı, projeyi hiç görmemiş bir yazılım mühendisinin sistemi devralabilmesidir. Özet değildir; her ilgili kaynak dosyanın kendi açıklaması vardır."))
    b.append((H, 2, "Dokümante edilen sürüm"))
    b.append((T, ["Alan", "Değer"], [
        ["Branch", meta["branch"]],
        ["Commit", meta["commit"]],
        ["Baz alınan main", meta["base"]],
        ["Üretim tarihi", meta["date"]],
    ]))
    b.append((N, "WARN", "Bu branch `main` ile birleştirilmemiştir. `main` üzerinde bulunmayan üç grup değişiklik içerir: (1) Operator/Admin UI bütünlük çalışması (`fa21ffe`, `63ca4a2`, `7bdb91b`), (2) RDS commissioning runtime çalışması (`52ce250`, `c452e67`), (3) yerel geliştirme yığını temizliği (`e84728b`). Dokümanda anlatılan davranış bu branch'in davranışıdır; `main` bu üç grubu içermez."))
    b.append((H, 2, "Kanıt seviyeleri"))
    b.append((P, "Doküman boyunca aşağıdaki etiketler kullanılır. Etiketsiz teknik ifadeler kod okumasına dayanır."))
    b.append((T, ["Etiket", "Anlamı"], [
        ["CODE-CONFIRMED", "İfade, dokümante edilen commit'teki kaynak koddan doğrudan okunmuştur."],
        ["DATABASE-CONFIRMED", "İfade, migration/şema dosyalarından veya çalışan bir veritabanı sorgusundan doğrulanmıştır."],
        ["DOCUMENTATION-CONFIRMED", "İfade, `RDS DÖKÜMANLARI/` altındaki SEER dokümanlarından veya depo içi dokümanlardan doğrulanmıştır."],
        ["COMMISSIONING VALIDATION REQUIRED", "Kaynak koddan kanıtlanamaz; sahada fiziksel devreye alma ile doğrulanmalıdır."],
        ["UNVERIFIED", "Ne kod ne doküman kanıtı bulunamadı; varsayım olarak işaretlenmiştir."],
    ]))
    b.append((H, 2, "Doküman nasıl okunmalı"))
    b.append((UL, [
        "Sistemi ilk kez devralıyorsanız: bölüm 2-5 (mimari, teknoloji, repo yapısı) ve bölüm 6 (domain modeli) zorunlu başlangıçtır.",
        "Bir hatayı araştırıyorsanız: bölüm 30 (Troubleshooting) ve bölüm 29 (hata kodları) karar ağaçları içerir.",
        "Belirli bir dosyayı arıyorsanız: bölüm 27 dosya-dosya referans, bölüm 32 tam dosya indeksidir.",
        "RDS/robot güvenliği ile ilgileniyorsanız: bölüm 11 ve 18-21 birlikte okunmalıdır.",
    ]))
    return b


def section_overview():
    b = []
    b.append((H, 1, "Proje Özeti"))
    b.append((P, "TKG_PDA, bir üretim tesisinde operatörlerin el terminali (PDA) üzerinden kasa (konteyner) lojistiği talebi oluşturmasını ve bu taleplerin SEER RDS robot filosu tarafından fiziksel olarak yerine getirilmesini sağlayan uçtan uca bir sistemdir."))
    b.append((P, "Sistem üç kullanıcı yüzeyi sunar: sahada operatörün kullandığı Flutter tabanlı **Operator PDA**, konfigürasyonu yöneten React tabanlı **Admin Web** ve bunların üzerinde çalıştığı ASP.NET Core **API**. Kalıcı durum PostgreSQL'de tutulur, fiziksel hareket ise RDS adaptörü üzerinden RDSCore'a devredilir."))
    b.append((H, 2, "Sistemin çözdüğü problem"))
    b.append((P, "Operatör, hangi fiziksel rafın hangi robot noktasına karşılık geldiğini bilmek zorunda kalmadan iş yapabilmelidir. Operatör sadece iş niyetini ifade eder: *hangi makine*, *hangi parça*, *hangi mantıksal kasa numarası*, *hangi işlem*. Fiziksel yönlendirmeyi (kaynak/hedef slot seçimi, WorkSite eşlemesi, robot seçimi) sunucu yapar. Bu ayrım dokümanın tamamında tekrar eden en önemli tasarım kararıdır."))
    b.append((H, 2, "Temel tasarım ilkeleri"))
    b.append((OL, [
        "**Tek çalışma yolu.** TKG'de simülasyon ve gerçek robot için ayrı iş mantığı yoktur. RDS'in simülasyonu mu yoksa fiziksel robotu mu sürdüğü TKG'yi ilgilendirmez.",
        "**Envanter gerçeği TKG'ye aittir.** Slot doluluk/yük durumunun sahibi TKG'dir; RDS telemetrisi bu gerçeği ezemez.",
        "**Fiziksel mutasyon varsayılan olarak kapalıdır.** `Rds:DispatchEnabled` varsayılanı `false`'tur ve yalnız çalışma ortamından açılır.",
        "**Fail-closed.** Kanıt eksik, belirsiz veya çelişkiliyse sistem hareketi reddeder; tahmin etmez.",
        "**Operatör iş sözleşmesi teknik değildir.** Ham RDS tanımlayıcıları (task id, WorkSite, AGV id, uuid) operatör yüzeyine hiç serialize edilmez.",
    ]))
    return b


def section_architecture():
    b = []
    b.append((H, 1, "Sistem Mimarisi"))
    b.append((P, "Katmanlar tek yönlü bağımlıdır: istemciler yalnız API ile, API Application/Domain ile, Application ise PostgreSQL ve RDS adaptörü ile konuşur. İstemciler hiçbir koşulda RDS ile doğrudan haberleşmez."))
    b.append((PRE, """
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
""".strip("\n")))
    b.append((H, 2, "Simülasyon ve gerçek robot ayrımı"))
    b.append((P, "TKG kaynak kodunda `SimulationMode`, `IsSimulation`, `SimulationDispatch` gibi hiçbir kavram yoktur; üretim kaynağında yapılan arama sıfır sonuç verir. Bunun nedeni mimari bir karardır: RDS zaten kendi içinde simülasyon ile fiziksel donanım arasında geçiş yapar. TKG'nin bu farkı bilmesi hem gereksiz hem de tehlikelidir, çünkü \"simülasyondayız\" varsayımı gerçek robotu hareket ettiren bir yolda yanlışsa sonucu fizikseldir."))
    b.append((N, "FACT", "CODE-CONFIRMED — `src/` altındaki tüm C#, Dart ve TypeScript üretim kaynağında simülasyon moduna dair üretim davranışı değiştiren hiçbir sembol bulunmamaktadır. Test sahteleri (fake/mock) bu kuralın dışındadır ve tests/ altında kalır."))
    b.append((H, 2, "İki ayrı anahtar: Rds.Enabled ve Rds.DispatchEnabled"))
    b.append((P, "Bu ikisi sık karıştırılır ama farklı sorulara cevap verir. Ayrı olmalarının nedeni, telemetri okumanın güvenli, hareket başlatmanın ise fiziksel sonuç doğuran bir eylem olmasıdır."))
    b.append((T, ["Anahtar", "Sorduğu soru", "Varsayılan", "Kapalıyken ne olur"], [
        ["Rds.Enabled", "RDS entegrasyonu hiç aktif mi?", "true", "Ne okuma ne yazma yapılır; worker yalnız reconcile için ayakta kalmaz."],
        ["Rds.DispatchEnabled", "TKG şu anda RDS'e hareket mutasyonu göndermeye yetkili mi?", "false", "Telemetri/okuma ve güvenlik durdurmaları çalışır; hareket başlatan mutasyonlar reddedilir."],
    ]))
    b.append((N, "WARN", "`DispatchEnabled` bir simülasyon bayrağı DEĞİLDİR. `localhost` veya `127.0.0.1` olması da hareketi otomatik olarak güvenli kılmaz: RDSCore aynı makinede çalışıp gerçek bir robotu sürüyor olabilir. Bu yüzden kod hiçbir yerde adrese bakarak mutasyon izni vermez."))
    b.append((H, 2, "Katman sorumlulukları"))
    b.append((T, ["Katman", "Sorumluluk", "Sorumlu olmadığı"], [
        ["TkgPda.Domain", "Değişmez iş kuralları, durum makinesi, slot durum geçişleri", "I/O, veritabanı, HTTP"],
        ["TkgPda.Application", "İş akışı orkestrasyonu, RDS yürütme koordinasyonu, projeksiyonlar", "SQL cümleleri, HTTP istemcisi"],
        ["TkgPda.Infrastructure.Postgres", "SQL, işlem sınırları, advisory lock, idempotency", "İş kuralı kararı"],
        ["TkgPda.Api", "HTTP yüzeyi, kimlik/yetki, RDS HTTP adaptörleri, konfigürasyon", "İş kuralı kararı"],
        ["TkgPda.Pda (Flutter)", "Operatör iş yüzeyi", "Fiziksel yönlendirme kararı"],
        ["TkgPda.AdminWeb (React)", "Konfigürasyon ve teknik teşhis yüzeyi", "Operatör iş akışı"],
    ]))
    return b


def section_stack():
    b = []
    b.append((H, 1, "Teknoloji Yığını"))
    b.append((T, ["Bileşen", "Teknoloji", "Kaynak"], [
        ["API", ".NET 10 / ASP.NET Core Minimal API", "src/TkgPda.Api/TkgPda.Api.csproj"],
        ["Uygulama/Domain", "C# 13, net10.0", "src/TkgPda.Application, src/TkgPda.Domain"],
        ["Veritabanı erişimi", "Npgsql (ham SQL, EF Core yok)", "src/TkgPda.Infrastructure.Postgres"],
        ["Veritabanı", "PostgreSQL 18", "db/migrations"],
        ["Operatör istemcisi", "Flutter / Dart", "src/TkgPda.Pda/pubspec.yaml"],
        ["Admin istemcisi", "React + TypeScript + Vite", "src/TkgPda.AdminWeb/package.json"],
        ["Admin state", "TanStack Query", "src/TkgPda.AdminWeb/src/api"],
        ["Test", "xUnit, flutter_test, Vitest, Playwright", "tests/, src/TkgPda.Pda/test, src/TkgPda.AdminWeb"],
        ["Robot kontrol", "SEER RDS / RDSCore HTTP API", "RDS DÖKÜMANLARI/ (untracked referans)"],
    ]))
    b.append((N, "NOTE", "EF Core bilinçli olarak kullanılmaz. Şemanın tek gerçek kaynağı `db/migrations` altındaki SQL dosyalarıdır; ORM migration üretimi projede yasaktır. Bunun nedeni, şemanın müşteri veri modeline ve commissioning kısıtlarına göre elle tasarlanmış olmasıdır."))
    return b


def section_repo():
    b = []
    b.append((H, 1, "Repository Yapısı"))
    b.append((T, ["Dizin", "İçerik"], [
        ["src/TkgPda.Api", "HTTP yüzeyi, kimlik doğrulama, RDS HTTP adaptörleri, konfigürasyon bağlama"],
        ["src/TkgPda.Application", "İş akışı servisleri, RDS yürütme koordinatörü, politikalar, projeksiyonlar"],
        ["src/TkgPda.Domain", "Saf iş kuralları: durum makinesi, transport kuralları, enum'lar"],
        ["src/TkgPda.Infrastructure.Postgres", "Npgsql tabanlı store implementasyonları"],
        ["src/TkgPda.Pda", "Flutter operatör uygulaması (lib/, test/, integration_test/)"],
        ["src/TkgPda.AdminWeb", "React admin uygulaması (src/, e2e/)"],
        ["db/migrations", "Şemanın tek gerçek kaynağı; sıralı SQL migration'ları"],
        ["db/seeds/generated", "Müşteri kaynak verisinden üretilmiş seed SQL"],
        ["tests", "xUnit test projeleri (Domain, Application, Auth, Api, Infrastructure.Postgres)"],
        ["tools", "Geliştirme ve veri dönüştürme araçları"],
        ["scripts", "Commissioning çalıştırma betiği"],
        ["docs", "Proje dokümantasyonu (bu doküman dahil)"],
        ["data/generated", "Müşteri kaynak verisinden üretilmiş ara çıktılar"],
        ["RDS DÖKÜMANLARI/", "SEER satıcı dokümanları — git tarafından takip EDİLMEZ, değiştirilmez"],
    ]))
    return b


def section_domain():
    b = []
    b.append((H, 1, "Domain Modeli ve Değişmez İş Kuralları"))
    b.append((P, "Domain katmanı projenin donmuş (frozen) kurallarını barındırır. Bu kurallar müşteri ile mutabık kalınmıştır ve teknik kolaylık için değiştirilemez."))
    b.append((H, 2, "Occupancy ve Load: iki ayrı eksen"))
    b.append((P, "Bir slotun durumu tek bir değerle değil, iki bağımsız eksenle ifade edilir. Bu ayrım kritik olduğu için veritabanı seviyesinde CHECK kısıtı ile korunur."))
    b.append((T, ["Eksen", "Değerler", "Anlamı"], [
        ["OccupancyState", "VACANT, OCCUPIED, UNKNOWN", "Slotta fiziksel bir kasa var mı?"],
        ["LoadState", "EMPTY, FULL, UNKNOWN", "Varsa, o kasanın içinde ürün var mı?"],
    ]))
    b.append((N, "WARN", "RESERVED asla bir occupancy değeri değildir. Bir slotun aktif taşıma talebi tarafından rezerve edilmiş olması, fiziksel doluluğunu değiştirmez. Rezervasyon `ops.transport_requests` üzerinden türetilir; `ops.slot_runtime_state` üzerinde bir REZERVE durumu yoktur. CODE-CONFIRMED: `TransportStateMachine.IsReservationActive`."))
    b.append((P, "Veritabanı `ck_slot_load_requires_occupancy` kısıtı ile mantıksız kombinasyonları engeller: dolu olmayan bir slot EMPTY/FULL yük durumu taşıyamaz. DATABASE-CONFIRMED — `db/migrations/001_initial_schema.sql`."))
    b.append((H, 2, "Taşıma operasyonları"))
    b.append((T, ["Operasyon", "Anlamı", "Operatör seçebilir mi?"], [
        ["BRING_EMPTY", "Boş kasa getir (kaynak: EMPTY_STORAGE tampon, hedef: makine slotu)", "Evet — \"Kasa Boş\""],
        ["REMOVE_FULL", "Dolu kasayı kaldır (kaynak: makine slotu)", "Evet — \"Kasa Doldu\""],
        ["REMOVE_EMPTY", "Boş kasayı kaldır", "HAYIR — yalnız iç orkestrasyon"],
    ]))
    b.append((N, "WARN", "REMOVE_EMPTY bir operatör aksiyonu DEĞİLDİR. Domain'de vardır ve \"Tüm Kasaları Kaldır\" akışı OCCUPIED+EMPTY pozisyonlar için bunu sunucu tarafında üretir; ancak genel taşıma oluşturma ucu bunu her rol için reddeder. CODE-CONFIRMED: `ApiEndpoints.EnsureTransportOperationAllowed` → `OPERATION_NOT_OPERATOR_SELECTABLE` (HTTP 422)."))
    b.append((H, 2, "WP-33 operatör akış kuralları"))
    b.append((P, "WP-33, operatörün gördüğü mantıksal kasa aksiyonlarını tanımlar. Operatör fiziksel slot seçmez; mantıksal kasa numarası (1. Kasa, 2. Kasa ...) seçer."))
    b.append((H, 3, "\"N. Kasa Doldu\" (REMOVE_FULL semantiği)"))
    b.append((T, ["Slot durumu", "Sonuç"], [
        ["OCCUPIED + EMPTY", "Yük durumu atomik olarak EMPTY→FULL yapılır ve REMOVE_FULL talebi oluşturulur"],
        ["OCCUPIED + FULL", "Zaten dolu; REMOVE_FULL idempotent olarak oluşturulur"],
        ["VACANT", "Reddedilir — `OCCUPIED_CONTAINER_REQUIRED`"],
        ["UNKNOWN (herhangi bir eksende)", "Reddedilir — `BUSINESS_RULE_NOT_CONFIGURED`"],
    ]))
    b.append((H, 3, "\"N. Kasa Boş\" (BRING_EMPTY semantiği)"))
    b.append((P, "VACANT + UNKNOWN durumundaki bir mantıksal pozisyon için boş kasa getirtir. Bu aksiyon **REMOVE_EMPTY anlamına gelmez**; isim benzerliği yanıltıcıdır. Kaynak, `EMPTY_STORAGE` rolündeki tampon slottan `FOR UPDATE ... SKIP LOCKED` ile tahsis edilir."))
    b.append((H, 3, "\"Tüm Kasaları Kaldır\" (Remove All)"))
    b.append((P, "Kapsamı **varyant/layout**tır, seçili parça değil. Her mantıksal pozisyon için durumuna göre operasyon türetilir ve tek bir batch içinde gönderilir."))
    b.append((T, ["Pozisyon durumu", "Türetilen operasyon"], [
        ["OCCUPIED + FULL", "REMOVE_FULL"],
        ["OCCUPIED + EMPTY", "REMOVE_EMPTY (yalnız burada, sunucu tarafından üretilir)"],
        ["VACANT + UNKNOWN", "Atlanır (null döner)"],
        ["UNKNOWN", "Tüm batch reddedilir — `REMOVE_ALL_POSITION_UNKNOWN`"],
        ["Çelişkili durum", "Tüm batch reddedilir — `REMOVE_ALL_POSITION_CONFLICT`"],
    ]))
    b.append((N, "FACT", "CODE-CONFIRMED — `src/TkgPda.Application/ContainerActions.cs`, `ContainerActionPolicy.ResolveRemoveAll`."))
    b.append((H, 2, "Envanter gerçeğinin sahipliği"))
    b.append((P, "RDS telemetrisi TKG envanterini güncellemez. RDS bir WorkSite için `filled` bilgisi döndürse bile bu bilgi `ops.slot_runtime_state` üzerine yazılmaz. Bunun nedeni, RDS'in kendi WorkSite modelinin TKG'nin iş modeliyle birebir örtüşmemesi ve robotun görmediği manuel operatör müdahalelerinin TKG tarafında bilinmesidir. RDS verisi yalnız yürütme hazırlığı (readiness) değerlendirmesinde kullanılır."))
    return b


def section_lifecycle():
    b = []
    b.append((H, 1, "Transport Request Yaşam Döngüsü"))
    b.append((N, "WARN", "Yaygın bir yanılgı: `WAITING_PREPARATION` ve `READY` birer *durum* değildir. Bunlar ayrı ve dik (orthogonal) bir eksendir. CODE-CONFIRMED: `src/TkgPda.Domain/WorkflowEnums.cs`."))
    b.append((H, 2, "İki eksen"))
    b.append((T, ["Eksen", "Tip", "Değerler"], [
        ["Durum", "TransportRequestStatus", "Created, Validating, Queued, SentToRds, Running, Completed, Failed, Cancelled"],
        ["Hazırlık", "TransportReadinessState", "NotRequired, WaitingPreparation, Ready"],
    ]))
    b.append((H, 2, "Durum geçiş diyagramı"))
    b.append((PRE, """
  Created ──► Validating ──► Queued ──► SentToRds ──► Running ──► Completed
     │            │            │            │            │
     │            ├──► Failed  │            ├──► Failed  ├──► Failed
     │            │            │            │            │
     └──► Cancelled ◄─────────-┴────────────┴────────────┘

  Terminal durumlar: Completed, Failed, Cancelled  (çıkış geçişi yoktur)
""".strip("\n")))
    b.append((P, "Geçişler `TransportStateMachine.LegalTransitions` sözlüğünde tanımlıdır ve `EnsureTransition` dışında hiçbir yol durum değiştiremez; illegal geçiş `ILLEGAL_STATUS_TRANSITION` ile reddedilir. CODE-CONFIRMED: `src/TkgPda.Domain/TransportStateMachine.cs`."))
    b.append((H, 2, "Durumların anlamı"))
    b.append((T, ["Durum", "Nasıl girilir", "Kim değiştirir", "Etki"], [
        ["Created", "Operatör talebi oluşturur", "Workflow store", "Rezervasyon aktif; slot meşgul sayılır"],
        ["Validating", "İş akışı doğrulama başlar", "Workflow store", "Rezervasyon aktif"],
        ["Queued", "Doğrulama başarılı; dispatch bekliyor", "Workflow store", "Worker'ın topladığı havuz"],
        ["SentToRds", "Native order RDS'e gönderildi", "RdsExecutionCoordinator", "integration.rds_tasks kaydı oluşur"],
        ["Running", "RDS siparişi çalışıyor", "Reconciliation / callback", "Robot fiziksel hareket halinde"],
        ["Completed", "RDS FINISHED bildirdi", "Reconciliation / callback", "Slot durumları uygulanır"],
        ["Failed", "Doğrulama veya yürütme başarısız", "Workflow / reconciliation", "Slot durumu korunur veya geri alınır"],
        ["Cancelled", "Operatör iptal etti", "CancelAsync", "Yürütme başladıysa /terminate ile durdurulur"],
    ]))
    b.append((H, 2, "İptal kuralları"))
    b.append((P, "`CanCancelBeforeExecution` yalnız Created, Validating ve Queued için true döner. Bu, RDS'e hiç gidilmemiş talepler için yerel iptaldir. SentToRds ve Running durumundaki talepler de iptal edilebilir, ancak bu yol native `/terminate` çağrısı gerektirir ve `CancelAsync` içinde ayrı ele alınır."))
    b.append((N, "NOTE", "Operatör kuyruğu (`/transport-requests/queue`) `CanCancel` alanını `CanCancelBeforeExecution` üzerinden hesaplar. Bu, koordinatörün gerçek yeteneğinden daha dardır: RUNNING bir talep kuyrukta iptal edilemez görünür ama `/operational-context` üzerinden iptal edilebilir. Bu tutarsızlık bilinçli değil, tespit edilmiş bir farktır."))
    b.append((H, 2, "Idempotency"))
    b.append((P, "Her taşıma talebi bir `Idempotency-Key` başlığı ile oluşturulur. Aynı anahtarla yapılan tekrar istekleri yeni kayıt üretmez; mevcut kaydı döndürür ve `IdempotentReplay` bayrağını set eder. Bu, ağ belirsizliğinde (timeout, bağlantı kopması) çift fiziksel hareketi engelleyen birincil mekanizmadır. Veritabanında `uq_transport_idempotency` benzersiz indeksi ile zorlanır. DATABASE-CONFIRMED."))
    return b
