"""Section 27: one subsection for every DOCUMENTED file.

Significant files carry authored analysis (DEEP). Every other file still gets its
own subsection built from the structural facts actually read off disk, plus a
role description derived from its place in the tree. Depth is graded honestly so
a reader can tell authored analysis from generated structure.
"""

import io
import json
import os

H, P, UL, OL, T, C, PRE, N, PB = "h", "p", "ul", "ol", "table", "code", "pre", "note", "pagebreak"

BUILD = os.path.dirname(os.path.abspath(__file__))

DEPTH_DEEP = "Derin analiz"
DEPTH_STRUCT = "Yapısal"
DEPTH_BRIEF = "Kısa"


# --------------------------------------------------------------------------
# Authored analysis for the files that carry real application logic.
# Keys: purpose, resp, methods (list of [name, description]), deps, callers,
# state, rules, errors, security, tests.
# --------------------------------------------------------------------------
DEEP = {
    "src/TkgPda.Application/RdsExecution.cs": {
        "purpose": "Projenin yürütme kalbi. Kuyruktaki taşıma taleplerini fiziksel RDS siparişlerine çeviren, sonuçlarını uzlaştıran, iptal eden ve uzman (expert) mutasyonlarını yürüten koordinatör burada yaşar. 2069 satırla en büyük tek üretim dosyasıdır ve büyüklüğü kasıtlıdır: fiziksel hareketi yetkilendiren her karar tek bir dosyada, birlikte okunabilir halde durur.",
        "resp": [
            "Global fiziksel mutasyon kapısını (`IsDispatchEnabled`) tek noktada tanımlamak.",
            "Robot kimliğini telemetriden çözmek ve robot kirasını yönetmek.",
            "Native order dispatch akışının tamamını yürütmek.",
            "Harita hazırlığı ve kilit kurtarma kararlarını vermek.",
            "RDS sonuçlarını TKG durum makinesine uzlaştırmak.",
            "Uzman mutasyonlarını (manualFinished, redoFailedOrder, setPriority, robot bakımı) politikaya tabi kılmak.",
        ],
        "methods": [
            ["DispatchQueuedAsync", "Worker girişi. Kapı kapalıysa boş liste döner ve hiçbir kira alınmaz. `ListQueuedRequestIdsAsync` ile en fazla `Clamp(limit,1,100)` talep alır, her biri için yeni bir `correlationId` üretir ve `DispatchOneAsync` çağırır. Üç istisna türünü ayrı ayrı yakalar: `RdsExecutionException` → RDS_DISPATCH_BLOCKED (karar), `RdsReadException` ve `RdsCoreNativeOrderException` → RDS_DISPATCH_DEFERRED (bilgi eksikliği). Bu ayrım olmasaydı geçici bir ağ kesintisi talepleri kalıcı olarak başarısız yapardı."],
            ["DispatchOneAsync", "Tek talep dispatch'i. Public ve doğrudan çağrılabilir olduğu için global kapıyı savunma derinliği olarak yeniden kontrol eder; kapalıysa denetim kaydı yazıp döner, kira almaz, RDS'e dokunmaz. Talep kirasını (`tkg:rds:{id}`) alır ve motor seçimine göre native ya da WindTask yoluna dallanır."],
            ["DispatchNativeOrderAsync", "Native order akışının tamamı. Navigation profile → gerekli harita → araç → robot kira anahtarı → robot kirası → fleet telemetrisi → çekirdek alarm kapısı → tekil robot çözümü → TOCTOU doğrulaması → bağlantı kontrolü → kilit kurtarma → dispatch edilebilirlik → harita hazırlığı → `/setOrder`. Her adımın kendi hata kodu vardır ve fiziksel çağrı en sondadır."],
            ["ReconcileAsync", "Devam eden görevleri RDS gerçeğiyle uzlaştırır. Salt-okunur olduğu için global kapıdan etkilenmez; kapı kapalıyken bile daha önce gönderilmiş bir siparişin sonucu öğrenilmelidir."],
            ["ApplyTaskStatusAsync", "RDS görev durumunu TKG durumuna çevirir. Her geçiş `TransportStateMachine.EnsureTransition` üzerinden yapılır, dolayısıyla RDS'ten gelen beklenmedik bir durum illegal bir geçiş üretemez."],
            ["ProcessCallbackAsync", "RDS callback'ini işler. Uzlaştırmanın yerine geçmez, onu hızlandırır: callback hiç gelmese bile periyodik uzlaştırma aynı sonuca varır."],
            ["CancelAsync", "İptal. Created/Validating/Queued için yereldir ve RDS'e hiç gidilmez. SentToRds/Running için `/terminate` çağrılır — bu SafetyStop sınıfındadır, dolayısıyla global kapı kapalıyken bile izinlidir."],
            ["ResolveRobotLeaseKeyAsync", "Telemetriden robot kira anahtarını çözer. `RdsRobotResolver.Resolve` sonucu Unique değilse null döner; belirsiz bir alias sessizce bir makine seçmez."],
            ["WithRobotLeaseAsync", "Uzman mutasyonlarını robot kirası altında çalıştıran sarmalayıcı. Kira anahtarı çözülemezse EXPERT_CURRENT_STATE_UNAVAILABLE, kira alınamazsa ROBOT_BUSY fırlatır. Tutulan anahtar iş fonksiyonuna verilir ki mutasyona uğrattığı robotun kiranın kapsadığı robot olduğunu kanıtlayabilsin."],
            ["EnsureLeaseCoversTarget", "TOCTOU değişmezi. Tutulan kira anahtarı ile yetkili yeniden okumadan gelen hedefin kira anahtarı eşleşmezse EXPERT_ROBOT_CHANGED_DURING_VALIDATION fırlatır. Kira ortada değiştirilmez — bu, kapatılmak istenen pencerenin ta kendisini yeniden açardı."],
            ["EnsureMutationAllowed", "Dispatch yolu dışındaki tek mutasyon kapısı uygulama noktası. Admin/Supervisor olmak bu kontrolün yerine geçmez."],
            ["RobotMaintenanceAsync", "Filo kapsamlı bakım (clearRobotAllError, gotoSitePause/Resume, dispatchable). Açık ve boş olmayan bir araç listesi zorunludur; yayın (broadcast) semantiği yoktur. Supervisor için makine kapsamı burada uydurulmaz."],
            ["ManualFinishAsync", "`/manualFinished`. MotionEnabling sınıfındadır. Mevcut yürütmeye bağlanır, kira altında çalışır ve TOCTOU doğrulaması yapar."],
            ["RedoFailedAsync", "`/redoFailedOrder`. MotionEnabling. Aynı kira ve TOCTOU koruması altındadır."],
            ["SetRequestPriorityAsync", "`/setPriority`. ExecutionMutation sınıfındadır: sıralamayı değiştirmek işin dispatch edilme zamanını değiştirir."],
            ["ResolveExpertTargetAsync", "Talep kimliğinden uzman hedefini (talep + RDS görevi) çözer."],
            ["ResolveCurrentExecutionTargetAsync", "Uzman mutasyonunu *o an çalışan* yürütmeye bağlar. Bu, geçmiş bir görevin üzerine mutasyon uygulanmasını engeller."],
            ["ReadFleetForMaintenanceAsync", "Bakım için telemetri okur ve okuyamazsa fail-closed davranır."],
            ["SynchronizeLoadContentAsync", "Slot yük durumunu RDS içerik değeriyle senkronlar."],
            ["RefreshRelevantWorkSitesAsync", "Yalnız ilgili WorkSite'leri tazeler; her turda tüm filoyu taramaz."],
        ],
        "deps": "`IRdsExecutionStore` (Postgres), `IRdsCoreNativeOrderClient`, `IRdsExecutionClient`, `IRdsReadClient`, `RdsMutationPolicy`, `RdsRobotResolver`, `RdsRobotIdentity`, `TransportStateMachine`.",
        "callers": "Arka plan worker'ı (dispatch + reconcile), `AdminEndpoints` (uzman operasyonlar), `ApiEndpoints` (iptal), `RdsIntegrationEndpoints` (callback).",
        "state": "Kendi içinde durum tutmaz. Tüm kalıcı durum store üzerinden PostgreSQL'dedir; kiralar advisory lock olarak veritabanında yaşar.",
        "rules": [
            "Fiziksel mutasyon yalnız `Rds:DispatchEnabled=true` iken yapılır.",
            "Bir robot üzerinde aynı anda tek TKG operasyonu çalışır.",
            "Kira alındıktan sonra hedef robot değişirse operasyon reddedilir.",
            "Belirsiz alias asla bir makineye çözülmez.",
            "Çekirdek düzeyi bloklayan alarm dispatch'i durdurur.",
            "Denetim yazımı başarısız olduğu için fiziksel operasyon tekrar edilmez.",
        ],
        "errors": "RDS_DISPATCH_DISABLED, RDSCORE_HAS_BLOCKING_ALARM, ROBOT_OFFLINE, ROBOT_BUSY, ROBOT_NOT_DISPATCHABLE, ROBOT_ALIAS_AMBIGUOUS, ROBOT_IDENTITY_UNRESOLVED, ROBOT_CHANGED_DURING_VALIDATION, EXPERT_ROBOT_CHANGED_DURING_VALIDATION, EXPERT_CURRENT_STATE_UNAVAILABLE, MAP_SWITCH_LOCK_RECOVERY_REQUIRED, LOCK_RECOVERY_UNSAFE, NAVIGATION_PROFILE_UNRESOLVED, REQUIRED_MAP_UNRESOLVED, VEHICLE_UNRESOLVED, VEHICLES_REQUIRED.",
        "security": "Fiziksel güvenliğin merkezi. Rol tabanlı yetki burada mutasyon kapısının yerine geçmez; iki kontrol bağımsız ve birlikte uygulanır.",
        "tests": "`tests/TkgPda.Application.Tests` altındaki RDS yürütme testleri; kapı, kira, TOCTOU, alarm ve kilit kurtarma senaryoları ayrı ayrı kapsanır.",
    },
    "src/TkgPda.Application/RdsMutationPolicy.cs": {
        "purpose": "Bir RDS operasyonunun fiziksel işe göre güvenlik sınıfını tanımlar. Global dispatch kapısının ne anlama geldiğinin **tek** tanımıdır; hiçbir adaptör veya uç nokta ikinci, çelişkili bir fiziksel izin kavramı taşıyamaz.",
        "resp": [
            "Dört mutasyon sınıfını tanımlamak: ReadOnly, SafetyStop, MotionEnabling, ExecutionMutation.",
            "Kapı kararını tek bir saf fonksiyonda vermek.",
            "`/dispatchable` gibi gövdesine göre sınıf değiştiren operasyonları sınıflandırmak.",
            "Reddetme kodunu ve insan okunur mesajını sağlamak.",
        ],
        "methods": [
            ["IsAllowed", "ReadOnly ve SafetyStop daima izinli; diğer iki sınıf `globalDispatchEnabled` değerine bağlı. Saf fonksiyon, yan etkisiz, test edilmesi önemsiz."],
            ["ClassifyDispatchable", "`type` alanı `dispatchable` ise MotionEnabling, aksi halde SafetyStop. Robotu dispatch edilebilir yapmak hareketi mümkün kılar; dispatch dışına almak güvenliği artırır."],
            ["BlockedMessage", "Reddin *neden* verildiğini açıklayan mesajı üretir: operasyonun fiziksel işi başlatacağı/sürdüreceği/ilerleteceği belirtilir."],
        ],
        "deps": "Yok. Bilinçli olarak bağımlılıksızdır; hiçbir I/O yapmaz.",
        "callers": "`RdsExecutionCoordinator.EnsureMutationAllowed`, `AdminEndpoints` uzman operasyon yolları, RDS adaptörleri.",
        "state": "Durumsuz statik sınıf.",
        "rules": [
            "Hareketi azaltan operasyonlar kapı kapalıyken bile izinlidir.",
            "Hareketi başlatan, sürdüren, yeniden etkinleştiren veya ilerleten her şey kapıya tabidir.",
            "Rol yetkisi kapıyı ikame etmez.",
        ],
        "errors": "`BlockedCode = \"RDS_DISPATCH_DISABLED\"`.",
        "security": "Projenin en güvenlik-kritik tek dosyası. Buradaki bir sınıflandırma hatası doğrudan istenmeyen fiziksel harekete dönüşür.",
        "tests": "`tests/TkgPda.Application.Tests` — her sınıf için kapı açık/kapalı matrisi ve `/dispatchable` tip varyasyonları.",
    },
    "src/TkgPda.Application/RdsRobotIdentity.cs": {
        "purpose": "SEER'in iki kimlik uzayını (`uuid` ve `vehicle_id`) tek bir tipte modelleyerek, hangi uzayın hangi amaçla kullanılacağını yapısal olarak sabitler.",
        "resp": [
            "İki kimlik uzayını birlikte taşımak.",
            "Amaç bazlı türetilmiş kimlikleri sağlamak: MutationTarget, DiscoveryKey, RobotLeaseKey.",
            "İki uzayda birden eşleme yapmak (`Matches`).",
            "Telemetri elemanından kimlik okumayı tek noktada toplamak (`FromRobotElement`).",
        ],
        "methods": [
            ["MutationTarget", "VehicleId ?? Uuid. Vehicle-kapsamlı RDSCore API'leri `vehicle_id` üzerinde sözleşir; `uuid` yalnız onu atlayan payload'lar için yedektir."],
            ["DiscoveryKey", "Uuid ?? VehicleId. `/getRobotParkPoints` uuid anahtarlı istek alır, `/queryDist` uuid anahtarlı harita döner."],
            ["RobotLeaseKey", "VehicleId ?? Uuid. Bugün MutationTarget ile aynı çözülür ama ayrı bir kavramdır: *hangi kira bu robotu kapsıyor?*. Ayrı tutulması, bir uç sözleşmesi değişirse kira alanının sessizce ikiye bölünmesini engeller."],
            ["Matches", "Aday değer iki uzaydan birinde eşleşiyorsa true. Boş adaya asla genişlemez."],
            ["FromRobotElement", "Bir `/robotsStatus` `report[]` elemanından her iki uzayı okur. Tüm telemetri okuyucuları bunu kullanır, böylece üç parser tekrar birbirinden ayrışamaz."],
        ],
        "deps": "`System.Text.Json` dışında yok.",
        "callers": "`RdsRobotResolver`, `RdsExecutionCoordinator`, native order ve read istemcileri.",
        "state": "Değişmez (immutable) record.",
        "rules": [
            "Görüntüleme kimliği (model/isim) bilinçli olarak burada modellenmez; bir gösterim metni asla mutasyon hedefi olamaz.",
            "Aynı robotun iki alias'ı asla iki farklı kira üretmemelidir.",
        ],
        "errors": "Kendi kodu yoktur; null `MutationTarget` çağıranlar tarafından fail-closed olarak yorumlanır.",
        "security": "Yanlış kimlik uzayı seçimi geçmişte kilit korelasyonunun gerçek robotta sessizce başarısız olmasına yol açmıştı; bu tip o hatanın yapısal çözümüdür.",
        "tests": "`tests/TkgPda.Application.Tests` — her türetilmiş özellik ve `Matches` için ayrı vakalar.",
    },
    "src/TkgPda.Application/RdsRobotResolution.cs": {
        "purpose": "Bir alias'ın fiziksel bir robota çevrildiği **tek** yer. Belirsizliği yakalar ve tahmin etmeyi imkânsız kılar.",
        "resp": [
            "NoMatch / Unique / Ambiguous üç sonuçlu çözümleme.",
            "Ayrıklığı tüm kimlik çifti üzerinden hesaplamak.",
            "Boş/whitespace alias'ı NoMatch olarak ele almak.",
        ],
        "methods": [
            ["Resolve", "Alias'a cevap veren robotları iki uzayda birden arar. Farklı kimlik çifti sayısı 1 ise Unique, değilse Ambiguous. Aynı robotun payload'da iki kez görünmesi hâlâ tek robottur."],
        ],
        "deps": "`RdsRobotIdentity`, `RdsCoreRobotTelemetry`.",
        "callers": "`RdsExecutionCoordinator.ResolveRobotLeaseKeyAsync` ve `ResolveUniqueRobot`.",
        "state": "Durumsuz.",
        "rules": [
            "Belirsizlikte daima fail-closed: hangi makinenin kastedildiğini tahmin etmenin güvenli bir yolu yoktur.",
            "Ayrıklık tek bir çözülmüş string üzerinden hesaplanamaz — bu, (uuid=X,vehicle=null) ile (uuid=null,vehicle=X) çiftlerini birleştirip aranan belirsizliği gizlerdi.",
        ],
        "errors": "Çağıran tarafında `ROBOT_ALIAS_AMBIGUOUS`.",
        "security": "Önceki `FirstOrDefault` davranışı, payload sırasına göre farklı bir fiziksel makineyi hedefleyebiliyordu — hareket doğuran bir mutasyon için mümkün olan en kötü hata biçimi.",
        "tests": "`tests/TkgPda.Application.Tests` — tek robot, çakışan iki robot, tekrar eden aynı robot ve boş alias vakaları.",
    },
    "src/TkgPda.Application/RdsRobotAlarmParsing.cs": {
        "purpose": "Tek SEER alarm ayrıştırıcısı. Dört dokümante zarfı ve iki giriş şeklini destekler, severity'yi korur ve bloklama kararını severity üzerinden verdirir.",
        "resp": [
            "`RdsAlarmSeverity` sıralamasını tanımlamak (Notice < Warning < Error < Fatal).",
            "Dört bucket'ı hem doğrudan hem `alarms` sarmalı formda okumak.",
            "Şekil A (`code`) ve Şekil B (dinamik anahtarlı) girişleri ayrıştırmak.",
            "Null bucket'ı istisna değil \"alarm yok\" olarak ele almak.",
        ],
        "methods": [
            ["Parse (container)", "Verilen nesnedeki tüm dokümante bucket'ları hem doğrudan hem sarmalı formda okur; tek bir dokümante yanıt iki formu birden taşıdığı için biri diğerinin lehine atlanamaz."],
            ["IsBlocking (RdsParsedAlarm)", "Yalnız Fatal ve Error true döner. Warning ve Notice raporlanır ama bloklamaz."],
        ],
        "deps": "`System.Text.Json`.",
        "callers": "`RdsCoreNativeOrderClient`, `RdsReadClient`.",
        "state": "Durumsuz statik sınıf.",
        "rules": [
            "Dokümante olmayan hiçbir zarf veya bucket yazımı uydurulmaz.",
            "Şekil B'de alarm kodu özellik ADIdır; bilinen kardeş alanlar hariç tutularak bulunur.",
            "Bir notice asla bloklayan alarm gibi davranmaz.",
        ],
        "errors": "Kendi kodu yoktur; sonuçları `RDSCORE_HAS_BLOCKING_ALARM` ve robot düzeyi dispatch reddine beslenir.",
        "security": "Bozuk bir parser, alarmlı bir robota dispatch yapılmasına izin verirdi — bu tam olarak yaşanmış bir hatadır ve tek parser kuralı onun tekrarını engeller.",
        "tests": "`tests/TkgPda.Application.Tests` — dört zarf, iki şekil, null bucket ve severity matrisleri.",
    },
    "src/TkgPda.Application/RdsCoreNativeOrders.cs": {
        "purpose": "Native order modelinin tip sözleşmesi: sipariş, blok, telemetri, kilit bilgisi, filo anlık görüntüsü ve istemci arayüzü.",
        "resp": [
            "`RdsCoreRobotTelemetry` ve türetilmiş güvenlik özelliklerini tanımlamak.",
            "`RdsCoreFleetStatus` ile robot ve çekirdek alarmlarını birlikte döndürmeyi zorlamak.",
            "`RdsCoreRobotLockInfo.CorrelatesWith` kilit parmak izi korelasyonunu tanımlamak.",
            "`IRdsCoreNativeOrderClient` arayüzünü sabitlemek.",
        ],
        "methods": [
            ["CorrelatesWith", "İki kilit bilgisinin aynı kilidi tanımlayıp tanımlamadığına karar verir. Her iki taraf kilitli ve kullanılabilir parmak izine sahip olmalı; mevcut olan her birincil tanımlayıcı (TimeT, Ip, NickName) karşı tarafta da bulunup eşleşmeli; ikincil alanlar (Port, Type) yalnız ikisinde de varsa çelişki yaratabilir; en az bir birincil eşleşme zorunludur. Boş parmak izi hiçbir şeyle korele olmaz."],
            ["HasBlockingAlarm", "Yalnız fatal/error için true. Alarm listesinin dolu olması arıza demek değildir."],
            ["MayBeExecutingBusinessWork", "`ProcBusiness != false`. Yani true veya null/absent iken true döner — bilinmeyen iş durumu fail-closed ele alınır. Bilinçli olarak kanıtlayabildiği şeyle adlandırılmıştır ve tek başına boşta olmanın kanıtı sayılmaz."],
            ["ResolvedIdentity", "Kimlik yoksa `VehicleId`'yi vehicle uzayı kimliği olarak ele alır — okuyucunun ürettiği biçim budur."],
            ["GlobalHasBlockingAlarm", "Çekirdek düzeyi fatal/error. RDSCore'un kendi durumunun dispatch için güvensiz olduğunu söyler."],
        ],
        "deps": "`RdsParsedAlarm`, `RdsRobotIdentity`.",
        "callers": "`RdsCoreNativeOrderClient`, `RdsExecutionCoordinator`.",
        "state": "Değişmez record'lar.",
        "rules": [
            "Kök (çekirdek) alarmlar robotlara kopyalanmaz; yapısal bir atıf yoktur.",
            "Batarya seviyesi null ise UNAVAILABLE demektir, %0 değil.",
            "Filo anlık görüntüsü robotları ve kök alarmları birlikte döndürür; ayrı istenemez.",
        ],
        "errors": "Yok (model katmanı).",
        "security": "`MayBeExecutingBusinessWork` ve `CorrelatesWith` doğrudan kilit kurtarma güvenliğini belirler.",
        "tests": "`tests/TkgPda.Application.Tests` ve `tests/TkgPda.Api.Tests/RdsCoreNativeOrderClientTests.cs`.",
    },
    "src/TkgPda.Api/RdsCoreNativeOrderClient.cs": {
        "purpose": "RDSCore native order HTTP adaptörü. İzin verilen operasyonların kayıt tablosunu tutar, istekleri kurar, yanıtları ayrıştırır ve telemetriyi tiplere çevirir.",
        "resp": [
            "Operasyon kayıt tablosu (`Registry`): metot, yol ve mutasyon bayrağı.",
            "`/robotsStatus` yanıtını `RdsCoreFleetStatus`'a çevirmek — robotlar ve kök alarmlar birlikte.",
            "Mutasyon operasyonlarını `MutationEnabled` ile kapılamak.",
            "Zaman aşımı, güvensiz uzak HTTP ve hata kodu haritalamasını uygulamak.",
        ],
        "methods": [
            ["SetOrderAsync", "`/setOrder`. Fiziksel hareketi başlatan çağrı."],
            ["GetRobotsStatusAsync", "`/robotsStatus`. Robot listesini ve kök alarmları tek anlık görüntüde döndürür. `procBusiness` yoksa null bırakılır — UNKNOWN, boşta değil."],
            ["TerminateAsync", "`/terminate`. SafetyStop."],
            ["LockAsync / UnlockAsync / SwitchMapAsync", "Harita hazırlığı üçlüsü. ExecutionMutation."],
            ["ManualFinishedAsync / RedoFailedOrderAsync / ClearRobotAllErrorAsync / GotoSiteResumeAsync", "MotionEnabling operasyonları."],
            ["GotoSitePauseAsync / DispatchableAsync(undispatchable)", "SafetyStop operasyonları."],
            ["GetOrderDetailsAsync / GetOrdersAsync / GetOrderDetailsByExternalIdAsync / GetOrderDetailsByBlockIdAsync / GetBlockDetailsByIdAsync", "Salt-okunur sipariş sorguları."],
            ["AllowedOperations", "Kayıt tablosunu dışarıya açar; Admin Web teşhis sayfası hangi operasyonların var olduğunu buradan öğrenir."],
        ],
        "deps": "`HttpClient`, `RdsRobotAlarmParser`, `RdsRobotIdentity`, `RdsCoreExecutionOptions`.",
        "callers": "`RdsExecutionCoordinator`, `AdminEndpoints` teşhis uçları.",
        "state": "Durumsuz; `HttpClient` DI ile verilir.",
        "rules": [
            "Kayıt tablosunda olmayan hiçbir RDSCore yolu çağrılamaz.",
            "Telemetri okuma daima paylaşılan kimlik ve alarm parser'ını kullanır.",
        ],
        "errors": "`RdsCoreNativeOrderException` ve taşıdığı kodlar.",
        "security": "`BaseAddressDisplaySafe` günlüklemede adres bileşenlerinin sızmasını engeller; `AllowInsecureRemoteHttp=false` uzak düz HTTP'yi reddeder.",
        "tests": "`tests/TkgPda.Api.Tests/RdsCoreNativeOrderClientTests.cs`.",
    },
    "src/TkgPda.Api/PostOutcomeAudit.cs": {
        "purpose": "Fiziksel sonuç gerçekleştikten sonra yazılan denetim kaydını, çağıranın iptal edilmiş olabileceği koşullarda bile en iyi çabayla yazan küçük yardımcı.",
        "resp": [
            "Kendi 5 saniyelik sınırlı token'ı altında çalışmak.",
            "`OperationCanceledException` dahil tüm istisnaları yutmak.",
            "Çağıranın akışını asla etkilememek.",
        ],
        "methods": [
            ["RunAsync", "Verilen denetim yazma işini kendi bounded token'ı ile çalıştırır. Hiçbir istisna dışarı çıkmaz; başarısızlık sessizce yutulur."],
        ],
        "deps": "Yok (delegate alır).",
        "callers": "`RdsExecutionCoordinator` uzman ve dispatch yolları.",
        "state": "Durumsuz.",
        "rules": [
            "Denetim yazımı başarısız olduğu için fiziksel operasyon ASLA tekrar denenmez.",
        ],
        "errors": "Yok — bilinçli olarak hiçbir hata yaymaz.",
        "security": "Robot çoktan hareket etmişken denetim hatasının operasyonu tekrarlatması çift hareket üretirdi. Bu dosya o riski yapısal olarak kaldırır.",
        "tests": "`tests/TkgPda.Api.Tests` — iptal edilmiş token ve fırlatan denetim yazıcı senaryoları.",
    },
    "src/TkgPda.Api/ApiErrors.cs": {
        "purpose": "Tüm API istisnalarını tek bir zincirde HTTP yanıtlarına çeviren middleware ve problem gövdesi yazıcısı.",
        "resp": [
            "İş kuralı ve API istisnalarını kod + durum ile yanıtlamak.",
            "Tek bir iptal yolu tanımlamak.",
            "PostgreSQL kısıt ihlallerini 409'a, erişilemezliği 503'e çevirmek.",
            "Beklenmeyen istisnaları 500 olarak günlüklemek.",
        ],
        "methods": [
            ["InvokeAsync", "İstisna zinciri. Sıra anlamlıdır: ApiException/WorkflowRuleException → BadHttpRequest → JsonException → OperationCanceledException (yalnız RequestAborted iptal edilmişse) → PostgresException (unique/fk/check) → NpgsqlException → Exception."],
            ["WriteAsync (ApiErrorWriter)", "Problem gövdesini yazar: code, message, traceId."],
        ],
        "deps": "`Npgsql`, ASP.NET Core.",
        "callers": "Program.cs pipeline'ı.",
        "state": "Durumsuz middleware.",
        "rules": [
            "İstemci bağlantıyı kopardığında sunucu hatası üretilmez ve yanıt gövdesi yazılmaz.",
            "`RequestAborted` iptal edilmemişken gelen bir `OperationCanceledException` gerçek bir iç arızadır ve 500'e düşer.",
        ],
        "errors": "INVALID_HTTP_REQUEST, INVALID_JSON, CONFIGURATION_CONFLICT, DATABASE_UNAVAILABLE.",
        "security": "Yanıt gövdesi yığın izi taşımaz; ayrıntı yalnız sunucu günlüğüne yazılır.",
        "tests": "`tests/TkgPda.Api.Tests` — her dal için ayrı vaka.",
    },
    "src/TkgPda.Api/ApiSecurity.cs": {
        "purpose": "Kimlik, roller, politikalar, JWT üretimi/doğrulaması ve cihaz bağlama middleware'i.",
        "resp": [
            "Rol kodlarını ve politika üyeliklerini tanımlamak.",
            "JWT seçeneklerini doğrulamak ve token üretmek.",
            "Cihaz bağlamayı zorlamak (`X-Device-Code`).",
            "İstemci tipi kısıtlarını uygulamak.",
        ],
        "methods": [
            ["JwtOptions.Validate", "Issuer/audience zorunlu; imzalama anahtarı en az 32 bayt olmalı. Zayıf anahtarla başlatma yapısal olarak imkânsızdır."],
            ["Token üretimi", "Kullanıcı rollerini `ClaimTypes.Role` olarak, cihaz kodunu `device_code` iddiası olarak ekler."],
            ["Cihaz middleware'i", "Başlıktaki cihaz kodu ile token iddiasını karşılaştırır (`DEVICE_CONTEXT_MISMATCH`), ardından cihazın kayıtlı ve aktif olduğunu doğrular (`DEVICE_NOT_AUTHORIZED`), sonra cihazı istek bağlamına koyar."],
            ["IsAdministrator", "ADMIN rol kontrolü kısayolu."],
        ],
        "deps": "`Microsoft.IdentityModel.Tokens`, `System.IdentityModel.Tokens.Jwt`, `IApiDataStore`.",
        "callers": "`Program.cs`, tüm uç noktalar.",
        "state": "Durumsuz.",
        "rules": [
            "WP-05A: READY işaretleme bir depo hazırlık eylemidir; LINE_OPERATOR bilinçli olarak dışlanmıştır.",
            "Admin Web token'ı ADMIN rolü taşımalıdır.",
        ],
        "errors": "DEVICE_CONTEXT_MISMATCH, DEVICE_NOT_AUTHORIZED, ADMIN_WEB_ROLE_REQUIRED, AMBIGUOUS_LOGIN_IDENTITY.",
        "security": "Bilgi güvenliğinin merkezi. İmzalama anahtarı yapılandırmadan gelir ve depoda bulunmaz — `<JWT_SIGNING_KEY>`.",
        "tests": "`tests/TkgPda.Auth.Tests`, `tests/TkgPda.Api.Tests`.",
    },
    "src/TkgPda.Api/ApiEndpoints.cs": {
        "purpose": "Operatör HTTP yüzeyi. Kimlik, makine listesi, operasyonel bağlam, taşıma oluşturma, kasa aksiyonları, kuyruk, iptal ve depo hazırlık uçları burada kayıtlıdır.",
        "resp": [
            "Operatör uçlarını kaydetmek ve politikalara bağlamak.",
            "Girdi doğrulaması ve idempotency başlığı yönetimi.",
            "Operatöre kapalı operasyonları elemek.",
            "Uygulama servislerine delege etmek.",
        ],
        "methods": [
            ["LoginAsync", "Sicil numarası veya legacy kullanıcı adı ile giriş. İkisi birden verilirse `AMBIGUOUS_LOGIN_IDENTITY`."],
            ["CurrentUserAsync", "Token'daki kimliği ve rolleri döndürür."],
            ["ListMachinesAsync", "Kullanıcının yetkili olduğu makineler."],
            ["OperationalContextAsync", "Makine için operatör projeksiyonu: pozisyonlar, aksiyon uygunlukları, mutasyon kapısı."],
            ["CreateTransportAsync", "Genel taşıma oluşturma. `EnsureTransportOperationAllowed` ile operatöre kapalı operasyonları eler."],
            ["CreateContainerActionAsync", "WP-33 tekil kasa aksiyonu."],
            ["RemoveAllContainersAsync", "Varyant kapsamlı toplu kaldırma."],
            ["OperatorQueueAsync", "Devam eden talepler; `CanCancel` alanı `CanCancelBeforeExecution` üzerinden hesaplanır."],
            ["CancelTransportAsync", "İptal; koordinatöre delege eder."],
            ["MarkTransportReadyAsync", "Depo hazırlık; warehouse-preparation politikası."],
            ["EnsureTransportOperationAllowed", "REMOVE_EMPTY gibi operatöre kapalı operasyonları her rol için reddeder → `OPERATION_NOT_OPERATOR_SELECTABLE` (HTTP 422)."],
        ],
        "deps": "`PdaOperationalContextService`, `ContainerActionService`, `ITransportWorkflowStore`, `RdsExecutionCoordinator`, `ApiSecurity`.",
        "callers": "Flutter `PdaRepository`.",
        "state": "Durumsuz.",
        "rules": [
            "Operatöre ham RDS tanımlayıcısı serialize edilmez.",
            "REMOVE_EMPTY operatör tarafından seçilemez; yalnız sunucu orkestrasyonu üretir.",
        ],
        "errors": "OPERATION_NOT_OPERATOR_SELECTABLE, AMBIGUOUS_LOGIN_IDENTITY, BATCH_* ailesi, OCCUPIED_CONTAINER_REQUIRED, BUSINESS_RULE_NOT_CONFIGURED.",
        "security": "Her uç bir politikaya bağlıdır; politikasız uç yoktur.",
        "tests": "`tests/TkgPda.Api.Tests` — uç bazlı sözleşme testleri.",
    },
    "src/TkgPda.Api/AdminEndpoints.cs": {
        "purpose": "Admin HTTP yüzeyi: ana veri yönetimi, layout sürümleme, kullanıcı/cihaz yönetimi, RDS entegrasyon teşhisi ve uzman robot operasyonları.",
        "resp": [
            "Admin uçlarını `administrator` / `variant-manager` politikalarına bağlamak.",
            "Konfigürasyon CRUD'unu `AdminConfigurationService`'e delege etmek.",
            "Uzman RDS operasyonlarını koordinatöre yönlendirmek — kendi mutasyon kararını vermeden.",
        ],
        "methods": [
            ["Layout uçları", "Liste, oku, oluştur, güncelle, sürüm klonla, yayınla."],
            ["Slot uçları", "Slot CRUD, geometri, slot ↔ parça eşlemesi."],
            ["Ana veri uçları", "Makine, varyant, parça, ürün grubu, kasa tipi."],
            ["Kimlik uçları", "Kullanıcı, rol, makine yetkisi, cihaz."],
            ["RDS teşhis uçları", "WorkSite projeksiyonu, robot telemetrisi, izin verilen operasyon listesi, entegrasyon durumu."],
            ["Uzman operasyon uçları", "manualFinished, redoFailedOrder, setPriority, robot bakımı. Hepsi `RdsExecutionCoordinator` üzerinden geçer ve mutasyon kapısına tabidir."],
        ],
        "deps": "`AdminConfigurationService`, `RdsExecutionCoordinator`, `RdsIntegrationService`, `ApiSecurity`.",
        "callers": "Admin Web `client.ts`.",
        "state": "Durumsuz.",
        "rules": [
            "Admin olmak fiziksel mutasyon kapısını açmaz.",
        ],
        "errors": "ADMIN_WEB_ROLE_REQUIRED, CONFIGURATION_CONFLICT, RDS_DISPATCH_DISABLED ve koordinatörün ürettiği tüm kodlar.",
        "security": "Uzman operasyonlar burada kendi izin kararını vermez; karar tek yerde, politikadadır.",
        "tests": "`tests/TkgPda.Api.Tests`.",
    },
    "src/TkgPda.Api/RdsIntegrationEndpoints.cs": {
        "purpose": "RDS'in TKG'ye geri bildirim gönderdiği callback ucu. Küçük ama güvenlik-kritiktir.",
        "resp": [
            "Callback anahtarını doğrulamak.",
            "Doğrulanan olayı koordinatöre iletmek.",
        ],
        "methods": [
            ["Callback handler", "Yapılandırılmış bir `CallbackKey` yoksa hiçbir isteği kabul etmez (fail-closed). Anahtar karşılaştırması `FixedTimeEquals` ile sabit zamanlıdır."],
        ],
        "deps": "`RdsOptions`, `RdsExecutionCoordinator`.",
        "callers": "RDS (dışarıdan).",
        "state": "Durumsuz.",
        "rules": [
            "Anahtar yapılandırılmamışsa uç kapalıdır.",
            "Callback uzlaştırmanın yerine geçmez; onu hızlandırır.",
        ],
        "errors": "Yetkisiz callback reddi.",
        "security": "Sabit zamanlı karşılaştırma, anahtarın zamanlama saldırısıyla çıkarılmasını engeller.",
        "tests": "`tests/TkgPda.Api.Tests`.",
    },
    "src/TkgPda.Api/Program.cs": {
        "purpose": "Uygulama kompozisyon kökü: yapılandırma bağlama, DI kaydı, kimlik doğrulama/yetkilendirme kurulumu, middleware sırası, uç nokta kaydı ve arka plan worker'ı.",
        "resp": [
            "Yapılandırmayı seçenek tiplerine bağlamak ve doğrulamak.",
            "Store ve adaptör implementasyonlarını kaydetmek.",
            "Middleware sırasını kurmak (hata → kimlik → cihaz → uçlar).",
            "RDS worker'ını `Rds:Enabled` durumuna göre kaydetmek.",
            "Başlangıçta güvenli bir durum özeti günlüklemek.",
        ],
        "methods": [
            ["Başlangıç günlüğü", "`RDS Integration: {Status}, RDS Dispatch: {Dispatch}, RDSCore: {Endpoint}` satırı. Devreye alma mühendisinin kapının durumunu tek bakışta görmesi için vardır ve adresi `BaseAddressDisplaySafe` üzerinden yazar."],
        ],
        "deps": "Tüm katmanlar.",
        "callers": "Host.",
        "state": "Kompozisyon kökü.",
        "rules": [
            "Sırlar yapılandırma dosyasından değil ortamdan gelir.",
            "`Rds:Enabled=false` iken worker hiç kaydedilmez.",
        ],
        "errors": "Başlangıç doğrulama hataları (eksik bağlantı dizesi, kısa imzalama anahtarı).",
        "security": "Günlüklenen hiçbir alan sır içermez.",
        "tests": "`tests/TkgPda.Api.Tests` barındırılan host testleri.",
    },
    "src/TkgPda.Domain/TransportStateMachine.cs": {
        "purpose": "Taşıma talebi durum geçişlerinin tek yetkili tanımı. 47 satırdır ve bu kısalık kasıtlıdır: kural küçük ve okunabilir olmalıdır.",
        "resp": [
            "Yasal geçiş kümesini tanımlamak.",
            "Geçişleri zorlamak.",
            "Rezervasyon aktifliğini ve iptal edilebilirliği türetmek.",
        ],
        "methods": [
            ["LegalTransitions", "Durumdan izinli hedef durumlara sözlük. Terminal durumların (Completed/Failed/Cancelled) çıkışı yoktur."],
            ["EnsureTransition", "Yasal olmayan geçişi `ILLEGAL_STATUS_TRANSITION` ile reddeder. Durum değiştiren her yol buradan geçer."],
            ["IsReservationActive", "Terminal olmayan durumlar rezervasyon tutar. Varyant değiştirme ve slot yeniden kullanımı bu tanıma bakar."],
            ["CanCancelBeforeExecution", "Created, Validating, Queued için true — RDS'e hiç gidilmemiş talepler."],
        ],
        "deps": "`WorkflowEnums`.",
        "callers": "`PostgresTransportWorkflowStore`, `RdsExecutionCoordinator`, operatör kuyruğu projeksiyonu.",
        "state": "Durumsuz.",
        "rules": [
            "Durum yalnız `EnsureTransition` üzerinden değişir.",
            "RESERVED bir durum değildir; rezervasyon durumdan türetilir.",
        ],
        "errors": "ILLEGAL_STATUS_TRANSITION.",
        "security": "RDS'ten gelen beklenmedik bir durum illegal geçiş üretemez.",
        "tests": "`tests/TkgPda.Domain.Tests` — tam geçiş matrisi.",
    },
    "src/TkgPda.Domain/WorkflowEnums.cs": {
        "purpose": "Domain'in donmuş enum'ları: taşıma durumu, hazırlık durumu, operasyon türü, doluluk ve yük eksenleri, slot rolleri.",
        "resp": [
            "`TransportRequestStatus` sekiz değerini tanımlamak.",
            "`TransportReadinessState` üç değerini ayrı bir eksen olarak tanımlamak.",
            "`OccupancyState` ve `LoadState` eksenlerini ayrı tutmak.",
            "`TransportOperation` üç değerini tanımlamak.",
        ],
        "methods": [
            ["Ayrıştırma yardımcıları", "Metin ↔ enum dönüşümleri; bilinmeyen değer sessizce varsayılana düşmez."],
        ],
        "deps": "Yok.",
        "callers": "Tüm katmanlar.",
        "state": "Durumsuz.",
        "rules": [
            "`WAITING_PREPARATION` bir durum değildir; ayrı ve dik bir eksendir.",
            "RESERVED asla bir occupancy değeri değildir.",
        ],
        "errors": "Yok.",
        "security": "Yok.",
        "tests": "`tests/TkgPda.Domain.Tests`.",
    },
    "src/TkgPda.Domain/TransportRules.cs": {
        "purpose": "Taşıma oluşturmanın saf iş kuralları: hangi operasyon hangi slot durumunda meşrudur, hangi kaynak/hedef kombinasyonu geçerlidir.",
        "resp": [
            "Operasyon ↔ slot durumu uyumunu doğrulamak.",
            "Kaynak ve hedef slot rollerini kontrol etmek.",
            "Geçersiz kombinasyonları iş kuralı hatasıyla reddetmek.",
        ],
        "methods": [
            ["Doğrulama metotları", "Her biri tek bir kuralı kontrol eder ve ihlalde `WorkflowRuleException` fırlatır. I/O yapmaz, bu yüzden tamamı saf birim testi ile kapsanabilir."],
        ],
        "deps": "`WorkflowEnums`, `WorkflowModels`.",
        "callers": "`ContainerActionPolicy`, `TransportWorkflowService`, Postgres store.",
        "state": "Durumsuz.",
        "rules": [
            "UNKNOWN bir eksende varsa iş reddedilir.",
            "Kural teknik kolaylık için gevşetilmez.",
        ],
        "errors": "OCCUPIED_CONTAINER_REQUIRED, BUSINESS_RULE_NOT_CONFIGURED ve ilgili kod ailesi.",
        "security": "Yanlış bir kural kabulü robotu boş bir slota veya dolu bir slota gönderirdi.",
        "tests": "`tests/TkgPda.Domain.Tests`.",
    },
    "src/TkgPda.Application/ContainerActions.cs": {
        "purpose": "WP-33 kasa aksiyonlarının politika ve servis katmanı. Operatörün mantıksal niyetini domain operasyonuna çevirir.",
        "resp": [
            "Tekil kasa aksiyonunu (FULL/EMPTY) domain operasyonuna çevirmek.",
            "Remove-All kapsamında her pozisyon için operasyon türetmek.",
            "Uygun olmayan durumları anlamlı iş kuralı hatalarıyla reddetmek.",
            "Aksiyon uygunluğunu (`enabled` + `disabledMessage`) operatör projeksiyonu için üretmek.",
        ],
        "methods": [
            ["Resolve", "Tekil aksiyon çözümü. OCCUPIED+EMPTY için yük durumunu FULL yapıp REMOVE_FULL; OCCUPIED+FULL için idempotent REMOVE_FULL; VACANT için `OCCUPIED_CONTAINER_REQUIRED`; UNKNOWN için `BUSINESS_RULE_NOT_CONFIGURED`."],
            ["ResolveRemoveAll", "Varyant kapsamlı toplu kaldırma. OCCUPIED+FULL → REMOVE_FULL, OCCUPIED+EMPTY → REMOVE_EMPTY (yalnız burada, sunucu üretir), VACANT+UNKNOWN → null (atlanır), UNKNOWN → `REMOVE_ALL_POSITION_UNKNOWN` (tüm batch red), çelişki → `REMOVE_ALL_POSITION_CONFLICT` (tüm batch red)."],
            ["Uygunluk projeksiyonu", "Her pozisyon için `fullAction` ve `emptyAction` uygunluğunu ve devre dışıysa sebebini üretir. Bu, operatör ekranındaki buton durumunun tek kaynağıdır."],
        ],
        "deps": "`TransportRules`, `WorkflowEnums`, `ITransportWorkflowStore`.",
        "callers": "`ApiEndpoints`, `PdaOperationalContextService`.",
        "state": "Durumsuz.",
        "rules": [
            "Remove-All kapsamı varyant/layout'tur, seçili parça değil.",
            "Bir pozisyon UNKNOWN veya çelişkiliyse tüm batch reddedilir — kısmi uygulama yoktur.",
            "REMOVE_EMPTY yalnız Remove-All orkestrasyonunda üretilir.",
        ],
        "errors": "OCCUPIED_CONTAINER_REQUIRED, BUSINESS_RULE_NOT_CONFIGURED, REMOVE_ALL_POSITION_UNKNOWN, REMOVE_ALL_POSITION_CONFLICT.",
        "security": "Yanlış operasyon türetmek robotu yanlış işle gönderirdi.",
        "tests": "`tests/TkgPda.Application.Tests` — durum matrisi tam kapsanır.",
    },
    "src/TkgPda.Application/PdaOperationalContextService.cs": {
        "purpose": "Operatör ekranının gördüğü her şeyi üreten projeksiyon servisi. Fiziksel gerçeği mantıksal iş yüzeyine çevirir.",
        "resp": [
            "Makine, aktif varyant, layout ve slot durumlarını okumak.",
            "Fiziksel slotları mantıksal kasa pozisyonlarına (1. Kasa, 2. Kasa …) eşlemek.",
            "Her pozisyon için aksiyon uygunluğunu hesaplamak.",
            "Mutasyon kapısını ve devam eden taşıma durumlarını projeksiyona koymak.",
        ],
        "methods": [
            ["Bağlam üretimi", "Slotları `display_order` ile sıralar ve pozisyon numaralarını buradan türetir. Operatör asla bir slot UUID'si görmez."],
            ["Aksiyon uygunluğu", "`ContainerActionPolicy` sonucunu `enabled` + `disabledMessage` çiftine çevirir."],
            ["Taşıma durumu projeksiyonu", "Devam eden talepleri sunum durumlarına (READY / REQUESTED / QUEUED / IN_PROGRESS …) çevirir ve kuyruk pozisyonunu ekler."],
        ],
        "deps": "`IApiDataStore`, `ContainerActionPolicy`, `IRdsReadClient`, `TransportStateMachine`.",
        "callers": "`ApiEndpoints.OperationalContextAsync`.",
        "state": "Durumsuz.",
        "rules": [
            "Ham RDS tanımlayıcısı projeksiyona hiç konmaz.",
            "Sunum durumları `TransportRequestStatus` ile birebir aynı değildir; hazırlık ekseni ve robot ataması burada birleştirilir.",
        ],
        "errors": "Veri eksikliğinde ilgili iş kuralı kodları.",
        "security": "Bilgi sınırının uygulandığı yer: operatöre gitmemesi gereken her alan burada filtrelenir.",
        "tests": "`tests/TkgPda.Application.Tests`.",
    },
    "src/TkgPda.Infrastructure.Postgres/PostgresTransportWorkflowStore.cs": {
        "purpose": "Taşıma iş akışının SQL implementasyonu. İşlem sınırları, idempotency, rezervasyon koruması ve durum geçişlerinin kalıcılığı burada.",
        "resp": [
            "Taşıma taleplerini oluşturmak ve durumlarını ilerletmek.",
            "Idempotency'yi advisory lock + benzersiz indeks ile garanti etmek.",
            "Aktif rezervasyon varken varyant değişimini engellemek.",
            "Olayları `ops.transport_request_events` tablosuna yazmak.",
        ],
        "methods": [
            ["Talep oluşturma", "`pg_advisory_xact_lock(hashtextextended(idempotency_key,0))` ile aynı anahtarlı eşzamanlı istekleri serileştirir; mevcut kayıt varsa onu döndürür ve `IdempotentReplay` işaretler."],
            ["Durum güncelleme", "`TransportStateMachine.EnsureTransition` ile doğrulanan geçişi yazar ve olayı kaydeder."],
            ["Varyant değiştirme", "Aktif rezervasyon varsa `ACTIVE_TRANSPORT_EXISTS` fırlatır."],
            ["Kuyruk sorgusu", "Operatör kuyruğunu ve `CanCancel` alanını üretir."],
        ],
        "deps": "`Npgsql`, `TransportStateMachine`, `TransportRules`.",
        "callers": "`ApiEndpoints`, `TransportWorkflowService`, `ContainerActionService`.",
        "state": "Veritabanı; kendi içinde durum tutmaz.",
        "rules": [
            "Idempotency iki katmanla korunur: advisory lock yarışı serileştirir, benzersiz indeks son savunmadır.",
            "Durum yalnız durum makinesi üzerinden değişir.",
        ],
        "errors": "ACTIVE_TRANSPORT_EXISTS, ILLEGAL_STATUS_TRANSITION ve ilgili kodlar.",
        "security": "Çift kayıt = çift fiziksel hareket. Bu dosya o riski kapatır.",
        "tests": "`tests/TkgPda.Infrastructure.Postgres.Tests` — gerçek PostgreSQL gerektirir.",
    },
    "src/TkgPda.Infrastructure.Postgres/PostgresTransportWorkflowStore.ContainerActions.cs": {
        "purpose": "Kasa aksiyonlarının SQL implementasyonu: tekil aksiyon, batch ve Remove-All akışları.",
        "resp": [
            "Kasa aksiyonunu tek transaction içinde uygulamak.",
            "Boş kasa tahsisini `FOR UPDATE ... SKIP LOCKED` ile yapmak.",
            "Yük durumu geçişini (EMPTY→FULL) atomik uygulamak.",
            "Batch bütünlüğünü korumak: kısmi uygulama yok.",
        ],
        "methods": [
            ["Tekil aksiyon", "Advisory lock → slot satırı `FOR UPDATE` → gerekiyorsa yük güncelleme → talep INSERT → olay INSERT, tek transaction."],
            ["Boş kasa tahsisi", "`EMPTY_STORAGE` rolündeki OCCUPIED+EMPTY slotlardan `FOR UPDATE SKIP LOCKED` ile bir tanesi seçilir. `SKIP LOCKED` olmadan iki eşzamanlı BRING_EMPTY aynı tampon kasayı seçerdi."],
            ["Remove-All", "Tüm pozisyonlar için türetilen operasyonlar tek transaction içinde yazılır; biri reddedilirse hiçbiri yazılmaz."],
        ],
        "deps": "`Npgsql`, `ContainerActionPolicy`, `TransportRules`.",
        "callers": "`ContainerActionService`.",
        "state": "Veritabanı.",
        "rules": [
            "Batch ya tamamen uygulanır ya hiç uygulanmaz.",
            "Yük durumu değişimi ile talep oluşturma aynı transaction içindedir; ikisi ayrılamaz.",
        ],
        "errors": "REMOVE_ALL_POSITION_UNKNOWN, REMOVE_ALL_POSITION_CONFLICT, OCCUPIED_CONTAINER_REQUIRED, BATCH_* ailesi.",
        "security": "Kısmi batch uygulaması, operatörün gördüğü durumla fiziksel gerçeği ayırırdı.",
        "tests": "`tests/TkgPda.Infrastructure.Postgres.Tests`.",
    },
    "src/TkgPda.Infrastructure.Postgres/PostgresRdsExecutionStore.cs": {
        "purpose": "RDS yürütmesinin kalıcılık katmanı: kuyruk okuma, görev kayıtları, denetim yazımı ve iki ayrı advisory kira implementasyonu.",
        "resp": [
            "Kuyruktaki talep kimliklerini listelemek.",
            "Talep kirasını (`tkg:rds:`) ve robot kirasını (`tkg:robot:`) sağlamak.",
            "`integration.rds_tasks` kayıtlarını yönetmek.",
            "Yürütme denetim kayıtlarını yazmak.",
            "Serbest bırakılmamış harita geçiş kilidi kaydını okumak.",
        ],
        "methods": [
            ["TryAcquireLeaseAsync", "Talep kirası. `SELECT pg_try_advisory_lock(hashtextextended(@key,0))`, anahtar `tkg:rds:{requestId:D}`."],
            ["TryAcquireRobotLeaseAsync", "Robot kirası. Ayrılmış bir bağlantıda SESSION düzeyinde tutulur; işlem düzeyinde olsaydı commit anında — fiziksel mutasyondan önce — serbest kalırdı. Anahtar `tkg:robot:{key}` biçimindedir ve küçük harfe indirgenir, böylece bir robot anahtarı bir talep anahtarıyla çarpışamaz."],
            ["WriteExecutionAuditAsync", "Eylem + sebep kodu + mesaj + korelasyon kimliği yazar."],
            ["GetUnreleasedMapSwitchLockAsync", "Araca göre filtrelenmiş kurtarma kaydını döndürür; çağıran ayrıca mevcut bağlama karşı yeniden doğrular."],
            ["ListQueuedRequestIdsAsync", "Dispatch için kuyruk okuma."],
        ],
        "deps": "`Npgsql`.",
        "callers": "`RdsExecutionCoordinator`.",
        "state": "Veritabanı; kira nesneleri kendi bağlantılarını tutar ve `DisposeAsync` ile bırakır.",
        "rules": [
            "Kira ad alanı öneki anahtar dizesinin içindedir.",
            "Kira serbest bırakma `await using` üzerinden her yolda garantidir.",
        ],
        "errors": "`NpgsqlException` yukarıya taşınır ve 503'e çevrilir.",
        "security": "Kira alanlarının ayrılması ve session ömrü, robot serileştirmesinin doğruluğunun temelidir.",
        "tests": "`tests/TkgPda.Infrastructure.Postgres.Tests` — gerçek PostgreSQL ile eşzamanlılık senaryoları.",
    },
    "src/TkgPda.Infrastructure.Postgres/PostgresAdminConfigurationStore.Layouts.cs": {
        "purpose": "Layout ve slot yönetiminin SQL implementasyonu; sürüm klonlamanın ilişkisel bütünlük koruması burada.",
        "resp": [
            "Layout CRUD ve sürümleme.",
            "Slot CRUD ve geometri.",
            "Slot ↔ parça ilişkisi yönetimi.",
            "Klonlamada ilişkisel kaybı tespit etmek.",
        ],
        "methods": [
            ["CloneLayoutAsync", "Yeni layout (`max(version)+1`, DRAFT) → slotlar (yeni UUID, aynı `code`) → `layout_slot_parts` ilişkileri (`code` üzerinden JOIN). Kopyalanan satır sayısı beklenenle karşılaştırılır; eşit değilse `InvalidOperationException` fırlatılır ve transaction geri alınır."],
            ["PublishLayoutAsync", "DRAFT → PUBLISHED geçişi."],
            ["Slot eşleme metotları", "Slot ↔ parça ilişkilerini kurar ve kaldırır."],
        ],
        "deps": "`Npgsql`.",
        "callers": "`AdminConfigurationService` → `AdminEndpoints`.",
        "state": "Veritabanı.",
        "rules": [
            "`code` üzerinden eşleme `uq_layout_slot_code UNIQUE (layout_id, code)` sayesinde tekildir; JOIN çoğaltma üretemez.",
            "İlişkisel kayıp tespit edilirse klon commit edilmez.",
        ],
        "errors": "\"Relational loss during layout version cloning\" — ilişkisel bütünlük ihlali.",
        "security": "Sessizce parça ilişkisi kaybeden bir klon, robotu yanlış slota yönlendirebilirdi.",
        "tests": "`tests/TkgPda.Infrastructure.Postgres.Tests/PostgresLayoutCloningTests.cs` — gerçek veritabanı ile.",
    },
    "src/TkgPda.Pda/lib/app/app_controller.dart": {
        "purpose": "Operatör uygulamasının tüm durumunu tutan tek `ChangeNotifier`. 1790 satırdır ve tek olması kasıtlıdır: operatör akışı doğrusaldır, durumun tek yerde olması iki ekranın çelişkili bilgi göstermesini imkânsız kılar.",
        "resp": [
            "Aşama makinesini (`AppStage`) yönetmek.",
            "Kimlik yaşam döngüsünü yürütmek.",
            "Makine/varyant/parça seçim akışını yönetmek.",
            "Kasa aksiyonlarını ve batch gönderimini koordine etmek.",
            "Depo hazırlık akışını yürütmek.",
            "Hata ve yükleme durumlarını arayüze taşımak.",
        ],
        "methods": [
            ["initialize", "Cihaz kodunu yükler, oturumu geri yükler, başlangıç aşamasını belirler."],
            ["login / logout / handleUnauthorized", "401 alındığında oturum belleği temizlenir ve giriş ekranına dönülür."],
            ["selectMachine / refreshContext", "Operasyonel bağlamı yükler ve tüm panelleri besler."],
            ["switchVariant", "Varyant değiştirir; aktif rezervasyon varsa sunucu 409 ile reddeder ve mesaj gösterilir."],
            ["submitContainerAction", "WP-33 doğrudan gönderim. Onay diyaloğu yoktur."],
            ["removeAllContainers", "Varyant kapsamlı toplu kaldırma; gönderim sırasında buton devre dışıdır."],
            ["enterBatchSelection / addPartToBatch / beginBatchConfiguration / confirmBatchItemCommand / submitBatch", "Çok parçalı sepet akışı; her kalem ayrı yapılandırılır ve tek batch olarak gönderilir."],
            ["skipInvalidBatchItem / reconfigureBatchItem / retryAmbiguousBatch", "Batch içindeki hatalı kalemlerin ele alınması; belirsiz batch körlemesine tekrarlanmaz."],
            ["enterWarehousePreparation / markPreparationReady", "Depo hazırlık akışı."],
            ["retryRobotAlarmSummary", "Alarm bandını yeniden yükler."],
            ["isSubmittingContainerAction / operationInProgress", "Arayüzün buton durumlarını hesaplaması için gönderim bayrakları."],
        ],
        "deps": "`PdaRepository`, `SubmissionCoordinator`, `SessionMemory`, `DeviceCodeStore`, `ApiClient`.",
        "callers": "Tüm ekranlar.",
        "state": "Uygulamanın tek durum sahibi. `notifyListeners` ile arayüzü besler.",
        "rules": [
            "Fiziksel slot kimliği hiçbir zaman istemcide bulunmaz.",
            "Aynı anda tek gönderim; `SubmissionCoordinator` bunu zorlar.",
        ],
        "errors": "`ApiProblem` kodları Türkçe mesajlara çevrilerek gösterilir.",
        "security": "JWT yalnız bellekte; diske yazılmaz.",
        "tests": "`src/TkgPda.Pda/test/widget/*` ve `test/unit/*`.",
    },
    "src/TkgPda.Pda/lib/features/operation/container_position_panel.dart": {
        "purpose": "WP-33 iş aksiyon yüzeyi. Yalnız backend'in mantıksal pozisyon projeksiyonunu tüketir ve hiçbir zaman fiziksel slot tanımlayıcısı almaz.",
        "resp": [
            "Yatay kaydırmalı pozisyon kartı listesi çizmek.",
            "Her pozisyon için iki iş butonu sunmak.",
            "Devre dışı sebebini görünür metin olarak göstermek.",
            "Varyant kapsamlı \"Tüm Kasaları Kaldır\" aksiyonunu sunmak.",
            "Taşıma durumunu erişilebilir bir rozetle göstermek.",
        ],
        "methods": [
            ["build (ContainerPositionPanel)", "Yükleniyor, hata, boş ve dolu durumlarını ayrı ayrı ele alır. Boş durumda `no-container-positions` anahtarlı açıklayıcı metin gösterilir."],
            ["_PositionCard.build", "Pozisyon etiketi (birden çok pozisyon varsa `n / toplam` ile), durum rozeti ve iki iş butonu."],
            ["_BusinessActionButton.build", "Etkinlik üç koşulun birleşimidir: `availability.enabled`, `mutationGate.canMutate`, `!submitting`. Devre dışıysa sebep hem tooltip'te hem buton içinde küçük punto ile basılır — bir el terminalinde hover olmadığı için tooltip tek başına yeterli değildir."],
            ["_StateBadgeState._syncAnimation", "Yalnız REQUESTED, QUEUED, IN_PROGRESS için nabız animasyonu kurar; diğer durumlarda controller dispose edilir. `MediaQuery.disableAnimations` etkinse animasyon hiç kurulmaz ve `Semantics` etiketi her iki yolda da aynı bilgiyi taşır."],
            ["_stateLabel", "Sunum durumlarını Türkçe etiketlere çevirir; bilinmeyen değer \"Güncelleniyor\" olur."],
        ],
        "deps": "`AppController`, `pda_models.dart`.",
        "callers": "`operation_screen.dart`.",
        "state": "Durumsuz widget; durum `AppController`'dadır. Yalnız `_StateBadge` kendi animasyon controller'ını tutar ve `dispose` eder.",
        "rules": [
            "Buton anahtarı MANTIKSAL iş aksiyonunu adlandırır ve kasa numarasını içerir: `direct-action-{ACTION}-{positionNumber}`.",
            "Onay diyaloğu yoktur; WP-33 doğrudan gönderim ister.",
            "\"Tüm Kasaları Kaldır\" yalnız batch yapılandırma modunda değilken gösterilir.",
        ],
        "errors": "Sunucu tarafı sebepler `availability.disabledMessage` ile gelir ve olduğu gibi gösterilir.",
        "security": "Fiziksel slot kimliği bu widget'a hiç ulaşmaz.",
        "tests": "`src/TkgPda.Pda/test/widget/wp33_operator_ux_test.dart`.",
    },
    "src/TkgPda.Pda/lib/features/transport/submission_coordinator.dart": {
        "purpose": "İstemci tarafı çift gönderim koruması ve idempotency anahtarı yaşam döngüsü.",
        "resp": [
            "Maddi imzaya bağlı idempotency anahtarı üretmek.",
            "Aynı anda birden fazla gönderimi engellemek.",
            "Belirsiz mutasyon sonrası anahtarı korumak.",
        ],
        "methods": [
            ["keyFor", "Komutun maddi imzası değiştiyse yeni anahtar üretir, değişmediyse mevcut anahtarı döndürür. Böylece aynı iş tekrar denendiğinde sunucu tarafı idempotency devreye girer."],
            ["submit", "`_inFlight` ise `StateError` fırlatır. Başarıda `clear()` çağırır. `AmbiguousMutationException` yakalanırsa `_ambiguous` işaretlenir ve **anahtar temizlenmez** — sonuç bilinmiyorken yeni anahtar üretmek çift fiziksel hareket riski doğururdu."],
            ["resolveByRefresh", "Kullanıcı durumu yenileyerek belirsizliği çözdüğünde anahtarı temizler."],
            ["clear", "İmza, anahtar ve belirsizlik bayrağını sıfırlar."],
        ],
        "deps": "`uuid`, `ApiProblem`, `pda_models.dart`.",
        "callers": "`AppController.submitContainerAction` ve batch gönderim yolları.",
        "state": "Maddi imza, bekleyen anahtar, uçuş ve belirsizlik bayrakları.",
        "rules": [
            "Belirsiz mutasyon sonrası otomatik yeniden gönderim yoktur.",
            "Aynı anda yalnız bir taşıma isteği gönderilebilir.",
        ],
        "errors": "`StateError` (eşzamanlı gönderim), `AmbiguousMutationException` (yeniden fırlatılır).",
        "security": "Bir kasa taşıma emri belirsiz durumdayken körlemesine tekrarlanmaz.",
        "tests": "`src/TkgPda.Pda/test/unit/submission_presentation_test.dart`.",
    },
    "src/TkgPda.Pda/lib/data/repositories/pda_repository.dart": {
        "purpose": "Operatör uygulamasının tek HTTP erişim noktası. Her API çağrısı burada tiplenir.",
        "resp": [
            "API uçlarını tip güvenli metotlara sarmak.",
            "Idempotency başlığını iletmek.",
            "Hata gövdelerini `ApiProblem`'e çevirmek.",
        ],
        "methods": [
            ["login / me", "Kimlik uçları."],
            ["operationalContext / switchVariant", "Bağlam ve varyant."],
            ["createTransport / createTransportBatch", "Genel taşıma oluşturma."],
            ["createContainerAction / createContainerActionBatch / removeAllContainers", "WP-33 kasa aksiyonları."],
            ["operatorQueue / transport / cancel", "Kuyruk ve iptal."],
            ["markTransportReady", "Depo hazırlık."],
            ["robotAlarmSummary", "Operatöre gösterilen alarm bandı verisi."],
            ["pauseRobot / resumeRobot / clearRobotErrors / setRequestPriority", "Yetkili roller için uzman operasyonlar; sunucu tarafında mutasyon kapısına tabidir."],
        ],
        "deps": "`ApiClient`, `pda_models.dart`, `api_problem.dart`.",
        "callers": "`AppController`.",
        "state": "Durumsuz.",
        "rules": [
            "İstemci hiçbir zaman RDS ile doğrudan konuşmaz.",
        ],
        "errors": "`ApiProblem` üzerinden sunucu kodları.",
        "security": "Tüm istekler JWT ve `X-Device-Code` taşır.",
        "tests": "`src/TkgPda.Pda/test/support/fakes.dart` ile sahtelenir; widget testleri bunu kullanır.",
    },
    "src/TkgPda.AdminWeb/src/pages/LayoutEditorPage.tsx": {
        "purpose": "Projedeki en yoğun tek arayüz sayfası (966 satır). Layout slot geometrisi, slot ↔ parça eşlemesi ve RDS eşlemeleri burada yönetilir.",
        "resp": [
            "Slot listesini ve geometrisini düzenlemek.",
            "Slot ↔ parça ilişkisini kurmak ve kaldırmak.",
            "RDS navigasyon profili, slot eşlemesi ve WorkSite verilerini göstermek.",
            "Değişiklikleri optimistik olmayan biçimde kaydedip sorguları geçersizleştirmek.",
        ],
        "methods": [
            ["Sekiz API ailesi kullanımı", "`layouts`, `slots`, `machines`, `containers`, `parts`, `rdsNavigationProfiles`, `rdsSlotMappings`, `rdsWorkSites`. Bu sayfa, konfigürasyonun farklı alanlarının kesiştiği tek yerdir."],
            ["Slot düzenleme", "ui_x / ui_y / ui_width / ui_height alanları ile yerleşim; kaydetme sonrası ilgili sorgular yeniden çekilir."],
            ["Parça eşleme", "`master.layout_slot_parts` ilişkisini kurar — çalışma zamanı yönlendirmesinin yetkili kaynağı."],
        ],
        "deps": "TanStack Query, `api/client.ts`, `components/ui.tsx`, `components/Toast.tsx`.",
        "callers": "`App.tsx` router.",
        "state": "Sunucu durumu TanStack Query'de; form durumu yerel hook'larda.",
        "rules": [
            "`metadata_json` içindeki köken bilgisi burada gösterilebilir ama yönlendirme kararı vermez.",
        ],
        "errors": "409 CONFIGURATION_CONFLICT eşzamanlı düzenlemede gösterilir.",
        "security": "administrator politikası gerektirir.",
        "tests": "`src/TkgPda.AdminWeb/src/pages/LayoutEditorPage.test.tsx`.",
    },
    "src/TkgPda.AdminWeb/src/pages/RdsIntegrationPage.tsx": {
        "purpose": "RDS entegrasyonunun teknik teşhis yüzeyi: robot telemetrisi, alarmlar, kilit durumu, izin verilen operasyonlar ve uzman mutasyonlar.",
        "resp": [
            "Filo telemetrisini severity ayrımıyla göstermek.",
            "Çekirdek düzeyi alarmları robot alarmlarından ayrı sunmak.",
            "Uzman operasyonları tetiklemek ve sonuçlarını göstermek.",
            "Mutasyon kapısı kapalıyken sebebi anlaşılır biçimde iletmek.",
        ],
        "methods": [
            ["Telemetri tablosu", "Robot başına kimlik, harita, bağlantı, dispatch edilebilirlik, batarya (null ise UNAVAILABLE), alarm listesi ve kilit durumu."],
            ["Uzman aksiyonlar", "manualFinished, redoFailedOrder, setPriority, robot bakımı. Hepsi sunucudaki mutasyon kapısına tabidir."],
        ],
        "deps": "TanStack Query, `api/client.ts`.",
        "callers": "`App.tsx` router.",
        "state": "Sunucu durumu sorgularda.",
        "rules": [
            "Arayüzde bir butonun görünür olması hareketin izinli olduğu anlamına gelmez; karar sunucudadır.",
            "Warning/notice alarmlar bloklayan alarmlardan görsel olarak ayrılır.",
        ],
        "errors": "RDS_DISPATCH_DISABLED kullanıcıya açıklayıcı mesaj olarak gösterilir.",
        "security": "administrator/variant-manager politikası gerektirir.",
        "tests": "`src/TkgPda.AdminWeb/src/pages/RdsIntegrationPage.test.tsx`.",
    },
    "db/migrations/001_initial_schema.sql": {
        "purpose": "Şemanın temeli. Beş şema ve 29 tablo ile birlikte kritik kısıtları kurar.",
        "resp": [
            "auth, master, ops, integration, audit şemalarını oluşturmak.",
            "Ana veri, çalışma zamanı ve entegrasyon tablolarını tanımlamak.",
            "Doluluk/yük tutarlılığını CHECK kısıtı ile korumak.",
            "Idempotency benzersizliğini indeksle zorlamak.",
        ],
        "methods": [
            ["ck_slot_load_requires_occupancy", "Dolu olmayan bir slotun EMPTY/FULL yük durumu taşımasını engeller. İki eksenin bağımsızlığı burada sınırlanır: yük ancak doluluk varsa anlamlıdır."],
            ["uq_layout_slot_code", "(layout_id, code) benzersiz. Layout klonlamanın `code` üzerinden eşleme yapabilmesinin sebebi budur."],
            ["uq_transport_idempotency", "Aynı idempotency anahtarıyla iki taşıma kaydını imkânsız kılar."],
        ],
        "deps": "PostgreSQL 18.",
        "callers": "`tools/dev/start_local_stack.ps1`, devreye alma kurulumu.",
        "state": "Şema.",
        "rules": [
            "Şemanın tek gerçek kaynağı budur; EF migration üretilmez.",
        ],
        "errors": "Kısıt ihlalleri API'de 409 CONFIGURATION_CONFLICT'e çevrilir.",
        "security": "Yetki tabloları (`auth.*`) ve denetim tabloları (`audit.*`) burada tanımlanır.",
        "tests": "`tests/TkgPda.Infrastructure.Postgres.Tests` bu şemaya karşı çalışır.",
    },
    "db/migrations/005_layout_slot_part_mapping.sql": {
        "purpose": "Parça ↔ Slot yönlendirmesinin yetkili tablosunu ekler. Operatörün slot koduna elle dokunmasını gereksiz kılar.",
        "resp": [
            "`master.layout_slot_parts` tablosunu oluşturmak.",
            "Ters arama indeksini eklemek.",
        ],
        "methods": [
            ["Tablo tanımı", "(layout_slot_id, part_id) birincil anahtar; her iki yabancı anahtar `ON DELETE CASCADE`."],
            ["ix_layout_slot_parts_part_id", "\"Bu parça hangi slotlara uygun?\" — otomatik makine-slot çözümlemesinin gerçekten sorguladığı yön."],
        ],
        "deps": "001 migration.",
        "callers": "Şema kurulumu.",
        "state": "Şema.",
        "rules": [
            "Çoktan-çoğa. Bir slot birden çok parça kodu tutabilir; bir parça birden çok slotta olabilir. \"Bir parça → bir global slot\" kısıtı uygulanmaz.",
            "Yalnız kimlik ilişkisi kaydedilir; hiçbir çalışma zamanı durumu taşımaz.",
            "`layout_slots.metadata_json` içindeki çıkarım kanıtı bu tablonun yerine geçmez.",
        ],
        "errors": "Yok.",
        "security": "Yanlış eşleme robotu yanlış rafa gönderir — bu yüzden ilişki elle küratörlüdür, otomatik çıkarım değil.",
        "tests": "`tests/TkgPda.Infrastructure.Postgres.Tests` klonlama testleri bu tabloyu kullanır.",
    },
    "scripts/run_commissioning.ps1": {
        "purpose": "API host'unu devreye alma modunda, fiziksel dispatch açık olarak başlatan tek betik.",
        "resp": [
            "`Rds__Enabled=true` ve `Rds__DispatchEnabled=true` ortamını kurmak.",
            "Bağlantı dizesini yerel yığın durum dosyasından çözmek.",
            "Eksik zorunlu yapılandırmada hızlı başarısız olmak.",
            "Sırsız bir başlangıç özeti basmak.",
        ],
        "methods": [
            ["Parametre çözümü", "Her sır önce ortam değişkeninden, sonra parametreden alınır. JWT anahtarı hiç verilmezse 48 baytlık kriptografik rastgele üretilir."],
            ["Bağlantı dizesi çözümü", "`%LOCALAPPDATA%\\TKG_PDA_DEV\\state\\stack_state.json` dosyasından okunur; depoya hiç yazılmaz."],
            ["Başlatma", "`dotnet run --no-launch-profile` — `launchSettings.json` git tarafından yok sayıldığı için paylaşılan bir profil olamaz."],
        ],
        "deps": "PowerShell 7+, .NET SDK, çalışan PostgreSQL.",
        "callers": "Devreye alma mühendisi (elle).",
        "state": "Ortam değişkenleri.",
        "rules": [
            "Üretimle aynı worker, aynı `RdsMutationPolicy` ve aynı native order yolu kullanılır — devreye alma için ayrı kod yolu yoktur.",
            "Normal geliştirme bu betiği çalıştırmaz.",
        ],
        "errors": "Eksik yapılandırmada hızlı hata.",
        "security": "Sırları asla sabit kodlamaz ve günlüğe basmaz. COMMISSIONING VALIDATION REQUIRED — bu betik gerçek robotu hareket ettirebilir.",
        "tests": "Otomatik testi yoktur; sahada elle doğrulanır.",
    },
}


