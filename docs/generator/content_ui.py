"""Client applications, tests, tooling and commissioning sections."""

H, P, UL, OL, T, C, PRE, N, PB = "h", "p", "ul", "ol", "table", "code", "pre", "note", "pagebreak"


def section_pda():
    b = []
    b.append((H, 1, "Operator PDA (Flutter)"))
    b.append((P, "Operatör uygulaması el terminalinde çalışır ve tek bir kurala göre tasarlanmıştır: **operatör fiziksel yönlendirme kararı vermez**. Ham RDS tanımlayıcıları (task id, WorkSite, AGV id, uuid) bu yüzeye hiç serialize edilmez."))
    b.append((H, 2, "Katmanlar"))
    b.append((T, ["Katman", "Dizin", "Sorumluluk"], [
        ["Uygulama kabuğu", "lib/app", "AppController (durum), router, tema"],
        ["Çekirdek", "lib/core", "Ağ, kimlik, yapılandırma, platform, depolama"],
        ["Veri", "lib/data", "API modelleri ve repository"],
        ["Özellikler", "lib/features", "Ekranlar ve iş yüzeyleri"],
        ["Paylaşılan", "lib/shared", "Sunum yardımcıları, geri bildirim bantları"],
    ]))
    b.append((H, 2, "AppController"))
    b.append((P, "1790 satırlık tek bir `ChangeNotifier`; uygulamanın tüm durumunu tutar. Bu bilinçli bir seçimdir: operatör akışı doğrusaldır ve durumun tek bir yerde olması, iki ekranın çelişkili durum göstermesini imkânsız kılar."))
    b.append((P, "Aşama makinesi `AppStage` enum'u ile ifade edilir. Başlıca metotlar:"))
    b.append((T, ["Metot", "Ne yapar"], [
        ["initialize", "Cihaz kodunu yükler, oturumu geri yükler, başlangıç aşamasını belirler"],
        ["login / logout / handleUnauthorized", "Kimlik yaşam döngüsü; 401 alındığında oturum belleği temizlenir"],
        ["selectMachine / leaveMachine / refreshContext", "Makine bağlamı ve operasyonel bağlam yenileme"],
        ["switchVariant", "Aktif varyantı değiştirir — aktif rezervasyon varsa sunucu reddeder"],
        ["selectMainPartOption / selectEffectivePart / selectCompatibleVariant", "Parça-öncelikli seçim akışı"],
        ["enterBatchSelection / addPartToBatch / beginBatchConfiguration / submitBatch", "Çok parçalı toplu gönderim akışı"],
        ["submitContainerAction", "WP-33 doğrudan kasa aksiyonu"],
        ["removeAllContainers", "Varyant kapsamlı toplu kaldırma"],
        ["enterWarehousePreparation / markPreparationReady", "Depo hazırlık akışı"],
        ["retryRobotAlarmSummary", "Robot alarm özetini yeniden okur"],
    ]))
    b.append((H, 2, "Ekranlar"))
    b.append((T, ["Ekran", "Dosya", "Amaç"], [
        ["Cihaz kurulumu", "features/device_setup/device_setup_screen.dart", "Cihaz kodunu bir kez girer ve güvenli depolamaya yazar"],
        ["Giriş", "features/login/login_screen.dart", "Sicil numarası (veya legacy kullanıcı adı) ile giriş"],
        ["Makine listesi", "features/machines/machine_list_screen.dart", "Yetkili makineler"],
        ["Operasyon", "features/operation/operation_screen.dart", "Ana iş ekranı"],
        ["Parça-öncelikli", "features/operation/part_first_screen.dart", "Parçadan varyanta çözümleme"],
        ["Çoklu parça batch", "features/operation/multi_part_batch_screen.dart", "Sepet ve toplu gönderim"],
        ["Kasa paneli", "features/operation/container_position_panel.dart", "WP-33 kasa aksiyonları"],
        ["Taşıma paneli", "features/operation/transport_action_panel.dart", "Taşıma aksiyonları"],
        ["Operatör kuyruğu", "features/tasks/operator_queue_screen.dart", "Devam eden talepler ve iptal"],
        ["Depo hazırlık", "features/warehouse/warehouse_preparation_screen.dart", "READY işaretleme"],
    ]))
    b.append((H, 2, "WP-33 kasa paneli davranışı"))
    b.append((P, "`ContainerPositionPanel` yalnız backend'in mantıksal pozisyon projeksiyonunu tüketir ve **hiçbir zaman fiziksel slot tanımlayıcısı almaz**. Her pozisyon kartı iki buton taşır:"))
    b.append((T, ["Buton", "Widget anahtarı", "Etiket"], [
        ["Kasa Doldu", "direct-action-REMOVE_FULL-{positionNumber}", "{n}. Kasa Doldu"],
        ["Kasa Boş", "direct-action-BRING_EMPTY-{positionNumber}", "{n}. Kasa Boş"],
    ]))
    b.append((N, "NOTE", "Anahtar, WP-33 altında MANTIKSAL iş aksiyonunu adlandırır: (makine, varyant/layout, parça, mantıksal kasa numarası, aksiyon). Kasa numarasının anahtarın parçası olmasının sebebi budur; aynı ekranda birden çok kasa aynı aksiyonu sunabilir."))
    b.append((P, "Buton etkinliği üç koşulun birleşimidir: `availability.enabled` (sunucu projeksiyonu), `controller.mutationGate.canMutate` (global mutasyon kapısı) ve `!submitting`. Devre dışıysa sebep hem tooltip'te hem de butonun altında görünür metin olarak gösterilir."))
    b.append((N, "WARN", "Sebep yalnız tooltip'te gösterilmez. Bir el terminalinde hover yoktur; tooltip tek başına erişilebilir bir açıklama değildir. Bu yüzden `disabledReason` ayrıca butonun içinde küçük punto ile basılır."))
    b.append((H, 2, "Durum rozeti ve animasyon"))
    b.append((P, "`_StateBadge` taşıma durumunu Türkçe etiketle gösterir ve yalnız `REQUESTED`, `QUEUED`, `IN_PROGRESS` durumlarında nabız animasyonu çalıştırır. `MediaQuery.disableAnimations` etkinse animasyon hiç kurulmaz; `Semantics` etiketi her iki durumda da aynı bilgiyi taşır."))
    b.append((T, ["Durum", "Türkçe etiket"], [
        ["READY", "Hazır"],
        ["WAITING_PREPARATION", "Hazırlık Bekleniyor"],
        ["REQUESTED", "İstendi"],
        ["QUEUED", "Sırada"],
        ["ROBOT_ASSIGNED", "Robot atandı"],
        ["IN_PROGRESS", "Devam ediyor"],
        ["COMPLETED", "Tamamlandı"],
        ["ERROR", "Hata"],
        ["(diğer)", "Güncelleniyor"],
    ]))
    b.append((N, "NOTE", "Bu etiketler operatöre gösterilen **sunum durumlarıdır**, `TransportRequestStatus` enum'u değil. İkisi kasıtlı olarak ayrıdır: sunum katmanı hazırlık eksenini ve robot atamasını tek bir rozette birleştirir."))
    b.append((H, 2, "SubmissionCoordinator — istemci tarafı idempotency"))
    b.append((P, "Bu küçük sınıf, çift gönderimi engelleyen istemci tarafı mekanizmadır ve mantığı incelikli olduğu için ayrıca açıklanmayı hak eder."))
    b.append((C, """String keyFor(TransportCommand command) {
  if (_materialSignature != command.materialSignature) {
    _materialSignature = command.materialSignature;
    _idempotencyKey = _keyFactory();
    _ambiguous = false;
  }
  return _idempotencyKey!;
}""", "dart"))
    b.append((UL, [
        "Idempotency anahtarı komutun **maddi imzasına** bağlıdır. Operatör aynı işi tekrar denerse aynı anahtar kullanılır; farklı bir iş seçerse yeni anahtar üretilir.",
        "`_inFlight` aynı anda birden fazla gönderimi engeller ve `StateError` fırlatır.",
        "`AmbiguousMutationException` alındığında anahtar **temizlenmez**: sonuç bilinmiyorken yeni anahtar üretmek çift fiziksel hareket riski doğururdu. `hasAmbiguousRequest` true olur ve kullanıcı yenileme ile durumu netleştirir (`resolveByRefresh`).",
    ]))
    b.append((N, "WARN", "Belirsiz mutasyon (ağ zaman aşımı gibi) sonrası otomatik yeniden gönderim YOKTUR. Bu bilinçlidir: bir kasa taşıma emri belirsiz durumdayken körlemesine tekrarlanamaz."))
    b.append((H, 2, "Güvenli depolama ve platform ayrımı"))
    b.append((P, "Cihaz kodu depolaması platforma göre koşullu import ile seçilir: `device_code_store_secure.dart` (mobil, secure storage), `device_code_store_web.dart` (web) ve bir stub. Aynı desen `platform_info` ve `runtime_config_loader` için de kullanılır. Bu, Flutter'ın koşullu import mekanizmasının standart uygulamasıdır ve web derlemesinin mobil-özel paketleri çekmesini önler."))
    b.append((H, 2, "Oturum belleği"))
    b.append((P, "`session_memory.dart` JWT'yi yalnız **bellekte** tutar; diske yazmaz. Uygulama kapandığında token kaybolur ve yeniden giriş gerekir. Cihaz kodu ise kalıcıdır — çünkü o bir sır değil, cihaz kimliğidir."))
    return b


