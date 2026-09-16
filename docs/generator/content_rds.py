"""RDS integration, safety and execution sections of the technical reference."""

H, P, UL, OL, T, C, PRE, N, PB = "h", "p", "ul", "ol", "table", "code", "pre", "note", "pagebreak"


def section_container_pipeline():
    b = []
    b.append((H, 1, "Container Action Pipeline"))
    b.append((P, "Operatörün bir kasa butonuna basmasından RDS'e sipariş gitmesine kadar geçen yol tek bir hattır. Bu bölüm o hattın her durağını sırayla anlatır."))
    b.append((OL, [
        "**Flutter — `_BusinessActionButton`.** `direct-action-REMOVE_FULL-{n}` veya `direct-action-BRING_EMPTY-{n}` anahtarlı buton `controller.submitContainerAction(position, action)` çağırır. Onay diyaloğu YOKTUR; WP-33 doğrudan gönderim ister.",
        "**Flutter — `AppController.submitContainerAction`.** İstek gövdesine mantıksal pozisyon numarası ve aksiyon konur; fiziksel slot kimliği istemcide hiç bulunmaz. Idempotency anahtarı burada üretilir.",
        "**API — `ApiEndpoints`.** Kimlik, cihaz ve rol doğrulanır. `EnsureTransportOperationAllowed` operatörün seçemeyeceği operasyonları burada eler.",
        "**Application — `ContainerActionPolicy`.** Slotun mevcut occupancy/load durumundan hangi domain operasyonunun üretileceğine karar verir; uygun değilse iş kuralı hatası fırlatır.",
        "**Infrastructure — `PostgresTransportWorkflowStore.ContainerActions`.** Tek bir transaction içinde: idempotency advisory lock alınır, gerekiyorsa yük durumu EMPTY→FULL güncellenir, `ops.transport_requests` satırı yazılır, olay `ops.transport_request_events` tablosuna işlenir.",
        "**Worker — `RdsExecutionCoordinator.DispatchQueuedAsync`.** Kuyruktaki talepler periyodik olarak toplanır (`Rds:WorkerIntervalSeconds`, varsayılan 3 sn).",
        "**Coordinator — `DispatchNativeOrderAsync`.** Navigation profile çözümlenir, robot kirası alınır, telemetri doğrulanır, gerekiyorsa harita hazırlığı yapılır, `/setOrder` gönderilir.",
        "**Reconciliation — `ReconcileAsync`.** RDS'teki sipariş durumu periyodik okunur ve TKG durumu buna göre ilerletilir.",
    ]))
    b.append((N, "NOTE", "Idempotency anahtarı zincirin en başında üretilir ve sonuna kadar taşınır. Operatör aynı butona iki kez bassa bile ikinci istek aynı anahtarla gelir ve yeni bir fiziksel hareket doğurmaz."))
    return b


def section_rds_arch():
    b = []
    b.append((H, 1, "RDS Entegrasyon Mimarisi"))
    b.append((P, "RDS entegrasyonu tek bir istemci değil, farklı sözleşmeleri olan dört adaptörden oluşur. Her biri ayrı bir taban adres ve ayrı bir sorumluluk taşır."))
    b.append((T, ["Adaptör", "Dosya", "Taban adres", "Sorumluluk"], [
        ["RdsReadClient", "src/TkgPda.Api/RdsReadClient.cs", "Rds:BaseUrl (varsayılan :8080)", "WorkSite/WindTask okuma, salt-okunur telemetri"],
        ["RdsExecutionClient", "src/TkgPda.Api/RdsExecutionClient.cs", "Rds:BaseUrl", "WindTask tabanlı yürütme motoru"],
        ["RdsCoreNativeOrderClient", "src/TkgPda.Api/RdsCoreNativeOrderClient.cs", "RdsCore:BaseUrl (varsayılan :8088)", "Native order motoru: /setOrder, /terminate, /robotsStatus, /lock, /switchMap, /unlock"],
        ["RdsCommissioningDiscoveryClient", "src/TkgPda.Api/RdsCommissioningDiscoveryClient.cs", "RdsCore:BaseUrl", "Devreye alma keşfi: park noktaları, mesafe sorgusu, çekirdek parametreler"],
    ]))
    b.append((H, 2, "İzin verilen RDSCore operasyonları"))
    b.append((P, "`RdsCoreNativeOrderClient` içinde her operasyon bir kayıt tablosunda (`Registry`) HTTP metodu, yolu ve mutasyon olup olmadığıyla birlikte tanımlıdır. Kayıtlı olmayan hiçbir yol çağrılamaz; bu, denemelik bir RDSCore ucunun koda sızmasını engelleyen yapısal bir kısıttır."))
    b.append((T, ["Operasyon", "Metot", "Yol", "Mutasyon"], [
        ["SetOrder", "POST", "/setOrder", "Evet"],
        ["OrderDetails", "GET", "/orderDetails/{id}", "Hayır"],
        ["Orders", "GET", "/orders", "Hayır"],
        ["OrderDetailsByExternalId", "GET", "/orderDetailsByExternalId/{externalId}", "Hayır"],
        ["OrderDetailsByBlockId", "GET", "/orderDetailsByBlockId/{blockId}", "Hayır"],
        ["BlockDetailsById", "GET", "/blockDetailsById/{blockId}", "Hayır"],
        ["Terminate", "POST", "/terminate", "Evet"],
        ["SetPriority", "POST", "/setPriority", "Evet"],
        ["ClearRobotAllError", "POST", "/clearRobotAllError", "Evet"],
        ["GotoSitePause", "POST", "/gotoSitePause", "Evet"],
        ["GotoSiteResume", "POST", "/gotoSiteResume", "Evet"],
        ["ManualFinished", "POST", "/manualFinished", "Evet"],
        ["RedoFailedOrder", "POST", "/redoFailedOrder", "Evet"],
        ["Dispatchable", "POST", "/dispatchable", "Evet"],
        ["RobotsStatus", "GET", "/robotsStatus", "Hayır"],
        ["Lock", "POST", "/lock", "Evet"],
        ["Unlock", "POST", "/unlock", "Evet"],
        ["SwitchMap", "POST", "/switchMap", "Evet"],
    ]))
    b.append((N, "FACT", "CODE-CONFIRMED — `src/TkgPda.Api/RdsCoreNativeOrderClient.cs`, `Registry` sözlüğü."))
    b.append((H, 2, "Commissioning keşif uçları"))
    b.append((P, "Devreye alma sırasında sahanın gerçek geometrisini okumak için kullanılan salt-okunur uçlar: `/binCheck`, `/devicesDetails`, `/getRobotParkPoints`, `/getChargeParam`, `/getDisablePoints`, `/getDisablePaths`, `/getCoreParam`, `/queryConnectivity`, `/queryDist`. Bunların hiçbiri hareket doğurmaz."))
    b.append((N, "WARN", "COMMISSIONING VALIDATION REQUIRED — Bu uçlardan dönen park noktası, mesafe ve çekirdek parametre değerleri sahaya özgüdür. Depoda bulunan hiçbir örnek değer üretim haritasının gerçeği değildir."))
    return b


