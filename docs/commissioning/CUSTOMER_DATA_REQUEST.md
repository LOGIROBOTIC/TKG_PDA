# TKG PDA — Müşteri Bilgi ve Veri Talep Listesi

Bu belge, mevcut Excel paketinden kesin olarak çıkarılamayan bilgileri toplamak içindir. Bilinen kayıtları yeniden istemez; tahmin edilebilir gibi görünen fiziksel/RDS alanlara değer uydurmaz. Credential veya secret bu belgeye yazılmamalıdır.

## A. Müşteri teyidi gerekli

1. **48 mapping'siz lojistik pozisyonun Part↔Slot karşılığı**
   - Boş/belirsiz etiket: 28 pozisyon — 412B: 1, GAR: 11, SEALER: 8, SH-02-P1/P2: 8.
   - Variant-mismatch-only kanıt: 20 pozisyon — SH-01-A V4: 2, SH-01-B V2: 4, SH-01-P1/P2 shared V1/V2: 14.
   - SH-01-P1/P2 layout etiketlerinde BN400 ailesi, textual planning'de BN000 ailesi bulunduğu için 14 pozisyon otomatik eşlenmedi.
   - Beklenen cevap: machine, variant, Excel bölgesi/pozisyon ve exact canonical part code. Bir pozisyon birden fazla part destekliyorsa hepsi ayrıca belirtilmeli.

2. **SEALER Variant 3 fiziksel yerleşimi**
   - V3, `AMR Çalışma Planı.xlsx` içinde geçerli aktif master veridir.
   - Mevcut ortak fiziksel plan yalnız V1/V2 için kanıtlıdır.
   - Beklenen cevap: V3 mevcut layout'u mu paylaşır, yoksa ayrı onaylı yerleşim mi vardır? Ayrıysa görsel/Excel kaynak sağlanmalıdır.

3. **Boş kasa/palet buffer gerçeği (`EMPTY_STORAGE`)**
   - Mevcut customer seed'de EMPTY_STORAGE rolünde pozisyon yoktur.
   - Her ilgili machine/alan için gerçek buffer konumu, pozisyon adedi, ortak mı machine'e özel mi olduğu ve izin verilen container tipleri istenir.
   - Bu bilgi `BRING_EMPTY` source ve `REMOVE_EMPTY` destination için zorunludur.

4. **Dolu kasa/palet destination kuralı (`REMOVE_FULL`)**
   - Dolu yük nereye bırakılacak?
   - Tek ortak destination mı; machine, variant, part veya container'a göre değişir mi?
   - Ara staging/warehouse onayı var mı?
   - Bu karar olmadan production `REMOVE_FULL` route/task oluşturulamaz.

5. **Container ve lot eksikleri**
   - 80 layout pozisyonunda unique container association kanıtlanamamıştır.
   - Textual importta 60 `UNKNOWN_CONTAINER_MAPPING`, 23 eksik/invalid lot ve 33 Excel formula error hücresi vardır.
   - Açıklanması gereken raw container kodları: boş değer, `1284`, `1285`, `285C`, `ARABA`, `KCK`, `ORT`.
   - Her kod için exact tip/ad, dış ölçüler, taşıma yönü/orientation, stacking/loading kuralı ve eksik lot miktarı istenir. `BYK`, `4311`, `4322`, `4622` gibi tanımlı kodlar için de production fiziksel footprint ayrıca sahada doğrulanmalıdır.

6. **Non-blocking part-code source farkı**
   - Canonical kod `67115BN050-10-HAOS` olarak korunmuştur.
   - SH-01-A V1 yerleşiminde görülen `HMTR` varyantının yazım/versiyon farkı olup olmadığı teyit edilmelidir. Bu teyit mevcut akışı durdurmaz.

7. **Warehouse preparation business rule**
   - Hangi operation/machine/container kombinasyonları `WAITING_PREPARATION` ile başlamalı, hangileri `NOT_REQUIRED` olmalı?
   - Hazır kararını hangi rol verir? Yazılım Warehouse/Supervisor yetkisini destekler; kural kaynaktan tahmin edilmemiştir.

Kaynakta 140 ilişkinin tamamı açıkça `Alt Parça` kanıtıdır; ayrı RELATED kaydı yoktur. Bu nedenle mevcut veri için ALT/RELATED yeniden sınıflandırma talebi bulunmaz. Müşteri ayrı RELATED semantiği istiyorsa yeni, açık kaynak vermelidir.

## B. Logirobotic sahada ölçmeli / devreye almalı

| Veri | Neden gerekli |
|---|---|
| Her source/destination için fiziksel pickup/drop pozu | Robotun ekrandaki normalize dikdörtgeni gerçek docking hedefine çevirebilmesi için |
| Yaklaşma yönü ve docking orientation | Çatalın/yükün doğru yönden ve çarpışmasız girmesi için |
| Fork insertion yönü ve kullanılabilir fork/jack yüksekliği | Container/palet zararını ve yanlış load/unload aksiyonunu önlemek için |
| Container/palet clearance ve güvenlik payı | Pose toleransı, raf/makine çarpışma sınırı için |
| Koridor genişliği, dönüş ve kaçış alanı | Robot modeli/rota ve trafik uygunluğunu doğrulamak için |
| Son layout koordinatları ve sahadaki pozisyon etiketi | UI slotu ile RDS WorkSite'ın aynı fiziksel yeri temsil ettiğini kanıtlamak için |
| Her slotun ilk durumu: `VACANT`, `OCCUPIED+EMPTY`, `OCCUPIED+FULL` | İlk request öncesi hatalı operasyon açılmasını önlemek için |
| Sensör/recognition gereksinimi ve tolerans | Varsa yük/pozisyon algılamasının task parametrelerini belirlemek için |

## C. RDS / robot konfigürasyonu gerekli

1. Production RDS base URL/host, TLS veya ağ izolasyonu ve firewall/reverse-proxy kararı.
2. Fiziksel CBD15 `uuid/vehicle_id`, robot grubu ve izin verilen task kapsamı. `sim_01/sim_02` production robot değildir.
3. Production navigation profile/scene kimliği ve bunun hangi machine/layout kapsamına ait olduğu.
4. Doğrulanmış physical WorkSite listesi ve 172 lojistik pozisyon içinden production'da kullanılacak slot→WorkSite mapping'leri.
5. `BRING_EMPTY`, `REMOVE_FULL`, `REMOVE_EMPTY` için production WindTask label'ları, exact input parametre adları/tipleri ve binding'leri.
6. Robot aksiyonu kararı: `ForkLoad/ForkUnload`, `JackLoad/JackUnload`, `binTask` veya onaylı başka mekanizma.
7. Pickup/drop geometrisi, fork yükseklikleri ve varsa recognition parametreleri; bunlar guessed default olamaz.
8. RDS `content` alanı için exact EMPTY/FULL değerleri ve WorkSite `filled/content` operasyon semantiği.
9. TKG callback URL'i ve ulaşılabilirlik/TLS testi.
10. Integration key ve callback key: **secure environment/configuration required**. Anahtarlar ortak dokümana, Git'e veya ekran görüntüsüne yazılmaz.

## D. Opsiyonel / gelecek

- PLC sinyal entegrasyonu ve `Tüm Kasaları Kaldır` otomasyon tetikleme kuralı.
- SAP/ERP emir, stok veya out-order entegrasyonu.
- Alarm acknowledge/reset/clear/history; mevcut kapsam salt-okunur görünürlüktür.
- Otomatik map/profile switching veya route/stitch yönetimi.
- Confirmed source sağlanırsa yeni RELATED taxonomy veya yeni fiziksel layout versiyonları.
