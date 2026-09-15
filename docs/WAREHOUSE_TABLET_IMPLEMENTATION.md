# Üretim odaklı operatör ve Android depo tableti

Durum: geliştirme başladı; bu belge tüm depo uygulamasının tamamlandığı anlamına gelmez.
Başlangıç: `9043d4081c35e4142b66ebc4ca0226e94e373713`.
Çalışma dalı: `codex/warehouse-operator-foundation`.

## Kullanıcının teyit ettiği kurallar

- Depo için ayrı Android tablet uygulaması; sicil numarası ve yetkilendirilmiş cihaz ile giriş.
- Makine operatörü üretilecek ana parçayı seçer. Miktar girmez; standart paketleme müşteri konfigürasyonundan gelir. Eksik lot/adet bilgisi uydurulmaz.
- Üretim ekranının odağı seçilen ana ürün ve onunla ilişkili parçalardır. RELATED, ALT_PART ile eş anlamlı değildir. İlişki kayıtlarından otomatik BOM/üretim reçetesi çıkarılmaz.
- Üretime başla/bitir kayıtları isteniyor; bunlar taşıma görevini tamamlamak, robotu durdurmak veya tüm kasaları kaldırmak değildir.
- Üretimi Bitir, makinede açık taşıma varsa engellenecek; talepler tamamlanmalı veya mevcut kurallarla ayrıca iptal edilmelidir.
- Dört hazırlık alanı bütün makineler için ortaktır. Depo, isteyen makineyi, parçayı, istenen boş/dolu içeriği, hazırlanacak taşıma kabını ve talep zamanını görür.
- **Dört ortak alanın her biri aynı anda en fazla bir palet tutar.** Talep için hazırlanan palet tek talebe, tek parça türüne ve tek hedef makineye bağlıdır. Depo operatörü gelen talebe göre paleti hazırlayıp boş alanlardan birine yerleştirir ve Hazır olarak işaretler. Aynı alanda farklı siparişler veya farklı parça türleri karıştırılmaz; diğer alanlarda başka talepler hazırlanabilir. Alanlar belirli bir ürüne veya makineye kalıcı olarak bağlanmaz. Ayrı alanlarda aynı parça türünün bulunmasını yasaklayan bir benzersizlik kuralı yoktur.
- Boş palet/sepet/kasa ve dolu alt parça kasası hazırlanabilir.
- Boş kasa/palet/sepet talep gelmeden alana yerleştirilebilir. Yarı mamul/alt parça kasası ise ancak ilgili talep geldikten sonra hazırlanıp yerleştirilir; talepsiz yarı mamul ön hazırlığı yapılmaz. Diğer içerik türlerine kendiliğinden aynı izin verilmez.
- **Boş kasayı alma ve yeni dolu kasayı getirme ayrı taleplerdir.** Otomatik değişim zinciri veya tek birleşik talep yaratılmayacak.
- **Yeni dolu kasa talebi, makine tarafındaki ilgili boş kasa kaldırılmadan açılamaz.** Kullanıcı bekleyen dolu kasa talebi senaryosunu seçmemiştir. Boş kasayı kaldırma talebinin oluşturulmuş/gönderilmiş olması tek başına yeterli değildir; ilgili fiziksel konumun boşaldığı mevcut workflow üzerinden doğrulanmalıdır. Bu karar tüm makinenin bütün konumlarının boş olması şartı anlamına gelmez.
- **Hazırlandı onayı**, diğer güvenlik koşulları da sağlanınca otomatik dispatch'e uygunluk verir; kendisi teslimat veya başarı bildirimi değildir. İstemci RDS çağırmaz.
- Tablet ana sayfası Yarı Mamul / Hazırlık ve Bitmiş Ürün Deposu olarak ayrılacak.
- Doğrulanmış bitmiş ürün teslimatı depoya bildirilir. Operatör fiziksel olarak boşalttığı alanı bildirir. Görevin gönderilmiş olması teslimat değildir.
- Talep, ürün yerleştirme/alma ve alan boşaltma kayıtlarında sunucu zaman damgası, kullanıcı, cihaz ve konum ilişkisi gerekir.
- 5–10 saniyelik yenileme; olay kimliğine göre tekilleştirilmiş, farklı türlerde görsel/sesli uyarılar. Aynı olay her yenilemede yeniden çalmayacak.
- Bilgisayar/cihaz adı otomatik doldurma isteniyor; bulunan ad yetkilendirilmiş cihaz kimliğinin yerine geçmeyecek.

## Dilim 1 — uygulanan ekran düzeltmeleri

