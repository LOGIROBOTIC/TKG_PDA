# RDSCore Native Order Simulator Acceptance Runbook

## Durum ve amaç

Bu runbook WP-31B için hazırlanmış, **henüz çalıştırılmamış** simulator kabul prosedürüdür. Amaç yalnız aşağıdaki navigation/order yaşam döngüsünü doğrulamaktır:

```text
TKG native-order client
  -> POST /setOrder
  -> sealed NORMAL order
  -> simulator robot assignment/navigation
  -> GET /orderDetails/{id}
  -> FINISHED
```

Bu kabul; üretim WorkSite anlamını, binTask/fork sözleşmesini veya TKG fiziksel business state sonucunu kanıtlamaz.

> **LIVE EXECUTION REQUIRES EXPLICIT USER AUTHORIZATION.** Açık kullanıcı yetkisi verilmeden `POST /setOrder` çağrısı yapılmaz. Yapılandırmada `RdsCoreExecution.Enabled=true` olması veya bu runbook'un bulunması yürütme yetkisi değildir.

## Kanıtlanmış simulator baseline'ı

Önceki salt-okunur discovery snapshot incelemesinde aşağıdaki kanıtlar kaydedilmiştir:

- RDS Web UI: `http://127.0.0.1:8080/#/view`
- RDSCore origin: `http://127.0.0.1:8088`
- Simulator robotlar: `sim_01`, `sim_02`
- Robot grubu: `g2`
- Map: `default`
- Geçerli WorkSite'lar: `PS-1-1`…`PS-1-11`, `WS-1-1`…`WS-1-11`
- Point bağları: `PS-1-1`…`PS-1-11` → `AP12`…`AP22`; `WS-1-1`…`WS-1-11` → `AP23`…`AP33`
- Gözlenen binTask: yok
- Disabled point/path: yok
- Bağımlı cihaz: yok

Bu bilgiler geçmiş evidence'tır; canlı kabul öncesi pre-flight ile yeniden doğrulanır. `PS` ve `WS` adlarından üretimsel pickup/dropoff anlamı çıkarılmaz.

## Önerilen kabul girdileri

| Girdi | Aday değer | Durum |
|---|---|---|
| `vehicle` | `sim_01` | Açık kullanıcı onayı gerekir |
| `pickupLocation` | `PS-1-1` (`AP12`) | Açık kullanıcı onayı gerekir |
| `dropoffLocation` | `PS-1-2` (`AP13`) | Açık kullanıcı onayı gerekir |

Bu çift, snapshot'ta geçerli ve bağlı point'leri bulunan ardışık iki simulator WorkSite olduğu için kısa navigation-only adaydır. BinTask, device, charge veya park davranışına ihtiyaç duymaz; bilinen disabled point/path yoktur. Bu seçim fiziksel/üretim anlamı taşımaz.

Gelecekteki pre-flight sırasında aday robot boşta değilse veya aday lokasyonlardan biri güvenli değilse **otomatik olarak `sim_02` ya da başka bir WorkSite'a geçilmez**. Test durdurulur ve yeni girdiler için açık onay alınır.

## Runtime girdileri

Her kabul koşusu aşağıdaki değerleri açıkça almalıdır:

- `rdsCoreBaseUrl`: açık simulator origin'i; `8088` türetilmez.
- `targetClassification`: tam olarak `SIMULATOR`.
- `vehicle`: onaylanmış simulator robot ID'si.
- `pickupLocation`: onaylanmış simulator WorkSite.
- `dropoffLocation`: onaylanmış, pickup'tan farklı simulator WorkSite.
- `orderUuid`: koşu başında üretilen yeni UUID.
- `explicitLiveAuthorization`: bu tek simulator order'ını açıkça onaylayan kayıt.

Girdi değerleri production uygulama koduna hard-code edilmez. Yetki kaydı yoksa prosedür pre-flight'tan sonra bile mutation aşamasına geçmez.

## Pre-flight: yalnız salt-okunur

Pre-flight sonuçları aynı kısa zaman aralığında alınmalı ve kabul raporuna eklenmelidir.

### 1. Robot kontrolü