def section_mutation_policy():
    b = []
    b.append((H, 1, "Mutasyon Politikası ve Fiziksel Hareket Kapısı"))
    b.append((P, "`RdsMutationPolicy` projedeki en güvenlik-kritik tek dosyadır. Bir RDS operasyonunun fiziksel işe göre sınıfını tanımlar ve global dispatch kapısının ne anlama geldiğinin tek tanımıdır."))
    b.append((H, 2, "Dört mutasyon sınıfı"))
    b.append((T, ["Sınıf", "Tanım", "Kapı kapalıyken"], [
        ["ReadOnly", "Yalnız okuma. Hiç kapılanmaz.", "İzinli"],
        ["SafetyStop", "Zaten süren işi durdurur veya duraklatır: `/terminate`, `/gotoSitePause`, robotu dispatch dışı bırakma.", "İzinli — hareketi azaltır"],
        ["MotionEnabling", "Fiziksel işi başlatır, sürdürür, yeniden etkinleştirir veya ilerletir: `/gotoSiteResume`, `dispatchable=true`, `/clearRobotAllError`, `/manualFinished`, `/redoFailedOrder`.", "Reddedilir"],
        ["ExecutionMutation", "Yürütme kimliğini veya sırasını değiştirir: `/setOrder`, WindTask oluşturma, `/setPriority`, ve dispatch öncesi `/lock` + `/switchMap` + `/unlock` hazırlığı.", "Reddedilir"],
    ]))
    b.append((C, """public static bool IsAllowed(RdsMutationClass mutationClass, bool globalDispatchEnabled) =>
    mutationClass switch
    {
        RdsMutationClass.ReadOnly => true,
        RdsMutationClass.SafetyStop => true,
        _ => globalDispatchEnabled
    };""", "csharp"))
    b.append((H, 2, "Neden `/clearRobotAllError` MotionEnabling sayılır?"))
    b.append((P, "Adı bir temizleme işlemi gibi görünse de, robotu tutan hatayı kaldırmak robotu yeniden hareket edebilir hale getirir. Politika, operasyonun adına değil fiziksel sonucuna bakar. Aynı mantıkla `/manualFinished` bir sonraki bloğa ilerlettiği, `/redoFailedOrder` bir siparişi yeniden çalıştırdığı için MotionEnabling'dir."))
    b.append((H, 2, "`/dispatchable` sınıflaması dinamiktir"))
    b.append((C, """public static RdsMutationClass ClassifyDispatchable(string? type) =>
    (type ?? string.Empty).Trim().ToLowerInvariant() == "dispatchable"
        ? RdsMutationClass.MotionEnabling
        : RdsMutationClass.SafetyStop;""", "csharp"))
    b.append((P, "Aynı uç, gövdesindeki `type` alanına göre farklı sınıfa düşer: robotu dispatch edilebilir yapmak hareketi mümkün kılar, dispatch dışına almak ise güvenliği artırır."))
    b.append((H, 2, "Rol yetkisi kapıyı ikame etmez"))
    b.append((N, "WARN", "Bir operasyonun yalnız Admin/Supervisor'a açık olması onu izinli kılmaz. `EnsureMutationAllowed` her mutasyon yolunda ayrıca çağrılır ve `RDS_DISPATCH_DISABLED` ile reddeder. Kaynak kodundaki yorum bunu açıkça söyler: *Being Admin/Supervisor-only does not by itself make an operation permissible.*"))
    b.append((H, 2, "Dispatch uygunluğu bileşimi"))
    b.append((C, """private bool IsGlobalDispatchAllowed => settings.DispatchEnabled;

private bool IsNativeMode => nativeOrderClient is not null
    && nativeOrderClient.Mode == RdsExecutionMode.RdsCoreNativeOrder
    && nativeOrderClient.MutationEnabled;

public bool IsDispatchEnabled => IsGlobalDispatchAllowed
    && (executionClient.DispatchEnabled || IsNativeMode);""", "csharp"))
    b.append((P, "Native motorun etkin olması global kapıyı asla atlayamaz: bileşim `AND`'dir. Motor seçimi ile fiziksel izin ayrı sorulardır."))
    return b


