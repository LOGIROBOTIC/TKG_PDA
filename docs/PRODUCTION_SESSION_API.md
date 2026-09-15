# Üretim oturumu API ve operatör UI — dilim 2–3

Durum: backend ve mevcut Flutter operatör ekranı uygulandı. Ayrı depo tableti dahil değildir. 15 Eylül 2026 kullanıcı onayıyla yerel geliştirme ortamına yedekli/korumalı kurulum yapıldı; aşağıdaki yerel kurulum bölümü önceki dilimlerin kurulum yapılmadı notlarını tarihsel olarak tamamlar. Robot görev gönderimi kapalıdır.

## Teyit edilen iş kuralı

Kullanıcı, açık taşıma talepleri varken **Üretimi Bitir işleminin engellenmesini** onayladı. Talebin ayrıca iptal edilmesi mevcut iptal yetkisi/kurallarından geçer. Üretimi bitirme işlemi robot görevini iptal etmez, taşımayı tamamlamaz, kasaları boşaltmaz, varyant değiştirmez.

Bir makinede aynı anda tek açık üretim bulunur. Üretim, müşteri kaynaklı ana parça + ürün grubu + etkin varyantı kaydeder. Miktar girişi veya tahmini standart lot yoktur. RELATED/ALT taşıma parçası seçimi üretimin ana parçasını değiştirmez.

Başlangıç da temiz bir sınır gerektirir: mevcut açık taşımalar bitmeden yeni üretim kaydı açılamaz. Bu sayede önceki talepler yeni üretime geriye dönük bağlanmaz.

## HTTP sözleşmesi

Ortak kök: `/api/v1/machines/{machineId}/production-sessions`.

| Yöntem/yol | İstek | Sonuç |
| --- | --- | --- |
| `GET /active` | Gövde yok | Açık oturum için 200; açık oturum yoksa 204 |
| `POST /` | `variantId`, `productGroupId`, `mainPartId`; `Idempotency-Key` başlığı | Yeni kayıt için 201, aynı isteğin tekrarı için 200 |
| `POST /{sessionId}/end` | Gövde yok | 200; zaten kapanmış aynı oturumu kapatma tekrarı da 200 |

Başlatma/bitirme cevabı `{ session, wasIdempotentReplay }` biçimindedir; `Idempotency-Replayed` başlığı da döner. GET doğrudan `session` kaydını döndürür.

Oturum alanları: `id`, `machineId`, `variantId`, `productGroupId`, `mainPartId`, `status`, `startedAt`, `startedBy`, `startedDeviceId`, `endedAt`, `endedBy`, `endedDeviceId`. `status`, `endedAt` üzerinden ACTIVE/ENDED olarak türetilir; ayrı ve çelişebilir bir durum kolonu yoktur. Zamanlar PostgreSQL tarafından üretilir. UI bu audit kimliklerini veya teknik ayrıntıları operatör etiketi olarak göstermek zorunda değildir.

API fiziksel slot, RDS haritası, waypoint, görev şablonu veya miktar almaz. İstemcinin seçtiği varyantın etkin runtime bağlamıyla ve ana parçanın ürün grubuyla tutarlı olması doğrulanır. Başlatma mevcut variant-selection yetkisini dolanmaz; kendi başına varyant seçmez veya fiziksel layout oluşturmaz.

## Yetki

- DEVICE_BOUND oturumu, geçerli JWT ve eşleşen `X-Device-Code` gerekir.
- Roller: LINE_OPERATOR, SUPERVISOR veya ADMIN. Yalnız WAREHOUSE_OPERATOR bu yüzeyi kullanamaz.
- Başlat/bitir için makinede `can_operate`, oku için `can_view`; mevcut ADMIN muafiyeti korunur.
- ADMIN_WEB token'ı bu saha işlemlerini yapamaz.
- Actor/device body'den alınmaz. Kimlik, middleware'in doğruladığı oturumdan alınır. Yazım sırasında store da aktif kullanıcı/cihaz ve makine yetkisini kontrol eder.

## Tekrar, transaction ve kilitler