`GET /robotsStatus`

Seçilen robot için doğrula:

- robot mevcut;
- `connection_status` bağlı durumu gösteriyor;
- `dispatchable == true`;
- current map `default`;
- yürütmeyi engelleyen alarm yok;
- başka bir business order çalıştırmıyor.

Son madde `/robotsStatus` yanıtından kesinleşmiyorsa `GET /orders` ile robotun aktif `CREATED`, `TOBEDISPATCHED`, `WAITING`, `RUNNING` veya `SUSPENDED` order'ları kontrol edilir ve RDS UI'da doğrulanır. Kanıt belirsizse order oluşturulmaz.

### 2. WorkSite kontrolü

`POST /binCheck` çağrısı yalnız seçilmiş iki ID ile yapılır:

```json
{
  "bins": ["<pickupLocation>", "<dropoffLocation>"]
}
```

Her iki lokasyon için doğrula:

- `exist == true`;
- `valid == true`;
- boş olmayan `point_name` mevcut;
- navigation-only kullanımını engelleyen hata yok.

Bin adı veya point'ten business yük anlamı çıkarılmaz.

### 3. Navigation kısıtları

- `GET /getDisablePoints`
- `GET /getDisablePaths`

Seçilen bağlı point'lerin disabled olmadığı ve aday geçişi engelleyen belirgin disabled path bulunmadığı doğrulanır. Exact rota planlama tahmin edilmez; şüphe varsa order oluşturulmaz.

### Pre-flight fail-closed kuralı

Robot, WorkSite veya navigation kontrollerinden biri başarısız/belirsizse:

- `POST /setOrder` çağrılmaz;
- başka robot/lokasyon otomatik seçilmez;
- sonuç `BLOCKED` olarak raporlanır.

## Onaylandığında izin verilen order

WP-31B'deki tek planlı business mutation, açıkça onaylanan **tam bir** simulator order'ı için bir kez `POST /setOrder` çağrısıdır.

```json
{
  "id": "<orderUuid>",
  "vehicle": "<vehicle>",
  "blocks": [
    {
      "blockId": "<orderUuid>:pickup",
      "location": "<pickupLocation>"
    },
    {
      "blockId": "<orderUuid>:dropoff",
      "location": "<dropoffLocation>"
    }
  ],
  "complete": true
}
```

Payload'da bulunmayacak alan/davranışlar:

- production/external mapping;
- `binTask`, `goodsId`;
- `operation`, `operationArgs`;
- `Script`, `scriptArgs`;
- fork height veya recognition;
- `postAction`;
- IO veya cihaz davranışı.

`id`, testte üretilen UUID ile aynı; block ID'leri deterministic olmalı ve `complete=true` değiştirilmemelidir.

## Yasak mutation'lar ve acil cleanup

Kabul sırasında aşağıdakiler kullanılmaz:

- `/addBlocks`
- `/setForkHeight`
- `/controlMotion`
- `/setRobotIO`
- `/manualFinished`
- `/cancelBlock`
- `/setPriority`
- `/setLabel`
- `/lock`, `/unlock`
- `/reloc`, `/switchMap`
- diğer robot/RDS mutation endpoint'leri

`POST /terminate` yalnız bu exact kabul order'ı timeout/abort nedeniyle güvenli biçimde temizlenmek zorundaysa kullanılabilir:

```json
{
  "id": "<same-orderUuid>",
  "disableVehicle": false
}
```

Boş `idList`, terminate-all ve vehicle disable kesinlikle yasaktır. Cleanup ihtiyacı ve sonucu ayrıca raporlanır.

## SetOrder sonucu

Kaydedilecek alanlar:

- HTTP status;
- Core business `code`;
- `msg`;
- varsa `order_details`;
- `errors`, `warnings`, `notices`.

HTTP 2xx tek başına yeterli değildir. Core `code=0` yalnız API-level kabul kanıtıdır. `order_details.errors` boş değilse test `BLOCKED` olur ve lifecycle başarı sayılmaz.

## Bounded reconciliation gözlemi

Order oluşturulduktan sonra yalnız `GET /orderDetails/{orderUuid}` kullanılarak muhafazakâr polling yapılır:

