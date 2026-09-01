# TKG PDA — Müşteri Kabul ve Devreye Alma Hazırlık Denetimi

**Denetim baseline'ı:** `71425246a4ecfbe7e07da6db5c749a11181f70d8`

**Kapsam:** mevcut müşteri kaynakları, normalize master/layout verisi, Flutter Operator akışı ve production commissioning kapıları

## Hazırlık kararı

| Kabul seviyesi | Durum | Açıklama |
|---|---|---|
| Yazılım / demo kabulü | **READY** | Operator akışı, yetkilendirme, iş kuralları ve simulator entegrasyon sınırı doğrulanmıştır |
| Production fiziksel commissioning | **NOT YET READY** | Boş/dolu kasa alanları, eksik parça-konum teyitleri, saha ölçümleri ve production RDS/robot konfigürasyonu tamamlanmamıştır |
| SAT / canlı robot kabulü | **BLOCKED BY CUSTOMER + SITE + RDS COMMISSIONING DATA** | Fiziksel robot yürütmesi simulator kanıtıyla PASS sayılamaz |

Bu nedenle bu rapordaki **PASS**, yalnız ilgili yazılım davranışının uygulanıp doğrulandığını belirtir; production fiziksel sistemin son kabul için hazır olduğu anlamına gelmez.

Bu denetim önceki phase raporlarını sayı kaynağı olarak tekrar etmez. Sekiz Excel dosyasının mevcut byte hash/size değerleri source manifestlerle; normalize çıktılar da generated seed ve deterministic pipeline testleriyle yeniden karşılaştırıldı. Proje dizininde müşteriye ait PDF veya DOCX bulunmadı; PDF kanıtı varmış gibi yorum yapılmadı. Ham dosyalar değiştirilmedi.

## 1. Müşteri kaynak matrisi

| Kaynak | Kapsam / makine | Sağladığı kanıt | Sağlamadığı kanıt |
|---|---|---|---|
| `AGV - BAKIM - PLC Çalışma.xlsx` | SH-01-A ve PLC/operasyon örneği | parça/alternatif parça; `Kasa Doldu`, `Kasa Boş`, `Tüm Kasaları Kaldır`; sinyalden iç lojistik akışı | production WorkSite, robot geometrisi, RDS task label |
| `AMR Takt time study.xlsx` | dokuz müşteri makine/hücresi | machine, variant, part, container metni, lot/takt/volume planlama kanıtı | slot bazlı fiziksel footprint veya robot koordinatı |
| `AMR Çalışma Planı.xlsx` | dokuz makine; 25 variant | ana parça, alternatif parça, product-group bağlamı, container/lot ve SEALER V3 metinsel kanıtı | SEALER V3 fiziksel layout; eksik lot/container değerleri |
| `PAH-01-A-412B.xlsx` | PAH-01-A, 412B | V1/V2 PAH ve V1/V2/V3 412B fiziksel renkli layout kanıtı | mm cinsinden footprint, robot pickup/drop pozu |
| `PAH-02-A-GAR.xlsx` | PAH-02-A, GAR | V1/V2 PAH ve V1/V2/V3 GAR fiziksel layout kanıtı | mm footprint ve bazı boş parça etiketleri |
| `SH-01-A HÜCRESİ VARYANT KUTU YERLEŞİMİ.xlsx` | SH-01-A V1–V5 | variant bazlı fiziksel yerleşim ve parça etiketi | bazı variant-context uyumsuzluklarının müşteri kararı |
| `SH-01-B HÜCRESİ VARYANT KUTU YERLEŞİMİ.xlsx` | SH-01-B V1–V3 | variant bazlı fiziksel yerleşim ve parça etiketi | bazı variant-context uyumsuzluklarının müşteri kararı |
| `SH-01-P1--SH-01-P2-SH-02-P1 -SH-02-P2.xlsx` | SH-01-P1/P2, SH-02-P1/P2, SEALER | paylaşılan fiziksel plan; SEALER V1/V2 kanıtı | SH-01-P tarafındaki BN400/BN000 uyuşmazlığı; SH-02 boş etiketler; SEALER V3 layout |

