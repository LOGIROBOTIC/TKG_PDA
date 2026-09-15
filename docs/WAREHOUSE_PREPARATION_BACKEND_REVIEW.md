# Ortak hazırlık backend'i — uygulanan kurallar ve şema inceleme sınırı

Durum: domain kuralları uygulandı; aşağıdaki kalıcılık/taşıma değişiklikleri
**öneridir, uygulanmadı**. Depo API'si veya ayrı Android uygulaması tamamlanmadı.

## Kesinleşen müşteri akışı

- Dört ortak alan; her alanda en fazla bir palet.
- Talep paleti: bir talep, bir parça türü, bir hedef makine. Farklı talepler veya
  parça türleri aynı alana karıştırılmaz. Farklı alanlarda aynı parça türü olabilir.
- Boş kap önceden yerleştirilebilir; yarı mamul yalnız talep geldikten sonra hazırlanır.
- Makine tarafındaki ilgili boş kasa kaldırılmadan dolu kasa talebi açılamaz.
  Boş kasa kaldırma ile dolu kasa getirme bağımsız taleplerdir.
- Hazır onayı teslimat değildir. Önceden yerleştirilmiş boş palet, bir talebe
  eşleştirilmeden sevke hazır bir taşıma oluşturmaz.
- Operatör miktar girmez. Eksik kasa/lot/ölçü veya RDS eşlemesi tahmin edilmez.

## Uygulanan bölüm

`src/TkgPda.Domain/WarehousePreparationRules.cs` yan etkisiz kurallar içerir:

- 1–4 mantıksal alan numarası; doğrulanmış boş ve tahsis edilmemiş alan kontrolü;
- EMPTY/FULL ayrımı; dolu palet için talep ve parça zorunluluğu;
- palet içeriği ile talebin parça, kap ve boş/dolu gereksinimi uyumu;
- sevk edilmiş/terminal veya hazırlık beklemeyen talepte yeni hazırlık engeli;
- hazır onayı öncesi fiziksel yerleştirme ve talep eşleşmesi kontrolü;
- dolu kasa talebi öncesi makine konumunda VACANT/UNKNOWN, içeriksizlik ve
  aktif rezervasyon bulunmaması kontrolü.

Boş paletin fiziksel `PartId` alanı NULL'dır; bu, boş kabın istendiği talepteki
üretim parçası kimliğinin silinmesi anlamına gelmez. İçerik ve talep bağlamı ayrıdır.

`WarehousePreparationDemand` sunucunun yükleyeceği kanıt için domain değeridir;
HTTP sözleşmesi değildir. Bir UUID verilmesi gerçek talep veya doğru operasyon
kanıtı sayılmaz. Özellikle REMOVE_FULL kaydı bu modele çevrilerek dolu kasa
getirme gibi kullanılamaz. FULL inbound operasyon henüz desteklenmemektedir.

Bu dosya henüz API/store tarafından çağrılmıyor. Alan başına fiziksel tek palet
kuralının yarış koşullarında DB'de korunması, idempotency, yetki ve audit aşağıdaki
kalıcılık diliminin işidir; unit testler bunların çalıştığına kanıt değildir.

## Mevcut kodda doğrulanan iki engel

### 1. Konumlar makineye bağlı

`001_initial_schema.sql` içinde `master.layouts.machine_id` zorunludur;
`master.layout_slots` bir layout'a aittir. `ops.transport_requests.source_slot_id`
ve `destination_slot_id` yalnız bu tabloya FK taşır.

`TransportRules.ValidateSuppliedSlot` her iki uçta da talebin aynı makine/layout
bağlamını doğrular. `AllocateBufferSlotAsync` boş kap kaynağını aynı layout'un
EMPTY_STORAGE konumlarından seçer. Hazırlığın kaynağı henüz belli olmadan mevcut
BRING_EMPTY akışı ayrıca uygun buffer gerektirir.

Bu nedenle dört ortak alanı dokuz makinede çoğaltmak veya ortak fiziksel alanı
bir makineye aitmiş gibi kaydetmek doğru değildir. Mevcut constraint'i sessizce
gevşetmek de kapsamlı bir konum modeli değişikliğidir.

### 2. Dolu kasa getirme operasyonu yok

