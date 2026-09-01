# TKG PDA — Customer Source Gap Reconciliation

Bu rapor, müşteriden yeni bilgi istenmeden önce mevcut kaynak paketinin tamamında yapılan uzlaştırmayı kaydeder. Sonuçlar `A — müşteri açıkça sağladı`, `B — kaynaklar birlikte değerlendirildiğinde yüksek güvenle türetilebilir` ve `C — gerçekten eksik/çelişkili` olarak sınıflandırılmıştır. `C` kayıtları için değer üretilmemiştir.

## İncelenen kaynaklar

- sekiz müşteri Excel dosyası ve tüm sheet/merged-cell/formül içerikleri;
- Excel içindeki dokuz çalışma-planı görseli ile bir AGV/PLC görseli;
- `Hyundai STP/1284 ÇELİK KASA.png`, `285C - Çelik kasa.jpg` ve `CLK Kasa tipleri.png`;
- mevcut source manifestleri, Phase 02A/02B normalize çıktıları ve commissioning notları.

Proje paketinde müşteri PDF'i bulunmadığı ayrıca doğrulandı. DWG/STEP/7z dosyaları parça/ürün teknik varlıklarıdır; eksik lojistik pozisyon, buffer veya operasyon hedefi tanımlamadıkları için bunlardan routing sonucu çıkarılmadı. Ham müşteri dosyaları değiştirilmedi.

## Sonuç özeti

| Konu | Önce | A/B ile kapanan | Sonra | Sınıflandırma |
|---|---:|---:|---:|---|
| Part↔Slot eşlemesi olmayan lojistik slot | 48 | 0 | 48 | C |
| Container association belirsiz slot | 80 | 6 | 74 | 6 B, 74 C |
| Fiziksel footprint'ü bilinmeyen region | 183 | 54 | 129 | 54 A/B, 129 C/site |
| Kategorisi çözülemeyen nonblank container code | 6 | 5 | 1 (`1285`) | 5 A/B, 1 C |
| Eksik lot girdisi | 23 | 0 | 23 | C |
| Excel formül hatası | 33 | 0 | 33 | C; eksik girdinin sonucu |
| SEALER V3 layout assignment | yok | 0 | yok | C |
| `EMPTY_STORAGE` fiziksel slot | 0 | 0 | 0 | C |
| `REMOVE_FULL` hedefi | yok | 0 | yok | C |

## A — Müşterinin açıkça sağladığı bilgi

### Container master kanıtı

| Code | Doğrulanan bilgi | Kaynak | DB/normalize sonucu |
|---|---|---|---|
| `1284` | Çelik kasa; tüm sekiz plan satırında `1200/800/400` mm | `1284 ÇELİK KASA.png`; Çalışma Planı PAH-01-A/PAH-02-A | `CONTAINER`, 1200×800×400 |
| `285C` | Çelik kasa | `285C - Çelik kasa.jpg` üzerindeki kasa etiketi | `CONTAINER`; ölçü hâlâ `NULL` |
| `BYK` | Büyük boy kasa, 75×120×60 cm | `CLK Kasa tipleri.png` | `CONTAINER`, 1200×750×600 |
| `ORT` | Orta boy kasa, 65×95×55 cm | `CLK Kasa tipleri.png` | `CONTAINER`, 950×650×550 |
| `KCK` | Küçük boy kasa, 55×80×45 cm | `CLK Kasa tipleri.png` | `CONTAINER`, 800×550×450 |
| `ARABA` | Taşıma arabası kategorisi | Çalışma Planı'ndaki açık `ARABA` kodu/etiketi | `CART`; çelişkili ölçüler nedeniyle ölçü `NULL` |

BYK/ORT/KCK görsel ölçüleri, bazı satır bazlı `Mamül Kasa Ölçüsü` değerleriyle uyuşmadığında dedicated kasa-tipleri görseli container master için kullanıldı; satır değerleri planning provenance olarak aynen korundu ve conflict issue üretildi. Bu nominal ölçüler robot yaklaşımı, clearance, fork veya docking geometrisi değildir.

## B — Yüksek güvenle türetilen bilgi

### SEALER container eşlemeleri

SEALER V1/V2 fiziksel layout'unda aşağıdaki exact part kodları açıkça yazılıdır. SEALER planning satırında container code boş olsa da aynı exact part kodu SH-02 planning bağlamında tek ve tutarlı container code'a sahiptir. Kategoriye, çizim oranına veya benzer parçaya göre tahmin yapılmamıştır.

| SEALER slot | Exact part | Türetilen container | Kanıt |
|---|---|---|---|
| `SLOT-001`, `SLOT-009` | `71517BN400-20` | `4311` | SH-02 V1/V2 exact-part planning satırları |
| `SLOT-002` | `71566BN400-20` | `KCK` | SH-02 V2 exact-part planning satırı |
| `SLOT-003` | `71586BN400-20` | `KCK` | SH-02 V2 exact-part planning satırı |
| `SLOT-010` | `71556BN400-20` | `KCK` | SH-02 V1 exact-part planning satırı |
| `SLOT-011` | `71576BN400-20` | `KCK` | SH-02 V1 exact-part planning satırı |

Bu altı slot `default_container_type_id` alır. KCK olan dört slot ayrıca customer kasa-tipleri görselindeki nominal footprint'i taşır. Cross-context fallback yalnız exact part için, tek container sonucu varsa ve slotta variant mismatch yoksa çalışır.

## C — Gerçekten eksik veya çelişkili bilgi

### 48 Part↔Slot boşluğu

