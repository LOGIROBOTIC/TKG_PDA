# RDSCore İlk Salt-Okunur Snapshot Runbook'u

## Amaç ve çalıştırma yasağı

Bu runbook, WP-30 ile eklenen RDSCore Commissioning Discovery yeteneğinden ilk kanıt snapshot'ının nasıl alınacağını tarif eder. Bu belge hazırlanırken capture çalıştırılmamıştır. İlk gerçek çalışma ayrıca yetkilendirilmeli ve hedef `SIMULATOR`, `LOCAL_TEST` veya `PRODUCTION` olarak kaydedilmelidir.

Snapshot kanıttır; robot görevi oluşturmaz, robotu hareket ettirmez ve TKG/RDS yapılandırmasını otomatik değiştirmez.

## 1. Gerekli girdiler

Capture başlamadan önce aşağıdakilerin tamamı yazılı olarak bilinmelidir:

- Açık RDSCore BaseURL: tam `scheme://host:port/` origin'i. Port varsayılmaz ve mevcut RDS Web API adresinden/port `8080` değerinden türetilmez.
- Hedef sınıfı: yalnız `SIMULATOR`, `LOCAL_TEST` veya `PRODUCTION`.
- `RdsCoreDiscovery:Enabled = true` olduğunun açık teyidi.
- Backend API yüzeyi kullanılacaksa geçerli `ADMIN_WEB` yetkilendirmesi. Operator/PDA token'ı kabul edilmez.
- TKG API BaseURL'si ve capture için atanmış correlation ID.
- Çıktıyı alacak güvenli, Git dışı yerel dizin.

Uygulama ayarları servis ortamında açıkça sağlanır:

```text
RdsCoreDiscovery__Enabled=true
RdsCore__BaseUrl=<EXPLICIT_RDSCORE_ORIGIN>
RdsCore__TimeoutSeconds=5
RdsCore__AllowInsecureRemoteHttp=false
```

Loopback dışındaki HTTP ancak saha güvenlik değerlendirmesi ve açık commissioning kararıyla `AllowInsecureRemoteHttp=true` yapılabilir. BaseURL credential, path, query veya fragment içeremez. Admin parolası/JWT, RDS anahtarı veya başka secret runbook'a, komut geçmişine ya da çıktı klasörü adına yazılmaz.

## 2. Capture öncesi güvenlik kanıtı

Capture API'si serbest `method + path + JSON` proxy değildir. Kod içindeki closed typed registry yalnız aşağıdaki endpoint'leri seçebilir.

Varsayılan envanter:

- `GET /robotsStatus`
- `POST /binCheck`
- `GET /devicesDetails`
- `POST /getRobotParkPoints`
- `GET /getChargeParam`
- `GET /getDisablePoints`
- `GET /getDisablePaths`

Yalnız sonradan açıkça talep edilebilen typed probe'lar:

- `POST /getCoreParam`
- `POST /queryConnectivity`
- `POST /queryDist`
- `GET /orderDetails/{id}`
- `GET /orderDetailsByExternalId/{externalId}`
- `GET /blockDetailsById/{blockId}`

İlk çalışmadan önce deployment'ın incelenen WP-30 commit'ini içerdiği, `RdsCoreDiscoveryClient.AllowedOperations` listesinin yukarıdaki 13 kayıttan oluştuğu ve API request modelinde arbitrary HTTP method/path/body alanı bulunmadığı doğrulanır.

Capture hiçbir koşulda şunları çağırmamalıdır:

- `/setOrder`
- `/terminate`
- `/lock`
- `/unlock`
- `/dispatchable`
- `/switchMap`
- `/reloc`
- `/setForkHeight`
- `/controlMotion`
- `/setRobotIO`
- `/setCoreParam`
- başka herhangi bir mutation endpoint'i

RDS execution worker/dispatch bu kontrolün parçası değildir. Salt-okunur capture süresince mevcut production dispatch politikasında değişiklik yapılmaz; discovery'yi etkinleştirmek `Rds:DispatchEnabled` değerini etkinleştirmez.

## 3. Varsayılan ilk snapshot

İlk baseline capture yalnız aşağıdaki yedi çağrıyı kullanır:

```text
GET  /robotsStatus
POST /binCheck              {"bins":[]}
GET  /devicesDetails
POST /getRobotParkPoints    {"vehicles":[]}
GET  /getChargeParam
GET  /getDisablePoints
GET  /getDisablePaths
```

`{"bins":[]}` yalnız `/binCheck` read/discovery sözleşmesinde bütün bin'leri listelemek içindir; genel empty-array mutation güvenliğini değiştirmez.

İlk request body yalnız açık onayı içermelidir:

```json
{
  "confirmReadOnlyDiscovery": true
}
```

İlk baseline'da şu alanlar gönderilmez ve endpoint'ler çağrılmaz:

- `/getCoreParam`
- `/queryConnectivity`
- `/queryDist`
- `/orderDetails` ve `/orderDetailsByExternalId` probe'ları
- `/blockDetailsById` probe'ları

Bunlar ancak baseline incelendikten sonra mimari tarafından ayrı ve açık kapsamla istenebilir.

## 4. Hazırlanmış capture prosedürü — çalıştırma

Bu komutlar yalnız capture ayrıca onaylandığında çalıştırılır. `<TKG_API_ORIGIN>` ve hedef sınıfı açık değerlerle doldurulur; JWT güvenli süreçle `TKG_ADMIN_WEB_JWT` environment variable'ına verilir ve ekrana basılmaz.

