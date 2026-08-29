# Customer WP-01A — Operator Employee / Registry Identity Contract

**Kapsam:** Production Operator kimlik doğrulamasının müşteri **sicil numarası /
employee number** ile yapılmasını sağlayan backend + veritabanı sözleşmesi.

**DB migration:** `db/migrations/002_operator_employee_identity.sql`

**Flutter UI değişikliği:** **YOK** — WP-01A Flutter login ekranını değiştirmez.

## Amaç

Production Operator, keyfi bir uygulama `username`'i yerine müşterinin sicil
numarası ile giriş yapabilmelidir. Admin Web kimlik doğrulaması ayrı kalır ve
`username + password` ile çalışmaya devam eder. Operator kimliği `employee
identity + password + device binding` olarak korunur.

## Mevcut alanın yeniden kullanımı

Veritabanı zaten `auth.users.employee_no` alanını içerir (Admin bu alanı
yönetir). WP-01A **ikinci bir employee alanı oluşturmaz** ve mevcut alanı
yeniden adlandırmaz.

## Normalizasyon kuralları

Tek normalizasyon sınırı: `TkgPda.Application.EmployeeNumber`.

- Değer her zaman **STRING**'dir; asla sayı olarak ayrıştırılmaz.
- Baştaki/sondaki boşluk kırpılır; anlamlı karakterler ve **baştaki sıfırlar
  korunur** (`"001245"` → `"001245"`, asla `"1245"` olmaz).
- Karşılaştırma ve benzersizlik **trim + büyük/küçük harf duyarsız**dır.
- Boş/yalnız-boşluk değer, Admin tarafından yönetilen kullanıcılar için "employee
  number yok" (NULL) ile eşdeğerdir.
- Operator employee-number login'inde boş değer geçersizdir.

`Canonicalize(value)` → saklama/görüntüleme temsili (kırpılmış veya NULL).
`NormalizeForLookup(value)` → `UPPER(BTRIM(...))` ile eşleşen arama anahtarı.

## Benzersizlik kuralları

`002_operator_employee_identity.sql`, boş olmayan her employee number için
normalize edilmiş kısmi UNIQUE fonksiyonel indeks oluşturur:

```
uq_users_employee_no_normalized ON auth.users (UPPER(BTRIM(employee_no)))
  WHERE employee_no IS NOT NULL AND BTRIM(employee_no) <> ''
```

- Çakışır: `ABC123`, `abc123`, `" ABC123 "`, `"abc123 "`.
- Ayrık kalır: `001245`, `1245`.
- NULL ve boş değerler muaf (birden fazla olabilir).
- `employee_no` **global olarak NOT NULL yapılmadı** — admin/servis kimlikleri
  employee number olmadan var olabilir.
- Migration, normalize edilmiş mevcut çift kayıt varsa **başarısız olur** (veriyi
  tahminle çözmez).

### Migration rollout / bootstrap kuralı

Migration'lar deterministik dosya-adı sırasıyla uygulanır: `001` → `002`. Güncel
TKG şemasını taze bir veritabanında oluşturan **ve güncel backend'i çalıştıran**
her aktif yol (Category A) `002`'yi `001`'den hemen sonra uygular:

