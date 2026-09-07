# RDSCore Native Order Contract

## Amaç ve mimari sınır

WP-31A, `TransportRequest` ile RDSCore Native Order arasında üretime aday, kapalı ve typed bir entegrasyon portu sağlar. Bu port mevcut WindTask entegrasyonunu silmez veya devralmaz. Varsayılan mod `WIND_TASK`, native execution ise varsayılan olarak kapalıdır.

```text
TransportRequest UUID
  -> IRdsCoreNativeOrderClient
  -> POST /setOrder
  -> normal, sealed order blocks
  -> GET /orderDetails/{id}
  -> saf reconciliation policy
```

İstemci yalnız aşağıdaki endpoint'leri bilir:

| Tür | Endpoint |
|---|---|
| Mutation | `POST /setOrder` |
| Read | `GET /orderDetails/{id}` |
| Read | `GET /orders` |
| Read | `GET /orderDetailsByExternalId/{externalId}` |
| Read | `GET /orderDetailsByBlockId/{blockId}` |
| Read | `GET /blockDetailsById/{blockId}` |
| Control | `POST /terminate` |

Serbest method/path/body proxy'si yoktur. `addBlocks`, joinable order, recovery/maintenance endpoint'leri ve terminate-all biçimleri bu portta temsil edilmez.

## Yapılandırma ve mod güvenliği

```json
{
  "RdsCore": {
    "BaseUrl": "",
    "TimeoutSeconds": 5,
    "AllowInsecureRemoteHttp": false
  },
  "RdsCoreDiscovery": {
    "Enabled": false
  },
  "RdsCoreExecution": {
    "Enabled": false,
    "Mode": "WIND_TASK"
  }
}
```

- `RdsCore:BaseUrl` açık bir HTTP(S) origin olmalıdır; `8088` veya localhost fallback'i türetilmez.
- Discovery ve execution aynı açık Core origin'ini kullanır fakat enable sınırları bağımsızdır.
- Discovery'nin başarılı/aktif olması execution'ı açmaz.
- Native mutation için hem `RdsCoreExecution:Enabled=true` hem `Mode=RDSCORE_NATIVE_ORDER` gerekir.
- Mevcut WindTask `Rds:*` yapılandırması ve worker'ı değişmeden kalır. WP-31A herhangi bir production default switch yapmaz.
- Önceki WP-30 kurulumları için eski `RdsCoreDiscovery:BaseUrl/TimeoutSeconds/AllowInsecureRemoteHttp` anahtarları geçici okuma uyumluluğu olarak kabul edilir; yeni kurulumlarda ortak `RdsCore:*` anahtarları kullanılmalıdır.

## Normal ve sealed order sözleşmesi

Yalnız normal order desteklenir; joinable order modeli yoktur. Desteklenen üst alanlar:

`id`, `externalId`, `vehicle`, `group`, `label`, `keyRoute`, `keyTask`, `priority`, `blocks`, `complete`.

`keyRoute` SEER sözleşmesindeki gibi string dizisidir. `vehicle`, `group`, `label`, route ve task değerleri yalnız açık commissioning/configuration girdisinden gelebilir; uygulama bunları tahmin etmez.

TKG order'ları her zaman `complete=true` ile mühürlenir. Açık order, `markComplete` ve `addBlocks` desteklenmez.

## Kimlik ve idempotency

- `setOrder.id` doğrudan küçük harfli canonical `TransportRequest UUID` (`D` formatı) olur.
- Ayrı ve ilişkisiz bir RDS kimliği üretilmez.
- Pickup block ID: `<transport-request-uuid>:pickup`.
- Dropoff block ID: `<transport-request-uuid>:dropoff`.
- Factory bu iki ID dışındaki block kimliklerini reddeder.
- `externalId` yalnız ikincil iş korelasyonudur. Aynı external ID birden fazla RDS order'a ait olabileceğinden primary reconciliation anahtarı değildir.
- RDS `50003` duplicate-ID yanıtı ayrı işaretlenir; bu yanıt kendi başına execution başarı kanıtı değildir.

## Block şekilleri

İki block'lu ilk sözleşme pickup ve dropoff kimliklerini kullanır. Her block için `location` açıkça sağlanmalıdır.

- Navigation-only: `blockId`, `location`.
- Operation: ek olarak `operation`, isteğe bağlı `operationArgs`, `postAction`.
- Script: ek olarak sabit `operation: "Script"`, `scriptName`, isteğe bağlı `scriptArgs`, `postAction`.
- Bin task: ek olarak `binTask`, isteğe bağlı `goodsId`, `postAction`.