def section_adminweb():
    b = []
    b.append((H, 1, "Admin Web (React)"))
    b.append((P, "Admin Web, konfigürasyonu ve teknik teşhisi yöneten React + TypeScript + Vite uygulamasıdır. Operatör iş akışı içermez."))
    b.append((H, 2, "Yapı"))
    b.append((T, ["Dizin", "İçerik"], [
        ["src/api", "Tip güvenli API istemcisi (`client.ts`), tipler (`types.ts`), context sağlayıcı"],
        ["src/auth", "AuthContext — token yalnız bellekte"],
        ["src/components", "AppShell, Toast, paylaşılan UI ilkelleri"],
        ["src/pages", "Sayfa bileşenleri"],
        ["src/test", "Vitest kurulum, render yardımcıları, fixture'lar"],
        ["e2e", "Playwright uçtan uca senaryoları"],
    ]))
    b.append((H, 2, "Sayfalar"))
    b.append((T, ["Sayfa", "Amaç"], [
        ["LoginPage", "Kullanıcı adı + parola ile ADMIN girişi"],
        ["DashboardPage", "Sistem durumu özeti"],
        ["MachinesPage", "Makine ana verisi"],
        ["CatalogPage", "Varyant, parça, ürün grubu kataloğu"],
        ["ContainersPage", "Kasa tipleri"],
        ["LayoutsPage", "Layout listesi ve sürümleme"],
        ["LayoutEditorPage", "Slot düzenleyici; slot ↔ parça eşlemesi, RDS eşlemeleri"],
        ["UsersPage", "Kullanıcı, rol ve makine yetkileri"],
        ["DevicesPage", "İstemci cihazları"],
        ["RdsIntegrationPage", "RDS entegrasyon durumu, robot telemetrisi, uzman operasyonlar"],
        ["AuditPage", "Denetim kayıtları"],
        ["DataQualityPage", "Veri kalitesi ve köken raporları"],
        ["NotFoundPage", "404"],
    ]))
    b.append((H, 2, "Durum yönetimi"))
    b.append((P, "Sunucu durumu TanStack Query ile yönetilir; yerel bileşen durumu React hook'ları ile. Yazma işlemleri `useMutation` ile yapılır ve başarıda ilgili sorgular geçersizleştirilir. Global bir Redux benzeri store yoktur."))
    b.append((H, 2, "Kimlik"))
    b.append((N, "WARN", "Token **yalnız bellekte** tutulur; `localStorage` veya `sessionStorage` kullanılmaz. Pratik sonucu: tam sayfa yeniden yüklemesi (`page.goto`, F5) oturumu düşürür. Bu bilinçli bir güvenlik kararıdır, bir hata değil — E2E testleri bu davranışa göre yazılmıştır."))
    b.append((H, 2, "RdsIntegrationPage"))
    b.append((P, "Bu sayfa operatörden gizlenen teknik gerçeği gösterir: robot telemetrisi, alarm listeleri (severity ile), kilit durumu, RDSCore uç sağlığı ve uzman operasyonlar. Uzman operasyonlar global mutasyon kapısına tabidir ve kapı kapalıyken sunucu `RDS_DISPATCH_DISABLED` döner — arayüz bu kodu kullanıcıya anlaşılır bir mesaj olarak gösterir."))
    b.append((N, "NOTE", "Arayüzde bir butonun görünür olması hareketin izinli olduğu anlamına gelmez. Yetki kararı daima sunucudadır; istemci yalnız sonucu gösterir."))
    b.append((H, 2, "LayoutEditorPage"))
    b.append((P, "Projedeki en yoğun tek sayfa (966 satır). Sekiz farklı API ailesini birlikte kullanır: `layouts`, `slots`, `machines`, `containers`, `parts`, `rdsNavigationProfiles`, `rdsSlotMappings`, `rdsWorkSites`. Slot geometrisi (ui_x/ui_y/ui_width/ui_height) düzenlenebilir ve slot ↔ parça ilişkisi burada kurulur."))
    return b


