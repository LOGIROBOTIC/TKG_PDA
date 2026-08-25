# RDS simulator commissioning tasks

Bu dizindeki görevler yalnız development/commissioning kanıtıdır. Hiçbiri production `BRING_EMPTY`, `REMOVE_FULL` veya `REMOVE_EMPTY` workflow'u değildir.

## TKG_SIM_MOVE — Phase 08C frozen artifact

`TKG_SIM_MOVE.task`, RDS simulator commissioning için kullanıcı tarafından sağlanan sabit görev tanımıdır.

- Robot: `sim_01`
- Rota: `AP15 → AP20`
- Aksiyonlar: yalnız `Wait`
- PLC / WorkSite mutation / fork-jack / LOAD / UNLOAD: yok
- Üretim iş akışı: değildir

Kullanıcının Downloads altındaki değişmemiş kaynak SHA-256 değeri `1066f25dbd0f8ff9f7ec274e91df8a48ffd99b42c57cc3f98ca5b1c94dc96866`'dır. Git'teki metin kopyası terminal LF içerdiği için byte hash'i `b1124c87dea21200a52438e081bf312bc3c70bdab87aa43bd0b8de73b16458ad` olur; JSON payload içeriği aynıdır.

Bu export task-level `inputParams` içermez; AP15/AP20 doğrudan bloklarda sabittir. TKG commissioning template'i `fromPoint=AP15` ve `toPoint=AP20` değerlerini gönderse de sabit task tanımı bu parametreleri tüketmez. Bu tarihsel artifact değiştirilmez veya dinamik sözleşme kanıtı olarak yorumlanmaz.

## TKG_SIM_DYNAMIC_MOVE — Phase 10A dynamic artifact

`TKG_SIM_DYNAMIC_MOVE.task`, Phase 10A sırasında RDS Wind Task UI'sinde oluşturulmuş ve RDS'nin `/api/windTaskdef/batchExportTask` akışıyla dışa aktarılmış canonical artifact'tir.

- Task definition ID: `1c434014-e1c0-4237-8724-e670972d905f`
- Version: `2`
- Task-level input: `from : String`, required
- Task-level input: `to : String`, required
- Select Robot `keyRoute`: `Expression / taskInputs.from`
- İlk Robot Universal Action `targetSiteLabel`: `Expression / taskInputs.from`
- İkinci Robot Universal Action `targetSiteLabel`: `Expression / taskInputs.to`
- Aksiyonlar: yalnız `Wait`
- Sabit robot değeri: yok
- PLC / WorkSite mutation / fork-jack / LOAD / UNLOAD: yok
- RDS export SHA-256: `49abed5e2f36feadeb2ad7b8e9775e095b1055b75599c8b36a1486cf7d0de1d3`

Blok adlarındaki tarihsel `sim_01`, `ap15` ve `ap20` metinleri kopyalanan editör etiketleridir; çalıştırma parametresi değildir. Artifact testleri gerçek `inputParams` alanlarında sabit robot veya AP değeri bulunmadığını doğrular.

Phase 10A canlı kabul harness'i validation-only çağrısını görev oluşturmadan doğrular ve fresh geçici PostgreSQL üzerinde TKG yönünü `AP20 → AP15` olarak bağlar:

```powershell
$env:Rds__Enabled = 'true'
$env:Rds__DispatchEnabled = 'true'
$env:Rds__IntegrationKey = '<secret>'
$env:Rds__CallbackKey = '<secret>'
$env:TKG_PDA_PHASE10A_SIMULATOR_SCENE_CONFIRMED = 'true' # RDS UI ile yeniden doğrulandıktan sonra
pwsh tests/phase_10a/run_live_dynamic_rds_e2e.ps1
```

Production task commissioning sonraki ayrı faza ertelenmiştir. AP15/AP20 birer Core navigation location'ıdır; TKG layout slot'u, RDS physical WorkSite'ı veya fork pickup geometry kanıtı değildir.

## Phase 08C historical harness

Canlı kabul harness'i:

```powershell
$env:Rds__Enabled = 'true'
$env:Rds__DispatchEnabled = 'true'
$env:Rds__IntegrationKey = '<secret>'
$env:Rds__CallbackKey = '<secret>'
$env:TKG_PDA_PHASE08C_SIMULATOR_SCENE_CONFIRMED = 'true' # RDS UI ile yeniden doğrulandıktan sonra
pwsh tests/phase_08c/run_live_rds_simulator_e2e.ps1
```

Harness yalnız loopback RDS, port 5000 callback, fresh geçici PostgreSQL cluster ve development simulator task'ı ile çalışır. Secret değerleri repoya yazılmaz.