Navigation-only şekil, mevcut simulator snapshot'ında 22 geçerli WorkSite üzerinde binTask görülmemesi nedeniyle bağımsız desteklenir. Kodda AP/PS/WS adı hard-code edilmez. `ForkLoad`, `ForkUnload`, fork yüksekliği, recognition, pickup/drop geometrisi veya TKG operation-to-binTask eşlemesi üretilmez.

## `/setOrder` yanıt semantiği

Üç kanıt ayrı tutulur:

- `HTTP_ACCEPTED`: HTTP 2xx alındı.
- `CORE_ACCEPTED`: HTTP kabul edildi ve business `code=0`.
- `EXECUTION_VALIDATED`: Core kabul etti, `order_details` döndü ve `errors` boş.

`code=0`, tek başına yalnız API seviyesinde kabul kanıtıdır. RDS parametresi `RDSSetOrderReturnMoreInfo` kapalıysa `order_details` gelmeyebilir; bu durumda execution doğrulandı denmez. `errors`, `warnings` ve `notices` typed olarak korunur. Bilinmeyen response alanları raw JSON kanıtında saklanır ve parsing'i bozmaz.

## Order ve block durumları

Bilinen normal-order durumları: `CREATED`, `WAITING`, `RUNNING`, `SUSPENDED`, `FINISHED`, `FAILED`, `STOPPED`, `Error`, `TOBEDISPATCHED`.

Bilinen block durumları: `CREATED`, `RUNNING`, `WAITING`, `POSTING`, `SUSPENDED`, `FINISHED`, `FAILED`, `STOPPED`, `MANUAL_FINISHED`, `Error`.

Bilinmeyen veya eksik durum exception üretmez; `Unknown` sınıfıyla birlikte ham değer korunur.

`GET /orders` için typed ve sınırlandırılmış `page`, `size`, `orderBy`, `orderMethod` modeli kullanılır. Raw `where` JSON filtresi bu ilk portta açılmaz. External-ID sorgusu dizi döndürür; block-ID ve primary-ID sorguları tek order döndürür.

## Reconciliation politikası

WP-31A politikası saf bir karar katmanıdır; repository veya workflow state'ini kendisi değiştirmez.

| RDS durumu | TKG yorumu |
|---|---|
| `CREATED`, `TOBEDISPATCHED`, `WAITING` | Pending / dispatched family |
| `RUNNING` | Running |
| `SUSPENDED` | Suspended/recoverable; otomatik TKG mutasyonu yok |
| `FINISHED` | Completed candidate |
| `FAILED`, `Error` | Failed candidate |
| `STOPPED` | Cancelled/terminated candidate |
| Bilinmeyen | Kanıt; otomatik mutasyon yok |

Politika hedefe ancak mevcut `TransportStateMachine` üzerinden ulaşan yasal bir geçiş yolu üretir. Terminal TKG state daha sonraki çelişkili RDS kanıtıyla yeniden yazılmaz. TKG'de `SUSPENDED` state bulunmadığından bu durum commissioning/reconciliation kararı bekler.

RDS `FINISHED`, fiziksel iş sözleşmesi commissioning ile kanıtlanana kadar slot occupancy/load sonucunu kanıtlamaz. WP-31A hiçbir slot, load veya occupancy güncellemesi yapmaz.

## Terminate güvenliği

Normal iptal yalnız belirli tek order için şu şekildedir:

```json
{
  "id": "<transport-request-uuid>",
  "disableVehicle": false
}
```

SEER varsayılanı `disableVehicle=true` olduğundan alan hiçbir zaman varsayılana bırakılmaz. Port `idList`, `vehicles`, `clearAll` veya boş dizi kabul etmez; dolayısıyla terminate-all davranışı normal TKG execution yolundan ifade edilemez.

## Commissioning boşlukları

Bu implementasyon aşağıdakileri çözmez:

- production WorkSite/robot eşlemeleri;
- TKG operation-to-binTask sözleşmesi;
- fork yüksekliği, recognition ve fiziksel hareket geometrisi;
- native `FINISHED` kanıtının slot occupancy/load sonucuna dönüşümü;
- müşteri master-data boşlukları (`Part↔Slot`, container ilişkileri, SEALER V3, `EMPTY_STORAGE`, `REMOVE_FULL` destination, lot ve HAOS/HMTR).

Bu boşluklar onaylanmadan native-order modu production varsayılanı yapılamaz.

## WP-31A doğrulama sınırı

Testler yalnız mock HTTP yanıtları kullanır. WP-31A sırasında canlı RDS çağrısı, order oluşturma, terminate veya robot hareketi yapılmaz.

## Çoklu Harita (Multi-Map) Yürütme ve Harita Semantiği (WP-31B / Readiness)