def section_tests():
    b = []
    b.append((H, 1, "Test Mimarisi"))
    b.append((P, "Testler dört ayrı yığında çalışır. Her yığın farklı bir soruyu cevaplar ve birbirinin yerine geçmez."))
    b.append((T, ["Yığın", "Konum", "Dosya", "Cevapladığı soru"], [
        ["xUnit — Domain", "tests/TkgPda.Domain.Tests", "9", "İş kuralları doğru mu?"],
        ["xUnit — Application", "tests/TkgPda.Application.Tests", "16", "Orkestrasyon ve RDS politikaları doğru mu?"],
        ["xUnit — Auth", "tests/TkgPda.Auth.Tests", "7", "Kimlik ve yetki doğru mu?"],
        ["xUnit — Api", "tests/TkgPda.Api.Tests", "32", "HTTP sözleşmesi ve adaptörler doğru mu?"],
        ["xUnit — Postgres", "tests/TkgPda.Infrastructure.Postgres.Tests", "15", "SQL ve işlem sınırları doğru mu?"],
        ["flutter_test", "src/TkgPda.Pda/test", "12", "Operatör yüzeyi doğru mu?"],
        ["Flutter integration_test", "src/TkgPda.Pda/integration_test", "1", "Uçtan uca operatör akışı çalışıyor mu?"],
        ["Vitest", "src/TkgPda.AdminWeb/src/**/*.test.tsx", "6", "Admin bileşenleri doğru mu?"],
        ["Playwright", "src/TkgPda.AdminWeb/e2e", "—", "Gerçek tarayıcıda admin akışı çalışıyor mu?"],
    ]))
    b.append((H, 2, "PostgreSQL testleri gerçek veritabanı ister"))
    b.append((P, "`TkgPda.Infrastructure.Postgres.Tests` ve API entegrasyon testleri sahte (in-memory) bir veritabanı kullanmaz. Gerekli ortam değişkenleri:"))
    b.append((T, ["Değişken", "Amaç"], [
        ["TKG_PDA_API_TEST_CONNECTION_STRING", "Test veritabanı bağlantısı — `<DB_CONNECTION>`"],
        ["ConnectionStrings__TkgPda", "Barındırılan `Program` aynı bağlantıyı ayrıca ister"],
        ["Authentication__Jwt__SigningKey", "Barındırılan host başlatma doğrulaması — `<JWT_SIGNING_KEY>`"],
    ]))
    b.append((N, "WARN", "Yalnız `TKG_PDA_API_TEST_CONNECTION_STRING` verilerek koşulan API entegrasyon paketi, barındırılan host'un başlatma doğrulamasına takılır. Bu bir ürün hatası değil, bir ortam yapılandırma hatasıdır; belirti yanıltıcı olduğu için burada kayda geçirilmiştir."))
    b.append((N, "WARN", "Kirlenmiş bir test veritabanı da yanlış başarısızlık üretir: yarıda kesilen bir koşu `finally` temizliğini çalıştıramadan ölürse artık kayıtlar sonraki koşuyu düşürür. Doğru çözüm testi gevşetmek değil, gerçekten temiz bir veritabanı sağlamaktır."))
    b.append((H, 2, "Testlerin kuralı zayıflatmaması ilkesi"))
    b.append((P, "Bu projede birkaç test, ürün davranışını değil bir **hatayı** kodluyordu (örneğin REMOVE_EMPTY'nin operatöre açık olduğunu varsayan bir test). Bunlar gerçek sözleşmeye göre yeniden yazıldı; kural gevşetilmedi. Bir testin geçmesi için `find.byKey(...)` çağrısını `.first` veya `findsWidgets` ile değiştirmek bu projede kabul edilebilir bir düzeltme değildir."))
    b.append((H, 2, "E2E kontrol sayacı"))
    b.append((P, "Operatör E2E senaryosu, çalıştırdığı doğrulama sayısını sayar ve beklenen sayıdan sapma testi düşürür. Bu sayaç bilinçli olarak \"kırılgan\"dır: atlanan bir doğrulamanın sessizce geçmesini engellemek için vardır."))
    b.append((H, 2, "Bilinen test kırılganlığı"))
    b.append((N, "WARN", "`PostgresLayoutCloningTests` içinde `SELECT id FROM auth.users LIMIT 1` sonucu koruma olmadan `Guid`'e çevrilir. Kullanıcısı olmayan bir veritabanında bu `NullReferenceException` üretir; kullanıcısı olan bir veritabanında test geçer. Bu bir **test kırılganlığıdır**, ürün hatası değil — klonlama kodu ayrıca doğrulanmıştır. Bu doküman salt-okunur olarak üretildiği için düzeltilmemiştir."))
    return b