- Başlangıç anahtarı makine içinde tekildir. Eşit payload: makine + varyant + ürün grubu + ana parça + kullanıcı + cihaz. İstemci zamanı, UI metadatası veya yeni JWT eşitliğe dahil değildir.
- Aynı anahtar farklı payload ile gelirse `IDEMPOTENCY_KEY_REUSED`. Aynı anahtarın kapanmış oturum için tekrarı o kapanmış kaydı döndürür; yeni üretim açmaz. Yeni bir başlangıç yeni anahtar gerektirir.
- Bitiş tam `sessionId` hedefler. Eski ekranın tekrarı yeni açılan oturumu kapatamaz. İlk başarılı bitirmenin kullanıcı/cihaz/zamanı korunur.
- Başlat/bitir, mevcut transport workflow ile aynı `ops.machine_runtime_state` satırını önce kilitler. Üretim yolu slot/request kilidi almaz. Transport yolu makine → UUID-sıralı slotlar → request sırasını korur.
- Audit aynı transaction içindedir. Audit başarısızsa üretim değişikliği de geri alınır. Tekrarlı istek ek audit olayı yaratmaz.
- Açık üretim varken etkin varyant/layout/navigation bağlamı değiştirilemez; aynı bağlamın yeniden seçimi mevcut transport guard'ına tabi olarak mümkündür.
- Açık üretimde yeni taşımalar aynı ürün grubundan ve geçerli main/RELATED/ALT üyeliğinden gelmelidir. Oluşan her TransportRequest, makine kilidi altında `production_session_id` ile ilişkilendirilir. Batch çocukları ayrı request/idempotency/RDS kimliklerini korur.

## Forward migration incelemesi

`db/migrations/006_production_sessions.sql`, 001–005 sonrasında uygulanır. Önceki migration/seed içerikleri değiştirilmez.

- Yeni `ops.production_sessions`: müşteri kimlikleri, başlangıç/bitiş actor/device/time, başlangıç anahtarı.
- Makine başına tek ACTIVE oturum için partial unique index.
- Bitiş alanları birlikte NULL veya birlikte dolu olmalıdır; bitiş başlangıçtan önce olamaz.
- Makine/varyant composite FK ve ürün grubu/varyant/ana parça composite FK, yanlış müşteri bağlamını DB düzeyinde de reddeder. İkinci FK için `master.product_groups` üzerine yalnız ek bir unique index eklenir; müşteri satırları değişmez.
- `ops.transport_requests.production_session_id` nullable eklenir; eski talepler geriye dönük sınıflandırılmaz. Composite FK başka makinenin oturumuna bağlantıyı reddeder.
- Referans verilen kullanıcı/cihaz ve müşteri kimlikleri fiziksel silmeye karşı korunur. Arşivleme mevcut Admin davranışıdır. Oturum tarihçesi otomatik silinmez.
- Migration tekrar uygulanabilir. Testte seed sonrasında ve oluşan runtime/audit kayıtlarından sonra yeniden uygulanır; ikinci durumda kayıt özeti hash'i aynı kalmalıdır.
- Güncel backend'i başlatan mevcut geliştirme/test kurulumlarına 006 eklendi. Tarihsel Phase 02 seed-only testleri bu backend'i çalıştırmadıkları için değiştirilmedi. Canlı test betiklerinin migration listesi güncellendi, **betikler çalıştırılmadı**.

## Hata davranışı

| Kod | HTTP | Anlam |
| --- | --- | --- |
| `ID_REQUIRED`, `IDEMPOTENCY_KEY_REQUIRED`, `IDEMPOTENCY_KEY_TOO_LONG` | 400 | Eksik/geçersiz istek |
| `PRODUCTION_ACCESS_DENIED`, mevcut rol/makine/cihaz retleri | 403 veya mevcut auth kodu | Yetki yok |
| `PRODUCTION_SESSION_NOT_FOUND` | 404 | Oturum bu makineye ait değil veya yok |
| `PRODUCTION_SESSION_ALREADY_ACTIVE` | 409 | Makinede açık üretim var |
| `ACTIVE_TRANSPORT_EXISTS` | 409 | CREATED/VALIDATING/QUEUED/SENT_TO_RDS/RUNNING talebi var |
| `PRODUCTION_CONTEXT_MISMATCH` | 409 | Üretim veya taşıma bağlamı uyuşmuyor |
| `PRODUCTION_SESSION_CHANGED` | 409 | İstemcinin gönderdiği üretim oturumu artık etkin değil; yeni talep oluşturulmadı |
| `PRODUCTION_SESSION_ACTIVE` | 409 | Açık üretimde runtime bağlamı değiştirilmeye çalışılıyor |
| `IDEMPOTENCY_KEY_REUSED` | 409 | Anahtar farklı başlangıç için kullanılmış |

## Aşamalı geçiş sınırı / tamamlanmayanlar

