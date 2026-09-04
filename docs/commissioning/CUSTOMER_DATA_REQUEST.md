# TKG PDA — Müşteri Bilgi Talep Listesi

Bu liste, mevcut müşteri kaynaklarının tamamı uzlaştırıldıktan sonra gerçekten eksik veya çelişkili kalan kararları içerir. Yazılım/demo kabulüne hazırdır; production fiziksel commissioning ve canlı robot SAT henüz hazır değildir.

`1284`, `285C`, `BYK`, `ORT`, `KCK` ve `ARABA` için mevcut pakette kanıtlanan kategori/ölçü tekrar istenmez. Robot pozu, fork yüksekliği, WorkSite, robot kimliği ve benzeri teknik commissioning değerleri müşteriden değil Logirobotic/RDS-IT ekiplerinden alınır.

## Kapanan konu — taşıma alanlarının iş anlamı

`EMPTY_STORAGE` ve `REMOVE_FULL` hedefinin müşteri iş/fiziksel alanı artık müşteri karar boşluğu değildir. Açık kalan iş, Logirobotic/RDS-IT commissioning ekibinin bu bilinen alanları gerçek robot/site haritasındaki exact RDS WorkSite/bin/point kimlikleriyle eşlemesidir. Bu belge RDS isim veya koordinatı üretmez.

## Öncelik 1 — Temel taşımayı devreye almak için zorunlu

### 1. Parçası teyit edilmesi gereken 48 lojistik pozisyon

| Makine/alan | Pozisyon | Adet | Neden kaynaklardan çözülemedi |
|---|---|---:|---|
| 412B V2 | `SLOT-009` | 1 | Etiket boş |
| GAR V1/V2/V3 | ilgili 11 blank slot | 11 | Etiketler boş |
| SEALER V1/V2 | `SLOT-004`–`008`, `012`–`014` | 8 | Etiketler boş |
| SH-02-P1/P2 V1/V2 | `SLOT-001`–`008` | 8 | Etiketler boş |
| SH-01-A V4 | `SLOT-004`, `005` | 2 | Layout part etiketi planning'de V3 bağlamında |
| SH-01-B V2 | `SLOT-001`, `006`–`008` | 4 | Layout etiketleri planning'de V1 bağlamında |
| SH-01-P1/P2 V1/V2 | `SLOT-001`–`014` | 14 | Layout BN400, SH-01 planning BN000; bölge yalnız `HÜCRE` diye etiketli |
| **Toplam** |  | **48** | 28 blank + 20 source/planning conflict |

Müşteri her pozisyon için kullanılacak part kodunu/kodlarını veya pozisyonun operational olarak kullanılmadığını teyit etmelidir. Birleşik SH dosyasındaki BN400 bölgesi için ayrıca doğru makine/alan açıkça belirtilmelidir.

### 2. SEALER V3 fiziksel yerleşimi

SEALER V3 geçerli textual master data'dır ancak fiziksel layout kanıtı yoktur. Müşteri şunlardan birini teyit etmelidir:

- V3 mevcut V1/V2 layout'unu aynen kullanır; veya
- V3 için ayrı onaylı layout/kaynak sağlanır.

## Öncelik 2 — Kalan customer master data

### 5. Kalan container association ve nominal type boşlukları

Altı SEALER slotunun container eşlemesi mevcut exact-part planning kanıtından kapatıldı; belirsiz fiziksel slot/container sayısı **74** olarak kaldı:

| Alan | Adet |
|---|---:|
| 412B | 1 |
| GAR | 13 |
| SEALER | 8 |
| SH-01-A | 18 |
| SH-01-B | 12 |
| SH-01-P1/P2 | 14 |
| SH-02-P1/P2 | 8 |

Müşteriden yalnız şu eksikler istenir:

