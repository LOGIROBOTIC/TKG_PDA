# TKG PDA — Müşteri Bilgi Talep Listesi

Bu liste, TKG PDA yazılım demosundan fiziksel robot devreye almaya geçebilmek için müşteriden teyit edilmesi gereken operasyon bilgilerini toplar. Yazılım/demo kabulüne hazırdır; production fiziksel commissioning ve canlı robot SAT henüz hazır değildir.

Teknik saha ölçümleri ve robot/RDS ayarları müşteriden istenmez; bunlar Logirobotic ve yetkili RDS/IT ekiplerinin sorumluluğunda ayrı bölümlerde gösterilmiştir. Parola, anahtar veya benzeri gizli bilgiler bu dokümana yazılmamalıdır.

## Öncelik 1 — Temel taşımayı devreye almak için zorunlu

### 1. Boş kasa/palet bekleme alanları

Her ilgili üretim alanı için müşteri şunları teyit etmelidir:

- boş kasa/paletlerin robot tarafından alınacağı fiziksel alan;
- bu alandaki pozisyon sayısı;
- her pozisyonda kullanılmasına izin verilen kasa/palet tipleri;
- alanın ortak mı, belirli bir makineye mi ait olduğu.

Bu bilgi **Boş Kasa Getir** ve **Boş Kasayı Kaldır** operasyonları için zorunludur. Mevcut Excel paketinde bu alanlar kesin olarak tanımlı değildir.

### 2. Dolu kasa/palet bırakma alanı

**Dolu Kasayı Kaldır** operasyonu için:

- dolu kasa/palet fiziksel olarak nereye bırakılacak;
- tek ortak hedef mi kullanılacak;
- hedef makineye, parçaya veya kasa/palet tipine göre değişecek mi;
- ara bekleme veya depo onayı gerekecek mi

teyit edilmelidir.

### 3. Parçası teyit edilmesi gereken 48 lojistik pozisyon

48 fiziksel lojistik pozisyonunda hangi parça veya parçaların kullanılacağı kesinleştirilmelidir:

| Makine/alan | Pozisyon adedi | Mevcut durum |
|---|---:|---|
| 412B | 1 | Parça etiketi boş/belirsiz |
| GAR | 11 | Parça etiketi boş/belirsiz |
| SEALER | 8 | Parça etiketi boş/belirsiz |
| SH-02-P1 / SH-02-P2 | 8 | Parça etiketi boş/belirsiz |
| SH-01-A V4 | 2 | Yerleşim ile üretim planındaki varyant/parça bağlamı uyuşmuyor |
| SH-01-B V2 | 4 | Yerleşim ile üretim planındaki varyant/parça bağlamı uyuşmuyor |
| SH-01-P1 / SH-01-P2 V1–V2 | 14 | Yerleşimde BN400, üretim planında BN000 ailesi bulunuyor |
| **Toplam** | **48** | |

Her pozisyon için makine, varyant ve kullanılabilecek parça kodu/kodları teyit edilmelidir. Bir pozisyon birden fazla parça için kullanılıyorsa hepsi belirtilmelidir.

### 4. SEALER V3 fiziksel yerleşimi

SEALER V3, üretim planında geçerli bir varyanttır; ancak fiziksel yerleşimi kanıtlanmamıştır. Müşteri şunlardan birini teyit etmelidir:

- V3, mevcut V1/V2 yerleşimini aynen kullanır; veya
- V3 için ayrı bir onaylı yerleşim vardır ve kaynak dosya/görsel sağlanır.

## Öncelik 2 — Fiziksel robot commissioning için zorunlu

### 5. Kasa/palet tanımları ve operasyon kısıtları

Mevcut kaynakta iki farklı eksik kategori vardır:

- **80 fiziksel lojistik pozisyonunda** kullanılacak kasa/palet tipi kesin değildir.
- **60 planlama kaydında** parçanın kasa/palet tipi boş veya belirsizdir.

Bu sayılar aynı şeyi saymaz ve tek toplam olarak birleştirilmemelidir. Açıklanması gereken kaynak kodları: boş değer, `1284`, `1285`, `285C`, `ARABA`, `KCK`, `ORT`.

Müşteriden, varsa onaylı kasa/palet teknik dokümanı ve operasyon kısıtları istenir:

- kasa/palet tipi ve kullanıldığı parçalar;
- nominal dış ölçüler;
- hatta kullanılan yön/orientation;
- çatal girişine izin verilen taraf;
- istifleme ve yükleme kısıtları.