- Ana parça seçilince diğer ana parça seçenekleri gizlenir; açık bir Ana Parçayı Değiştir eylemiyle yeniden açılır. Ana parça için ilk seçim hâlâ tek dokunuştur.
- Ana ürün bağlamı ile taşıma istenen parça ayrı gösterilir. RELATED seçimi alternatif diye sunulmaz; yalnız ALT_PART ilişkisi alternatif olarak etiketlenir. Bilinmeyen/çelişen ilişkiler yeniden sınıflandırılmaz.
- İlgili/alternatif parça seçildikten sonra aynı ana ürün için talebe dönülebilir. Bu eylem kendi başına taşıma oluşturmaz.
- Gönderim sürerken veya sonucu belirsizken parça değiştirilemez; mevcut idempotency bağlamı korunur. Eski bir uyumluluk cevabı temizlenen seçimi geri getiremez.
- Depo hazırlığında Kaynak, Hedef ve Kasa Tipi satırları kaldırılır. Doğru kabın hazırlanabilmesi için API'nin verdiği kap kodu kimlik bilgisi olarak korunur; koddan palet/sepet tipi tahmin edilmez.
- İstenme tarih/saat/saniye ve bekleme süresi gösterilir. Eksik/geçersiz zaman için cihazın şimdiki zamanı uydurulmaz.
- Hazırlandı onayı otomatik taşıma sonucunu açıklar; bilinmeyen işlem türü için hazırlık onayı kapalıdır.

Bu dilim, mevcut istemci ve mevcut API üzerinde çalışır. Henüz ayrı Android depo APK'sı, üretim oturumu veya dört alanlı konum envanteri sağlamaz. Müşteri kaynakları, seed, migration, RDS adapter ve routing sözleşmeleri değiştirilmez.

## Dilim 2 — üretim oturumu backend'i

Üretim başlangıç/bitiş/aktif oturum API'si ve `006_production_sessions.sql` eklendi. Aynı makinede tek açık üretim, müşteri ana ürün/varyant doğrulaması, makine kilidi, idempotency, transaction içi audit ve açık taşıma varken bitirme engeli uygulanır. Ayrıntılar ve aşamalı geçiş sınırlamaları: [PRODUCTION_SESSION_API.md](PRODUCTION_SESSION_API.md).

Backend dilimini izleyen dilim 3 ile API Flutter düğmelerine bağlandı. Geçici PostgreSQL testlerinden sonra 15 Eylül 2026 kullanıcı onayıyla yerel veritabanı yedeklenip yalnız 006 uygulandı; mevcut 31 tablonun içeriği korundu. API ve operatör ekranı robot görev gönderimi kapalı olarak açıldı. Oturum kimliği göndermeyen legacy HTTP akışı korunur; üretim başlatmayı bütün istemcilere zorunlu kılan rollout henüz yapılmadı.

## Dilim 3 — üretim oturumunun operatöre bağlanması

Mevcut Flutter operatöründe Üretime Başla / Üretimi Bitir, aktif üretim geri yükleme, üretim boyunca ana ürün/varyant kilidi ve etkin ürün grubuyla sınırlı ilgili parça seçimi uygulandı. Normal saha UI'sı artık üretim başlamadan taşıma oluşturmaz. Bitiş açık talepler, gönderilmemiş sepet ve sonucu belirsiz gönderimler varken engellenir; hiçbir taşıma otomatik iptal edilmez. Tekli/çoklu taşıma üretim kimliğini gönderir; eski tablette kalan oturum sunucuda makine kilidi altında doğrulanır. Ayrıntılı sözleşme ve test sonuçları [PRODUCTION_SESSION_API.md](PRODUCTION_SESSION_API.md) içindedir.

Bu henüz ayrı depo Android uygulaması değildir. Cihaz kurulumu, dört ortak hazırlık alanı ve dolu alt parça besleme devam eden ayrı işlerdir.

## Sonraki backend dilimi — açık sözleşme ve şema ihtiyacı

Onaylı hazırlık kurallarının yan etkisiz domain uygulaması ve 72/72 etkilenen
domain testi tamamlandı. Bu kurallar henüz API/store'a bağlı değildir. Ortak
konumlar ve dolu kasa inbound operasyonu için mevcut koddan doğrulanan şema ve
sözleşme boşlukları, uygulanmamış sonraki dilim önerisiyle birlikte
[WAREHOUSE_PREPARATION_BACKEND_REVIEW.md](WAREHOUSE_PREPARATION_BACKEND_REVIEW.md)
içindedir. Hazır onayının çalışan backend entegrasyonu henüz tamamlanmadı.

