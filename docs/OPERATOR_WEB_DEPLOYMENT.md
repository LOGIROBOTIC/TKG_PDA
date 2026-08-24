# TKG Operator Web/PWA Deployment

Bu rehber Windows üretim hattı dokunmatik terminallerinde Flutter Web/PWA operator uygulamasını yayınlama ve işletme adımlarını tanımlar. Customer credential, production IP veya secret içermez.

## 1. Production build

Flutter 3.44.8 ile repository kökünden:

```powershell
cd src/TkgPda.Pda
flutter pub get
flutter build web --release --no-web-resources-cdn --base-href /operator/ --output build/operator
```

Çıktı `src/TkgPda.Pda/build/operator/` altındadır ve Git'e alınmaz. CI/CD bu klasörün içeriğini static host'ta `/operator/` altına yayınlar. Root deployment gerekiyorsa `--base-href /` kullanılmalıdır.

Build sonrası gerçek HTTP kontrolü yapılmalıdır; `file://` geçerli PWA kabulü değildir:

```powershell
python -m http.server 8088 --bind 127.0.0.1 --directory build
# http://127.0.0.1:8088/operator/
```

Build; CanvasKit/WASM, Material/Cupertino fontları, Flutter fallback fontları, ikonlar ve uygulama kodunu yerel sunar. Runtime public CDN/Internet gerektirmez.

## 2. Static hosting ve routing

Host şu kuralları sağlamalıdır:

- `/operator/` altındaki dosyaları doğru MIME tipleriyle sunar (`.js`, `.wasm`, `.json`, `.woff2`);
- HTTPS ve güncel TLS kullanır;
- hash/fragment tabanlı Flutter routing'i korur;
- bilinmeyen operator navigation isteklerini `/operator/index.html` dosyasına fallback eder;
- `operator_service_worker.js` için güncelleme kontrolüne izin verir;
- `manifest.json` ve ikonları `/operator/` scope'unda sunar.

Uygulama mevcut hash route stratejisini kullandığı için normal `#/login`, `#/machines` ve `#/operation` yenilemeleri static server'da server-side path üretmez. Yine de `/operator/` fallback'i deployment hatalarına karşı önerilir.

## 3. Same-origin önerisi

Tercih edilen topoloji:

```text
https://tkg.example/operator/  → Flutter static files
https://tkg.example/api/       → ASP.NET Core reverse proxy
```

`web/operator-config.json` içindeki `apiBaseUrl` boş olduğunda Flutter browser origin'ini kullanır. Reverse proxy `/api/` çağrılarını TKG API'ye yönlendirir. JWT cookie değildir; `Authorization: Bearer` ve `X-Device-Code` merkezi Flutter client tarafından gönderilir.

## 4. Ayrı API origin'i

Ayrı API host gerekiyorsa deploy edilmiş `operator-config.json` secret içermeyen exact origin ile değiştirilebilir:

```json
{
  "apiBaseUrl": "https://api.example.invalid"
}
```

Alternatif build-time yaklaşım:

```powershell
flutter build web --release --no-web-resources-cdn --base-href /operator/ --output build/operator `
  --dart-define=TKG_API_BASE_URL=https://api.example.invalid