Excel'de geçen `Tüm Kasaları Kaldır`, dördüncü bir transport operation'a çevrilmedi. Gerekirse birbirinden bağımsız mevcut taleplerin `TransportBatch` ile gruplanmasıdır.

## 2. Doğrulanmış müşteri baseline'ı

| Varlık | Doğrulanan adet | Not |
|---|---:|---|
| Machine/cell master | 9 | Kaynak kodları aynen korunur; fazladan machine yoktur |
| Aktif/geçerli variant | 25 | Hepsinin textual/planning kanıtı vardır |
| Fiziksel layout assignment'ı kanıtlanmış variant | 24 | SEALER V3 tek istisnadır |
| Unique canonical part | 161 | Sessiz case/karakter normalizasyonu veya duplicate canonical kod yoktur |
| Product group | 51 | Product Group, Variant olarak yorumlanmaz |
| Contextual ALT relationship | 140 | Kaynaklar bunları `Alt Parça` olarak tanımlar |
| RELATED relationship | 0 | Müşteri kaynağında ayrı ve kanıtlanmış RELATED sınıfı yoktur |
| Planning row | 191 | Hesaplanamayan alanlar `NULL` kalır |
| Container master | 10 | Bilinmeyen kategori/footprint tahmin edilmez |
| Layout | 21 | Bazı variant'lar aynı fiziksel layout'u paylaşır |
| Variant-layout assignment | 24 | Zorunlu 1:1 ilişki yoktur |
| Extracted layout region | 183 | 172 lojistik pozisyonu + 11 display-only MACHINE bölgesi |
| Explicit Part↔Slot row | 143 | 124 farklı lojistik slotu kapsar |
| Part mapping'i olmayan lojistik slot | 48 | 28 boş/belirsiz + 20 variant-mismatch-only kanıt |
| Container association'ı belirsiz slot | 80 | Değer uydurulmadı |
| Physical footprint'ü bilinmeyen region | 183 | 172 lojistik + 11 MACHINE; normalize UI geometri mm değildir |

### Veri boşluğu kategorileri

Bu sayılar farklı soruları temsil eder ve birbiri yerine kullanılamaz:

| Kategori | Adet | Anlamı |
|---|---:|---|
| Parça-konum teyidi eksik lojistik pozisyon | 48 | Bu fiziksel pozisyonlarda hangi parça/parçaların kullanılacağı henüz kesin değildir |
| Kasa/palet ilişkisi belirsiz lojistik pozisyon | 80 | Pozisyonun kullanacağı kasa/palet tipi tekil ve güvenilir biçimde belirlenememiştir |
| Güvenilir fiziksel ölçüsü olmayan layout region | 183 | Excel'deki ekran dikdörtgenleri mm ölçüsü, robot pozu veya güvenlik mesafesi değildir |
| Eksik lot/planlama girdisi | 23 | Kutu lotu boş/geçersizdir; bağımlı hesaplar boş bırakılmıştır |
| Excel formül hatası | 33 hücre | Hatalı formül sonucu değer uydurulmamıştır |

Textual planlamada ayrıca 60 container mapping eksikliği bulunur. Bu, 80 fiziksel pozisyon sayısıyla aynı veri kümesi değildir ve iki sayı toplanarak tek bir toplam üretilmemelidir.

Machine kodları tam olarak: `412B`, `GAR`, `PAH-01-A`, `PAH-02-A`, `SEALER`, `SH-01-A`, `SH-01-B`, `SH-01-P1--SH-01-P2`, `SH-02-P1 -SH-02-P2`.