**Yeni Flutter operatör akışı üretim başlangıcını gerektirir; legacy HTTP istemcileri için genel zorunluluk getirilmedi.** Hiç açık oturum yoksa oturum kimliği göndermeyen eski transport akışı çalışır ve yeni talebin `production_session_id` alanı NULL kalır. Bitir/create yarışında bitirme önce kilidi alıp tamamlanmışsa sonradan gelen legacy talep kapanmış oturuma bağlanmaz. Taşıma önce oluşmuşsa bitirme reddedilir. Bu ayrım PostgreSQL testindedir.

Bu API tek başına üretim reçetesi/BOM veya tüm Admin konfigürasyonunu dondurma mekanizması değildir. Mevcut Admin arşivleme/eşleme kuralları bu dilimde genişletilmedi; üretim oturumu içindeki müşteri kimlikleri korunur, fakat tüm konfigürasyonun değişmez sürümü saklanmaz.

Dolu alt parça getirme, mevcut BRING_EMPTY olarak uygulanmadı. Yeni besleme operasyonunun rezervasyon, readiness, pickup/drop ve failure reconciliation sözleşmesi ayrı dilimde netleştirilecek. Dört ortak hazırlık konumu, konuma ürün atama/boşaltma hareketleri, ayrı Android uygulaması ve bildirimler henüz uygulanmadı. Fiziksel isim/eşleme/kapasite bilgisi uydurulmadı.

## Dilim 2 doğrulaması (önceki backend dilimi)

Etkilenen uygulama testleri: **38/38 PASS** (11 yeni üretim servisi testi + 27 mevcut workflow/container policy testi). Yeni dosyalar için `dotnet format whitespace` çalıştırıldı. API Release derlemesi sıfır hata/uyarıyla geçti.

PostgreSQL **18.3** üzerinde HTTP/DB testleri: **66/66 PASS** (32 yeni üretim oturumu testi + 34 mevcut container action/batch/readiness testi). Eşzamanlı aynı/farklı anahtarla başlatma, eşzamanlı bitirme, start/variant-switch, start/transport ve end/transport yarışları geçti. Audit hatası hem başlangıcı hem bitişi atomik geri aldı. Açık durumların tamamında bitirme reddedildi; readiness ve request status değiştirilmedi.

006 migration seed sonrasında ve test kayıtları oluştuktan sonra tekrar uygulandı: üretim/transport/audit kayıt özeti **UNCHANGED**. Production↔transport orphan: **0**. Test DB'sindeki müşteri makine sayısı **9**, RDS task/worksite sayıları **0/0**. `git diff --check` geçti; 001–005 ve seed içerikleri değişmedi.

Testler RDS/dispatch kapalı, 127.0.0.1:55461 üzerinde geçici PostgreSQL kümesinde çalıştı; normal geliştirme DB'sini kullanmadı. Test kümesi durduruldu ve yalnız geçici test verileri temizlendi. Fiziksel/simülatör hareket kabulü değildir. Flutter testleri bu backend diliminde tekrar çalıştırılmadı; önceki dilimin 70/70 sonucu kendi kapsamıyla geçerlidir.

```powershell
dotnet test tests/TkgPda.Application.Tests/TkgPda.Application.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ProductionSessionServiceTests|FullyQualifiedName~TransportWorkflowServiceTests|FullyQualifiedName~ContainerActionResolutionPolicyTests'
powershell -NoProfile -ExecutionPolicy Bypass -File tests/phase_04/run_postgres_api_integration.ps1 -Port 55461 -TestFilter 'FullyQualifiedName~ProductionSessionApiTests|FullyQualifiedName~ContainerActionsApiTests|FullyQualifiedName~TransportBatchApiTests|FullyQualifiedName~TransportReadinessApiTests'
```

## Dilim 3 — operatör ekranı ve eski tablet koruması