- bu 74 pozisyonda izin verilen container code/code'ları;
- `1285` code'unun tipi ve nominal dış ölçüsü;
- `285C` için nominal dış ölçü (`CONTAINER` kategorisi zaten kanıtlıdır);
- `ARABA` için tek nominal ölçü varsa doğru değer (kaynak satırları çelişmektedir; `CART` kategorisi kanıtlıdır);
- `4311`, `4322`, `4622` için standart tek nominal ölçü varsa doğru type-level değer;
- container code'u boş kalan planning kayıtlarının doğru code'u.

`1284 = 1200×800×400`, `BYK = 1200×750×600`, `ORT = 950×650×550`, `KCK = 800×550×450` mevcut müşteri kaynaklarından alınmıştır ve tekrar sorulmaz.

### 6. Eksik lot ve planlama değerleri

- 23 planning context'te kutu lotu boş/geçersizdir.
- 33 Excel hücresindeki formül hatası downstream hesap hatasıdır; başka kaynakta eksik input bulunamamıştır.

Günlük kasa/taşıma planlamasında kullanılacak kayıtlar için doğru lot değerleri teyit edilmelidir. Değer uydurulmayacaktır.

## Öncelik 3 — İş süreci ve engelleyici olmayan teyitler

### 7. Depo hazırlık kuralı

- Hangi operation/makine/container kombinasyonlarında depo hazırlığı gerekir?
- Hangi işler doğrudan sıraya alınabilir?
- Hazır kararını hangi rol verir?

### 8. HAOS/HMTR isim farkı — non-blocking

`67115BN050-10-HAOS` canonical kayıttır. SH-01-A V1 layout'undaki iki `HMTR` etiketinin yazım/versiyon farkı olduğu teyit edilmelidir. Bu konu demo veya diğer master-data akışını durdurmaz.

## Mevcut kaynakla kapanan ve tekrar istenmeyen konu

Müşteri kaynağında 140 contextual `ALT_PART` ilişkisi vardır; ayrı `RELATED` ilişkisi kanıtlanmamıştır. Müşteri sürecinde ALT'tan ayrı bir RELATED sınıfı kullanılmıyorsa ek veri talep edilmez.

## Sorumluluk ayrımı

### Müşteri teyit eder

- 48 pozisyonun part/machine/variant bağlamını veya kullanılmayacağını;
- SEALER V3 layout kararını;
- 74 pozisyonun allowed container ilişkisini ve kalan type/lot master boşluklarını;
- depo hazırlık ve HAOS/HMTR iş kuralını.

### Logirobotic sahada ölçer ve konfigüre eder

- exact pickup/drop pose, approach ve docking orientation;
- fork giriş yönü/yüksekliği, clearance ve güvenlik mesafesi;
- saha etiketi/UI slot doğrulaması ve ilk runtime state;
- bilinen `EMPTY_STORAGE` ve `REMOVE_FULL` alanlarının exact TKG slot ↔ RDS WorkSite/bin/point eşlemesi.

### RDS / IT ekibi teknik kanaldan sağlar

- production RDS ağ/TLS/erişim politikası;
- navigation/scene, robot kimliği ve dispatchability;
- production task label/input contract ve callback altyapısı;
- credentials/secrets güvenli environment/configuration kanalıyla.

## Toplantı / e-posta cevap tablosu

| Konu | Makine/Alan | Talep edilen bilgi | Müşteri cevabı | Durum / Not |
|---|---|---|---|---|
| Empty/full RDS eşlemesi |  | Bilinen fiziksel alanların WorkSite/bin/point karşılığı |  | Logirobotic/RDS-IT commissioning |
| Part↔Slot teyidi |  | Part code/code'ları veya kapsam dışı |  |  |
| SEALER V3 | SEALER | Shared V1/V2 veya ayrı layout |  |  |
| Container association/type |  | 74 slot ve kalan nominal master boşlukları |  |  |
| Lot |  | 23 context için doğru lot |  |  |
| Depo hazırlık |  | Hazırlık gereken işler ve onaylayan rol |  |  |
| HAOS/HMTR | SH-01-A V1 | İsim/versiyon teyidi |  | Non-blocking |