| Machine | Aktif variant | Layout | Assignment | Lojistik slot | Mapping'li slot | Mapping'siz | Container belirsiz |
|---|---|---:|---:|---:|---:|---:|---:|
| 412B | 1, 2, 3 | 3 | 3 | 25 | 24 | 1 | 1 |
| GAR | 1, 2, 3 | 3 | 3 | 25 | 14 | 11 | 13 |
| PAH-01-A | 1, 2 | 2 | 2 | 13 | 13 | 0 | 0 |
| PAH-02-A | 1, 2 | 2 | 2 | 13 | 13 | 0 | 0 |
| SEALER | 1, 2, 3 | 1 | 2 | 14 | 6 | 8 | 14 |
| SH-01-A | 1, 2, 3, 4, 5 | 5 | 5 | 36 | 34 | 2 | 18 |
| SH-01-B | 1, 2, 3 | 3 | 3 | 24 | 20 | 4 | 12 |
| SH-01-P1--SH-01-P2 | 1, 2 | 1 | 2 | 14 | 0 | 14 | 14 |
| SH-02-P1 -SH-02-P2 | 1, 2 | 1 | 2 | 8 | 0 | 8 | 8 |

On iki part kodunun birden fazla geçerli product-group bağlamında kullanılması kaynakla uyumludur; duplicate master oluşturmaz. `67115BN050-10-HAOS` canonical kalır; tekil `HMTR` yerleşim görünümü provenance/data issue olarak korunur.

## 3. Part↔Slot ve routing doğrulaması

- Runtime machine-slot adayları yalnız `master.layout_slot_parts` join'i ile bulunur.
- `layout_slots.metadata_json/canonical_part_codes` yalnız extraction provenance'dır ve routing sorgusunda kullanılmaz.
- Aktif layout, exact effective part, enabled slot, runtime occupancy/load ve aktif reservation filtrelenir.
- Machine-side seçim `MACHINE` ve `EMPTY_STORAGE` rollerini hariç tutar.
- Buffer allocator yalnız açık `EMPTY_STORAGE` rolüne bakar; isim, kod, WorkSite veya metadata'dan rol tahmin etmez.
- Aday sıralaması deterministiktir; PostgreSQL row/advisory locking ve `FOR UPDATE SKIP LOCKED` yarışan taleplerin aynı slotu almasını engeller.
- Client normal transport payload'unda source/destination slot göndermez. Backend çözer, reserve eder ve sonucu response projection'ında döndürür.

Mevcut customer seed'de `EMPTY_STORAGE` rolünde slot yoktur; 172 lojistik slotun tamamı `OTHER` rolündedir. Dolayısıyla production `BRING_EMPTY`/`REMOVE_EMPTY`, müşteri buffer kararı ve saha commissioning olmadan yürütülemez. `REMOVE_FULL` için de kanıtlanmış full-container destination yoktur.

## 4. Operator UI kabul matrisi

Durum yorumları:

- **PASS:** yazılım davranışı uygulanmış ve doğrulanmıştır;
- **PARTIAL:** yazılım vardır ancak tüm müşteri konfigürasyonu tamamlanmamıştır;
- **BLOCKED_BY_CUSTOMER_DATA:** iş/fiziksel yer tanımı müşteri tarafından henüz kesinleştirilmemiştir;
- **BLOCKED_BY_COMMISSIONING:** saha veya production RDS/robot doğrulaması yapılmamıştır.