- `ProductionCoordinator` yalnız sunucunun doğruladığı oturumu kullanır. Makine seçimi, yeniden giriş ve uygulamaya dönüş aktif üretimi GET ile yükler; otomatik başlangıç POST'u yoktur. Bozuk/eksik cevap ve başarısız okuma taşıma/üretim düğmelerini kapatır. Geç kalan başka makine cevapları seçimi geri yükleyemez.
- LINE_OPERATOR/SUPERVISOR/ADMIN saha istemcisi için akış: makine → ana ürün/uyumlu varyant → **Üretime Başla** → ana ürün veya o ürün grubundaki ilgili/alternatif parça için taşıma. Miktar alanı yoktur. WAREHOUSE_OPERATOR-only hazırlık ekranına bu saha zorunluluğu eklenmedi.
- Açık üretimde ana ürün/varyant sabittir. İlgili parçalar bütün varyantların birleşiminden değil, etkin üretimin ürün grubundan gelir. RELATED alternatif değildir. Diğer varyanta ait ALT seçimi otomatik varyant değişimine yol açmaz.
- **Üretimi Bitir** ayrı açıklamalı onay ister. Açık taşıma, gönderilmemiş sepet veya sonucu belirsiz talep varken kapalıdır. Dialog açıkken üretim değişirse yeni üretim kapatılmaz. HTTP tarafında aynı kural transaction içinde tekrar kontrol edilir.
- Başlatma sonucu belirsizse tam payload ve başlangıç anahtarı bellekte korunur; yalnız açık yeniden deneme aynı isteği gönderir. Belirsiz bitiş tam eski sessionId ile tekrarlanır. Yeni oturum kendiliğinden kapatılmaz. Uygulama tamamen yeniden açılırsa GET ile sunucu durumu alınır, eski POST otomatik gönderilmez.
- Tekli kasa, kasa batch ve remove-all gövdelerinde opsiyonel `productionSessionId` eklendi. Yeni Flutter normal akışında doludur. Sunucu yeni taleplerde makine kilidi altında etkin oturumla eşitliğini kontrol eder. Eski tablet isteği `409 PRODUCTION_SESSION_CHANGED` ile, slot/load/request değişikliği olmadan reddedilir.
- İdempotent transport tekrarında beklenen oturum, mevcut request/çocuk request'lerin `production_session_id` değeriyle karşılaştırılır. Aynı eski oturuma ait retry oturum kapandıktan sonra da geçerlidir; farklı oturum kimliği `IDEMPOTENCY_KEY_REUSED` olur. Gönderim belirsizse istemci yakaladığı üretim kimliğini korur.
- Fiziksel slot, RDS haritası ve rezervasyon istemciye taşınmadı. Üretim paneli bir robot kontrolü değildir. Gönderilmemiş sepet, sunucuda üretimin değiştiği doğrulanırsa temizlenir; gönderim sonucu belirsiz sepet tekrar kimliğini korur.

### Dilim 3 doğrulama

- Flutter ilgili unit/widget/network testleri: **149/149 PASS**, bunların **27** tanesi yeni üretim akışı testidir (17 unit/contract + 10 widget). 390×844, 800×1280, 1280×800 üretim ekranı ölçümleri geçti. Eski akış fixture'ları açıkça `enableProductionWorkflow: false` kullanarak legacy sözleşmeleri korur; yeni üretim testleri ve gerçek uygulama varsayılan **true** kullanır. Test silinmedi.
- `flutter analyze --no-pub`: **PASS**, sorun yok.
- PostgreSQL 18.3 ilgili HTTP/DB testleri: **71/71 PASS**, 37 üretim oturumu + 34 mevcut container/batch/readiness. Beş ek test eski session kimliği, aynı eski kimlikle replay ve stale batch/remove-all atomikliğini doğrular. 006 replay **UNCHANGED**, orphan **0**, test makine sayısı **9**, RDS task/worksite **0/0**. Geçici test kümesi durduruldu ve temizlendi.
- `dotnet build -c Release --no-restore`: **PASS**, sıfır hata/uyarı.
- İlgili Application testleri tekrar: **38/38 PASS** (üretim servisi + transport workflow + container policy).
- `flutter build apk --debug --no-pub`: **PASS**. Mevcut operatör APK'sı `src/TkgPda.Pda/build/app/outputs/flutter-apk/app-debug.apk`; cihaza kurulmadı ve ayrı depo uygulaması değildir. Gradle/Java native-access uyarısı görüldü; araç zinciri değiştirilmedi.
- Browser E2E kaynağı üretim başlatma/bitirmeye uyarlandı (58 mevcut + 3 yeni kontrol). Chrome/Edge ve gerçek cihaz kabulü bu dilimde **çalıştırılmadı**; yukarıdaki sonuçlar bunların yerine geçtiği iddiası taşımaz.
- Çalışan dev DB/API, simülatör, müşteri verileri ve eski migration/seed içerikleri değiştirilmedi. Geliştirme ortamında kullanımdan önce 006 ve güncel API/frontend birlikte kurulmalıdır; yalnız frontend yenilemesi yeterli değildir.

## Yerel korumalı kurulum — 2026-09-15

