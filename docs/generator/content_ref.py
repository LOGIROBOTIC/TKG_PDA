"""Reference sections: call graphs, DB flows, error codes, troubleshooting,
security, glossary, coverage and the complete file index."""

import io
import json
import os

H, P, UL, OL, T, C, PRE, N, PB = "h", "p", "ul", "ol", "table", "code", "pre", "note", "pagebreak"

BUILD = os.path.dirname(os.path.abspath(__file__))


def _load(name):
    with io.open(os.path.join(BUILD, name), encoding="utf-8") as fh:
        return json.load(fh)


def section_callgraphs():
    b = []
    b.append((H, 1, "Çağrı Grafikleri"))
    b.append((P, "Aşağıdaki on iki grafik sistemin ana yollarını uçtan uca gösterir. Her düğüm gerçek bir tip veya metottur; hiçbiri şematik değildir."))

    b.append((H, 2, "A — Operatör girişi"))
    b.append((PRE, """LoginScreen
  -> AppController.login
    -> PdaRepository.login
      -> POST /api/v1/auth/login              (X-Device-Code)
        -> ApiEndpoints.LoginAsync
          -> EmployeeNumber.Normalize          (sicil kimliği)
          -> IApiDataStore.FindUser...
          -> JwtTokenFactory.Create            (roller + device_code iddiası)
        <- { accessToken, user, roles }
    -> SessionMemory.set                       (yalnız bellek)
  -> AppStage.machineSelection""".strip()))

    b.append((H, 2, "B — Operasyonel bağlam yükleme"))
    b.append((PRE, """OperationScreen
  -> AppController.refreshContext
    -> PdaRepository.operationalContext
      -> GET /api/v1/machines/{id}/operational-context
        -> PdaOperationalContextService
          -> IApiDataStore  (makine, varyant, layout, slot durumları)
          -> ContainerActionPolicy  (her pozisyon icin fullAction/emptyAction)
          -> RdsReadClient          (WorkSite tazeliği, salt okuma)
        <- OperationalContext { positions[], mutationGate, activeVariantCode }
  -> ContainerPositionPanel yeniden çizilir""".strip()))

    b.append((H, 2, "C — WP-33 kasa aksiyonu (tekil)"))
    b.append((PRE, """_BusinessActionButton (key: direct-action-REMOVE_FULL-2)
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
  -> SubmissionCoordinator.clear""".strip()))

    b.append((H, 2, "D — Tüm kasaları kaldır (batch)"))
    b.append((PRE, """ContainerPositionPanel (key: remove-all-containers)
  -> AppController.removeAllContainers
    -> PdaRepository.removeAllContainers
      -> POST /api/v1/.../containers/remove-all
        -> ContainerActionPolicy.ResolveRemoveAll   (her pozisyon icin)
             OCCUPIED+FULL   -> REMOVE_FULL
             OCCUPIED+EMPTY  -> REMOVE_EMPTY        (yalniz sunucu üretir)
             VACANT+UNKNOWN  -> null (atlanir)
             UNKNOWN         -> REMOVE_ALL_POSITION_UNKNOWN  (tüm batch red)
             çelişki         -> REMOVE_ALL_POSITION_CONFLICT (tüm batch red)
        -> tek transaction icinde tüm talepler""".strip()))

    b.append((H, 2, "E — Dispatch worker döngüsü"))
    b.append((PRE, """RdsBackgroundWorker (her Rds:WorkerIntervalSeconds)
  -> RdsExecutionCoordinator.DispatchQueuedAsync(limit)
       if (!IsDispatchEnabled) return []
    -> IRdsExecutionStore.ListQueuedRequestIdsAsync
    -> for each id:
         DispatchOneAsync(id, correlationId)
         catch RdsExecutionException        -> audit RDS_DISPATCH_BLOCKED
         catch RdsReadException             -> audit RDS_DISPATCH_DEFERRED
         catch RdsCoreNativeOrderException  -> audit RDS_DISPATCH_DEFERRED""".strip()))

    b.append((H, 2, "F — Native order dispatch"))
    b.append((PRE, """DispatchOneAsync
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
  <- await using: robot kirasi serbest, talep kirasi serbest""".strip()))

    b.append((H, 2, "G — Uzman mutasyon (ManualFinish örneği)"))
    b.append((PRE, """AdminEndpoints  (administrator/variant-manager politikasi)
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
              -> PostOutcomeAudit.RunAsync""".strip()))

    b.append((H, 2, "H — İptal"))
    b.append((PRE, """OperatorQueueScreen / AdminEndpoints
  -> RdsExecutionCoordinator.CancelAsync(requestId)
    -> durum Created/Validating/Queued ?
         evet -> yerel iptal, RDS'e hic gidilmez
         hayir (SentToRds/Running) ->
              TerminateAsync   (SafetyStop -> kapi kapali olsa bile izinli)
    -> TransportStateMachine.EnsureTransition(-> Cancelled)
    -> rezervasyon serbest""".strip()))

    b.append((H, 2, "I — Uzlaştırma (reconciliation)"))
    b.append((PRE, """RdsBackgroundWorker
  -> RdsExecutionCoordinator.ReconcileAsync(limit)
    -> ListInFlightTasksAsync
    -> nativeOrderClient.GetOrderDetailsAsync / GetOrdersAsync
    -> ApplyTaskStatusAsync
         -> TransportStateMachine.EnsureTransition
         -> tamamlandiysa slot durumlari uygulanir
    -> RefreshRelevantWorkSitesAsync   (Rds:WorkSiteFreshnessSeconds)""".strip()))

    b.append((H, 2, "J — RDS callback"))
    b.append((PRE, """RDS  -> POST (Rds:CallbackUrl)
  -> RdsIntegrationEndpoints
    -> CallbackKey yapilandirilmis mi ?   degilse reddet
    -> FixedTimeEquals(supplied, CallbackKey)   sabit zamanli karsilastirma
    -> RdsExecutionCoordinator.ProcessCallbackAsync
      -> ApplyTaskStatusAsync   (uzlastirma ile ayni yol)""".strip()))

    b.append((H, 2, "K — Layout sürüm klonlama"))
    b.append((PRE, """LayoutsPage / LayoutEditorPage
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
         COMMIT""".strip()))

    b.append((H, 2, "L — Robot alarm özeti (operatör görünümü)"))
    b.append((PRE, """OperationScreen banner
  -> AppController.retryRobotAlarmSummary
    -> PdaRepository.robotAlarmSummary
      -> GET /api/v1/.../robot-alarm-summary
        -> RdsReadClient / RdsCoreNativeOrderClient
          -> RdsRobotAlarmParser  (4 zarf, 2 giriş şekli)
          -> RdsCoreFleetStatus { Robots, GlobalAlarms }
        <- yalniz IsBlocking olanlar operatöre bant olarak gösterilir""".strip()))
    return b


