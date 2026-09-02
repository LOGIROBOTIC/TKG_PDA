# RDS Commissioning Snapshot Schema

## Sözleşme

Canonical JSON schema sürümü `rds_commissioning_snapshot_v1`'dir. JSON object property'leri ordinal olarak sıralanır; normalize listeler sabit anahtarlarla sıralanır ve dosya tek newline ile biter. SHA-256 bu canonical UTF-8 metin üzerinden hesaplanır.

Üst seviye alanlar:

| Alan | Tip | Açıklama |
|---|---|---|
| `schemaVersion` | string | Snapshot sözleşme sürümü |
| `capturedAtUtc` | ISO-8601 timestamp | Bilinçli değişken capture metadatası |
| `correlationId` | UUID | API/audit korelasyonu |
| `source` | object | Sanitize origin, opsiyonel Core version, observed maps ve map hash kanıtı |
| `endpointResults` | array | Her çağrının method/path, HTTP/business code ve normalize sonucu |
| `robots` | array | Canonical `uuid`, ayrıca `vehicleId`, group/label/map/telemetry/park/alarm kanıtı |
| `workSites` | array | bin id, exist/valid, point, ham RDS semantics ve binTask listesi |
| `devices` | array | Cihaz türü/adı ve sanitize edilmiş ham durum |
| `navigation` | object | Disabled point/path ve typed connectivity/distance probe'ları |
| `chargeParameters` | array | Robot bazlı ham eşik metinleri |
| `selectedCoreParameters` | object | Allowlist durumu ve yalnız onaylı parametre sonuçları |
| `orderProbes` | array | Yalnız açık istekle alınan sanitize order kanıtı |
| `blockProbes` | array | Yalnız açık istekle alınan sanitize block kanıtı |

## Robot kimliği

`robots[].canonicalId`, `uuid` mevcutsa ona eşittir. `robots[].uuid` ve `robots[].vehicleId` ayrı alanlar olarak korunur. `uuid` yoksa geriye dönük gözlem için `vehicleId` canonical fallback olabilir; bu fallback production robot eşlemesi oluşturmaz.

## WorkSite ham semantiği

`workSites[]` şunları taşır:

- `id`, `exists`, `valid`, `pointName`;
- `filled`, `container`, `goodsId`, `loaded` — kaynak JSON tipleri korunmuş ham RDS kanıtı;
- `binTasks[].key`, `operation`, `scriptName`, sanitize `safeArguments`.

`filled`, `container`, `goodsId` veya `loaded`, TKG `EMPTY/FULL` load state'ine çevrilmez. binTask adından `BRING_EMPTY`, `REMOVE_FULL` veya `REMOVE_EMPTY` ilişkisi türetilmez.

## Endpoint sonucu

`endpointResults[].status` değerleri:

- `SUCCESS`;
- `FAILED`;
- `NOT_FOUND`.

Normalize error code'ları `RDSCORE_BUSINESS_FAILURE`, `RDSCORE_HTTP_FAILURE`, `RDSCORE_NOT_FOUND`, `RDSCORE_TIMEOUT`, `RDSCORE_UNAVAILABLE` ve `RDSCORE_INVALID_JSON` olabilir. Core business `code` mevcutsa `0` başarıdır; `200` Core business başarı kodu olarak varsayılmaz.

## Redaction

Snapshot'a alınan serbest JSON kanıtı recursive sanitize edilir. Adı aşağıdaki secret-shaped parçaları içeren property değerleri `[REDACTED]` olur:

`password`, `passwd`, `secret`, `token`, `credential`, `authorization`, `apiKey`, `privateKey`, `integrationKey`, `callbackKey`.

Normal log ve audit'e request/response body yazılmaz. Audit yalnız schema version, safe origin, robot/WorkSite/endpoint/finding sayıları, snapshot SHA-256 ve correlation bilgisini taşır. BaseURL kullanıcı bilgisi içeremez; tanılama yalnız `scheme://host:port` gösterir.

## Determinizm notu

Aynı normalize veri, aynı `capturedAtUtc` ve aynı `correlationId` ile byte-eşdeğer canonical JSON ve Markdown raporu üretir. Capture zamanı ve correlation ID bilinçli değişken metadatadır; iki ayrı canlı capture'ın hash'lerinin eşit olması beklenmez.

## Kısaltılmış örnek

```json
{
  "blockProbes": [],
  "capturedAtUtc": "2026-09-02T10:00:00+00:00",
  "chargeParameters": [],
  "correlationId": "11111111-1111-1111-1111-111111111111",
  "devices": [],
  "endpointResults": [],
  "navigation": {
    "connectivity": [],
    "disabledPaths": [],
    "disabledPoints": [],
    "distances": []
  },
  "orderProbes": [],
  "robots": [
    {
      "canonicalId": "robot-uuid",
      "uuid": "robot-uuid",
      "vehicleId": "sim_01"
    }
  ],
  "schemaVersion": "rds_commissioning_snapshot_v1",
  "selectedCoreParameters": {
    "status": "NOT_CAPTURED",
    "values": {}
  },
  "source": {
    "safeOrigin": "https://rds.example.invalid:8088"
  },
  "workSites": []
}
```

Örnek yalnız alan anlamını gösterir; zorunlu serializer çıktısındaki diğer nullable robot/source alanları burada kısaltılmıştır.