Yeniden inceleme hiçbirini güvenli operational mapping'e dönüştürmedi:

| Makine/layout | Slotlar | Adet | Neden C |
|---|---|---:|---|
| 412B V2 | `SLOT-009` (`BR156:CC163`) | 1 | Kaynak dikdörtgeni boş; diğer variant'taki konumsal benzerlik part kanıtı değildir |
| GAR V1 | `SLOT-007`–`009` | 3 | Etiketler boş |
| GAR V2 | `SLOT-005`–`008` | 4 | Etiketler boş |
| GAR V3 | `SLOT-001`–`004` | 4 | Etiketler boş |
| SEALER shared V1/V2 | `SLOT-004`–`008`, `SLOT-012`–`014` | 8 | Fiziksel dikdörtgenler var, part etiketi yok |
| SH-02-P1/P2 shared V1/V2 | `SLOT-001`–`008` | 8 | Sağ bölgedeki dikdörtgenler boş |
| SH-01-A V4 | `SLOT-004`, `005` | 2 | Layout `67131BN400-10-HMTR` diyor; planning bu part'ı V3'te gösteriyor |
| SH-01-B V2 | `SLOT-001`, `006`–`008` | 4 | BN400/`81110BN400-40`/`66425BN400-50` etiketleri planning'de V1 bağlamında |
| SH-01-P1/P2 shared V1/V2 | `SLOT-001`–`014` | 14 | Layout'un sol bölgesi BN400 ailesi; SH-01 planning BN000, BN400 ailesi SH-02 planning'de |
| **Toplam** |  | **48** | 28 boş + 20 source/planning conflict |

Birleşik SH dosyasında sol bölge yalnız `HÜCRE` olarak etiketlenmiştir; SH-01 veya SH-02 adı yazmaz. Dosya adı sırası ile part ailesi çeliştiği için BN400 slotlarını otomatik olarak SH-02'ye taşımak da, SH-01 operational mapping'i olarak kabul etmek de güvenli değildir. `master.layout_slot_parts` sayısı bu nedenle 143 ve kapsanan slot sayısı 124 olarak kalır.

### SEALER V3

Çalışma Planı ve takt dosyası V3'ü ve parçalarını doğrular. Birleşik layout yalnız SEALER V1/V2 part evidence içerir; V3 etiketi, ayrı sheet, görsel veya “V1/V2 ile aynıdır” notu yoktur. V3 assignment C olarak kalır.

### EMPTY_STORAGE ve REMOVE_FULL

AGV/PLC workbook'undaki `KASA BOŞ`, `KASA DOLU`, `1. KASA BOŞ` benzeri ifadeler durum/proses metnidir. Çalışma Planı'ndaki dolu/boş hareket adetleri de fiziksel pickup/drop alanı değildir. Paket; `EMPTY_STORAGE` konumu/capacity'si veya `REMOVE_FULL` nihai hedefini tanımlamaz. “Ortak alım noktası” ifadesi bulunsa bile bunun empty buffer ya da full final destination ile aynı fiziksel yer olduğunu kanıtlayan konum tanımı yoktur.

### Container ve footprint kalanları

- 74 slotta tekil container association yoktur: 412B 1, GAR 13, SEALER 8, SH-01-A 18, SH-01-B 12, SH-01-P1/P2 14, SH-02-P1/P2 8.
- `1285` için kategori ve nominal ölçü bulunmamıştır.
- `285C` kasa kategorisi kanıtlıdır fakat ölçüsü yoktur.
- `ARABA` kategorisi kanıtlıdır fakat planning satırları 1200×800 ve 1200×800×1500 verdiği için tek type-level ölçü yoktur.
- `4311`, `4322`, `4622` için row-level ölçüler birden fazla değerdir; tek nominal type footprint seçilmemiştir.
- 129 region'ın fiziksel footprint'i hâlâ bilinmez: 118 lojistik + 11 display-only MACHINE region.

### Lot/formül, HAOS/HMTR ve ilişkiler

- 23 lot girdisinin hiçbiri Takt, Work Plan veya layout kaynaklarının başka bir yerinde dolu değildir. 33 `#DIV/0!` hücresi bunların ve diğer eksik inputların downstream hesap hatasıdır; değer üretilmedi.
- `67115BN050-10-HAOS` planning/interaction kaynaklarında canonical kalır. İki SH-01-A V1 slotundaki `HMTR` raw provenance olarak tutulur; fark non-blocking müşteri teyididir.
- 140 contextual `ALT_PART` kanıtı vardır. Ayrı `RELATED` terimi/ilişkisi yoktur; sayı 0 kalır.

## Sorumluluk

| Açık konu | Owner |
|---|---|
| 48 slotun part/variant/machine teyidi veya açıkça kullanılmayacağı kararı | CUSTOMER |
| SEALER V3 shared/separate layout kararı | CUSTOMER |
| Empty-container business area/capacity/allowed container tanımı | CUSTOMER |
| Full-container business destination stratejisi | CUSTOMER |
| 74 slot container association, `1285`, eksik nominal type bilgileri ve 23 lot | CUSTOMER |
| Exact pose, approach, clearance, orientation, fork/docking ölçümü ve saha etiketi doğrulaması | LOGIROBOTIC SITE |
| Production WorkSite, navigation/profile, task label, robot ve credentials | RDS / IT |

Bu rapordaki hiçbir bulgu WorkSite adı/ID'si, robot mapping'i, production task label'ı, fork yüksekliği veya pickup/drop koordinatı üretmez.