def section_dbflows():
    b = []
    b.append((H, 1, "Veritabanı Akış Diyagramları"))
    b.append((H, 2, "Taşıma talebi oluşturma — işlem sınırı"))
    b.append((PRE, """BEGIN TRANSACTION
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
COMMIT""".strip()))
    b.append((P, "Advisory lock ile benzersiz indeks birlikte kullanılır: kilit yarışı serileştirir, indeks ise kilit hiç alınmamış bir yol kalsa bile çift kaydı imkânsız kılar."))

    b.append((H, 2, "Boş kasa tahsisi — SKIP LOCKED"))
    b.append((PRE, """BEGIN TRANSACTION
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
COMMIT""".strip()))
    b.append((N, "NOTE", "`SKIP LOCKED` olmadan iki eşzamanlı BRING_EMPTY talebi aynı tampon slotu seçer ve ikincisi ya bekler ya da çakışır. `SKIP LOCKED` ile ikinci talep bir sonraki uygun tampon slota geçer."))

    b.append((H, 2, "Robot kirası — ayrı bağlantı, session ömrü"))
    b.append((PRE, """[Baglanti A: is transaction'i]        [Baglanti B: kira]
   BEGIN                                 SELECT pg_try_advisory_lock(hash)
   ...                                     -> true  = kira alindi
   COMMIT   <-- burada xact kilidi          -> false = ROBOT_BUSY
              serbest kalirdi
                                          ... FIZIKSEL RDS MUTASYONU ...
                                          SELECT pg_advisory_unlock(hash)
                                          (await using -> finally)""".strip()))
    b.append((P, "Kira iş transaction'ından bağımsız bir bağlantıda tutulur; bu, mutasyonun tamamlanmasından önce serbest kalmasını engeller."))

    b.append((H, 2, "Varyant değiştirme — rezervasyon koruması"))
    b.append((PRE, """UPDATE ops.machine_runtime_state SET active_variant_id = ?
  |
  +-- ÖNCE: aktif rezervasyon var mi ?
  |     SELECT 1 FROM ops.transport_requests
  |      WHERE machine_id = ? AND status IN (Created, Validating, Queued,
  |                                          SentToRds, Running)
  |     varsa -> ACTIVE_TRANSPORT_EXISTS (HTTP 409)
  |
  +-- yoksa -> varyant degistirilir ve denetim kaydi yazilir""".strip()))
    b.append((P, "Aktif rezervasyon `TransportStateMachine.IsReservationActive` tanımına göre belirlenir; terminal durumlar (Completed/Failed/Cancelled) rezervasyon tutmaz."))

    b.append((H, 2, "Layout klonlama — ilişkisel bütünlük"))
    b.append((PRE, """BEGIN
  INSERT master.layouts        -> new_id, version = max+1, DRAFT
  INSERT master.layout_slots   -> yeni UUID'ler, code korunur
  INSERT master.layout_slot_parts
        JOIN ON new_slot.code = old_slot.code    (uq_layout_slot_code sayesinde tekil)
        -> copiedCount
  SELECT count(*) kaynak iliskileri              -> expectedCount
  IF copiedCount <> expectedCount -> THROW  ==> ROLLBACK
  INSERT audit.audit_logs  ADMIN_LAYOUT_VERSION_CREATED
COMMIT""".strip()))
    return b