# --------------------------------------------------------------------------
# Role descriptions for files without authored analysis.
# --------------------------------------------------------------------------
ROLES = [
    ("src/TkgPda.Pda/lib/core/network/", "Operatör uygulamasının HTTP taşıma katmanı: JWT ve cihaz başlığı ekleme, zaman aşımı, hata gövdesi ayrıştırma."),
    ("src/TkgPda.Pda/lib/core/auth/", "Oturum belleği. JWT yalnız bellekte tutulur, diske yazılmaz."),
    ("src/TkgPda.Pda/lib/core/config/", "Çalışma zamanı yapılandırma yükleyicisi; web ve yerel platformlar için koşullu import ile ayrışır."),
    ("src/TkgPda.Pda/lib/core/errors/", "Sunucu problem gövdesinin Dart tarafı temsili ve kullanıcıya gösterilecek mesaj üretimi."),
    ("src/TkgPda.Pda/lib/core/platform/", "Platform tespiti; koşullu import ile web ve IO ayrılır."),
    ("src/TkgPda.Pda/lib/core/storage/", "Cihaz kodu kalıcı depolaması. Cihaz kodu bir sır değil, cihaz kimliğidir; bu yüzden kalıcıdır."),
    ("src/TkgPda.Pda/lib/data/models/", "API sözleşmesinin Dart tarafı modelleri; JSON ayrıştırma burada yapılır."),
    ("src/TkgPda.Pda/lib/features/", "Operatör ekranı. Yalnız `AppController` durumunu tüketir ve fiziksel yönlendirme kararı vermez."),
    ("src/TkgPda.Pda/lib/shared/", "Ekranlar arasında paylaşılan sunum yardımcıları ve geri bildirim bileşenleri."),
    ("src/TkgPda.Pda/lib/app/", "Uygulama kabuğu: yönlendirme, tema ve kök widget."),
    ("src/TkgPda.Pda/lib/main.dart", "Flutter giriş noktası; yapılandırmayı yükler ve kök widget'ı çalıştırır."),
    ("src/TkgPda.Pda/test/unit/", "Flutter birim testi; iş mantığı ve sunum dönüşümlerini widget ağacı olmadan doğrular."),
    ("src/TkgPda.Pda/test/widget/", "Flutter widget testi; gerçek widget ağacında operatör akışını doğrular."),
    ("src/TkgPda.Pda/test/support/", "Test sahteleri (fake repository) — üretim kodunda karşılığı yoktur."),
    ("src/TkgPda.Pda/integration_test/", "Uçtan uca operatör senaryosu; gerçek API'ye karşı çalışır."),
    ("src/TkgPda.Pda/", "Flutter proje yapılandırması."),
    ("src/TkgPda.AdminWeb/src/api/", "Admin Web API istemcisi ve tip sözleşmesi. Her uç burada tiplenir."),
    ("src/TkgPda.AdminWeb/src/auth/", "Admin kimlik bağlamı. Token yalnız bellekte tutulur; sayfa yenilemesi oturumu düşürür."),
    ("src/TkgPda.AdminWeb/src/components/", "Paylaşılan arayüz bileşenleri: kabuk, bildirim, temel UI ilkelleri."),
    ("src/TkgPda.AdminWeb/src/pages/", "Admin sayfa bileşeni. Sunucu durumu TanStack Query ile yönetilir."),
    ("src/TkgPda.AdminWeb/src/test/", "Vitest kurulum ve render yardımcıları."),
    ("src/TkgPda.AdminWeb/e2e/", "Playwright uçtan uca senaryosu; gerçek tarayıcı ve gerçek API kullanır."),
    ("src/TkgPda.AdminWeb/src/", "Admin Web uygulama kökü."),
    ("src/TkgPda.AdminWeb/", "Admin Web proje/araç yapılandırması."),
    ("src/TkgPda.Api/", "API katmanı: HTTP yüzeyi, güvenlik ve RDS adaptörleri."),
    ("src/TkgPda.Application/", "Uygulama katmanı: iş akışı orkestrasyonu ve politikalar. SQL veya HTTP içermez."),
    ("src/TkgPda.Domain/", "Domain katmanı: saf iş kuralları. I/O içermez."),
    ("src/TkgPda.Infrastructure.Postgres/", "PostgreSQL erişim katmanı: ham SQL, işlem sınırları, advisory lock."),
    ("db/migrations/", "Şema migration'ı. Şemanın tek gerçek kaynağı; sıralı ve idempotent uygulanır."),
    ("db/seeds/generated/", "Müşteri kaynak verisinden ÜRETİLMİŞ seed SQL. Gerçek veritabanlarına karşı çalıştırıldığı için dokümante edilir; elle düzenlenmez, kaynak veri değişirse yeniden üretilir."),
    ("db/seeds/", "Seed verisi."),
    ("tests/TkgPda.Domain.Tests/", "Domain birim testi; iş kurallarını I/O olmadan doğrular."),
    ("tests/TkgPda.Application.Tests/", "Uygulama katmanı testi; orkestrasyon, RDS politikaları ve kimlik çözümlemesi."),
    ("tests/TkgPda.Auth.Tests/", "Kimlik ve yetki testi."),
    ("tests/TkgPda.Api.Tests/", "API sözleşme ve adaptör testi; bir kısmı gerçek PostgreSQL gerektirir."),
    ("tests/TkgPda.Infrastructure.Postgres.Tests/", "PostgreSQL entegrasyon testi; gerçek veritabanı zorunludur."),
    ("tests/phase_", "Faz doğrulama betiği/kaydı; ilgili çalışma paketinin kabul kanıtı."),
    ("tests/wp30/", "WP-30 doğrulama kaydı."),
    ("tests/db/", "Veritabanı doğrulama betiği."),
    ("tools/dev/", "Yerel geliştirme yığını betiği. Durum dosyası `%LOCALAPPDATA%` altında tutulur; depoya sır yazılmaz."),
    ("tools/phase_", "Müşteri kaynak verisi dönüştürme aracı. Çıktıları üretilmiş kabul edilir."),
    ("scripts/", "Operasyon betiği."),
    ("rds/scripts/", "RDS tarafı yardımcı betik. Üretim eşlemesi değildir; sahada doğrulanmalıdır."),
    ("rds/tasks/", "RDS görev şablonu örneği. Üretim geometrisi değildir; COMMISSIONING VALIDATION REQUIRED."),
    ("docs/commissioning/", "Devreye alma dokümanı; saha prosedürleri ve kanıt kayıtları."),
    ("docs/database/", "Veritabanı dokümanı."),
    ("docs/integration/", "Entegrasyon dokümanı."),
    ("docs/references/", "Referans dokümanı."),
    ("docs/", "Proje dokümanı; ilgili fazın veya çalışma paketinin kararlarını kaydeder."),
]