def section_tooling():
    b = []
    b.append((H, 1, "Betikler, Araçlar ve Yerel Yığın"))
    b.append((H, 2, "tools/dev — yerel geliştirme yığını"))
    b.append((T, ["Betik", "Ne yapar"], [
        ["start_local_stack.ps1", "PostgreSQL'i hazırlar, migration'ları ve seed'leri uygular, durum dosyasını yazar"],
        ["stop_local_stack.ps1", "Yerel yığını durdurur"],
        ["reset_local_database.ps1", "Veritabanını sıfırdan kurar"],
    ]))
    b.append((P, "Yığın durumu `%LOCALAPPDATA%\\TKG_PDA_DEV\\state\\stack_state.json` altında tutulur; bağlantı dizesi buradan çözülür ve depoya hiç yazılmaz."))
    b.append((H, 2, "scripts/run_commissioning.ps1"))
    b.append((P, "Devreye alma için API host'unu **dispatch açık** olarak başlatan tek betik. Normal geliştirme bu betiği çalıştırmaz."))
    b.append((C, """Rds__Enabled          = true
Rds__DispatchEnabled  = true""", "text"))
    b.append((T, ["Parametre", "Kaynak / varsayılan"], [
        ["-IntegrationKey", "$env:Rds__IntegrationKey — `<INTEGRATION_KEY>`"],
        ["-CallbackKey", "$env:Rds__CallbackKey — `<CALLBACK_KEY>`"],
        ["-CallbackUrl", "$env:Rds__CallbackUrl"],
        ["-ConnectionString", "$env:ConnectionStrings__TkgPda, yoksa yerel yığın durum dosyasından — `<DB_CONNECTION>`"],
        ["-JwtSigningKey", "$env:Authentication__Jwt__SigningKey, yoksa 48 baytlık kriptografik rastgele üretilir"],
        ["-RdsBaseUrl", "$env:Rds__BaseUrl, yoksa http://127.0.0.1:8080"],
        ["-RdsCoreBaseUrl", "$env:RdsCore__BaseUrl, yoksa http://127.0.0.1:8088"],
        ["-ApiPort", "5000"],
        ["-HostAddress", "0.0.0.0"],
    ]))
    b.append((UL, [
        "Sırları asla sabit kodlamaz; eksik zorunlu yapılandırmada hızlıca başarısız olur.",
        "Sırsız, güvenli bir başlangıç özeti basar.",
        "Üretimle **aynı** worker, aynı `RdsMutationPolicy` ve aynı native order yolunu kullanır — devreye alma için ayrı bir kod yolu yoktur.",
        "`dotnet run --no-launch-profile` ile çalışır, çünkü `launchSettings.json` git tarafından yok sayılır ve paylaşılan bir profil olamaz.",
    ]))
    b.append((N, "WARN", "Bu betik gerçek robotu hareket ettirebilir. Yalnız yetkili bir devreye alma oturumunda, sahada güvenlik önlemleri alınmışken çalıştırılmalıdır. COMMISSIONING VALIDATION REQUIRED."))
    b.append((N, "NOTE", "Betikte bulunan geliştirme anahtarları **yer tutucu** niteliğindedir ve üretim sırrı değildir. Üretimde her anahtar ortam değişkeninden verilmelidir."))
    b.append((H, 2, "tools/phase_02a, tools/phase_02b"))
    b.append((P, "Müşteri kaynak verisinden (Excel) `data/generated/` ve `db/seeds/generated/` çıktılarının üretildiği dönüştürme araçları. Çıktıları üretilmiş kabul edilir ve elle düzenlenmez; kaynak veri değişirse yeniden üretilir."))
    b.append((N, "WARN", "Bu araçların ürettiği eşlemeler **deneme/simülasyon eşlemeleridir**. Üretim WorkSite etiketleri, çatal yükseklikleri, geometri ve LOAD/UNLOAD değerleri sahada doğrulanmadan üretim eşlemesi olarak sunulamaz."))
    return b