def section_identity():
    b = []
    b.append((H, 1, "Robot Kimliği ve Alias Çözümleme"))
    b.append((P, "SEER `/robotsStatus` üzerinde iki ayrı kimlik uzayı yayınlar ve bu ayrım geçmişte gerçek bir hataya yol açmıştır."))
    b.append((T, ["Alan", "Anlamı", "Hangi API'ler sözleşir"], [
        ["uuid", "Robotun iç kimliği", "/getRobotParkPoints, /queryDist (uuid anahtarlı harita döner)"],
        ["vehicle_id", "Dispatch tanımlayıcısı", "/setOrder (vehicle), /lock, /unlock, /switchMap, /manualFinished, /redoFailedOrder, /dispatchable"],
    ]))
    b.append((N, "WARN", "İki değer yerel simülatörde çoğunlukla aynı, gerçek donanımda çoğunlukla farklıdır. Bu yüzden hiçbiri evrensel olarak kanonik sayılamaz. Bu tip var olmadan önce iki okuyucu birbiriyle çelişiyordu — biri `uuid ?? vehicle_id`, diğeri `vehicle_id ?? uuid` tercih ediyordu; sonuç, kilit korelasyonunun kimlik uzayları arasında karşılaştırma yapması ve gerçek robotta sessizce başarısız olurken simülatörde geçmesiydi."))
    b.append((H, 2, "RdsRobotIdentity türetilmiş özellikleri"))
    b.append((T, ["Özellik", "Çözüm sırası", "Cevapladığı soru"], [
        ["MutationTarget", "VehicleId ?? Uuid", "Bu robotu hedeflerken RDSCore'a ne göndereyim?"],
        ["DiscoveryKey", "Uuid ?? VehicleId", "Keşif uçları hangi kimliği bekliyor?"],
        ["RobotLeaseKey", "VehicleId ?? Uuid", "Bu robotu hangi dağıtık kira kapsıyor?"],
        ["HasValue", "MutationTarget is not null", "Hedeflenebilir mi?"],
    ]))
    b.append((P, "`RobotLeaseKey` bugün `MutationTarget` ile aynı şekilde çözülür ama bilinçli olarak ayrı bir özelliktir. İlk ikisi *bu SEER ucu ne istiyor?* sorusuna, üçüncüsü *hangi kira bu robotu kapsıyor?* sorusuna cevap verir. Ayrı tutulmaları, gelecekte bir uç sözleşmesi değişirse kira alanının sessizce ikiye bölünmesini imkânsız kılar."))
    b.append((P, "Tek sert gereksinim şudur: aynı robotun iki alias'ı asla iki farklı kira üretmemelidir. Bu yüzden her çağıran kimliği önce telemetriden çözer (`ResolveRobotLeaseKeyAsync`); yalnız uuid'e sahip bir çağıran ile yalnız vehicle_id'ye sahip bir çağıran aynı değere varır."))
    b.append((H, 2, "Matches() — iki uzayda birden eşleme"))
    b.append((P, "Korelasyon girdileri (denetim kilit parmak izleri, görev şablonu `vehicle` değerleri, navigasyon profilleri) her iki uzayda da kaydedilmiş olabilir. `Matches` ikisinden birinde eşleşmeyi kabul eder, hiçbirinde eşleşmemeyi reddeder ve boş bir adaya asla genişlemez."))
    b.append((H, 2, "Alias çözümleme: NoMatch / Unique / Ambiguous"))
    b.append((P, "Bir alias iki uzayda da aranınca çakışma mümkün olur. Bu çakışma eskiden çağıranlar tarafından `FirstOrDefault` ile çözülüyordu — yani RDSCore hangi robotu önce listelediyse o hedefleniyordu. Hareket doğuran bir mutasyon için bu, mümkün olan en kötü hata biçimidir."))
    b.append((T, ["Senaryo", "Fiziksel makine sayısı", "Sonuç"], [
        ["Tek robot, uuid == vehicle_id == \"X\"", "1", "Unique"],
        ["Robot A uuid \"X\", Robot B vehicle_id \"X\"", "2", "Ambiguous — fail closed"],
        ["Aynı robot payload'da iki kez", "1", "Unique"],
        ["Hiçbir robot eşleşmiyor", "0", "NoMatch"],
    ]))
    b.append((P, "Ayrıklık tek bir çözülmüş string üzerinden değil, **tüm kimlik çifti** üzerinden hesaplanır. Yalnız lease key kullanmak `(uuid=X, vehicle=null)` ile `(uuid=null, vehicle=X)` çiftlerini aynı değere indirger ve tam da yakalanmak istenen belirsizliği gizlerdi."))
    b.append((C, """var distinctMachines = matches
    .Select(robot => (
        Uuid: Normalize(robot.ResolvedIdentity.Uuid),
        Vehicle: Normalize(robot.ResolvedIdentity.VehicleId)))
    .Distinct()
    .Count();

return distinctMachines == 1 ? RdsRobotMatch.Unique(matches[0]) : RdsRobotMatch.Ambiguous;""", "csharp"))
    b.append((P, "Belirsizlik dışarıya `ROBOT_ALIAS_AMBIGUOUS` hata kodu ile verilir."))
    return b