`TransportTaskType` yalnız BringEmpty, RemoveFull, RemoveEmpty içerir.
`TransportRules.CompletionState(BringEmpty)` makine konumunu OCCUPIED/EMPTY yapar.
RemoveFull ise makineden dolu kasayı çıkarır; yönü ters çevrilerek kullanılamaz.
`PostgresRdsExecutionStore.GetDispatchContextAsync` mevcut slot eşlemelerini
kullanır; yeni ortak hazırlık alanını otomatik olarak okuyamaz.

Mevcut `MarkReadyAsync` yalnız talebin readiness'ını ve olayını yazar; alan/palet
eşleştirmesi veya fiziksel kaynak değişikliği yapmaz.

## İnceleme için önerilen sonraki dilim — henüz uygulanmadı

1. Önceki migration'ları koruyan yeni forward migration ile makineden bağımsız
   dört mantıksal hazırlık alanı, palet/atama kayıtları ve hareket/olay geçmişi.
   Aynı alanda en fazla bir aktif palet ve bir talep için en fazla bir aktif
   atama DB benzersizlik kısıtlarıyla korunmalı. Alan numarası RDS etiketi değildir.
2. Boş kap ön hazırlığında request/machine/part ilişkisi bulunmayabilir; yarı mamul
   hazırlığında gerçek inbound talep ve onun müşteri bağlamı zorunlu olmalı.
   Tarih/saat, kullanıcı ve yetkili cihaz sunucudan gelmeli.
3. Dolu alt parça getirme için ayrı `BRING_FULL` operasyonu önerilir. Mevcut üç
   operasyonun anlamı korunmalı. Makine konumu talep oluşturulurken boş olmalı;
   hazırlık bekleyen talebin henüz bilinmeyen kaynağı uydurulmamalı. Sevk kapısı
   eksik/uyuşmayan kaynak veya hazırlık kanıtı varsa kapalı kalmalı.
4. Hazır onayı, palet–alan–talep eşleştirmesini ve readiness değişikliğini tek
   transaction'da yapmalı. Eski ready endpoint'i yeni atamayı atlayarak sevk
   açamamalı. Kaynak RDS eşlemesi ancak doğrulanmış konfigürasyondan gelmeli.
5. Retry anahtarı işlem, alan, palet/atama kimliği, talep ve içerik bağlamına
   bağlı olmalı. Eski bir ekranın yeni paleti onaylamasını engellemek için beklenen
   kayıt sürümü ve palet kimliği doğrulanmalı. Replay yeni olay/audit üretmemeli.
6. Hazır olmak alanı boşaltmamalı. Fiziksel alma kanıtı doğrulanmadan alan tekrar
   kullanıma açılmamalı; başarısız/belirsiz robot sonucu fiziksel durumu tahminle
   geri yüklememeli. İptal edilen talebin alandaki paleti de kendiliğinden yok
   sayılamaz. Bu lifecycle ayrıntıları implementation öncesi gözden geçirilmeli.
7. Mevcut workflow kilitleriyle ortak alan kilitleri için tek sıra belirlenip
   claim/ready/cancel/dispatch yarışları gerçek geçici PostgreSQL üzerinde
   sınanmalı. Domain validator concurrency mekanizması değildir.

Bu öneri yeni tablo/operasyon ve execution sözleşmesi gerektirdiği için bu turda
migration, adapter veya API davranışı değiştirilmedi. Sahte müşteri/RDS kaynağı,
aktif sistem migration'ı ve robot testi yapılmadı.

## Doğrulama

Yeni hazırlık kural testleri: **44/44 PASS**. Mevcut readiness/buffer testleriyle
birlikte etkilenen üç sınıf: **72/72 PASS**, atlanan 0. Yeni dosyalarda
`dotnet format --verify-no-changes` ve `git diff --check`: PASS.

```text
dotnet test tests/TkgPda.Domain.Tests/TkgPda.Domain.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WarehousePreparationRulesTests|FullyQualifiedName~TransportReadinessDomainTests|FullyQualifiedName~TransportEmptyContainerBufferDomainTests"
```

Domain/test projeleri Release derlendi. Tam backend derlemesi, HTTP/PostgreSQL,
Flutter veya robot testleri bu turda çalıştırılmadı; çalışma zamanına bağlı bir
özellik tamamlandığı iddia edilmez.