```

Build define runtime config'den önceliklidir. API tarafında yalnız gerekli Web origin'i eklenir:

```json
{
  "Cors": {
    "AllowedOrigins": ["https://operator.example.invalid"]
  }
}
```

Wildcard, `AllowAnyOrigin`, cookies ve `AllowCredentials` kullanılmaz.

## 5. PWA installation

Edge/Chrome'da HTTPS operator URL'si açılır. Manifest ve service worker yüklendikten sonra browser'ın **Uygulamayı yükle / Install app** komutuyla `TKG Operator` kurulur. Installed PWA standalone pencere açar; yeni authentication yöntemi veya kiosk bypass sağlamaz.

İlk açılış:

1. **Cihaz Kurulumu** ekranında Admin Web'de önceden provision edilmiş Device Code girilir.
2. Örnek terminal kimliği `SH01A-TERM01`, display name `SH-01-A Hat Dokunmatik Terminali` olabilir.
3. Backend device aktif olmalı ve kullanıcı/makine yetkileri `SH-01-A` ile sınırlandırılmalıdır.
4. Operatör kişisel kullanıcı/parolasıyla login olur.

Device Code browser origin'ine bağlıdır. Host/origin değişirse terminal yeniden provision edilir. JWT her refresh/browser restart sonrasında kaybolur ve login tekrar gerekir.

## 6. Edge kiosk/fullscreen

PWA kurulumundan sonra Windows startup veya kurumun device-management aracı, installed PWA ya da Edge kiosk URL'sini başlatabilir. Örnek yaklaşım:

```text
msedge.exe --kiosk https://operator.example.invalid/operator/ --edge-kiosk-type=fullscreen
```

Komut production URL/kurum politikasına göre deployment ekibi tarafından uygulanır. Phase 09 geliştirici bilgisayarının global startup, browser policy veya OS güvenlik ayarlarını değiştirmez.

Kiosk değerlendirmesi:

- Edge/Chrome güvenlik güncellemeleri kontrollü maintenance penceresinde uygulanır;
- otomatik login veya saved password açılmaz;
- terminal tek uygulama penceresi kullansa da backend permission otoritesi değişmez;
- kiosk'tan çıkış yöntemi tesis IT politikasıyla korunur;
- dokunmatik kalibrasyon, ekran ölçekleme ve uyku politikası sahada kabul edilir.

## 7. Terminal replacement ve re-provisioning

Terminal değiştirilirken Admin Web'de eski device devre dışı bırakılır ve yeni terminal için yeni/uygun Device Code provision edilir. Uygulamada **Cihazı Değiştir / Yeniden Yapılandır** seçilir, onay verilir ve yeni kod girilir. Bu işlem oturum/operasyon cache'ini temizler; backend cihaz kaydını kendi kendine oluşturmaz.

Yanlış provision durumunda aynı deliberate akış kullanılır. Local browser storage doğrudan elle düzenlenmemelidir.

## 8. Cache, update ve rollback

Service worker yalnız static uygulama shell'ini cache'ler. API request/response, JWT ve mutation queue cache'lenmez. Yeni release:

1. Versioned static dosyaları ve en son `operator_service_worker.js` atomik olarak yayınlayın.
2. Staging'de hard refresh ve normal reload ile yeni worker aktivasyonunu doğrulayın.
3. Manifest, ikon, `main.dart.js`, local CanvasKit/WASM ve fallback font isteklerinin 200/304 döndüğünü kontrol edin.
4. Public CDN ve doğrudan RDS isteği olmadığını browser network panelinden doğrulayın.
5. Terminal pencerelerini kontrollü biçimde yeniden başlatın; kullanıcılar tekrar login olur.

Rollback için önceki onaylı static artifact bütünüyle geri yayınlanır; kısmi dosya karışımı yapılmaz. Service worker cache adı/release varlıkları önceki artifact ile uyumlu tutulur. Rollback DB migration gerektirmez; Phase 09 migration eklemez.

## 9. LAN ve güvenlik checklist'i

- Flutter Web host ve TKG API terminalden erişilebilir.
- Public Internet kapalıyken uygulama shell'i ve login ekranı yüklenir.
- Production HTTPS sertifika zinciri terminalce güvenilir.
- Device Code yalnız `tkg.pda.device-code` key'inde bulunur.
- JWT, password, user/context ve command browser storage'da yoktur.
- `/api/` yalnız TKG API'ye gider; RDS `:8080`, `script-api`, query/stop endpoint'leri browser'dan erişilmez.
- Offline iken transport/variant/load-state mutation çalışmaz ve replay kuyruğu yoktur.
- `TKG_SIM_MOVE` veya production RDS task commissioning bu deployment'ın parçası değildir.
