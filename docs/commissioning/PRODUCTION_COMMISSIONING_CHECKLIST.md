# TKG PDA — Production Commissioning Checklist

Bu checklist fiziksel robot dispatch'inden önce tamamlanacak fail-closed kapıdır. `TKG_SIM_MOVE` ve `TKG_SIM_DYNAMIC_MOVE` yalnız development/simulator kanıtıdır; production iş akışı olarak kullanılamaz. Secret değerleri bu belgeye yazılmaz.

## Hazırlık durumu

### SOFTWARE / SIMULATOR STATUS

#### SIMULATOR_ACCEPTED:
- Container Action -> Warehouse Preparation -> RDS dispatch -> completion
- Native RDSCore order creation
- Native reconciliation
- RUNNING cancellation through TKG API
- exact /terminate with disableVehicle=false
- queue / polling
- telemetry
- fallback ETA (elapsed-time cascade: 120s - elapsed = remaining, min 15s)
- SwitchMap A <-> B via shared SM11
- Operator authorization boundary
- native/Wind execution isolation
- restart/idempotency protections (idempotentReplay=true on duplicate key)
- /manualFinished on RUNNING order (code=0, reconciliation picks up FINISHED)
- /clearRobotAllError (code=0)
- /dispatchable toggle (type=dispatchable / undispatchable_unignore)
- /lock and /unlock vehicle control
- /gotoSitePause and /gotoSiteResume
- /setPriority on active order
- Full RDSCore API endpoint matrix (15 endpoints verified)

#### IMPLEMENTED_NOT_POSITIVELY_OBSERVED:
- positive real remaining_time (simulator always returns -1; code path verified correct)
- redoFailedOrder on a genuine FAILED block (simulator does not produce genuine failures)
- manualFinished on a genuine stuck/failed block (tested on RUNNING; stuck state requires physical hardware)
- native group assignment
- unknown future RDS order states

#### PHYSICAL_COMMISSIONING_PENDING:
- real TKG Slot -> RDS WorkSite/Bin mapping
- real binTask
- fork height
- pickup/drop
- recognition/calibration
- physical load handling
- plant Wi-Fi / roaming
- on-site SAT

> [!NOTE]
> `EMPTY_STORAGE` ve `REMOVE_FULL` fiziksel iş alanları kararlaştırılmıştır (customer-decision gap değildir). Gerçek saha RDS/site-map karşılıkları: **COMMISSIONING MAPPING PENDING**.

- **Yazılım / simulator kabulü:** SIMULATOR_ACCEPTED (CLOSED)
- **Production fiziksel commissioning:** NOT YET READY
- **SAT / canlı robot kabulü:** BLOCKED BY CUSTOMER + SITE + RDS COMMISSIONING DATA

Bu checklist tamamlanmadan simulator sonuçları fiziksel robot kabulü olarak yorumlanamaz ve production dispatch açılamaz.

## 1. Kabul baseline'ı ve sorumlular

- [ ] Kullanılacak Git commit/PR ve release artifact hash'i kaydedildi.
- [ ] Customer, Logirobotic commissioning, RDS/robot ve IT/network sorumluları isimlendirildi.
- [ ] Ham müşteri Excel'leri byte-identical; yeni kaynak varsa ayrı review/import sürecinden geçti.
- [ ] 9 machine ve 25 aktif variant listesi müşteri tarafından onaylandı.
- [ ] SEALER V3 için physical-layout kararı kapandı.
- [ ] 48 mapping'siz lojistik pozisyonun her biri onaylandı veya açıkça kapsam dışı bırakıldı.
- [ ] 74 container association gap'i ve eksik lotlar operation bazında kapandı/kapsam dışı kaydedildi.

## 2. Lojistik business kuralları

- [x] `EMPTY_STORAGE` ve `REMOVE_FULL` hedefinin müşteri iş/fiziksel alanı kararlaştırıldı.
- [ ] Bilinen empty/full alanların exact RDS WorkSite/bin/point karşılıkları gerçek saha haritasında eşlendi.
- [ ] Warehouse preparation kuralı `WAITING_PREPARATION` veya `NOT_REQUIRED` olarak açıkça konfigüre edildi.
- [ ] Warehouse/Supervisor yetkileri gerçek kullanıcılarla acceptance testinden geçti.
- [ ] Batch içindeki her item'ın bağımsız TransportRequest olduğu operasyon ekibiyle doğrulandı.

## 3. Saha ölçümü ve fiziksel güvenlik

- [ ] Her pickup/drop için fiziksel pose, approach direction ve docking orientation ölçüldü.
- [ ] Fork insertion yönü, min/max/operasyon yüksekliği ve yük merkezi doğrulandı.
- [ ] Container/palet ölçüleri, orientation, clearance ve stacking/loading limiti kaydedildi.
- [ ] Koridor genişliği, dönüş alanı, kaçış yolu ve insan/araç kesitleri risk analizinden geçti.
- [ ] E-stop, safety scanner/zone, manuel kurtarma ve fiziksel rollback prosedürü saha ekibince onaylandı.
- [ ] Her lojistik slotun sahadaki etiketi UI slotuyla iki kişi tarafından çapraz kontrol edildi.
- [ ] İlk runtime state her slot için `VACANT`, `OCCUPIED+EMPTY` veya `OCCUPIED+FULL` olarak fiziksel gözlemle girildi.

## 4. Production RDS / robot konfigürasyonu

