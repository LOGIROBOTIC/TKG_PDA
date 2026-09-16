"""Security, database, routing, API and configuration sections."""

import io
import json
import os

H, P, UL, OL, T, C, PRE, N, PB = "h", "p", "ul", "ol", "table", "code", "pre", "note", "pagebreak"

BUILD = os.path.dirname(os.path.abspath(__file__))


def _load(name):
    with io.open(os.path.join(BUILD, name), encoding="utf-8") as fh:
        return json.load(fh)


def section_auth():
    b = []
    b.append((H, 1, "Kimlik Doğrulama ve Yetkilendirme"))
    b.append((H, 2, "Üç kimlik sözleşmesi"))
    b.append((T, ["Kullanıcı", "Kimlik bilgisi", "Cihaz gerekir mi?"], [
        ["Admin", "kullanıcı adı + parola", "Hayır"],
        ["Operatör (üretim)", "sicil numarası (employeeNumber) + cihaz — PAROLA YOK", "Evet"],
        ["Operatör (eski/legacy)", "kullanıcı adı + parola + cihaz", "Evet"],
    ]))
    b.append((N, "NOTE", "Legacy operatör kullanıcı adı girişi backend'de geçici olarak desteklenmeye devam eder. Üretim sözleşmesi sicil numarasıdır."))
    b.append((H, 2, "employee_no normalizasyonu"))
    b.append((UL, [
        "Tip: string (sayı değil).",
        "Kırpılır (trim).",
        "Kimlik karşılaştırması büyük/küçük harf duyarsızdır.",
        "**Baştaki sıfırlar anlamlıdır** — `007` ile `7` farklı sicillerdir.",
        "Global olarak null olabilir (her kullanıcının sicili olmak zorunda değil).",
        "Dolu olduğunda normalize edilmiş biçimiyle benzersizdir.",
    ]))
    b.append((P, "Bu kurallar `src/TkgPda.Application/EmployeeNumber.cs` içinde tek yerde uygulanır. Sayıya çevirme yapılmaz; baştaki sıfırların korunması bunun sebebidir."))
    b.append((H, 2, "Roller ve politikalar"))
    b.append((T, ["Rol kodu", "Anlamı"], [
        ["ADMIN", "Tüm konfigürasyon ve teşhis"],
        ["SUPERVISOR", "Varyant yönetimi, uzman RDS operasyonları"],
        ["LINE_OPERATOR", "Hat operatörü — kasa iş akışları"],
        ["WAREHOUSE_OPERATOR", "Depo operatörü — hazırlık işaretleme"],
    ]))
    b.append((T, ["Politika", "İzinli roller"], [
        ["operator", "ADMIN, SUPERVISOR, LINE_OPERATOR, WAREHOUSE_OPERATOR"],
        ["variant-manager", "ADMIN, SUPERVISOR"],
        ["administrator", "ADMIN"],
        ["warehouse-preparation", "ADMIN, SUPERVISOR, WAREHOUSE_OPERATOR"],
    ]))
    b.append((N, "NOTE", "WP-05A gereği bir talebi READY işaretlemek bir **depo hazırlık** eylemidir, sıradan bir hat operatörü taşıma eylemi değil. `LINE_OPERATOR` bu politikadan bilinçli olarak dışlanmıştır."))
    b.append((H, 2, "Cihaz bağlama (device binding)"))
    b.append((P, "Operatör token'ları cihaza bağlıdır. Her istek `X-Device-Code` başlığı taşır ve middleware üç şeyi doğrular:"))
    b.append((OL, [
        "Başlıktaki cihaz kodu ile token içindeki `device_code` iddiası birebir eşleşmeli — aksi halde `DEVICE_CONTEXT_MISMATCH` (HTTP 403).",
        "Cihaz veritabanında (`audit.client_devices`) bulunmalı ve aktif olmalı — aksi halde `DEVICE_NOT_AUTHORIZED` (HTTP 403).",
        "Doğrulanan cihaz kaydı istek bağlamına konur ve aşağı katmanlarda yeniden sorgulanmaz.",
    ]))
    b.append((H, 2, "İstemci tipleri"))
    b.append((T, ["ClientType", "Kim kullanır", "Ek kısıt"], [
        ["DEVICE_BOUND", "Operator PDA", "X-Device-Code zorunlu"],
        ["ADMIN_WEB", "Admin Web", "Token ADMIN rolü taşımalı, aksi halde ADMIN_WEB_ROLE_REQUIRED"],
    ]))
    b.append((H, 2, "JWT yapılandırması"))
    b.append((T, ["Ayar", "Varsayılan", "Not"], [
        ["Authentication:Jwt:Issuer", "tkg-pda", ""],
        ["Authentication:Jwt:Audience", "tkg-pda-clients", ""],
        ["Authentication:Jwt:SigningKey", "(yok)", "`<JWT_SIGNING_KEY>` — en az 32 bayt olmalı, aksi halde başlangıçta hata"],
        ["Authentication:Jwt:ExpiryMinutes", "60", ""],
        ["Authentication:Jwt:ClockSkewSeconds", "30", ""],
    ]))
    b.append((N, "WARN", "İmzalama anahtarı `appsettings.json` içinde **yoktur** ve olmamalıdır. Ortam değişkeni `Authentication__Jwt__SigningKey` ile verilir. `Validate()` 32 bayttan kısa anahtarı reddeder, böylece zayıf bir anahtarla üretime çıkmak imkânsızdır."))
    b.append((H, 2, "Standart başlıklar"))
    b.append((T, ["Başlık", "Yön", "Amaç"], [
        ["X-Device-Code", "İstek", "Cihaz bağlama"],
        ["X-Correlation-ID", "İstek/Yanıt", "Uçtan uca iz sürme"],
        ["Idempotency-Key", "İstek", "Tekrar güvenliği"],
        ["Idempotency-Replayed", "Yanıt", "Bu yanıt mevcut bir kaydın tekrarı mı"],
    ]))
    return b


