# Depo hazırlık API'si

Depo tabletinin arka ucu. Dört ortak hazırlık alanı, alan başına tek palet, ve
makineye dolu malzeme besleyen `BRING_FULL` operasyonu.

Durum: backend tamamlandı ve canlı yığında doğrulandı. Ayrı Android tablet
uygulaması henüz yok; bu belge onun konuşacağı sözleşmeyi tarif eder.

## Neden dördüncü bir operasyon gerekti

Mevcut üç operasyon yalnızca **çıkış** tarafını modelliyordu: boş kasa gelir,
makine doldurur, dolu gider. Makineye alt parça malzemesi getirmenin karşılığı
yoktu. `BRING_EMPTY` yerine kullanılamaz, çünkü tamamlandığında konumu
`Occupied / Empty` yazar; dolu palet boş kasa olarak kaydedilirdi. Alarm
vermeyen bir veri bozulması.

`BRING_FULL` 15 Eylül 2026'da müşteri onayıyla eklendi. Diğer üçünün anlamı
değişmedi.

## Kurallar ve nerede tutuluyorlar

| Kural | Nerede |
|---|---|
| Alan başına en fazla bir palet | kısmi tekil indeks |
| Bir talep için en fazla bir palet | kısmi tekil indeks |
| Boş kapta malzeme olamaz | check kısıtı |
| Yarı mamul talebini ve parçasını adlandırır | check kısıtı |
| Hazır onayı olmadan "toplandı" olamaz | check kısıtı |
| Yarı mamul talep gelmeden hazırlanamaz | servis + store |
| Boş kap talep gelmeden hazırlanabilir | servis |
| Dolu kasa talebi, konum boşalmadan açılamaz | domain |
| Hazırlık atlanamaz | domain, çağıranın bayrağı yok sayılır |

Yarış durumlarını veritabanı çözüyor, önceden yapılan bir `SELECT` değil. İki
tablet aynı alanı doldurmaya çalışırsa tekil indeks birini reddediyor ve bu
hata operatörün anlayacağı sebebe çevriliyor.

## Uçlar

Hepsi `WAREHOUSE_PREPARATION` yetkisi ve yetkili cihaz ister. Hiçbiri RDS'e
dokunmaz.

```
GET  /api/v1/warehouse/preparation/areas
GET  /api/v1/warehouse/preparation/demands
POST /api/v1/warehouse/preparation/areas/{areaId}/pallet
POST /api/v1/warehouse/preparation/pallets/{palletId}/ready
POST /api/v1/warehouse/preparation/pallets/{palletId}/release
```

İki okuma ucu tablet tarafından 5–10 saniyede bir çekilecek şekilde tasarlandı.
`demands` en eski talep başta sıralanır; yan liste bir kuyruktur.

`pallet` ucu `Idempotency-Key` başlığı ister. Yanıtı kaybolan bir tablet aynı
anahtarla tekrar denediğinde ikinci palet oluşmaz, ilki döner.

`ready` ve `release` uçları ekranın gördüğü `expectedRowVersion` değerini ister.
Açık kalmış bir ekran, o palet toplanıp yerine yenisi konduysa yenisini
onaylayamaz.

`release` nedeni `COLLECTED` veya `WITHDRAWN`. İlki robotun fiziksel olarak
aldığını operatörün beyan etmesidir; talebin bir duruma gelmesi alanı kendi
başına boşaltmaz.

## Hazır onayı ne yapar, ne yapmaz

Paleti hazır işaretlemek, **aynı transaction içinde** talebin hazırlık durumunu
`READY` yapar. İki ayrı çağrı olsaydı ya hiçbir şey hazırlanmamışken sevke uygun
bir talep kalırdı, ya da hiç açılmamış bir talep için hazır bir palet.

Hazır onayı teslimat değildir ve başarı bildirimi değildir. Yalnızca talebi
mevcut kapıların altında sevke uygun hale getirir.

## Sevk kaynağı nasıl çözülür

`BRING_FULL`'ün kaynağı bir layout slotu değildir; hiçbir makine ortak alanı
sahiplenemez. Sevk sırasında şu zincir izlenir:

```
talep -> o talep için ayrılmış palet
      -> paletin durduğu alan
      -> alanın RDS eşlemesi
      -> talebin navigasyon profiline ait nokta
```

Zincirin herhangi bir halkası kopuksa kaynak çözülmez ve sevk
`BRING_FULL_SOURCE_UNRESOLVED` ile kapalı kalır. Yanlışlıkla kaynak slot grubu
tanımlanmış bir görev şablonu, başka bir layout'un slotunu sessizce yerine
koyamaz.

Aynı zemin konumu her haritada farklı nokta adı taşıdığı için eşleme
**navigasyon profili başına** yapılır.

## Devreye almadan önce yapılacaklar

Dört alanın hangi fiziksel noktaya karşılık geldiği **müşteri kararıdır ve henüz
verilmemiştir**. `integration.preparation_area_rds_mapping` tablosu boştur.
Doldurulana kadar hiçbir `BRING_FULL` sevk edilemez; bu bilinçli bir kapıdır,
eksiklik değil.

Karar verildiğinde her alan için her navigasyon profiline bir satır gerekir.

## Doğrulama

Canlı yığın ve gerçek veritabanı üzerinde, dört alan bulunduğu gibi bırakılarak:

- boş kap talepsiz kabul edildi, aynı anahtarla tekrar aynı paleti döndürdü
- aynı alana ikinci palet, aynı talebe ikinci alan reddedildi
- eşleşmemiş palete hazır onayı, eski ekran sürümü, hazır olmadan toplandı
  beyanı reddedildi
- `BRING_FULL` talebi çağıran seçmeden `WAITING_PREPARATION` başladı
- eski `ready` ucu hazırlıksız talebi reddetti
- palet hazır işaretlendiğinde talep `READY` oldu
- kaynak çözümlemesi altı durumun hepsinde doğru davrandı

Hareket geçmişi kullanıcıyı, cihazı ve sunucunun kendi zaman damgasını yazar.
Tekrar denemeleri ikinci bir olay üretmez.

İlgili belgeler:
[WAREHOUSE_TABLET_IMPLEMENTATION.md](WAREHOUSE_TABLET_IMPLEMENTATION.md),
[WAREHOUSE_PREPARATION_BACKEND_REVIEW.md](WAREHOUSE_PREPARATION_BACKEND_REVIEW.md).