- `tools/dev/start_local_stack.ps1` (yerel dev bootstrap)
- `tests/phase_03`, `tests/phase_04`, `tests/phase_05`
- `tests/phase_07a`, `tests/phase_07b`
- `tests/phase_08a` (readiness + live sync bootstrap'ı), `tests/phase_08b`
- `tests/phase_08c` (Phase 10A canlı kabul harness'i bunu sarar)
- `tests/phase_09`

Yalnız seed/pipeline doğrulayan ve güncel backend'i çalıştırmayan Phase 02A/02B
harness'ları (Category B) `001`-only kalır; migration 002 seed sayımlarını
etkilemez.

`002` idempotenttir (`CREATE UNIQUE INDEX IF NOT EXISTS`): mevcut pre-WP01A bir
veritabanına drop/recreate olmadan güvenle uygulanabilir ve indeks zaten varsa
no-op'tur. İlk (oluşturan) uygulama, normalize edilmiş mevcut çift kayıtlar
varsa yine de **başarısız olur** (tasarım gereği; veri tahminle düzeltilmez).
`tools/dev/start_local_stack.ps1` `002`'yi her başlangıçta idempotent olarak
uygular; böylece hem taze hem de mevcut pre-WP01A yerel veritabanı yerinde
yükseltilir. Admin Web e2e bootstrap'ı (`src/TkgPda.AdminWeb/e2e/global-setup.ts`)
de güncel backend'i çalıştırır; kapsam sınırı nedeniyle bu paket onu
değiştirmez ve mimar kararına bırakır.

## Operator geçiş (transitional) API davranışı

`POST /api/v1/auth/login` (device-bound) artık `employeeNumber` alanını da
kabul eder. `LoginRequest(username?, password, employeeNumber?)`.

| Girdi | Davranış |
|---|---|
| yalnız `employeeNumber` | employee number ile doğrula (yeni birincil yol) |
| yalnız `username` | mevcut username akışı (geçici geri uyumluluk) |
| her ikisi de | `400 AMBIGUOUS_LOGIN_IDENTITY` |
| hiçbiri | `400 INVALID_LOGIN_REQUEST` |

- `password` zorunlu; `X-Device-Code` device binding zorunlu (bugünküyle aynı).
- Employee yolu, aynı iç `User` kimliğini çözer ve aynı device-bound JWT +
  policy'leri üretir (`ClientTypes.DeviceBound`). Ayrı kullanıcı türü, ayrı JWT
  mantığı veya ikinci auth sistemi yoktur.
- Bilinmeyen employee number ve yanlış parola aynı `401 INVALID_CREDENTIALS`
  sonucunu döndürür (hesap sıralama/enumeration farkı yok). Lookup indekslidir;
  bellek içi tarama yapılmaz (`FindUserByEmployeeNumberAsync`).
- Employee number JWT'ye eklenmez; yetkilendirme kararlı iç kullanıcı ID + rol
  üzerinden yürür.

## Admin ayrımı

Admin Web (`/api/v1/admin/...`) yalnız `username + password` ile kalır. Token türü
`ADMIN_WEB` değişmedi; Admin'e employee-number login veya device binding
eklenmedi. Admin login'i `employeeNumber` alanını yok sayar.

## Admin kullanıcı yönetimi

Admin create/update, employee number'a aynı canonical kuralları uygular (trim,
boş → NULL, baştaki sıfırlar korunur). Normalize edilmiş çift employee number,
mevcut benzersizlik indeksi + `PostgresException(UniqueViolation)` middleware
kalıbı ile temiz `409 CONFIGURATION_CONFLICT` döndürür (500 değil). Yeni
error-envelope formatı eklenmedi.

## Geri uyumluluk sınırı

WP-01A sonunda: mevcut Flutter Operator `username` login'i **çalışmaya devam
eder**; yeni `employeeNumber` çağrıları çalışır; Admin `username` login'i çalışır.

**Mimar kararı (final):** Legacy Operator `username` fallback'i WP-01B'den **sonra
da kaldırılmaz**. WP-01B Flutter'ı `employeeNumber`'a geçirir, ancak backend
username fallback'i yalnız daha sonraki açık bir cleanup/hardening paketinde
kaldırılacaktır. Username Operator login'i üretim UX'i olarak belgelenmez.

**Sicil formatı:** Onaylanan canonical kurallar dışında hiçbir müşteriye özgü
sicil formatı (digits-only / sabit uzunluk / regex / checksum / numeric dönüşüm /
otomatik sıfır-doldurma) müşteri master-data formatı açıkça teyit edilene kadar
eklenmez.

## Testler

- **DB (Infrastructure, `tests/phase_03`):** case-insensitive benzersizlik,
  boşluk-duyarsız benzersizlik, baştaki sıfır ayrımı (`001245 != 1245`), çoklu
  NULL, boş değer muafiyeti, indeksli store lookup, indeks varlığı.
- **Auth/API (`tests/phase_04`, `AuthenticatedApiTests`):** employee login başarı,
  normalizasyon (boşluk/case/leading-zero), başarısızlıklar (bilinmeyen numara,
  yanlış parola, pasif kullanıcı, geçersiz device, boş numara, her ikisi, hiçbiri),
  legacy username login geri uyumluluğu.
- **Admin API (`tests/phase_05`, `AdminConfigurationApiTests`):** admin employee
  number oluştur/kırp, çift normalize numara temiz reddi, çoklu NULL, leading-zero
  ayrık kabul, admin login'in employee-number tabanlı olmadığı.

## WP-01B bağımlılığı

WP-01B, Flutter Operator login ekranını **Sicil Numarası** alanına geçirecek ve
istek sözleşmesini `employeeNumber` kullanacak şekilde güncelleyecektir. WP-01A
bu backend'i, Flutter değişmeden önce indirmek için ikili destek sağlar.