def section_errorcodes():
    codes = _load("errorcodes.json")
    b = []
    b.append((H, 1, "Kod Referansı: Hata ve Sonuç Kodları"))
    b.append((P, f"Aşağıdaki **{len(codes)} kod** `docs/generator/extract_facts.py` tarafından kaynak koddan çıkarılmıştır. Hiçbiri uydurulmamıştır; her satırın `Kaynak` sütunu kodun geçtiği dosyayı gösterir."))
    b.append((N, "NOTE", "Tabloda yalnız hata kodları değil, çağıranın dallanabildiği **sonuç ve denetim sebep kodları** da vardır (örneğin `MAP_PREP_DISPATCHED`, `RDS_EVENT_APPLIED`). Tür sütunu hangisi olduğunu söyler. Bir kod birden çok dosyada geçebilir; tablo ilk tanım dosyasını gösterir."))

    def rows_for(pred):
        out = []
        for code in sorted(codes):
            info = codes[code]
            if not pred(info.get("type", "")):
                continue
            msg = (info.get("msg") or "").strip()
            if len(msg) > 150:
                msg = msg[:147] + "..."
            files = info.get("files") or []
            src = files[0] if files else "-"
            src = src.replace("src/TkgPda.", "").replace("/", " / ")
            out.append([code, info.get("type", "-"), msg, src])
        return out

    exc = rows_for(lambda t: t.endswith("Exception"))
    other = rows_for(lambda t: not t.endswith("Exception"))
    b.append((H, 2, f"İstisna ile fırlatılan kodlar ({len(exc)})"))
    b.append((P, "Bu kodlar bir istisna ile fırlatılır ve API katmanında bir HTTP durumu ve problem gövdesine dönüşür."))
    b.append((T, ["Kod", "Tür", "Mesaj", "Kaynak"], exc))
    b.append((H, 2, f"Sonuç ve denetim sebep kodları ({len(other)})"))
    b.append((P, "Bu kodlar istisna fırlatmaz. Sonuç nesnelerinde, denetim kayıtlarında veya sabitlerde taşınır; çağıran bunlara göre dallanır ve devreye alma mühendisi denetim izinde bunları okur."))
    b.append((T, ["Kod", "Tür", "Mesaj", "Kaynak"], other))
    b.append((H, 2, "Güvenlik açısından kritik kodlar"))
    b.append((P, "Aşağıdaki kodlar fiziksel güvenlikle doğrudan ilgilidir. Bunlardan biri görüldüğünde sistem bilinçli olarak fail-closed davranmıştır."))
    critical = [
        ("RDS_DISPATCH_DISABLED", "Global mutasyon kapısı kapalı; hareket doğuran operasyon reddedildi."),
        ("RDSCORE_HAS_BLOCKING_ALARM", "RDSCore çekirdek düzeyinde fatal/error alarm bildiriyor."),
        ("ROBOT_ALIAS_AMBIGUOUS", "Alias birden çok fiziksel robota çözülüyor; tahmin edilmedi."),
        ("ROBOT_BUSY", "Robot kirası başka bir TKG operasyonunda."),
        ("EXPERT_ROBOT_CHANGED_DURING_VALIDATION", "Kira alındıktan sonra hedef robot değişti (TOCTOU)."),
        ("ROBOT_CHANGED_DURING_VALIDATION", "Dispatch yolunda aynı TOCTOU değişmezi."),
        ("EXPERT_CURRENT_STATE_UNAVAILABLE", "Telemetri okunamadı; güvenlik-kritik mutasyon için fail-closed."),
        ("MAP_SWITCH_LOCK_RECOVERY_REQUIRED", "Çökmüş bir harita geçişinden kalan kilit; elle kurtarma gerekli."),
        ("LOCK_RECOVERY_UNSAFE", "Kilit bizim ama çalışma zamanı durumu serbest bırakmayı yasaklıyor."),
        ("OPERATION_NOT_OPERATOR_SELECTABLE", "REMOVE_EMPTY gibi operatöre kapalı bir operasyon istendi."),
    ]
    rows = []
    for code, why in critical:
        present = "Evet" if code in codes else "HAYIR"
        rows.append([code, present, why])
    b.append((T, ["Kod", "Kodda mevcut", "Anlamı"], rows))
    return b