def section_lease():
    b = []
    b.append((H, 1, "Robot Kirası, Serileştirme ve TOCTOU"))
    b.append((P, "TKG aynı fiziksel robot üzerinde eşzamanlı iki operasyon yürütmeyi engellemek zorundadır. Talep başına kira bunun için yeterli değildir: iki farklı TransportRequest kendi talep kirasını tutup yine de aynı robota çözülebilir."))
    b.append((H, 2, "İki ayrı kira alanı"))
    b.append((T, ["Kira", "Anahtar biçimi", "Kapsam"], [
        ["Talep kirası", "tkg:rds:{requestId:D}", "Bir TransportRequest"],
        ["Robot kirası", "tkg:robot:{robotLeaseKey} (küçük harfe indirgenmiş)", "Bir fiziksel robot"],
    ]))
    b.append((P, "Ad alanı öneki anahtar dizesinin kendi içinde taşınır; bu sayede bir robot anahtarı bir talep anahtarı ile çarpışamaz — ikisi aynı hash uzayını paylaşsa bile."))
    b.append((H, 2, "Neden session düzeyinde advisory lock?"))
    b.append((C, """// Dedicated connection, SESSION-level advisory lock
"SELECT pg_try_advisory_lock(hashtextextended(@key, 0))"
command.Parameters.AddWithValue("key", $"tkg:robot:{robotLeaseKey.Trim().ToLowerInvariant()}");""", "csharp"))
    b.append((P, "İşlem düzeyinde (`pg_advisory_xact_lock`) bir kilit commit anında serbest bırakılır — yani fiziksel mutasyon henüz gönderilmeden önce. Bu yüzden robot kirası kendi ayrılmış bağlantısında session düzeyinde tutulur ve `await using` bloğunun örtük `finally`'sinde bırakılır: başarı, adaptör istisnası ve iptal yollarının hepsinde."))
    b.append((N, "NOTE", "Advisory lock PostgreSQL üzerinde tutulduğu için serileştirme tek bir işletim sistemi süreci içinde değil, TKG süreçleri arasında geçerlidir. Birden fazla API örneği çalıştığında da doğru kalır."))
    b.append((H, 2, "TOCTOU: EnsureLeaseCoversTarget"))
    b.append((P, "Kira, yetkili (authoritative) yeniden okumadan **önce** bir aday alias'tan alınır. Bu yüzden yeniden okuma meşru olarak farklı bir fiziksel robota inebilir: RDSCore arada siparişi başka robota atamış olabilir. Başka bir robotun kirasını tutarken bu robotu mutasyona uğratmak kiranın tüm amacını yok ederdi."))
    b.append((C, """private static void EnsureLeaseCoversTarget(string heldLeaseKey, RdsCoreRobotTelemetry target, string operation)
{
    var targetKey = target.ResolvedIdentity.RobotLeaseKey;
    if (!string.Equals(heldLeaseKey, targetKey, StringComparison.OrdinalIgnoreCase))
    {
        throw new RdsExecutionException(
            "EXPERT_ROBOT_CHANGED_DURING_VALIDATION",
            $"The robot for this request changed from '{heldLeaseKey}' to '{targetKey}' ...");
    }
}""", "csharp"))
    b.append((N, "WARN", "Kira ortada değiştirilmez. Yeni hedefin kirasına geçmek, kiranın kapatmak için var olduğu pencerenin ta kendisini yeniden açardı. Doğru davranış: reddet, çağıran taze durumdan tekrar denesin."))
    b.append((P, "Dispatch yolunda aynı değişmez `ROBOT_CHANGED_DURING_VALIDATION` koduyla, uzman (expert) yollarında `EXPERT_ROBOT_CHANGED_DURING_VALIDATION` koduyla uygulanır."))
    b.append((H, 2, "Kira alınamazsa"))
    b.append((P, "`TryAcquireRobotLeaseAsync` null dönerse operasyon `ROBOT_BUSY` ile reddedilir. Beklemez, kuyruğa girmez, zorlamaz. Dispatch yolunda bu `RDS_DISPATCH_DEFERRED` olarak denetlenir ve talep kuyrukta kalır."))
    b.append((H, 2, "Kimlik çözülemezse"))
    b.append((P, "`ResolveRobotLeaseKeyAsync` null dönerse — telemetri okunamadıysa veya alias belirsizse — güvenlik-kritik mutasyon için bu \"kilit gerekmiyor\" anlamına gelmez; fail-closed davranılır: uzman yollarında `EXPERT_CURRENT_STATE_UNAVAILABLE`, dispatch yolunda `ROBOT_IDENTITY_UNRESOLVED`."))
    return b


