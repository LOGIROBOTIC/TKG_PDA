# TKG_SIM_MOVE commissioning task

`TKG_SIM_MOVE.task`, RDS simulator commissioning için kullanıcı tarafından sağlanan sabit görev tanımıdır.

- Robot: `sim_01`
- Rota: `AP15 → AP20`
- Aksiyonlar: yalnız `Wait`
- PLC / WorkSite mutation / fork-jack / LOAD / UNLOAD: yok
- Üretim iş akışı: değildir

Kullanıcının Downloads altındaki değişmemiş kaynak SHA-256 değeri `1066f25dbd0f8ff9f7ec274e91df8a48ffd99b42c57cc3f98ca5b1c94dc96866`'dır. Git'teki metin kopyası terminal LF içerdiği için byte hash'i `b1124c87dea21200a52438e081bf312bc3c70bdab87aa43bd0b8de73b16458ad` olur; JSON payload içeriği aynıdır.

Bu export task-level `inputParams` içermez; AP15/AP20 doğrudan bloklarda sabittir. TKG commissioning template'i `fromPoint=AP15` ve `toPoint=AP20` değerlerini gönderse de mevcut sabit task tanımı bu parametreleri tüketmez. Dinamik `fromPoint`/`toPoint` sürümü, ancak RDS'nin gerçek expression serializer çıktısı alındıktan sonra üretilebilir; bu serialization henüz kanıtlanmamıştır ve biçimi tahmin edilmez.

Bu task production `BRING_EMPTY`, `REMOVE_FULL` veya `REMOVE_EMPTY` workflow'u olarak yeniden kullanılmamalıdır. Production task commissioning sonraki ayrı faza ertelenmiştir.

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