- [ ] Production RDS host/base URL sabitlendi; TLS/ağ izolasyonu ve firewall testi tamamlandı.
- [ ] Fiziksel CBD15 `uuid/vehicle_id`, robot group ve dispatchability doğrulandı.
- [ ] Production navigation profile/scene açık kimlikle TKG'ye eklendi; isimden tahmin edilmedi.
- [ ] WorkSite sync tamamlandı; kullanılan her WorkSite `type=1`, enabled, unlocked, `syncFailed=false` ve fresh.
- [ ] Her transport endpoint için slot→WorkSite mapping'i Admin tarafından explicit yapıldı ve machine scope kontrolü geçti.
- [ ] MACHINE display regions hiçbir WorkSite'a transport endpoint olarak map edilmedi.
- [ ] Production task label ve exact input bindings `BRING_EMPTY`, `REMOVE_FULL`, `REMOVE_EMPTY` için ayrı ayrı onaylandı.
- [ ] Task definition'lar gerçek WorkSite/robot modeline uygun Fork/Jack/binTask davranışı kullanıyor; simulator `Wait` task'larını kullanmıyor.
- [ ] Fork yükseklikleri, pickup/drop geometrisi ve recognition parametreleri signed commissioning kaynağından geldi.
- [ ] Callback URL ulaşılabilirliği ve certificate/TLS zinciri test edildi.
- [ ] Integration/callback anahtarları: **Yetkili teknik kanal üzerinden güvenli environment/configuration sağlanacaktır**; Git/log/shared docs taraması temiz.
- [ ] `Rds.Enabled` ve `Rds.DispatchEnabled` yalnız controlled test penceresinde, yetkili kişi tarafından açılıyor.

## 5. Dry-run ve kontrollü kabul

- [ ] Production task'lar önce non-motion validation'dan geçti: label mevcut, input binding exact, duplicate task yok.
- [ ] Mümkünse aynı production task tanımı simulation robot/izole sahnede her operation için denendi.
- [ ] TKG request UUID = RDS taskRecordId ve aynı kimlikle retry instance count = 1 kanıtlandı.
- [ ] Callback ve query reconciliation aynı terminal sonucu idempotent uyguladı.
- [ ] Cancel testinde stop HTTP cevabı tek başına terminal sayılmadı; RDS query/callback kanıtı gözlendi.
- [ ] Failure reconciliation her operation için fiziksel slot sonucunu doğru korudu/geri yükledi; belirsiz state tahmin edilmedi.
- [ ] WorkSite `filled/content` önce/sonra değerleri ve TKG slot state fiziksel gözlemle karşılaştırıldı.

## 6. Operator / Warehouse acceptance

- [ ] Gerçek sicil + kayıtlı cihazla şifresiz Operator login yapıldı; leading zero korundu.
- [ ] Kullanıcı yalnız yetkili machine'leri gördü; internal UUID/slot/WorkSite kodu gerekmedi.
- [ ] Ana parça, ALT ve varsa RELATED akışı müşteri tarafından kabul edildi.
- [ ] Tek/multi compatible variant davranışı ve machine değişiminde stale-state temizliği doğrulandı.
- [ ] Her operation normal `machine -> part -> operation` akışından oluşturuldu; Operator fiziksel slot seçmedi.
- [ ] Part + compatible variant sonrası `N. Kasa Doldu`, `N. Kasa Boş` ve `Tüm Kasaları Kaldır` aynı ekranda doğrulandı; standalone `REMOVE_EMPTY`, ayrı "İşleme Devam Et" veya layout sayfası görünmedi.
- [ ] Normal Operator UI'da fiziksel layout/slot seçimi olmadığı ve backend'in machine-side slot ile `EMPTY_STORAGE` allocation'ını yaptığı kabul edildi.
- [ ] Warehouse readiness ile execution status'un ayrı kaldığı kabul edildi.
- [ ] Operator alarm metni sade/actionable; kaynak `GET /api/agv-report/core`; reset/ack/clear/history yok.
- [ ] Gerçek Android PDA ve hedef Chrome/Edge terminalde touch, scroll, keyboard, offline/error/success durumları kabul edildi.

## 7. İlk fiziksel robot hareketi kapısı

- [ ] Exact robot, exact source, exact destination, exact operation ve test saati için yazılı yetki alındı.
- [ ] Alan fiziksel olarak izole; safety owner ve manuel stop operatörü hazır.
- [ ] Aktif task/robot/WorkSite state dispatch öncesi snapshot olarak kaydedildi.
- [ ] Tam olarak bir request dispatch edildi; paralel production talebi engellendi.
- [ ] Robot assignment, status sequence, endedReason, callbacks, final TKG status ve WorkSite önce/sonra kanıtı kaydedildi.
- [ ] Fiziksel sonuç yazılım sonucu ile eşleşmeden sonraki teste geçilmedi.
- [ ] Hata halinde dispatch kapatıldı, yeni hareket yapılmadı ve rollback/incident prosedürü uygulandı.

## 8. Go-live ve rollback

- [ ] DB backup/restore ve migration/seed restore prosedürü test edildi.
- [ ] API, Operator Web/PWA ve Admin Web release/rollback artifact'ları hazır.
- [ ] Cihaz provisioning, kiosk/browser cache/PWA update ve token/session sonlandırma prosedürü uygulandı.
- [ ] Monitoring, audit retention, saat/NTP ve support escalation bilgileri tanımlandı.
- [ ] Go-live onayı Customer + Logirobotic + RDS/robot + Safety sorumlularınca imzalandı.
- [ ] Go-live sonrası ilk vardiya için dispatch'i anında kapatma ve manuel lojistiğe dönme planı mevcut.