def section_alarms():
    b = []
    b.append((H, 1, "Alarm Modeli"))
    b.append((P, "Alarm ayrıştırma projede tek bir yerde yapılır: `RdsRobotAlarmParser`. Daha önce iki kopya vardı ve native telemetri istemcisindeki kopya `alarms.fatals` ile `rbk_report.alarms.fatals` yollarını okuyordu — bu yollar SEER dokümantasyonunda hiç geçmez, dolayısıyla gerçek bir payload'da hiçbir şeyle eşleşmiyordu. Sonuç: robotlar daima sıfır alarm bildiriyordu ve \"robotun bloklayan alarmı var\" kapısı hiç çalışamıyordu."))
    b.append((H, 2, "Severity ve bloklama"))
    b.append((T, ["Severity", "Bucket", "Bloklar mı?"], [
        ["Fatal", "fatals", "Evet"],
        ["Error", "errors", "Evet"],
        ["Warning", "warnings", "Hayır"],
        ["Notice", "notices", "Hayır"],
    ]))
    b.append((N, "WARN", "Alarm listesinin boş olmaması bir arıza demek DEĞİLDİR. Karar daima `HasBlockingAlarm` üzerinden verilir; bir notice asla bloklayan bir alarm gibi davranmamalı veya görünmemelidir."))
    b.append((H, 2, "Dört dokümante zarf (envelope)"))
    b.append((P, "Bucket adları daima çoğuldur ve hem bir durum nesnesinin doğrudan üzerinde hem de bir `alarms` sarmalayıcısının altında görünür. Tek bir dokümante `/robotsStatus` yanıtı iki formu birden taşır, bu yüzden biri diğerinin lehine atılamaz."))
    b.append((T, ["Zarf", "Yol", "Kaynak"], [
        ["Kök, sarmalı", "alarms.{fatals,errors,warnings,notices}", "SEER DOCX (çıkarım satırı L4052)"],
        ["Kök, doğrudan", "{fatals,errors,warnings,notices} — alarms ile kardeş", "Aynı yanıt, L4079; RdsScriptMethods_en.md:1040-1058"],
        ["Robot, sarmalı", "report[].rbk_report.alarms.{...}", "DOCX L4236, L4427"],
        ["Robot, doğrudan", "report[].rbk_report.{fatals,errors,...}", "RdsScriptMethods_en.md:1135-1136, 1166, 1195"],
    ]))
    b.append((N, "FACT", "DOCUMENTATION-CONFIRMED — Sarmalı bir bucket JSON null olabilir (DOCX L4443: alarms:{errors:null, fatals:null, notices:null, warnings:null}). Bu yalnızca \"alarm yok\" demektir ve istisna fırlatmamalıdır."))
    b.append((N, "NOTE", "Önceki bir inceleme turunda `alarms` sarmalayıcısının dokümante olmadığı sonucuna varılmıştı; bu, `RDS DÖKÜMANLARI/` altındaki Markdown dosyalarında yapılan bir grep'e dayanıyordu. Bu bir arama hatasıydı, kanıt değil: depo kökündeki yetkili DOCX bunu açıkça dokümante eder."))
    b.append((H, 2, "İki giriş şekli"))
    b.append((T, ["Şekil", "Örnek", "Kod nerede"], [
        ["A — açık kod", '{ "code": 52201, "desc": "...", "times": 1, "timestamp": ... }', "code özelliğinde"],
        ["B — dinamik anahtarlı", '{ "55001": 1634020734, "desc": "...", "times": 1 }', "Özellik ADI'nda; değer oluşum zaman damgası"],
    ]))
    b.append((N, "WARN", "Şekil B'de `code` özelliği aramak her girdiyi sessizce düşürür. Parser, alarm kodu olmadığı bilinen kardeş alanları (code, desc, description, times, time, timestamp, level, severity) hariç tutarak kalan anahtarı kod olarak alır."))
    b.append((H, 2, "Fleet snapshot: robot alarmları ve kök alarmlar"))
    b.append((P, "`GetRobotsStatusAsync` tek bir `RdsCoreFleetStatus(Robots, GlobalAlarms)` döndürür. İkisinin birlikte dönmesi bilinçlidir: parser kök alarmları bir süredir anlıyordu, ancak her iki istemci de ona yalnız daha dar bir nesne veriyordu — native istemci bir `report[]` elemanı, read istemcisi bir `rbk_report` — dolayısıyla dokümante bir çekirdek düzeyi fatal hiçbir çağırana ulaşamıyordu ve dispatch tamamen sağlıklı bir filo görüyordu."))
    b.append((P, "Kök alarmlar RDSCore/sahne düzeyindedir, robot düzeyinde değil: dokümante yanıtta `disable_paths`, `disable_points` ve `dynamic_obstacle` ile yan yana dururlar."))
    b.append((N, "WARN", "Kök alarmlar her robota kopyalanmaz. `desc` metni bir robotu adlandırsa bile (\"2000-1, have no charge point in map.\") yapısal olarak hiçbir şey onu tek bir robota atfetmez. Kopyalamak, dokümantasyonun yapmadığı bir atıf uydurmak ve tüm filoyu tek bir robotun şarj noktası sorununda mahsur bırakmak olurdu."))
    b.append((P, "İki ayrı kapı vardır: `fleet.GlobalHasBlockingAlarm` → `RDSCORE_HAS_BLOCKING_ALARM` (dispatch reddi), robot düzeyinde `robot.HasBlockingAlarm` → o robota dispatch reddi ve kilit kurtarma uygunsuzluğu."))
    return b