### 1. Harita Semantiği ve Pre-Dispatch Gereksinimi
`NavigationProfile.RobotMapName` (`requiredMap`) TKG iş sözleşmesinde şu anlama gelir:
> **Ana transport emri gönderilmeden önceki (PRE-DISPATCH) gerekli / tercih edilen robot haritası.**

Bu değer, ana native order boyunca robotun o haritada kalmasını zorunlu kılan bir runtime değişmezi (invariant) **değildir**.
Ana native order RDS'e sunulduktan sonra, RDSCore yapılandırılmış sahne (scene) rotası farklı haritalara bağlı bölgeleri (Area) kestiğinde kendi iç harita geçişlerini meşru biçimde yürütür.

Örnek Simülatör Sahnesi:
- `Area-01`: Eğrileri (curves) `26.7.2026_map_optimak_1` haritasına bağlıdır.
- `hairou`: Eğrileri `26.7.2026_map_optimak` haritasına bağlıdır.
- `AP21` istasyonu `Area-01` içerisinde, `AP7` istasyonu ise `hairou` içerisinde yer almaktadır.

Bu nedenle `AP21 -> AP7` rotası gerçek bir multi-map rotasıdır. RDSCore rota planlayıcısı, alan sınırını geçerken dahili olarak `generateSwitchMapTask -> syspy/switchMap.py` üretir. Bu durum bir simülatör veya kod hatası değil, SEER sahne mimarisinin doğal ve beklenen davranışıdır.

### 2. Kabul Senaryoları Sınıflandırması
1. **Senaryo 1 (Map A -> Map B)**:
   - Harita A başlangıcı → TKG ana dispatch öncesi Harita B'ye geçiş hazırlar → Ana native order Harita B'de başlar → Harita B üzerinde `AP21`'e ulaşılır → RDSCore `AP7`'ye doğru ilerlerken dahili olarak Harita A'ya geçer → Ana native order `FINISHED` → TKG `COMPLETED`.
   - **Sonuç**: `PASS_SIMULATOR_RDSCORE_MULTI_MAP_ROUTE`
2. **Senaryo 2 (Map B -> Map A)**:
   - Kararlı Harita B başlangıcı → TKG V1 için Harita A gereksinimini tespit eder → `MAP_PREP_LOCK_ACQUIRED` ve lock fingerprint yakalanır → Uygulama Harita A'ya `/switchMap` çağırır → `/unlock` → Ana native order `RUNNING` → `FINISHED` → TKG `COMPLETED`.
   - **Sonuç**: `PASS_SIMULATOR`
3. **Senaryo 3 (Aynı Harita / Same-Map Bypass)**:
   - `requiredMap == currentMap` → 0 `MAP_PREP` audit olayı → 0 lock → 0 switchMap → Doğrudan ana native order → `COMPLETED`.
   - **Sonuç**: `PASS_SIMULATOR`

### 3. Sorumluluk Sınırları ve Mimari İlke
- **TKG**: İş niyeti, Varyant çözümlemesi, NavigationProfile, dispatch öncesi harita hazır bulunuşluğu ve dispatch öncesi güvenli kilitli harita geçiş orkestrasyonu.
- **RDSCore**: Rota planlama, trafik koordinasyonu, MAPF, native order icrası ve RDS sahne konfigürasyonunun gerektirdiği rota içi çoklu harita geçişleri.
- **Kural**: Ana native order dispatch edildikten sonra robotu `NavigationProfile.requiredMap` üzerinde kalmaya zorlama girişimi yapılmamalıdır. RDSCore'un dahili harita geçişleriyle savaşan runtime mantığı eklenmemelidir.

### 4. Gelecek Konfigürasyon Modeli
Gelecekteki sürümlerde bloke edici olmayan bir iyileştirme olarak:
- `NavigationProfile.requiredMap` kavramsal olarak `entryMap` veya `preDispatchMap` olarak adlandırılabilir (bu görevde şema değişikliği yapılmaz).
- İleride açık bir geçiş politikası (`TKG_PRE_DISPATCH` vs `RDSCORE_ROUTE_MANAGED`) konfigürasyonu desteklenebilir.

### 5. Fiziksel Commissioning Limitleri (Açık Kalan Maddeler)
Yazılım/simülatör doğrulaması tamamlansa da, aşağıdaki unsurlar fiziksel sahada doğrulanana kadar beklemektedir (pending):
- Gerçek TKG `.smap` dosyaları
- Gerçek Area / curve harita bağları
- Gerçek WorkSite / Bin eşlemeleri
- Gerçek forklift / binTask icrası
- Docking / optik tanıma (recognition)
- Gerçek robot `SwitchMap` donanım tepkisi
- RoboShopPro / RDSCore kilit sahipliği davranışı
- Endüstriyel Wi-Fi / AP dolaşımı (roaming)
- Fiziksel Saha Kabul Testi (SAT)