def section_schema():
    schema = _load("schema.json")
    b = []
    b.append((H, 1, "PostgreSQL Şeması"))
    b.append((P, "Şemanın tek gerçek kaynağı `db/migrations` altındaki SQL dosyalarıdır. EF Core migration'ı yoktur ve eklenmemelidir."))
    total = sum(len(v) for v in schema.values())
    b.append((P, f"Toplam **{total} tablo**, **{len(schema)} şema** altında dağılır."))
    rows = []
    order = ["auth", "master", "ops", "integration", "audit"]
    purpose = {
        "auth": "Kullanıcılar, roller, makine bazlı yetkiler",
        "master": "Ana veri: makineler, varyantlar, parçalar, layout'lar, navigasyon profilleri",
        "ops": "Çalışma zamanı durumu: slot doluluk/yük, taşıma talepleri, olaylar",
        "integration": "RDS entegrasyon durumu: WorkSite projeksiyonu, slot eşlemesi, görevler",
        "audit": "Denetim kayıtları ve istemci cihazları",
    }
    for s in order:
        rows.append([s, str(len(schema[s])), purpose[s]])
    b.append((T, ["Şema", "Tablo", "Amaç"], rows))
    for s in order:
        b.append((H, 2, f"{s} şeması"))
        b.append((UL, [f"`{s}.{t}`" for t in schema[s]]))
    b.append((H, 2, "Kritik kısıtlar"))
    b.append((T, ["Kısıt", "Tablo", "Ne korur"], [
        ["ck_slot_load_requires_occupancy", "ops.slot_runtime_state", "Dolu olmayan bir slot EMPTY/FULL yük durumu taşıyamaz"],
        ["uq_layout_slot_code", "master.layout_slots", "(layout_id, code) benzersiz — layout klonlamanın eşleme anahtarı"],
        ["uq_transport_idempotency", "ops.transport_requests", "Aynı idempotency anahtarı iki kayıt üretemez"],
        ["PRIMARY KEY (layout_slot_id, part_id)", "master.layout_slot_parts", "Aynı parça bir slota iki kez bağlanamaz"],
    ]))
    b.append((N, "FACT", "DATABASE-CONFIRMED — Tüm kısıtlar `db/migrations/` altındaki SQL dosyalarından okunmuştur."))
    b.append((H, 2, "Migration dosyaları"))
    b.append((T, ["Dosya", "Getirdiği"], [
        ["001_initial_schema.sql", "29 tablo; auth, master, ops, integration, audit şemalarının tamamı"],
        ["002_operator_employee_identity.sql", "auth.users üzerinde employee_no ve normalize edilmiş benzersiz indeks"],
        ["003_transport_batch.sql", "ops.transport_batches — çok parçalı toplu gönderim"],
        ["004_transport_request_readiness.sql", "Talep hazırlık ekseni (TransportReadinessState) kolonları"],
        ["005_layout_slot_part_mapping.sql", "master.layout_slot_parts — Parça ↔ Slot yetkili eşlemesi"],
    ]))
    b.append((P, "Tüm migration'lar `IF NOT EXISTS` konvansiyonunu izler ve idempotenttir; sıralı uygulanmaları gerekir."))
    return b