def section_mapswitch():
    b = []
    b.append((H, 1, "Harita Geçişi ve Kilit Kurtarma"))
    b.append((P, "Bir taşıma, robotun o an bulunduğundan farklı bir haritada çalışmayı gerektiriyorsa dispatch öncesi harita hazırlığı yapılır: `/lock` → `/switchMap` → `/unlock`. Bu üçlü ExecutionMutation sınıfındadır ve global kapı kapalıyken hiç başlamaz."))
    b.append((H, 2, "Çökme senaryosu"))
    b.append((P, "TKG `/lock` ile `/unlock` arasında çökerse robot kilitli kalır. Bir sonraki dispatch denemesi bunu kayıttan görür ve otomatik kurtarma yapıp yapamayacağına karar verir. Otomatik `/unlock`, TKG'nin kilitli bıraktığına inandığı bir robot üzerinde fiziksel bir mutasyondur; korelasyon kilidin **bizim** olduğunu kanıtlar, şu anda serbest bırakmanın **güvenli** olduğunu kanıtlamaz. Sahiplik ve güvenlik ayrı sorulardır ve ikisi de adaptör çağrısından ÖNCE cevaplanmalıdır."))
    b.append((N, "NOTE", "Daha önce bloklayan-alarm ve uyumsuz-sipariş kontrolleri bu metodun aşağısında duruyordu ve dolayısıyla ancak `/unlock` zaten gönderildikten SONRA çalışıyordu. Şimdi tüm koşullar önce değerlendirilir, adaptör çağrısı en sondadır."))
    b.append((H, 2, "Dokuz koşul (A–I)"))
    b.append((T, ["#", "Koşul", "Kod ifadesi"], [
        ["A", "Bu talep için bir kurtarma kaydı var", "hasUnreleasedLock"],
        ["B", "Kayıt bu robotu adlandırıyor", "recordedVehicleMatches"],
        ["C", "Kayıt şu an hazırlanan haritayı hedefliyor", "recordedMapMatches"],
        ["D", "Robot şu an kendini kilitli bildiriyor", "robotReportsLocked"],
        ["E", "Canlı kilit parmak izi korele oluyor", "CorrelatesWith"],
        ["F", "Robot şu an gerekli haritada", "currentMapMatches"],
        ["G", "Aktif hiçbir sipariş yok", "!hasActiveOrder"],
        ["H", "Bloklayan (fatal/error) alarm yok", "!HasBlockingAlarm"],
        ["I", "İş durumu dokümante-boşta", "!MayBeExecutingBusinessWork"],
    ]))
    b.append((P, "A/B/C/E `isLockCorrelated` içinde toplanır. Dokuzunun tamamı sağlanmazsa otomatik kurtarma yapılmaz."))
    b.append((N, "NOTE", "B ve C kontrolleri savunma derinliğidir: store zaten kaydı araca göre filtreler, ancak dönen kayıt otomatik bir `/unlock` yetkilendirmeden önce MEVCUT hazırlık bağlamına karşı yeniden doğrulanır. Kaydedilen robot paylaşılan kimlik üzerinden eşleştirilir, böylece bir uuid asla bir vehicle_id ile karşılaştırılmaz."))
    b.append((H, 2, "G koşulu: hiçbir aktif sipariş, kendi siparişimiz dahil"))
    b.append((P, "Herhangi bir terminal-olmayan sipariş otomatik kurtarmayı bloklar — bu talebin kendi ana siparişi ve kendi MAP_PREP geçiş siparişi dahil. Önceki bir tur bu ikisini \"bu talebe ait\" gerekçesiyle muaf tutmuştu; ancak sahiplik güvenlik değildir: hâlâ RUNNING veya SUSPENDED olan bir MAP_PREP siparişi, bu kilidin var olma sebebi olan harita hazırlığının hâlâ sürdüğü anlamına gelir ve kilidi altından çekmek tam da güvensiz durumdur. SEER, altından unlock yapmanın kanıtlanabilir şekilde güvenli olduğu hiçbir sipariş durumu dokümante etmez."))
    b.append((H, 2, "I koşulu: procBusiness belirsizliği"))
    b.append((N, "WARN", "DOCUMENTATION AMBIGUITY — DOCX alan tablosu `procBusiness` alanını yalnızca \"Kullanıcı tarafından gönderilen order'ı yürütüyor mu\" olarak tanımlar; `RdsCustomProjectTutorial_en.md:350` boşta olmayı **birleşim** olarak verir: `dispatchable == true && procBusiness == false`. Hiçbiri tek başına `procBusiness == false`'un boşta demek olduğunu söylemez ve birleşim kilit kurtarma sırasında yazıldığı gibi kullanılamaz, çünkü kilit tutmanın kendisi `dispatchable`'ın false olması için bir sebeptir. SEER, çağıranın kendi kilidini kurtarması için hiçbir yüklem dokümante etmez."))
    b.append((C, """/// True for procBusiness == true and also for null/absent, so an unknown
/// business state fails closed.
public bool MayBeExecutingBusinessWork => ProcBusiness != false;""", "csharp"))
    b.append((P, "Özellik bilerek kanıtlayabildiği şeyle adlandırılmıştır ve yalnız bir **gerekli negatif koşul** olarak kullanılır; hiçbir zaman boşta olmanın yeterli kanıtı sayılmaz. Hiçbir şeyin çalışmadığına dair gerçek kanıt yanındaki sipariş-durum kontrolünden gelir."))
    b.append((H, 2, "Kilit parmak izi korelasyonu"))
    b.append((P, "`CorrelatesWith`, iki `lock_info` yapısının aynı kilidi tanımlayıp tanımlamadığına karar verir. Kural üç parçalıdır:"))
    b.append((OL, [
        "Her iki taraf da kilitli olmalı ve kullanılabilir bir parmak izi taşımalı (`HasUsableFingerprint`).",
        "Mevcut olan her birincil tanımlayıcı (`TimeT`, `Ip`, `NickName`) karşı tarafta da bulunmalı ve eşleşmeli; biri eşleşmezse korelasyon reddedilir.",
        "İkincil alanlar (`Port`, `Type`) yalnız her iki tarafta da varsa çelişki yaratabilir. En az bir birincil tanımlayıcı eşleşmiş olmalıdır — yani boş bir parmak izi hiçbir şeyle korele olmaz.",
    ]))
    b.append((H, 2, "Sonuçlar ve denetim izleri"))
    b.append((T, ["Durum", "Denetim eylemi", "Sebep kodu"], [
        ["Robot kilitsiz ve gerekli haritada", "MAP_PREP_LOCK_RELEASED", "LOCK_ALREADY_RELEASED"],
        ["Robot kilitsiz ama farklı haritada", "MAP_PREP_BLOCKED", "MAP_SWITCH_LOCK_RECOVERY_REQUIRED"],
        ["Kilit bizim ama koşullar uygun değil", "MAP_PREP_BLOCKED", "LOCK_RECOVERY_UNSAFE"],
        ["Kurtarma yapıldı ve RDSCore kabul etti", "MAP_PREP_LOCK_RELEASED", "LOCK_RECOVERY_SUCCESS"],
        ["Kurtarma sonrası hâlâ kilitli/dispatch dışı", "RDS_DISPATCH_BLOCKED", "MAP_SWITCH_LOCK_RECOVERY_REQUIRED"],
    ]))
    b.append((P, "`LOCK_RECOVERY_UNSAFE` denetim kaydına ayrıca bloklayan sebep yazılır: `MAP_MISMATCH`, `ROBOT_NOT_LOCKED`, `ACTIVE_ORDER_ON_ROBOT`, `BLOCKING_ALARM`, `BUSINESS_STATE_UNKNOWN` veya `BUSINESS_BUSY`. Bu, devreye alma mühendisinin kurtarmanın neden durduğunu görebilmesi içindir."))
    return b


