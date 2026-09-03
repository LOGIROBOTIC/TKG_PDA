# WP-33 — Operator UX V2 ve Ortak Taşıma Kuyruğu

WP-33, mevcut domain operasyonlarını müşterinin kasa diliyle sunar. Mimari yön değişmez:

```text
Flutter Operator
  -> ASP.NET Core container-action API
  -> Application policy
  -> PostgreSQL transaction / TransportBatch
  -> mevcut RDS execution boundary
```

Flutter slot UUID/kodu, WorkSite, RDS bin/order veya robot kimliği seçmez ve göstermez. Mantıksal `N. Kasa`, etkin `Variant + Layout + master.layout_slot_parts + ops.slot_runtime_state` verisinden deterministik türetilir. `metadata_json/canonical_part_codes` runtime routing kaynağı değildir.

## Operatör iş kuralları

| Operatör eylemi | Başlangıç | Atomik sonuç |
|---|---|---|
| `N. Kasa Doldu` | `OCCUPIED + EMPTY` | `FULL` beyanı + aynı slot için `REMOVE_FULL` |
| `N. Kasa Doldu` | `OCCUPIED + FULL` | idempotent `REMOVE_FULL` |
| `N. Kasa Boş` | `VACANT + UNKNOWN` | aynı slot için `BRING_EMPTY` |

VACANT konumda “Kasa Doldu”; OCCUPIED konumda “Kasa Boş”; UNKNOWN/çelişkili durum ve aktif reservation fail-closed reddedilir. FULL bilgisi RDS `loaded/filled/container/goodsId` alanlarından türetilmez; operatör düğmesi TKG iş kanıtıdır.

`REMOVE_EMPTY` domain operasyonu olarak korunur, ancak normal standalone Operator düğmesi değildir. `Tüm Kasaları Kaldır`, seçili parçayı değil etkin Variant/Layout içindeki tüm uygulanabilir lojistik konumları değerlendirir:

- `OCCUPIED + FULL -> REMOVE_FULL`
- `OCCUPIED + EMPTY -> REMOVE_EMPTY`
- `VACANT + UNKNOWN -> ignore`
- UNKNOWN, conflict veya reservation -> tüm atomik grup reddedilir

Çoklu-parça seçimi aynı ekranın opsiyonel genişlemesidir. Her item mantıksal kasa eylemi taşır; tek etkin varyant kuralı korunur ve server tek `TransportBatch` transaction'ında bağımsız child request'ler oluşturur.

## HTTP yüzeyi

- `GET /api/v1/machines/{machineId}/container-positions`
- `POST /api/v1/machines/{machineId}/container-actions`
- `POST /api/v1/machines/{machineId}/container-actions/batch`
- `POST /api/v1/machines/{machineId}/container-actions/remove-all`
- `GET /api/v1/transport-requests/queue?terminal=false&offset=0&limit=50`

Mutasyonlarda `Idempotency-Key` zorunludur. Eylem çözümü, slot lock, gerekiyorsa `EMPTY -> FULL`, request yaratma ve reservation aynı PostgreSQL transaction'ındadır. Lock sırası machine runtime, ilgili slot runtime kayıtları UUID sırası ve mevcut workflow request sınırıdır.

## Ortak görev kuyruğu ve ETA

Tüm authenticated Operator kullanıcıları, employee filtresi olmadan tüm makinelerin business-only kuyruğunu görür. Varsayılan görünüm yalnız aktif kayıtlardır; terminal kayıtlar ayrı ve sayfalıdır. Aktif sıralama `RUNNING`, robot atanmış/gönderilmiş, `QUEUED`, requested/preparation ve aynı grupta `created_at` artandır.

Projection yalnız sıra, makine, parça, mantıksal kasa numarası, iş eylemi, zaman, sade durum ve ETA içerir. Request UUID, slot, WorkSite, AGV ve ham RDS state yoktur. Güvenilir request-correlated remaining time veya yeterli tarihçe bulunmadığı için WP-33 ETA sonucu `null / UNAVAILABLE` kalır; sabit süre uydurulmaz.

## Commissioning sınırı

`EMPTY_STORAGE` ve `REMOVE_FULL` hedefinin müşteri iş/fiziksel alanı bilinmektedir. Açık kalan production işi bu alanların gerçek robot/site haritasındaki exact RDS WorkSite/bin/point eşlemesi ve production task contract commissioning'idir. WP-33 canlı RDS, simulator order veya robot hareketi çalıştırmaz.

Schema/migration değişikliği yoktur. Mantıksal kasa numarası mevcut layout ordering üzerinden türetilir.