EXT_KIND = {
    ".cs": "C# kaynak",
    ".dart": "Dart kaynak",
    ".ts": "TypeScript kaynak",
    ".tsx": "TypeScript React bileşeni",
    ".js": "JavaScript",
    ".mjs": "JavaScript modülü",
    ".css": "Stil sayfası",
    ".html": "HTML",
    ".sql": "SQL",
    ".ps1": "PowerShell betiği",
    ".sh": "Shell betiği",
    ".cmd": "Windows komut betiği",
    ".bat": "Windows toplu iş betiği",
    ".py": "Python betiği",
    ".json": "JSON yapılandırma",
    ".csproj": "MSBuild proje dosyası",
    ".slnx": "Çözüm dosyası",
    ".props": "MSBuild özellik dosyası",
    ".yaml": "YAML yapılandırma",
    ".yml": "YAML yapılandırma",
    ".xml": "XML yapılandırma",
    ".md": "Markdown doküman",
    ".txt": "Metin dosyası",
    ".properties": "Özellik dosyası",
    ".kts": "Kotlin betiği",
}


def _role(path):
    for prefix, text in ROLES:
        if path.startswith(prefix):
            return text
    return "Proje dosyası."


def _layer(path):
    if path.startswith("src/TkgPda.Domain"):
        return "Domain"
    if path.startswith("src/TkgPda.Application"):
        return "Application"
    if path.startswith("src/TkgPda.Infrastructure.Postgres"):
        return "Infrastructure"
    if path.startswith("src/TkgPda.Api"):
        return "API"
    if path.startswith("src/TkgPda.Pda"):
        return "Operator PDA"
    if path.startswith("src/TkgPda.AdminWeb"):
        return "Admin Web"
    if path.startswith("db/"):
        return "Veritabanı"
    if path.startswith("tests/"):
        return "Test"
    if path.startswith("tools/") or path.startswith("scripts/"):
        return "Araç/Betik"
    if path.startswith("docs/"):
        return "Doküman"
    if path.startswith("rds/"):
        return "RDS varlığı"
    return "Kök"