def section_commissioning():
    b = []
    b.append((H, 1, "Devreye Alma (Commissioning) Rehberi"))
    b.append((N, "WARN", "Bu bölümdeki her adım fiziksel sonuç doğurabilir. Tümü COMMISSIONING VALIDATION REQUIRED olarak işaretlidir: kaynak koddan doğrulanamazlar, yalnız sahada doğrulanabilirler."))
    b.append((H, 2, "Ön koşullar"))
    b.append((OL, [
        "PostgreSQL erişilebilir ve tüm migration'lar uygulanmış.",
        "Saha verisi (makineler, varyantlar, parçalar, layout'lar) yüklenmiş ve doğrulanmış.",
        "NavigationProfile kayıtları gerçek robot haritası adı ve gerçek araç kimliği ile yayınlanmış.",
        "`master.layout_slot_parts` eşlemesi saha ile doğrulanmış.",
        "`integration.layout_slot_rds_mapping` gerçek WorkSite etiketleri ile doldurulmuş.",
        "RDSCore erişilebilir; `/robotsStatus` gerçek robotları döndürüyor.",
        "Saha güvenlik önlemleri alınmış; robot çalışma alanında personel yok.",
    ]))
    b.append((H, 2, "Aşamalı açılış"))
    b.append((T, ["Aşama", "Rds:Enabled", "Rds:DispatchEnabled", "Doğrulanan"], [
        ["1 — Kapalı", "false", "false", "TKG tek başına çalışıyor; talepler oluşuyor ve kuyrukta kalıyor"],
        ["2 — Salt okuma", "true", "false", "Telemetri okunuyor, alarmlar görünüyor, WorkSite projeksiyonu tazeleniyor; hiçbir robot hareket etmiyor"],
        ["3 — Dispatch", "true", "true", "Fiziksel hareket; her talep tek tek ve gözetim altında"],
    ]))
    b.append((P, "Aşama 2 atlanmamalıdır. Kimlik çözümleme, alarm ayrıştırma ve WorkSite eşlemesindeki hataların tamamı bu aşamada hareket olmadan görülebilir."))
    b.append((H, 2, "Aşama 2'de doğrulanacaklar"))
    b.append((T, ["Kontrol", "Nasıl doğrulanır", "Başarısızlık işareti"], [
        ["Robot kimliği", "Admin Web RdsIntegrationPage robot listesi", "uuid ve vehicle_id boş veya beklenenden farklı"],
        ["Alias belirsizliği", "Yapılandırılan araç kimliğini telemetri ile karşılaştır", "ROBOT_ALIAS_AMBIGUOUS"],
        ["Alarm ayrıştırma", "Robotta bilerek bir uyarı oluştur, listede görünmeli", "Alarm listesi daima boş"],
        ["Harita adı", "NavigationProfile RobotMapName ile robotun CurrentMap değerini karşılaştır", "REQUIRED_MAP_UNRESOLVED veya sürekli harita hazırlığı"],
        ["WorkSite eşlemesi", "Slot ↔ WorkSite eşlemesini fiziksel konumla karşılaştır", "Yanlış rafa gitme riski — dispatch açılmadan düzeltilmeli"],
    ]))
    b.append((H, 2, "Aşama 3'e geçiş"))
    b.append((P, "`scripts/run_commissioning.ps1` ile başlatın. Başlangıç günlüğündeki şu satır kapının durumunu teyit eder:"))
    b.append((C, "RDS Integration: Enabled, RDS Dispatch: Enabled, RDSCore: http://<host>:8088", "text"))
    b.append((N, "WARN", "Bu satırda `RDS Dispatch: Enabled` görülüyorsa sistem gerçek robot hareketi başlatabilir. Sahada bu satır görülmeden fiziksel test başlatılmamalı; görüldükten sonra ise alan boşaltılmadan talep gönderilmemelidir."))
    b.append((H, 2, "Doğrulanması gereken saha değerleri"))
    b.append((T, ["Değer", "Nerede", "Neden uydurulamaz"], [
        ["WorkSite etiketleri", "integration.layout_slot_rds_mapping", "Yanlış etiket robotu yanlış rafa gönderir"],
        ["Çatal yükseklikleri", "RDS görev şablonları", "Yanlış yükseklik fiziksel çarpma riski"],
        ["Geometri / park noktaları", "RDSCore /getRobotParkPoints", "Sahaya özgü koordinatlar"],
        ["LOAD/UNLOAD değerleri", "Rds:EmptyContentValue / FullContentValue", "Yanlış içerik etiketi envanteri bozar"],
        ["Robot eşlemesi", "NavigationProfile.Vehicle", "Yanlış robot yanlış hattı çalıştırır"],
        ["Tanıma parametreleri", "RDSCore çekirdek parametreleri", "Sahaya özgü kalibrasyon"],
    ]))
    b.append((N, "WARN", "Depoda bulunan hiçbir örnek değer üretim gerçeği değildir ve simülatör deneme eşlemesi nihai üretim eşlemesi olarak sunulamaz."))
    return b