```powershell
$ErrorActionPreference = 'Stop'
$targetClass = '<SIMULATOR|LOCAL_TEST|PRODUCTION>'
if ($targetClass -notin @('SIMULATOR', 'LOCAL_TEST', 'PRODUCTION')) {
    throw 'Target classification must be explicit.'
}
if ([string]::IsNullOrWhiteSpace($env:TKG_ADMIN_WEB_JWT)) {
    throw 'ADMIN_WEB JWT must be supplied through the approved secure process.'
}

$stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$outputRoot = Join-Path $env:LOCALAPPDATA "TKG_PDA\commissioning-snapshots\$targetClass\$stamp"
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$headers = @{
    Authorization = "Bearer $env:TKG_ADMIN_WEB_JWT"
    'X-Correlation-ID' = [Guid]::NewGuid().ToString()
}
$response = Invoke-RestMethod `
    -Method Post `
    -Uri '<TKG_API_ORIGIN>/api/v1/admin/rds/commissioning/capture' `
    -Headers $headers `
    -ContentType 'application/json' `
    -Body '{"confirmReadOnlyDiscovery":true}'

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $outputRoot 'rds-core-snapshot.json'), [string]$response.canonicalJson, $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $outputRoot 'rds-core-snapshot.sha256'), ([string]$response.snapshotSha256 + "`n"), $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $outputRoot 'rds-core-report.md'), [string]$response.markdownReport, $utf8NoBom)
Remove-Item Env:TKG_ADMIN_WEB_JWT -ErrorAction SilentlyContinue
```

Bu prosedür şu üç çıktıyı Git dışındaki dizine yazar:

```text
%LOCALAPPDATA%\TKG_PDA\commissioning-snapshots\<TARGET>\<UTC_TIMESTAMP>\
  rds-core-snapshot.json
  rds-core-snapshot.sha256
  rds-core-report.md
```

Canonical JSON'un yerel SHA-256 değeri tekrar hesaplanıp `snapshotSha256` ile karşılaştırılır. Uyuşmazlık varsa çıktı incelemeye alınmaz.

Gerçek `PRODUCTION` snapshot'ı operasyonel topology, hostname/IP, map, cihaz ve yapılandırma kanıtı içerebilir. Otomatik olarak repository'ye kopyalanmaz veya commit edilmez. Simulator/local snapshot'lar da varsayılan olarak untracked kalır; herhangi bir snapshot'ın paylaşılması ayrıca veri sahibi ve mimari onayı gerektirir.

## 5. İlk snapshot inceleme checklist'i

### Robotlar

- Discovered robot count
- `uuid`
- `vehicle_id`
- Robot group
- Labels
- Current map
- Connection/online state
- Control lock/owner state
- Dispatchability
- Fatal/error/warning/notice alarms
- Battery level ve charging durumu

### Bin / WorkSite

- Toplam bin/WorkSite sayısı
- Her kayıt için `exist`
- Her kayıt için `valid`
- `point_name`
- binTask key listesi
- binTask operation türleri
- Döndürülmüşse script name
- Safe operation/script arguments; secret-shaped değerlerin redacted olduğu

### Navigation

- Disabled points
- Disabled paths

### Devices

- Terminals
- Doors
- Lifts
- Wind showers
- Her tip için connection/availability/disabled kanıtı

### Park / Charge

- Robot bazlı park points ve current target
- Robot bazlı charge configuration

### Teknik bütünlük

- Yedi default endpoint sonucunun her biri
- HTTP status ile RDSCore business `code` ayrımı
- Snapshot schema version
- Safe origin
- Observed maps
- Correlation ID
- Canonical JSON SHA-256 doğrulaması
- Markdown finding sınıfları: `BLOCKER`, `GAP`, `WARNING`, `INFO`, `NOT_EVALUATED`

## 6. Yorumlama kuralları

Snapshot'tan aşağıdakiler türetilmez veya otomatik yapılandırılmaz:

- TKG Part↔Slot ilişkisi
- bilinen `EMPTY_STORAGE` ve `REMOVE_FULL` alanlarının TKG Part↔Slot ilişkisi
- exact RDS WorkSite/bin/point commissioning eşlemesi
- Container `EMPTY/FULL` business state
- Operation→binTask sözleşmesi
- TKG'nin ihtiyaç duyduğu fork height değerleri
- Recognition gereksinimi veya recognition parametreleri

RDS `filled`, `container`, `goodsId` ve `loaded` alanları ham RDS kanıtıdır; TKG load state değildir. binTask key/operation/script görülmesi, onun `BRING_EMPTY`, `REMOVE_FULL` veya `REMOVE_EMPTY` için uygun olduğunu tek başına kanıtlamaz. Missing customer/site decision, RDS failure olarak etiketlenmez.

## 7. İnceleme sonrası karar

Gerçek snapshot mimari incelemeden geçtikten sonra WP-31, production RDSCore native Order API sözleşmesini tanımlayacaktır:

```text
TransportRequest
  → /setOrder
  → deterministic pickup/drop blocks
  → confirmed RDS bin
  → confirmed binTask
  → /orderDetails reconciliation
```

WP-31; production bin ve binTask sözleşmesi, deterministic block yapısı, gerekli fork/recognition kanıtı ve reconciliation kuralları gerçek snapshot ile doğrulanmadan başlamaz. Snapshot incelemesi öncesinde yalnız mimarinin açık onayı bu sırayı değiştirebilir.

## 8. Kapanış kaydı

Capture sonunda review kaydı hedef sınıfını, capture zamanını, correlation ID'yi, snapshot SHA-256'yı, inceleyen kişiyi ve sonucu içermelidir. JWT, parola, RDS key, response body veya production topology normal audit mesajına yazılmaz. Discovery işi bitince `RdsCoreDiscovery:Enabled` tekrar `false` yapılır; bu işlem RDS veya robot tarafına mutation göndermez.