Nihai ölçü, boşluk, robot yaklaşımı ve çatal geometrisi Logirobotic tarafından sahada ölçülecektir. Excel'deki ekran çizimleri fiziksel mm ölçüsü veya robot pozu kabul edilmeyecektir.

### 6. Eksik lot ve planlama değerleri

- 23 kayıtta kutu lotu boş veya geçersizdir.
- 33 Excel hücresinde formül hatası bulunur.

Günlük kasa/taşıma planlamasında kullanılacak kayıtlar için doğru lot değerleri müşteri tarafından teyit edilmelidir. Bilinmeyen değerler tahmin edilmeyecektir.

## Öncelik 3 — İş süreci ve engelleyici olmayan teyitler

### 7. Depo hazırlık kuralı

- Hangi operasyon/makine/kasa kombinasyonlarında depo hazırlığı gerekir?
- Hangi işler hazırlık beklemeden doğrudan sıraya alınabilir?
- Hazır kararını depo operatörü mü, vardiya sorumlusu mu verir?

### 8. HAOS/HMTR isim farkı

`67115BN050-10-HAOS` mevcut ana kayıt olarak korunmuştur. SH-01-A V1 yerleşiminde görülen `HMTR` ifadesinin yazım/versiyon farkı olup olmadığı teyit edilmelidir. Bu konu mevcut yazılım/demo akışını durdurmaz.

### 9. ALT / RELATED durumu

Müşteri kaynağında 140 bağlamsal **ALT (alternatif parça)** ilişkisi vardır. Ayrı bir **RELATED (ilişkili parça)** ilişkisi kanıtlanmamıştır. Uygulama her iki modeli destekler; eksik veri uydurulmamıştır.

Müşterinin gerçek sürecinde ALT'tan ayrı bir RELATED tanımı yoksa ek veri istenmez. Böyle bir ayrım kullanılıyorsa yalnız gerçek süreci gösteren kaynak paylaşılmalıdır.

## Sorumluluk ayrımı

### Müşteri teyit eder

- boş ve dolu kasa/palet alanlarının operasyon amacını;
- 48 pozisyonda kullanılan parçaları;
- SEALER V3 yerleşim kararını;
- kasa/palet tipi ile onaylı operasyon kısıtlarını;
- eksik lot/planlama değerlerini;
- depo hazırlık ve engelleyici olmayan isim kurallarını.

### Logirobotic sahada ölçer ve konfigüre eder

- nihai pickup/drop pozu ve yaklaşma yönünü;
- docking orientation ve çatal giriş geometrisini;
- gerçek fork/jack yüksekliğini;
- fiziksel clearance, koridor ve manevra uygunluğunu;
- her pozisyonun ilk doluluk/yük durumunu;
- saha etiketi ile uygulamadaki konumun aynı yeri gösterdiğini;
- uygulamadaki lojistik konumlarla RDS fiziksel durak noktalarının eşleştirilmesini ve robot kimliğini.

### RDS / IT ekibi teknik kanaldan sağlar

- production RDS ağ adresi, TLS ve erişim politikasını;
- production navigation/scene ve robot kimliklerini;
- onaylı fiziksel robot görevlerini ve parametre sözleşmesini;
- callback erişimi ve ağ/firewall ayarlarını;
- gerekli gizli anahtarları: **Yetkili teknik kanal üzerinden güvenli environment/configuration sağlanacaktır.**

Parola, entegrasyon anahtarı, callback anahtarı veya oturum bilgisi bu müşteri dokümanına yazılmaz.

## Toplantı / e-posta cevap tablosu

| Konu | Makine/Alan | Talep edilen bilgi | Müşteri cevabı | Durum / Not |
|---|---|---|---|---|
| Boş kasa/palet bekleme alanı |  | Konum, pozisyon adedi, izin verilen kasa/paletler |  |  |
| Dolu kasa/palet bırakma alanı |  | Ortak veya makine/parça/kasa bazlı hedef |  |  |
| Parça-konum teyidi |  | Pozisyonda kullanılan parça kodu/kodları |  |  |
| SEALER V3 | SEALER | V1/V2 ile ortak mı, ayrı yerleşim mi? |  |  |
| Kasa/palet tanımı |  | Tip, nominal ölçü, orientation, çatal giriş tarafı, kısıt |  |  |
| Lot/planlama değeri |  | Doğru kutu lotu veya formül sonucu |  |  |
| Depo hazırlık |  | Hazırlık gereken işler ve onaylayan rol |  |  |
| HAOS/HMTR | SH-01-A V1 | Doğru isim/versiyon teyidi |  |  |