def _file_block(path, facts, generated_paths):
    b = []
    b.append((H, 3, path))
    ext = os.path.splitext(path)[1].lower()
    kind = EXT_KIND.get(ext, "Dosya")
    deep = path in DEEP
    depth = DEPTH_DEEP if deep else (DEPTH_STRUCT if facts.get("loc", 0) > 25 else DEPTH_BRIEF)
    heads = ["Tür", "Katman", "Satır", "Analiz derinliği"]
    row = [kind, _layer(path), str(facts.get("loc", "-")), depth]
    if path in generated_paths:
        heads.append("Üretim")
        row.append("ÜRETİLMİŞ — elle düzenlenmez")
    b.append((T, heads, [row]))

    if deep:
        d = DEEP[path]
        b.append((P, "**Amaç.** " + d["purpose"]))
        b.append((P, "**Sorumluluklar.**"))
        b.append((UL, d["resp"]))
        types = facts.get("types") or []
        if types:
            b.append((P, "**Tanımlanan tipler.** " + ", ".join(f"`{t}`" for t in types)))
        b.append((P, "**Metotlar ve mantık.**"))
        b.append((T, ["Metot", "Ne yapar ve neden"], [[m, why] for m, why in d["methods"]]))
        b.append((P, "**Bağımlılıklar.** " + d["deps"]))
        b.append((P, "**Çağıranlar.** " + d["callers"]))
        b.append((P, "**Durum ve yan etki.** " + d["state"]))
        b.append((P, "**İş kuralları / değişmezler.**"))
        b.append((UL, d["rules"]))
        b.append((P, "**Hata yönetimi.** " + d["errors"]))
        b.append((P, "**Güvenlik.** " + d["security"]))
        b.append((P, "**Testler.** " + d["tests"]))
        return b

    b.append((P, "**Rol.** " + _role(path)))
    types = facts.get("types") or []
    methods = facts.get("methods") or []
    objects = facts.get("objects") or []
    imports = facts.get("imports") or []
    api_calls = facts.get("api_calls") or []
    hooks = facts.get("hooks") or []
    params = facts.get("params") or []

    if types:
        b.append((P, "**Tanımlanan tipler.** " + ", ".join(f"`{t}`" for t in types)))
    if methods:
        b.append((P, "**Tanımlanan üyeler.** " + ", ".join(f"`{m}`" for m in methods)))
    if objects:
        b.append((P, "**Oluşturulan veritabanı nesneleri.** " + ", ".join(f"`{o}`" for o in objects)))
    if params:
        b.append((P, "**Parametreler.** " + ", ".join(f"`{p}`" for p in params)))
    if api_calls:
        b.append((P, "**Kullandığı API aileleri.** " + ", ".join(f"`api.{a}`" for a in api_calls)))
    if hooks:
        b.append((P, "**Kullandığı React hook'ları.** " + ", ".join(f"`{h}`" for h in hooks)))
    if imports:
        b.append((P, "**Proje içi bağımlılıklar.** " + ", ".join(f"`{i}`" for i in imports)))
    if not (types or methods or objects or params or api_calls or imports):
        b.append((P, "Yapısal olarak çıkarılabilir bir tip veya üye tanımı içermez; içeriği yapılandırma, doküman veya veri metnidir."))
    return b