def section_dispatch_flow():
    b = []
    b.append((H, 1, "Native Order Dispatch Akışı"))
    b.append((P, "`DispatchNativeOrderAsync` projenin en uzun ve en güvenlik-kritik metodudur. Sırası tesadüfi değildir: her adım bir öncekinin kanıtı üzerine kurulur ve fiziksel çağrı en sondadır."))
    b.append((H, 2, "Adım adım"))
    b.append((T, ["#", "Adım", "Başarısızlık kodu"], [
        ["1", "Navigation profile çözümle (talep → varyant → makine)", "NAVIGATION_PROFILE_UNRESOLVED"],
        ["2", "Profilden gerekli robot haritasını al", "REQUIRED_MAP_UNRESOLVED"],
        ["3", "Aracı şablondan veya profilden çöz", "VEHICLE_UNRESOLVED"],
        ["4", "Telemetriden robot kira anahtarını çöz", "ROBOT_IDENTITY_UNRESOLVED"],
        ["5", "Robot kirasını al (session advisory lock)", "ROBOT_BUSY"],
        ["6", "`/robotsStatus` oku (fleet snapshot)", "ROBOT_OFFLINE"],
        ["7", "Çekirdek düzeyi bloklayan alarm kontrolü", "RDSCORE_HAS_BLOCKING_ALARM"],
        ["8", "Alias'ı tek robota çöz", "ROBOT_ALIAS_AMBIGUOUS"],
        ["9", "Kiranın hedefi kapsadığını doğrula (TOCTOU)", "ROBOT_CHANGED_DURING_VALIDATION"],
        ["10", "Bağlantı durumu kontrolü (ConnectionStatus == 1)", "ROBOT_OFFLINE"],
        ["11", "Serbest bırakılmamış harita kilidi kontrolü / kurtarma", "MAP_SWITCH_LOCK_RECOVERY_REQUIRED / LOCK_RECOVERY_UNSAFE"],
        ["12", "Dispatch edilebilirlik kontrolü", "ROBOT_NOT_DISPATCHABLE"],
        ["13", "Gerekiyorsa harita hazırlığı: /lock → /switchMap → /unlock", "—"],
        ["14", "`/setOrder` gönder", "—"],
    ]))
    b.append((N, "WARN", "6. adımda telemetri okuması başarısız olursa `ROBOT_OFFLINE` yazılır — ancak iptal (`OperationCanceledException`) bunun dışında tutulur. İptal asla bir robot arızası değildir: bir kapanış sağlıklı bir robota karşı ROBOT_OFFLINE olarak kaydedilmemelidir."))
    b.append((H, 2, "DispatchQueuedAsync — toplu çalışma"))
    b.append((P, "Worker her turda en fazla `Math.Clamp(limit, 1, 100)` kuyruktaki talebi alır ve her biri için ayrı bir `correlationId` üretir. Üç istisna türü ayrı ayrı yakalanır ve farklı denetim eylemleri yazar:"))
    b.append((T, ["İstisna", "Denetim eylemi", "Anlamı"], [
        ["RdsExecutionException", "RDS_DISPATCH_BLOCKED", "TKG kuralı reddetti; tekrar denemek yardımcı olmaz"],
        ["RdsReadException", "RDS_DISPATCH_DEFERRED", "RDS okuma kanıtı yok; talep kuyrukta kalır"],
        ["RdsCoreNativeOrderException", "RDS_DISPATCH_DEFERRED", "RDSCore geçici olarak erişilemez; talep kuyrukta kalır"],
    ]))
    b.append((P, "Ayrım önemlidir: *blocked* bir karardır, *deferred* bir bilgi eksikliğidir. İkisi aynı sayılsaydı geçici bir ağ kesintisi talepleri kalıcı olarak başarısız yapardı."))
    b.append((H, 2, "Savunma derinliği"))
    b.append((P, "`DispatchOneAsync` public ve doğrudan çağrılabilir olduğu için global mutasyon kapısı burada da yeniden kontrol edilir, yalnız `DispatchQueuedAsync` içinde değil. Kapı kapalıyken hiçbir kira alınmaz ve hiçbir RDS çağrısı yapılmaz; yalnız bir denetim kaydı yazılır."))
    b.append((H, 2, "Post-outcome audit"))
    b.append((P, "Fiziksel sonuç gerçekleştikten sonra yazılan denetim kaydı (`PostOutcomeAudit.RunAsync`) kendi 5 saniyelik sınırlı token'ı altında, best-effort olarak çalışır ve `OperationCanceledException` dahil tüm istisnaları yutar."))
    b.append((N, "WARN", "Denetim yazımı başarısız olduğu için fiziksel operasyon ASLA tekrar denenmez. Robot çoktan hareket etmiştir; denetim kaydının kaybı bir kayıt sorunudur, bir yürütme sorunu değil. Tersini yapmak çift hareket üretirdi."))
    return b