def section_troubleshooting():
    b = []
    b.append((H, 1, "Troubleshooting Karar Ağaçları"))
    b.append((P, "Her ağaç gözlemlenen belirtiyle başlar ve kaynak koddan doğrulanabilir bir sebebe iner. Tahmin adımı yoktur."))

    b.append((H, 2, "1 — Talep oluşuyor ama robot hiç hareket etmiyor"))
    b.append((PRE, """Talep ops.transport_requests icinde Queued'da mi ?
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
 HAYIR -> Worker hic calisiyor mu ? Rds:Enabled kontrol et.""".strip()))

    b.append((H, 2, "2 — ROBOT_ALIAS_AMBIGUOUS"))
    b.append((PRE, """/robotsStatus ciktisini al
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
 makineyi hareket ettirir. Kod bunu bilerek reddeder.""".strip()))

    b.append((H, 2, "3 — Robot alarmda ama TKG alarm göstermiyor"))
    b.append((PRE, """Alarm hangi severity ?
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
     RDSCORE_HAS_BLOCKING_ALARM olarak dispatch'i bloklar.""".strip()))

    b.append((H, 2, "4 — MAP_SWITCH_LOCK_RECOVERY_REQUIRED"))
    b.append((PRE, """Denetim kayitlarinda LOCK_RECOVERY_UNSAFE var mi ?
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
 COMMISSIONING VALIDATION REQUIRED.""".strip()))

    b.append((H, 2, "5 — \"N. Kasa Boş\" butonu devre dışı"))
    b.append((PRE, """Buton altinda gosterilen sebep metnini oku (tooltip'e guvenme)
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
 |         -> EMPTY_STORAGE rolunde OCCUPIED+EMPTY slot var mi ? kontrol et""".strip()))

    b.append((H, 2, "6 — Admin Web'de oturum sürekli düşüyor"))
    b.append((PRE, """Sayfa yenilendi mi (F5 / page.goto) ?
 |-- EVET -> BEKLENEN DAVRANIS. Token yalniz bellekte tutulur; localStorage
 |           veya sessionStorage kullanilmaz. Bu bilincli bir guvenlik karari.
 |
 HAYIR
 |
 Token suresi doldu mu ? (Authentication:Jwt:ExpiryMinutes, varsayilan 60)
 |-- EVET -> yeniden giris normal
 |
 HAYIR -> 403 ADMIN_WEB_ROLE_REQUIRED mi aliniyor ?
          -> kullanicida ADMIN rolu yok""".strip()))

    b.append((H, 2, "7 — Operatör 403 DEVICE_CONTEXT_MISMATCH alıyor"))
    b.append((PRE, """X-Device-Code basligi ile token icindeki device_code iddiasi ayni mi ?
 |-- HAYIR -> cihaz kodu degistirildi ama yeniden giris yapilmadi.
 |            Cozum: cikis yap, yeniden giris yap.
 |
 EVET -> DEVICE_NOT_AUTHORIZED mi ?
         -> audit.client_devices icinde kayit var mi ve is_active = true mi ?
         -> Admin Web > DevicesPage uzerinden cihazi aktive et""".strip()))

    b.append((H, 2, "8 — Layout klonlama başarısız: \"Relational loss\""))
    b.append((PRE, """InvalidOperationException: expected N layout_slot_parts, copied M
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
    WHERE layout_id = '<kaynak>' AND (code IS NULL OR btrim(code) = '');""".strip()))
    return b