Kullanıcı mevcut veriyi koruyarak kurulum seçeneğini onayladı. Genel `start_local_stack.ps1` betiği çalıştırılmadı: seed/temizlik ve slot durumlarını yeniden yazma davranışları bu işlem kapsamı dışındaydı.

- Mevcut PostgreSQL 18.3 kümesi `127.0.0.1:55450`, veritabanı `tkg_pda_dev` açıldı. Başlangıçta 9 makine, 21 COMPLETED + 10 CANCELLED taşıma ve 0 açık taşıma vardı.
- Git dışında yedek: `%LOCALAPPDATA%\TKG_PDA_DEV\backups\production-006-20260915-103347\tkg_pda_dev.before-006.dump`. Custom archive listesi okundu; tam restore kabul testi yapılmadı. SHA-256: `49C76697D5E554905B151DF265F886BBED50CF0031C4BA75C651DAB76AD673DA`.
- Yalnız `006_production_sessions.sql` uygulandı. Öncesinde/sonrasında **31 mevcut tablonun** satır sayısı ve sıralı satır özetleri karşılaştırıldı: **0 değişen tablo**. Transport karşılaştırması yalnız yeni nullable `production_session_id` alanını hariç tutar. Yeni oturum tablosu başlangıçta boştur. Ayrıntı yedeğin yanındaki `verification.json` dosyasındadır.
- Güncel Release API `http://127.0.0.1:5000`, Flutter operatör `http://127.0.0.1:7357` adresinde başlatıldı. API readiness doğrulandı; imzalama anahtarı yalnız süreç belleğindedir. Kullanıcı/şifre/yetki/cihaz seed edilmedi, müşteri/RDS eşlemeleri veya kasa durumları güncellenmedi.
- `Rds:Enabled=false`, `Rds:DispatchEnabled=false`, `RdsCoreExecution:Enabled=false`; discovery de kapalı. RDSCore servisinin kendisine müdahale edilmedi. UI'daki robot durumu görüntülenemiyor bildirimi bu kapalı entegrasyon modunda beklenir.
- Kullanıcının belirttiği Fatih hesabıyla, tarayıcıdaki mevcut DEV-SUPERVISOR cihaz kaydı değiştirilmeden giriş başarılı. Gerçek API üzerinden 9 makine, SH-01-A müşteri ana parça listesi ve yeni üretim paneli görüntülendi. Parça seçilmeden başlangıç/toplu taşıma düğmeleri kapalıdır.
- Kullanıcı SH-01-A / `67115BN050-10-HAOS` seçimini onayladı. Gerçek operatör ekranında tek üretim başlatıldı; ana ürün değiştirme düğmesinin kilitli olduğu doğrulandı. Tarayıcı tamamen yenilendi, aynı kullanıcıyla tekrar giriş yapılıp makine seçilince aynı oturum otomatik geri yüklendi. Üretim başlangıcı tekrar gönderilmedi. Bitiş açıklamalı dialog ile onaylandı; ana parça seçim ekranına geri dönüldü.
- Kabul oturumu `08ba68af-07ab-4980-a971-0216d095e5f0`: başlangıç **10:39:07**, bitiş **10:41:00** (Europe/Istanbul). Aynı Fatih kullanıcısı/DEV-SUPERVISOR cihazı; varyant **1**. DB sonucu: **1 kapanmış oturum, 0 açık oturum, 1 PRODUCTION_STARTED + 1 PRODUCTION_ENDED audit**. Bu kabul kaydı silinmedi.
- Taşıma sayısı hâlâ **31**, açık taşıma **0**. Eski tabloların son karşılaştırmasında yalnız `auth.users` (normal giriş bilgisi) ve `audit.audit_logs` değişti. Müşteri master, slot runtime, makine runtime, mapping ve RDS/transport kayıtlarının tamamı önceki özetleriyle aynı. Yerel ayrıntılı sonuç yedeğin yanındaki `ui-acceptance.json` dosyasındadır.
- Bu, gerçek yerel API/PostgreSQL ile **Codex uygulama içi tarayıcı** kabulüdür; Chrome/Edge otomatik suite veya Android cihaz kabulü değildir. Açık taşımada bitiş engeli bu turda yeni taşıma yaratarak tekrarlanmadı; önceki PostgreSQL/widget test kanıtı geçerlidir. **Taşıma düğmesine basılmadı, RDS/robot görevi gönderilmedi.**
- Admin Web bu kurulum adımında açılmadı. Başlatıcı yardımcıları `.runtime/` altında yerel/ignored kalır; yedek ve loglar repoya alınmaz. Commit/push yapılmadı.