| Gereksinim | Kaynak/niyet | Mevcut uygulama | Durum | Gap / aksiyon |
|---|---|---|---|---|
| Sicil + cihaz girişi | Production operator kimliği | Sicil, şifresiz, device-bound; leading zero korunur; teknik auth hatası sadeleştirilir | PASS | Kullanıcı/device provisioning kabulde doğrulanacak |
| Yetkili machine seçimi | 9 exact customer code | Yalnız backend'in yetkili listesi; machine değişimi stale part/batch/context temizler | PASS | Yetki matrisi müşteriyle yüklenmeli |
| Ana parça seçimi | Çalışma Planı | Tek dokunuş, belirgin seçim; gereksiz ikinci onay yok | PASS | Yok |
| ALT / RELATED | Kaynakta `Alt Parça` | Yalnız seçilen parent bağlamı; effective part değişir; unrelated sızmaz | PASS | RELATED UI desteklenir fakat baseline'da RELATED kanıtı yok |
| Variant seçimi | 25 valid variant | 1 compatible ise internal auto-resolve, birden fazlaysa explicit seçim, 0 ise sade hata | PARTIAL | SEALER V3 fiziksel layout bekliyor |
| Multi-part / batch | `Tüm Kasaları Kaldır` toplu niyeti | Duplicate engeli, düzenle/sil, item durumu; her item ayrı request/idempotency/RDS kimliği | PASS | Müşteri acceptance senaryosu koşulacak |
| Boş Kasa Getir | Operasyon kanıtı | Doğru domain/UI ve backend allocator hazır | BLOCKED_BY_CUSTOMER_DATA | EMPTY_STORAGE pozisyonları/allowed containers yok |
| Dolu Kasayı Kaldır | `Kasa Doldu`/kaldırma niyeti | OCCUPIED+FULL machine source kuralı hazır | BLOCKED_BY_CUSTOMER_DATA | Full destination stratejisi yok |
| Boş Kasayı Kaldır | `Kasa Boş`/kaldırma niyeti | OCCUPIED+EMPTY source ve EMPTY_STORAGE destination kuralı hazır | BLOCKED_BY_CUSTOMER_DATA | EMPTY_STORAGE pozisyonları yok |
| Otomatik fiziksel slot | Backend-authoritative kural | Client slot göndermez; backend transaction içinde çözer ve reserve eder | PARTIAL | 48/172 lojistik slotta explicit Part↔Slot mapping yok |
| Layout görselleştirme | 21 proven layout | Normalize 2D durum/inceleme; robot haritası olmadığı açık; tap execution yapmaz | PASS | SEALER V3 ayrı müşteri gap'i |
| Warehouse readiness | Ayrı hazırlık gerçeği | `WAITING_PREPARATION / READY / NOT_REQUIRED`, execution'dan ayrı; Warehouse/Supervisor yetkili | PASS | Hangi işlerin hazırlık istediği tahmin edilmedi; müşteri kuralı gerekir |
| Robot alarm görünürlüğü | RDS `GET /api/agv-report/core` | Operator'a sade/actionable read-only banner; reset/ack/clear/history yok | BLOCKED_BY_COMMISSIONING | Production RDS ve fiziksel robot alarm kabulü yapılmadı |
| Dar PDA / Web/PWA kullanımı | Android + browser hedefi | Narrow-width widget testleri, Chrome/Edge E2E; büyük action ve responsive canvas | PASS | Gerçek terminal/PDA saha kabulü gerekir |

## 5. Operator akışı sonucu

Doğrulanan akış:

```text
Sicil + kayıtlı cihaz
  -> yetkili machine
  -> ana parça
  -> opsiyonel ALT/RELATED
  -> compatible variant
  -> İşleme Devam Et
  -> Boş Kasa Getir / Dolu Kasayı Kaldır / Boş Kasayı Kaldır
  -> backend slot çözümü ve reservation
  -> request
  -> "Konum otomatik seçildi" + layout highlight
```

Operator layout'u pasif visualization/status yüzeyidir; slot tap herhangi bir seçim, detay paneli, load-state aksiyonu veya transport source/destination üretmez. Bir request oluşturulduğunda yalnız backend'in transaction içinde çözdüğü makine tarafındaki slot otomatik vurgulanır. Generated `SLOT-001` benzeri DB kodları Operator presentation'da gösterilmez; backend/Admin kimliği veya routing değişmez.

## 6. Production readiness sınırı

Simulator üzerinde request kimliği/idempotency/callback/reconciliation ve dinamik `from/to` serialization kanıtlanmıştır. Bu kanıt production forklift workflow kanıtı değildir. `TKG_SIM_MOVE` ve `TKG_SIM_DYNAMIC_MOVE` development/commissioning-only'dir; WorkSite, forklift load/unload, physical CBD15 ve production `BRING_EMPTY / REMOVE_FULL / REMOVE_EMPTY` davranışını kanıtlamaz.

Production dispatch ancak [CUSTOMER_DATA_REQUEST.md](CUSTOMER_DATA_REQUEST.md) ve [PRODUCTION_COMMISSIONING_CHECKLIST.md](PRODUCTION_COMMISSIONING_CHECKLIST.md) kapıları kapandıktan sonra açılmalıdır.