def section_security():
    b = []
    b.append((H, 1, "Güvenlik Modeli"))
    b.append((H, 2, "İki ayrı güvenlik alanı"))
    b.append((T, ["Alan", "Riski", "Ana kontrol"], [
        ["Bilgi güvenliği", "Yetkisiz veri erişimi", "JWT + rol politikaları + cihaz bağlama"],
        ["Fiziksel güvenlik", "İstenmeyen robot hareketi", "RdsMutationPolicy + dispatch kapısı + robot kirası"],
    ]))
    b.append((P, "İkisi ayrı ele alınır çünkü tehdit modelleri farklıdır: bilgi güvenliğinde en kötü sonuç veri sızıntısı, fiziksel güvenlikte ise insan yaralanmasıdır. Bu yüzden fiziksel tarafta \"yetkili kullanıcı\" hiçbir zaman tek başına yeterli bir kontrol sayılmaz."))
    b.append((H, 2, "Sır yönetimi"))
    b.append((T, ["Sır", "Yer tutucu", "Nereden gelir"], [
        ["Veritabanı bağlantı dizesi", "<DB_CONNECTION>", "ConnectionStrings__TkgPda ortam değişkeni"],
        ["JWT imzalama anahtarı", "<JWT_SIGNING_KEY>", "Authentication__Jwt__SigningKey ortam değişkeni"],
        ["RDS entegrasyon anahtarı", "<INTEGRATION_KEY>", "Rds__IntegrationKey ortam değişkeni"],
        ["RDS callback anahtarı", "<CALLBACK_KEY>", "Rds__CallbackKey ortam değişkeni"],
    ]))
    b.append((N, "WARN", "Hiçbiri `appsettings.json` içinde bulunmaz ve depoya işlenmez. Bu doküman da hiçbir gerçek sır içermez; tüm örnekler yukarıdaki yer tutucularla yazılmıştır."))
    b.append((H, 2, "Doğrulamalar"))
    b.append((UL, [
        "JWT imzalama anahtarı 32 bayttan kısaysa uygulama başlamaz.",
        "RDS entegrasyon ve callback anahtarları 32–512 karakter aralığında olmalıdır.",
        "Callback anahtarı karşılaştırması sabit zamanlıdır (`FixedTimeEquals`) — zamanlama saldırısına kapalı.",
        "Callback anahtarı yapılandırılmamışsa callback ucu hiçbir isteği kabul etmez (fail-closed).",
        "`AllowInsecureRemoteHttp=false` iken uzak bir adrese düz HTTP reddedilir.",
    ]))
    b.append((H, 2, "Günlükleme hijyeni"))
    b.append((P, "Başlangıç günlüğü RDS adreslerini `BaseAddressDisplaySafe` üzerinden yazar; adres bir kullanıcı bilgisi bileşeni taşıyorsa günlüğe düşmez. Denetim kayıtları sır içermez; yalnız kod, sebep ve korelasyon kimliği taşır."))
    b.append((H, 2, "Operatör yüzeyi bilgi sınırı"))
    b.append((P, "Operatör PDA'sına ham RDS tanımlayıcıları hiç gönderilmez: task id, WorkSite etiketi, AGV kimliği, robot uuid'i. Bu hem güvenlik hem de iş sözleşmesi kararıdır — operatör fiziksel yönlendirme kararı vermediği için bu bilgilere ihtiyacı yoktur ve bunların ekranda olması yanlış bir yetki hissi yaratır."))
    b.append((H, 2, "Fail-closed listesi"))
    b.append((P, "Aşağıdaki durumların **hepsinde** sistem reddeder; hiçbirinde tahmin etmez."))
    b.append((UL, [
        "Robot kimliği telemetriden çözülemiyor.",
        "Alias birden çok fiziksel robota çözülüyor.",
        "Robot kirası alınamıyor.",
        "Kira alındıktan sonra hedef robot değişmiş.",
        "`procBusiness` alanı yok veya true.",
        "Kilit parmak izi korele olmuyor.",
        "Slot durumu UNKNOWN.",
        "Remove-All kapsamında bir pozisyon UNKNOWN veya çelişkili.",
        "RDSCore çekirdek düzeyinde bloklayan alarm bildiriyor.",
        "Dokümantasyon güvenlik-kritik bir ön koşulda belirsiz.",
    ]))
    return b