def section_files(inventory, facts_path=None):
    facts_path = facts_path or os.path.join(BUILD, "facts.json")
    with io.open(facts_path, encoding="utf-8") as fh:
        facts_map = json.load(fh)

    documented = [p for cat, p, _ in inventory if cat == "DOCUMENTED"]
    generated_paths = {p for p in documented if p.startswith("db/seeds/generated/")}

    b = []
    b.append((H, 1, "Dosya Dosya Kaynak Referansı"))
    b.append((P, f"Bu bölüm envanterde DOCUMENTED olarak sınıflanan **{len(documented)} dosyanın her biri** için ayrı bir alt bölüm içerir. Alt bölümler, kaynak ağacındaki gruplara göre düzenlenmiştir."))
    b.append((N, "NOTE", "Analiz derinliği her dosyada açıkça belirtilir. **Derin analiz**: gerçek uygulama mantığı taşıyan, elle yazılmış açıklama. **Yapısal**: dosyadan okunan gerçek tip/üye bilgisi ve konumundan türetilen rol. **Kısa**: küçük yapılandırma veya veri dosyası. Bu ayrım bilinçlidir; her dosya için elle yazılmış analiz iddia etmek dürüst olmazdı."))

    groups = [
        ("Domain katmanı", lambda p: p.startswith("src/TkgPda.Domain")),
        ("Application katmanı", lambda p: p.startswith("src/TkgPda.Application")),
        ("Infrastructure — PostgreSQL", lambda p: p.startswith("src/TkgPda.Infrastructure.Postgres")),
        ("API katmanı", lambda p: p.startswith("src/TkgPda.Api")),
        ("Operator PDA — Flutter", lambda p: p.startswith("src/TkgPda.Pda")),
        ("Admin Web — React", lambda p: p.startswith("src/TkgPda.AdminWeb")),
        ("Veritabanı — migration ve seed", lambda p: p.startswith("db/")),
        ("Testler", lambda p: p.startswith("tests/")),
        ("Araçlar ve betikler", lambda p: p.startswith("tools/") or p.startswith("scripts/")),
        ("RDS varlıkları", lambda p: p.startswith("rds/")),
        ("Dokümanlar", lambda p: p.startswith("docs/")),
        ("Kök seviyesi dosyalar", lambda p: "/" not in p),
    ]

    assigned = set()
    for title, pred in groups:
        paths = sorted(p for p in documented if pred(p) and p not in assigned)
        if not paths:
            continue
        assigned.update(paths)
        b.append((H, 2, f"{title} ({len(paths)})"))
        for p in paths:
            b.extend(_file_block(p, facts_map.get(p, {}), generated_paths))

    leftovers = sorted(p for p in documented if p not in assigned)
    if leftovers:
        b.append((H, 2, f"Diğer ({len(leftovers)})"))
        for p in leftovers:
            b.extend(_file_block(p, facts_map.get(p, {}), generated_paths))

    return b, len(documented), sorted(assigned | set(leftovers))