def section_routing():
    b = []
    b.append((H, 1, "Parça ↔ Slot Yönlendirme ve Layout Sürümleme"))
    b.append((H, 2, "Neden ayrı bir ilişki tablosu?"))
    b.append((P, "Operatör hangi fiziksel slota dokunacağını elle seçmemelidir. \"Bu parça hangi makine slotuna ait?\" sorusunun yetkili cevabı `master.layout_slot_parts` tablosudur."))
    b.append((N, "WARN", "`master.layout_slots.metadata_json` içindeki `canonical_part_codes`, `part_association_unresolved` ve `confidence` alanları **çalışma zamanı yönlendirme kaynağı değildir**. Bunlar otomatik Excel çıkarımından gelen, kendi boşluklarını kendisi bildiren köken/veri-kalitesi kanıtıdır. Bir dispatch veya PART_CONFLICT kararı asla bu JSON'a dayanmaz."))
    b.append((H, 2, "Kardinalite"))
    b.append((P, "İlişki çoktan-çoğadır. Bir fiziksel slot meşru olarak birden çok müşteri parça kodunu tutabilir (ana + alternatifler); bir parça birden çok fiziksel slotta ve farklı varyanta özgü layout'larda farklı slotlarda görünebilir. \"Bir parça → bir global slot\" kısıtı **uygulanmaz**."))
    b.append((H, 2, "Kapsam sınırı"))
    b.append((P, "Bu tablo yalnız kimlik ilişkisini kaydeder (hangi parça hangi slota ait). Hiçbir yürütme/çalışma zamanı durumu taşımaz; doluluk ve yük yalnız `ops.slot_runtime_state` içinde kalır."))
    b.append((H, 2, "Layout sürüm klonlama"))
    b.append((P, "Bir layout'un yeni sürümü oluşturulduğunda üç şey sırayla kopyalanır ve son adım doğrulanır:"))
    b.append((OL, [
        "Yeni `master.layouts` satırı: aynı makine/kod, `max(version)+1`, durum `DRAFT`.",
        "`master.layout_slots` satırları yeni layout'a kopyalanır — yeni slotlar **yeni UUID** alır.",
        "`master.layout_slot_parts` ilişkileri, eski slot ile yeni slot `code` üzerinden eşleştirilerek kopyalanır.",
    ]))
    b.append((C, """INSERT INTO master.layout_slot_parts (layout_slot_id, part_id, created_at)
SELECT new_slot.id, lsp.part_id, now()
FROM master.layout_slots old_slot
JOIN master.layout_slot_parts lsp ON lsp.layout_slot_id = old_slot.id
JOIN master.layout_slots new_slot ON new_slot.layout_id = @new_id AND new_slot.code = old_slot.code
WHERE old_slot.layout_id = @source_id""", "sql"))
    b.append((P, "`code` üzerinden eşleme, `uq_layout_slot_code UNIQUE (layout_id, code)` kısıtı sayesinde güvenlidir: bir layout içinde bir kod en fazla bir slota aittir, dolayısıyla JOIN çoğaltma üretemez."))
    b.append((H, 2, "İlişkisel kayıp koruması"))
    b.append((C, """var copiedCount = await copyParts.ExecuteNonQueryAsync(cancellationToken);
// ... kaynak layout'taki gerçek ilişki sayısı okunur ...
if (copiedCount != expectedCount)
{
    throw new InvalidOperationException(
        $"Relational loss during layout version cloning: expected {expectedCount} layout_slot_parts, copied {copiedCount}.");
}""", "csharp"))
    b.append((N, "NOTE", "Sayım doğrulaması aynı transaction içindedir. Bir ilişki sessizce düşerse klon commit edilmez; yeni sürüm hiç oluşmaz. Bu, \"yeni layout sürümünde parçalar kayboldu\" sınıfı hataların üretime çıkmasını engelleyen tek koruma katmanıdır."))
    b.append((P, "Klonlama başarılı olursa `ADMIN_LAYOUT_VERSION_CREATED` denetim kaydı yazılır ve ardından transaction commit edilir."))
    return b