- önerilen interval: 2 saniye;
- önerilen üst sınır: 180 saniye;
- aynı order UUID dışında sorgu/mutation yok;
- gözlenen her yeni order state ve block state, ilk gözlenme zamanı ile bir kez kaydedilir.

Örnek olası order sırası `CREATED → TOBEDISPATCHED → RUNNING → FINISHED` olabilir; exact ara sıra varsayılmaz. Block'lar bağımsız izlenir.

Polling şu durumlarda sona erer:

- başarı adayı: `FINISHED`;
- terminal hata: `FAILED`, `STOPPED`, `Error`;
- 180 saniyelik timeout.

`SUSPENDED` gözlenirse TKG'de otomatik karşılığı olmadığı için polling kanıtı korunur, test güvenli reconciliation kararı bekleyen `BLOCKED` sonucuna döner. Bilinmeyen future state de ham değerle raporlanır ve otomatik TKG mutasyonu yapılmaz.

## Tekillik ve idempotency kanıtı

- `GET /orderDetails/{orderUuid}` primary correlation kaynağıdır.
- Read-only `GET /orders` sayfaları aynı `id` için taranarak instance sayısı `1` olarak doğrulanır.
- Kabul sırasında `/setOrder` ikinci kez çağrılmaz.
- Aynı ID ile ikinci order görülürse veya başka bir RDS ID üretilirse sonuç `BLOCKED` olur.

## Kabul kriterleri

### PASS

- Tam bir order UUID üretilmiş ve `setOrder.id` ile aynı.
- Pickup/dropoff block ID'leri deterministic.
- `complete=true`.
- RDS'te aynı ID için tam bir order instance var.
- Seçili simulator robot assignment'ı görünür.
- Order `FINISHED` olur.
- Pickup ve dropoff block'larının ikisi de `FINISHED` gösterir; `MANUAL_FINISHED` bu kabulde başarı sayılmaz.
- Gözlenen bütün durumlar güvenli biçimde parse/reconcile edilir.
- İzinli sınır dışında mutation yok.
- TKG fiziksel/business state mutasyonu yok.

### BLOCKED / FAIL

- Pre-flight başarısız veya belirsiz.
- Core `/setOrder` çağrısını reddeder.
- `order_details.errors` boş değildir.
- Lokasyon yok/geçersiz/ulaşılamaz.
- Robot bağlı veya dispatchable değildir ya da boşta olduğu kanıtlanamaz.
- Beklenmeyen duplicate vardır.
- `FAILED`, `Error` veya cleanup dışı `STOPPED` görülür.
- Bounded timeout oluşur.
- RDS state güvenli biçimde yorumlanamaz.

## Fiziksel ve business güvenliği

RDS order `FINISHED` olsa bile aşağıdakiler değiştirilmez:

- TKG occupancy;
- TKG load state;
- Part↔Slot;
- `EMPTY_STORAGE`;
- `REMOVE_FULL` destination.

WP-31B yalnız navigation ve native-order lifecycle kabulüdür.

## Gelecekteki completion report şablonu

```text
WP-31B NATIVE ORDER SIMULATOR ACCEPTANCE

ENVIRONMENT
SIMULATOR

RDSCORE
origin:

INPUTS
vehicle:
pickup:
dropoff:
order UUID:

PRE-FLIGHT
robot:
pickup:
dropoff:
disabled resources:

SETORDER
HTTP:
Core code:
errors:
warnings:
notices:

ORDER LIFECYCLE
state sequence:

BLOCK LIFECYCLE
pickup:
dropoff:

ROBOT
assigned robot:
start station:
finish station:

IDEMPOTENCY
RDS order instances:
duplicate detected:

TKG
physical-state mutations: 0

SAFETY
setOrder calls:
terminate calls:
other mutation calls:
fork actions:
IO actions:

RESULT
PASS / BLOCKED
```

## Mevcut hazırlık sonucu

Runbook hazırlanmıştır; canlı kabul yetkisi verilmemiştir. Bu hazırlık sırasında RDSCore çağrısı, order oluşturma, terminate veya robot hareketi yapılmamıştır.