def section_reconcile():
    b = []
    b.append((H, 1, "Reconciliation, Worker ve Callback"))
    b.append((H, 2, "Worker döngüsü"))
    b.append((P, "Arka plan servisi `Rds:WorkerIntervalSeconds` (varsayılan 3 sn) aralığıyla iki iş yapar: kuyruktaki talepleri dispatch etmeye çalışır ve devam eden talepleri RDS gerçeğiyle uzlaştırır. `Rds:Enabled=false` iken servis hiç çalışmaz."))
    b.append((H, 2, "ReconcileAsync"))
    b.append((P, "Uzlaştırma salt-okunur bir işlemdir ve global dispatch kapısından etkilenmez — bu bilinçlidir: kapı kapalıyken bile daha önce gönderilmiş bir siparişin sonucunu öğrenmek gerekir. RDS sipariş durumu okunur, TKG durum makinesine göre ilerletilir ve tamamlanan taşımalarda slot durumları uygulanır."))
    b.append((H, 2, "ApplyTaskStatusAsync"))
    b.append((P, "RDS görev durumundan TKG durumuna geçiş burada yapılır. Terminal RDS durumları TKG'de `Completed` veya `Failed` üretir; her geçiş `TransportStateMachine.EnsureTransition` üzerinden doğrulanır, dolayısıyla RDS'ten gelen beklenmedik bir durum illegal bir geçiş üretemez."))
    b.append((H, 2, "Callback yolu"))
    b.append((P, "`ProcessCallbackAsync`, RDS'in TKG'ye geri bildirim gönderdiği yolu işler (`Rds:CallbackUrl` ile yapılandırılır). Callback, uzlaştırmanın yerine geçmez; onu hızlandırır. Callback hiç gelmese bile periyodik uzlaştırma aynı sonuca varır."))
    b.append((N, "NOTE", "Bu tasarım bilinçlidir: callback ağ üzerinden gelen, kaybolabilen bir bildirimdir. Sistemin doğruluğu ona bağlı olsaydı tek bir kayıp paket taşımayı sonsuza dek asılı bırakırdı."))
    b.append((H, 2, "WorkSite tazeliği"))
    b.append((P, "`Rds:WorkSiteFreshnessSeconds` (varsayılan 300 sn) RDS WorkSite projeksiyonunun ne kadar süre taze sayılacağını belirler. `RefreshRelevantWorkSitesAsync` yalnız ilgili WorkSite'leri yeniler; tüm filoyu her turda taramaz."))
    b.append((H, 2, "SynchronizeLoadContentAsync"))
    b.append((P, "Slot yük durumu değiştiğinde RDS tarafındaki içerik değerini (`Rds:EmptyContentValue` / `Rds:FullContentValue`) senkronize eder. Bu değerler yapılandırmadan gelir ve varsayılanları **boştur**."))
    b.append((N, "WARN", "COMMISSIONING VALIDATION REQUIRED — `EmptyContentValue` ve `FullContentValue` sahaya özgü RDS içerik etiketleridir. Depoda boş bırakılmaları bilinçlidir: uydurulmuş bir değer yanlış WorkSite içeriği yazardı."))
    return b