def section_api():
    eps = _load("endpoints.json")
    b = []
    b.append((H, 1, "HTTP API Referansı"))
    b.append((P, f"API yüzeyinde toplam **{len(eps)} uç nokta** kayıtlıdır. Aşağıdaki tablolar `.MapGet/.MapPost/.MapPut/.MapDelete/.MapPatch` kayıtlarından doğrudan çıkarılmıştır."))
    groups = {}
    for cat, method, path, handler in eps:
        groups.setdefault(cat, []).append((method, path, handler))
    titles = {
        "Operator/Auth": "Operatör ve kimlik uçları",
        "Admin": "Admin uçları",
        "RDS/Internal": "RDS / dahili uçlar",
    }
    for cat in ["Operator/Auth", "Admin", "RDS/Internal"]:
        if cat not in groups:
            continue
        rows = groups[cat]
        b.append((H, 2, f"{titles.get(cat, cat)} ({len(rows)})"))
        b.append((T, ["Metot", "Yol", "Handler"], [[m, p, h] for m, p, h in rows]))
    b.append((H, 2, "Ortak yanıt sözleşmesi"))
    b.append((P, "Hatalar tek bir problem gövdesi ile döner: `code` (makine okunur), `message` (insan okunur), `traceId`. HTTP durumu hatanın sınıfına göre belirlenir."))
    b.append((T, ["Durum", "Anlamı", "Örnek kod"], [
        ["400", "İstek biçimi geçersiz", "BATCH_ITEMS_REQUIRED"],
        ["401", "Kimlik doğrulanmadı", "—"],
        ["403", "Yetki veya cihaz reddi", "DEVICE_CONTEXT_MISMATCH, ADMIN_WEB_ROLE_REQUIRED"],
        ["404", "Kaynak yok", "—"],
        ["409", "Çakışma / eşzamanlılık", "ACTIVE_TRANSPORT_EXISTS"],
        ["422", "İş kuralı reddi", "OPERATION_NOT_OPERATOR_SELECTABLE"],
        ["503", "Aşağı akış erişilemez", "RDS okuma hataları"],
    ]))
    b.append((H, 2, "Admin yazma uçlarında iyimser eşzamanlılık (ETag / If-Match)"))
    b.append((P, "Her Admin yazma ucu yanıtında bir `ETag` başlığı döner ve her mutasyon isteğinde `If-Match` başlığı **zorunludur**. Değer, kaydın sayısal eşzamanlılık jetonudur (`ConcurrencyToken`)."))
    b.append((T, ["Durum", "Kod", "HTTP", "Anlamı"], [
        ["If-Match yok veya boş", "IF_MATCH_REQUIRED", "428", "Bu mutasyon için başlık zorunludur"],
        ["If-Match sayısal değil veya ≤ 0", "INVALID_CONCURRENCY_TOKEN", "400", "API'nin döndürdüğü jeton kullanılmalıdır"],
        ["Jeton eskimiş", "CONFIGURATION_CONFLICT", "409", "Kayıt başkası tarafından değiştirilmiş"],
    ]))
    b.append((P, "Layout alt kaynaklarında (slotlar) ayrıca `X-Layout-Version` başlığı, üst layout sürümünü doğrulamak için aynı ayrıştırıcıdan geçer. Böylece bir slot güncellemesi, altında bulunduğu layout sürümü değişmişken uygulanamaz."))
    b.append((N, "NOTE", "428 seçimi bilinçlidir: eksik `If-Match` bir yetki veya doğrulama hatası değil, karşılanmamış bir ön koşuldur. İstemci jetonu okuyup isteği tekrar edebilir."))
    b.append((H, 2, "İptal ve istisna sıralaması"))
    b.append((P, "`ApiErrors` middleware'i istisnaları tek bir zincirde ele alır. Sıralama anlamlıdır ve **tek bir** iptal yolu vardır:"))
    b.append((T, ["Yakalanan", "Sonuç"], [
        ["ApiException / WorkflowRuleException", "Kodun taşıdığı HTTP durumu ve `code`"],
        ["BadHttpRequestException", "400 INVALID_HTTP_REQUEST"],
        ["JsonException", "400 INVALID_JSON"],
        ["OperationCanceledException (RequestAborted iptal edilmişse)", "Yanıt gövdesi yok; Information seviyesinde günlüklenir"],
        ["PostgresException (unique/fk/check ihlali)", "409 CONFIGURATION_CONFLICT"],
        ["NpgsqlException", "503 DATABASE_UNAVAILABLE"],
        ["Exception", "500, Error seviyesinde günlüklenir"],
    ]))
    b.append((N, "NOTE", "İstemci bağlantıyı kopardığında sunucu hatası üretilmez: istek yanıt gövdesi olmadan biter, çünkü istemcinin terk ettiği bir bağlantıya hiçbir şey yazılamaz. `RequestAborted` iptal edilmemişken gelen bir `OperationCanceledException` ise gerçek bir iç arızadır ve bilinçli olarak aşağıdaki 500'e düşer."))
    return b