def section_glossary():
    b = []
    b.append((H, 1, "Sözlük"))
    terms = [
        ("AGV", "Automated Guided Vehicle — otonom taşıyıcı araç."),
        ("Alias", "Bir robotu adlandıran, uuid veya vehicle_id uzayında olabilen serbest metin."),
        ("BRING_EMPTY", "Boş kasa getirme taşıma operasyonu."),
        ("CommissioningDependent", "Yalnız sahada fiziksel doğrulama ile teyit edilebilen değer veya davranış."),
        ("DiscoveryKey", "Keşif uçlarının beklediği kimlik: Uuid ?? VehicleId."),
        ("DispatchEnabled", "Fiziksel hareket mutasyonu izni. Rds:Enabled ile karıştırılmamalıdır."),
        ("ETag", "HTTP koşullu istek etiketi; eşzamanlı düzenleme çakışmalarını yakalar."),
        ("ExecutionMutation", "Yürütme kimliğini/sırasını değiştiren, kapıya tabi mutasyon sınıfı."),
        ("Fail-closed", "Kanıt eksik veya belirsizken reddetme davranışı."),
        ("Idempotency", "Aynı anahtarla tekrarlanan isteğin yeni etki üretmemesi."),
        ("LoadState", "Bir kasanın içeriği: EMPTY / FULL / UNKNOWN."),
        ("MotionEnabling", "Fiziksel işi başlatan/ilerleten, kapıya tabi mutasyon sınıfı."),
        ("MutationTarget", "RDSCore'a gönderilecek hedef kimlik: VehicleId ?? Uuid."),
        ("Native Order", "RDSCore'un /setOrder tabanlı yerel sipariş modeli."),
        ("NavigationProfile", "Varyant/makine için robot haritası ve araç eşlemesi."),
        ("OccupancyState", "Slotta kasa var mı: VACANT / OCCUPIED / UNKNOWN."),
        ("procBusiness", "SEER telemetri alanı: robot kullanıcı siparişi yürütüyor mu."),
        ("RDS", "SEER Robot Dispatch System — robot filo yönetim sistemi."),
        ("RDSCore", "RDS'in native order HTTP API'sini sunan çekirdek bileşeni."),
        ("ReadinessState", "Talebin hazırlık ekseni: NotRequired / WaitingPreparation / Ready."),
        ("REMOVE_EMPTY", "Boş kasa kaldırma; yalnız sunucu tarafı orkestrasyonda üretilir."),
        ("REMOVE_FULL", "Dolu kasa kaldırma taşıma operasyonu."),
        ("RobotLeaseKey", "Bir fiziksel robotu kapsayan TKG kirasının anahtarı."),
        ("SafetyStop", "Hareketi azaltan, kapı kapalıyken bile izinli mutasyon sınıfı."),
        ("SEER", "RDS/RDSCore'u üreten satıcı."),
        ("Slot", "Layout üzerindeki fiziksel kasa konumu."),
        ("SlotRole", "Slotun işlevi, örn. EMPTY_STORAGE tampon rolü."),
        ("TOCTOU", "Time-of-check to time-of-use — kontrol ile kullanım arasındaki yarış penceresi."),
        ("TransportRequest", "Bir taşıma işinin TKG'deki kaydı."),
        ("UUID", "Evrensel benzersiz tanımlayıcı."),
        ("Variant", "Bir makinede üretilen ürün konfigürasyonu. Product Group ile aynı şey DEĞİLDİR."),
        ("WindTask", "RDS'in alternatif görev modeli; native order'dan ayrıdır."),
        ("WorkSite", "RDS tarafındaki fiziksel çalışma noktası tanımı."),
        ("WP-33", "Operatör kasa aksiyonları iş paketi."),
    ]
    b.append((T, ["Terim", "Açıklama"], [[t, d] for t, d in terms]))
    return b