Mevcut `ContainerActionResolutionPolicy`, EMPTY eylemini yalnız VACANT konumda BRING_EMPTY'ye çözümler. Bu yol, içinde malzeme bitmiş fakat fiziksel kasası duran konumu ve dolu alt parça beslemeyi karşılamaz. **Dolu malzeme getirme BRING_EMPTY olarak kaydedilmeyecek.** Yeni operasyonun domain, rezervasyon, idempotency, failure reconciliation ve adapter sözleşmesi birlikte gözden geçirilmelidir.

Mevcut `MarkReadyAsync`, talep readiness'ını değiştirir; seçilen hazırlık alanına ürün yerleştirme kaydı oluşturmaz. `slot_runtime_state.current_part_id` tek başına üretim oturumu ve ortak alan ürün hareket geçmişinin yerine geçmez.

İzleyen uygulama dilimleri:

1. Onaylı dolu malzeme besleme sözleşmesi; mevcut migration'ları değiştirmeden, gerekiyorsa gözden geçirilmiş ileri migration.
2. Ortak hazırlık/depo konumları ve ürün hareket geçmişi; atomik atama, rezervasyon, tekrarlı onay güvenliği, audit ve yetki kontrolleri.
3. Bu API'leri kullanan ayrı Android depo uygulaması, dört alan kartı, talep yan listesi ve depo ekranı.
4. Kapasite ve kuyruk bildirimleri; yeniden bağlanma, yinelenen bildirim, stale veri ve birden çok tablet yarış testleri.

Hazırlık kapasitesi kullanıcı tarafından dört alan ve alan başına bir palet olarak teyit edildi. Fiziksel konum etiketleri/RDS eşlemeleri, bitmiş ürün deposunun konum sayısı ve uyarı eşikleri henüz teyitli değildir. Bir paletin parça adedi, taşıma kabı ölçüleri ve robot pickup eşlemesi bu kapasite kararından türetilmez. Bu değerler uydurulmayacak. İşlem ekranı seçimi kalıcı bir üretim oturumuymuş gibi gösterilmeyecek.

15 Eylül 2026 ek kullanıcı kararları yukarıdaki iş kurallarına kaydedildi. Alan başına tek palet, talep öncesi boş kap hazırlığı, talebe bağlı yarı mamul hazırlığı ve boş kasa kaldırılmadan dolu kasa talebi açmama kuralları henüz yeni depo backend'ine uygulanmış değildir; bu kayıt bir uygulama/test tamamlanma raporu değildir.

## Dilim 1 doğrulaması (Flutter)

Bu dalda yalnız etkilenen Flutter testleri, analiz ve derleme çalıştırılır. Sahte repository kullanan testler canlı API/DB/RDS erişimi yapmaz. Hiçbir test, robot hareketini veya yeni depo akışının fiziksel kabulünü kanıtladığı şeklinde raporlanmaz.

Sonuçlar:

- `flutter analyze --no-pub`: PASS, sorun bulunmadı.
- Aşağıdaki etkilenen unit/widget testleri: **70/70 PASS**. Ana ürün/RELATED/ALT bağlamı, tekli ve çoklu talep, belirsiz gönderimde seçim koruması, geç gelen uyumluluk cevabı, depo hazırlığı, eksik zaman damgası ve bilinmeyen işlem koruması kapsanır. Ekran boyutu kontrolleri 1024×768, 800×1280 ve 390×844 için geçti.
- `flutter build apk --debug --no-pub`: PASS. Çıktı mevcut operatör uygulamasına aittir: `src/TkgPda.Pda/build/app/outputs/flutter-apk/app-debug.apk`. **Ayrı depo uygulaması değildir**; cihaza kurulmadı. Derlemede Java native-access ve Android SDK XML araç sürümü uyarıları görüldü; araç zinciri değiştirilmedi.
- `git diff --check`: PASS. Backend, migration ve seed dosyaları değiştirilmedi; müşteri kaynaklarına dokunulmadı.
- Canlı API/PostgreSQL/RDS entegrasyon testi, cihaz üzerinde kabul testi veya robot hareketi yapılmadı.

Test komutu (`src/TkgPda.Pda` dizininde):

```text
flutter test --no-pub test/widget/part_first_flow_test.dart test/widget/warehouse_preparation_flow_test.dart test/widget/multi_part_batch_flow_test.dart test/widget/operator_usability_phase3_test.dart test/widget/wp33_operator_ux_test.dart test/unit/part_selection_safety_test.dart test/unit/operator_integrity_phase1_test.dart --reporter expanded
```