def section_config():
    b = []
    b.append((H, 1, "Konfigürasyon Referansı"))
    b.append((P, "Yapılandırma standart ASP.NET Core sırasını izler: `appsettings.json` → ortam değişkenleri (`__` ayırıcı). Sırlar dosyaya yazılmaz."))
    b.append((H, 2, "Rds bölümü"))
    b.append((T, ["Anahtar", "Varsayılan", "Anlamı"], [
        ["Rds:Enabled", "true", "RDS entegrasyonu aktif mi (okuma dahil)"],
        ["Rds:DispatchEnabled", "false", "Fiziksel hareket mutasyonu izni"],
        ["Rds:BaseUrl", "http://127.0.0.1:8080", "RDS okuma/WindTask adresi"],
        ["Rds:TimeoutSeconds", "5", "HTTP zaman aşımı"],
        ["Rds:CallbackUrl", "(boş)", "RDS'in TKG'ye geri bildirim adresi"],
        ["Rds:EmptyContentValue", "(boş)", "Boş kasa için RDS içerik etiketi — sahaya özgü"],
        ["Rds:FullContentValue", "(boş)", "Dolu kasa için RDS içerik etiketi — sahaya özgü"],
        ["Rds:AllowInsecureRemoteHttp", "false", "Uzak adrese düz HTTP izni"],
        ["Rds:WorkerIntervalSeconds", "3", "Worker döngü aralığı"],
        ["Rds:WorkSiteFreshnessSeconds", "300", "WorkSite projeksiyon tazelik penceresi"],
        ["Rds:IntegrationKey", "(yok)", "`<INTEGRATION_KEY>` — yalnız ortam değişkeninden; uzunluk 32–512 karakter"],
        ["Rds:CallbackKey", "(yok)", "`<CALLBACK_KEY>` — yalnız ortam değişkeninden; uzunluk 32–512 karakter"],
    ]))
    b.append((N, "NOTE", "`RdsOptions` iki anahtarın uzunluğunu doğrular: 32 karakterden kısa veya 512 karakterden uzun bir anahtar reddedilir. Callback doğrulaması sabit zamanlı karşılaştırma (`FixedTimeEquals`) kullanır, böylece anahtar karşılaştırması zamanlama saldırısına açık değildir. Callback anahtarı yapılandırılmamışsa callback ucu hiç kabul etmez."))
    b.append((H, 2, "RdsCore bölümü"))
    b.append((T, ["Anahtar", "Varsayılan", "Anlamı"], [
        ["RdsCore:BaseUrl", "http://127.0.0.1:8088", "RDSCore native order adresi"],
        ["RdsCore:TimeoutSeconds", "5", "HTTP zaman aşımı"],
        ["RdsCore:AllowInsecureRemoteHttp", "false", "Uzak adrese düz HTTP izni"],
        ["RdsCoreExecution:Enabled", "true", "Native order motoru yapılandırıldı mı"],
        ["RdsCoreExecution:Mode", "RDSCORE_NATIVE_ORDER", "Yürütme motoru seçimi"],
        ["RdsCoreDiscovery:Enabled", "true", "Commissioning keşif istemcisi"],
    ]))
    b.append((N, "WARN", "`RdsCoreExecution:Enabled=true` fiziksel hareket izni DEĞİLDİR. Yalnız hangi motorun yapılandırıldığını söyler; izin `Rds:DispatchEnabled` üzerindedir ve bileşim `AND`'dir."))
    b.append((H, 2, "Diğer bölümler"))
    b.append((T, ["Anahtar", "Varsayılan", "Anlamı"], [
        ["ConnectionStrings:TkgPda", "(yok)", "`<DB_CONNECTION>` — yalnız ortam değişkeninden"],
        ["Cors:AllowedOrigins", "127.0.0.1:8085, :7357 ve localhost karşılıkları", "Admin Web ve test kaynakları"],
        ["Logging:LogLevel:Default", "Information", ""],
        ["Logging:LogLevel:Microsoft.AspNetCore", "Warning", ""],
        ["AllowedHosts", "*", ""],
    ]))
    b.append((H, 2, "Adres güvenliği"))
    b.append((P, "`AllowInsecureRemoteHttp=false` iken uzak (loopback olmayan) bir adrese düz HTTP ile bağlanmak reddedilir. Başlangıç günlüğü adresi `BaseAddressDisplaySafe` üzerinden yazar; bu, adresin kullanıcı bilgisi/kimlik parçası içermesi durumunda onu günlüğe düşürmemek içindir."))
    b.append((C, """RDS Integration: {Status}, RDS Dispatch: {Dispatch}, RDSCore: {Endpoint}""", "text"))
    b.append((P, "Bu satır `src/TkgPda.Api/Program.cs` içinde başlangıçta yazılır ve bir devreye alma mühendisinin kapının açık mı kapalı mı olduğunu tek bakışta görmesini sağlar."))
    b.append((N, "WARN", "Bu doküman hiçbir gerçek sır içermez. Bağlantı dizesi, JWT imzalama anahtarı, RDS entegrasyon anahtarı ve callback anahtarı yalnız `<DB_CONNECTION>`, `<JWT_SIGNING_KEY>`, `<INTEGRATION_KEY>`, `<CALLBACK_KEY>` yer tutucularıyla anılır."))
    return b