def section_coverage(stats):
    b = []
    b.append((H, 1, "Kapsam Doğrulaması"))
    b.append((P, "Bu doküman, kapsamının denetlenebilir olması için makine okunur bir envanterle birlikte üretilmiştir: `docs/source_inventory.txt`. Envanterdeki her takip edilen dosya tam olarak bir kategoriye aittir."))
    b.append((T, ["Kategori", "Adet", "Anlamı"], [
        ["DOCUMENTED", str(stats["documented"]), "Bölüm 27'de kendi alt bölümü olan dosyalar"],
        ["GENERATED", str(stats["generated"]), "Araç çıktısı; elle düzenlenmez"],
        ["DEPENDENCY", str(stats["dependency"]), "Üçüncü taraf bağımlılık / lock dosyası"],
        ["BUILD_OUTPUT", str(stats["build_output"]), "Derleyici/paketleyici çıktısı"],
        ["IRRELEVANT_ARTIFACT", str(stats["irrelevant"]), "İkili varlık, platform iskelesi, satıcı dokümanı"],
        ["TOPLAM", str(stats["total"]), "git ls-files ile takip edilen tüm dosyalar"],
    ]))
    b.append((H, 2, "Sonuç"))
    b.append((T, ["Ölçüm", "Değer"], [
        ["Bulunan ilgili kaynak dosya", str(stats["documented"])],
        ["Dokümante edilen dosya bölümü", str(stats["sections"])],
        ["Kapsam", stats["coverage"]],
    ]))
    if stats["missing"]:
        b.append((N, "WARN", "KAPSAM TAM DEĞİL. Aşağıdaki dosyaların kendi bölümü yoktur:"))
        b.append((UL, [f"`{m}`" for m in stats["missing"]]))
    else:
        b.append((N, "FACT", "Envanterde DOCUMENTED olarak sınıflanan her dosyanın bölüm 27 içinde kendi alt bölümü vardır. Eksik dosya yoktur."))
    b.append((H, 2, "Kategori kararlarının gerekçesi"))
    b.append((UL, [
        "`db/seeds/generated/*.sql` üretilmiş olmasına rağmen DOCUMENTED sayılır: gerçek veritabanlarına karşı çalıştırılır, dolayısıyla açıklanmalı ve üretilmiş olduğu belirtilmelidir.",
        "`.gitignore` ve `.gitattributes` DOCUMENTED sayılır: `launchSettings.json` dosyasının git tarafından yok sayılması, paylaşılan bir devreye alma başlatma profilinin neden olamayacağının sebebidir — bu davranışsal bir gerçektir.",
        "Flutter platform iskelesi (`android/`, `ios/`, `windows/`, `web/` vb.) IRRELEVANT_ARTIFACT'tir: var olmak zorundadır ama hiçbir TKG iş mantığı taşımaz.",
        "`RDS DÖKÜMANLARI/` git tarafından takip edilmez ve dokunulmamıştır; envanterde yer almaz.",
    ]))
    b.append((N, "NOTE", "Envanter `git ls-files` çıktısından üretilir. Bu dokümanın kendisi ve onu üreten `docs/generator/` betikleri üretim anında henüz takip edilmediği için envanterde görünmez; bu, kapsam hesabını etkilemez çünkü kapsam üretim öncesi kaynak ağacını ölçer."))
    b.append((H, 2, "Nasıl yeniden üretilir"))
    b.append((OL, [
        "`python docs/generator/inventory.py` — envanteri ve yapısal gerçekleri yeniden çıkarır.",
        "`python docs/generator/build_doc.py` — `.md` ve `.pdf` çıktılarını yeniden üretir ve kapsamı doğrular.",
        "Kapsam %100 değilse `build_doc.py` sıfırdan farklı bir çıkış kodu döner ve eksik dosyaları listeler.",
    ]))
    return b


def section_fileindex(inventory):
    b = []
    b.append((H, 1, "Tam Dosya İndeksi"))
    b.append((P, "Takip edilen tüm dosyaların kategorileriyle birlikte tam listesi. Bu tablo `docs/source_inventory.txt` ile birebir aynı içeriktir."))
    order = ["DOCUMENTED", "GENERATED", "DEPENDENCY", "BUILD_OUTPUT", "IRRELEVANT_ARTIFACT"]
    by_cat = {}
    for cat, path, reason in inventory:
        by_cat.setdefault(cat, []).append(path)
    for cat in order:
        paths = by_cat.get(cat, [])
        if not paths:
            continue
        b.append((H, 2, f"{cat} ({len(paths)})"))
        b.append((T, ["#", "Dosya"], [[str(i + 1), p] for i, p in enumerate(sorted(paths))]))
    return b
