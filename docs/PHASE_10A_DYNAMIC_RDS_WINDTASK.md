# TKG PDA — Phase 10A Dynamic RDS Wind Task Contract

**Tarih:** 2026-08-24

**Kapsam:** RDS-native dynamic Wind Task input serializer doğrulaması ve simulator E2E

**DB migration:** Yok

**Production forklift workflow:** Başlatılmadı

## Sonuç

Phase 10A **LIVE ACCEPTANCE PASSED / COMPLETE**. Tüm artifact, regression, statik doğrulamalar ve canlı simulator E2E kabulü geçti. Backend-driven reverse-direction (`AP20 → AP15`) dinamik Wind Task kabulü `2026-08-29` tarihinde canlı simulator ortamında doğrulandı.

Phase 10A **kanıtlar**:

- dinamik Wind Task `from`/`to` girdileri (runtime `taskInputs.*` tüketimi)
- backend-driven dispatch (TKG uygulama akışı üzerinden)
- UUID kimliği (`TransportRequest UUID == RDS taskRecordId`)
- simulator yürütmesi (`1000 → 1003`)
- callback/reconciliation ile terminal durum uygulanması
- idempotency (`IDEMPOTENT_REPLAY`, aynı taskRecordId için tek instance)
- WorkSite immutability (digest değişmedi)

Phase 10A **kanıtlamaz** (bkz. [Phase sınırı](#phase-sınırı)): production `BRING_EMPTY` / `REMOVE_FULL` / `REMOVE_EMPTY`, fiziksel CBD15 hareketi, ForkLoad, ForkUnload, binTask, production WorkSite eşlemeleri, fork yükseklikleri, pickup/drop geometrisi, production recognition ayarları.

## Phase 08C sınırlılığı

Phase 08C, `TKG_SIM_MOVE` ile sabit `AP15 → AP20` hareketini kanıtladı. Bu görev tanımında task-level `inputParams` boştur — `from` ve `to` değerleri bloklarda doğrudan sabittir. TKG commissioning template'i `fromPoint=AP15` ve `toPoint=AP20` gönderse de bu parametreler task tarafından tüketilmedi. Dinamik Wind Task expression serialization biçimi **kanıtlanmamıştı**.

## Yeni görev tanımı

| Alan | Değer |
|---|---|
| Label | `TKG_SIM_DYNAMIC_MOVE` |
| Task definition ID | `1c434014-e1c0-4237-8724-e670972d905f` |
| Version | `2` |
| Remark | Development-only dynamic simulator commissioning. Not for production transport. |
| RDS export SHA-256 | `49abed5e2f36feadeb2ad7b8e9775e095b1055b75599c8b36a1486cf7d0de1d3` |
| Export boyutu | 1744 bytes |

### Task-level input parametreleri

| Parametre | Tip | Zorunlu | Varsayılan |
|---|---|---|---|
| `from` | `String` | evet | boş |
| `to` | `String` | evet | boş |

### RDS-native expression serializer

```text
RootBp
└── CSelectAgvBp
    │  vehicle:  { type: "Simple", value: "", required: true }
    │  keyRoute: { type: "Expression", value: "taskInputs.from", required: true }
    ├── CAgvOperationBp
    │      targetSiteLabel: { type: "Expression", value: "taskInputs.from", required: true }
    │      scriptName:      { type: "Simple", value: "Wait" }
    └── CAgvOperationBp
           targetSiteLabel: { type: "Expression", value: "taskInputs.to", required: true }
           scriptName:      { type: "Simple", value: "Wait" }
```

- `from` → Select Robot `keyRoute` → ilk hareket hedefi
- `to` → ikinci hareket hedefi
- Sabit robot değeri yok (boş `vehicle`)
- Sabit AP değeri yok
- Yalnız `Wait` aksiyonları
- WorkSite mutation / PLC / fork / LOAD / UNLOAD yok

Blok adlarındaki tarihsel `b_select_sim_01`, `b_move_ap15` ve `b_move_ap20` metinleri kopyalanan editör etiketleridir; çalıştırma parametresi değildir.

## Artifact test sonucu

```text
node --test tests/phase_10a/rds_dynamic_task_contract.test.mjs
3/3 PASS
```

Testler doğruladı: exact byte hash, label, ID, version, from/to String required, taskInputs.from/to expression, Select Robot, iki Robot Universal Action, Wait only, sabit robot yok, sabit AP yok, WorkSite/PLC/fork/load/unload yok, secret yok.

## Canlı kabul (2026-08-29)

Kabul yolu, mevcut TKG uygulama akışıdır: `TransportRequest → QUEUED → integration.rds_tasks → RdsExecutionWorker → RdsExecutionCoordinator → RdsExecutionClient → /script-api/tkg/create-transport → jj.createWindTask → TKG_SIM_DYNAMIC_MOVE → simulator → callback/reconciliation → TKG terminal`. Doğrudan `jj.createWindTask` veya manuel RDS Execute kullanılmadı.

| Alan | Değer |
|---|---|
| TransportRequest UUID | `e6c7d609-adcf-4d15-a657-50d280ccba1f` |
| RDS taskRecordId | `e6c7d609-adcf-4d15-a657-50d280ccba1f` |
| Kimlik | **MATCH** |
| Yön | `AP20 → AP15` (Phase 08C `AP15 → AP20` hareketinin tersi) |
| RDS inputParams | `from = AP20`, `to = AP15` |
| Çözümlenen dinamik hedefler | birinci = `AP20`, ikinci = `AP15` |
| Atanan simulator | `sim_02` |
| RDS gözlenen | `1000 → 1003` |
| RDS final | `1003` |
| endedReason | `[RootBp]End Of Task` |
| Blok zinciri | `RootBp`, `CSelectAgvBp`, `CAgvOperationBp`, `CAgvOperationBp` — tümü `1003` |

### TKG E2E kabulü

| Alan | Değer |
|---|---|
| TKG gözlenen | `QUEUED → SENT_TO_RDS → RUNNING → COMPLETED` |
| TKG final | **COMPLETED** |
| Authenticated callback | gözlendi (sayı = 1) |
| Callback sonucu | `RDS_CALLBACK_APPLIED` |
| Terminal tamamlanma kaynağı | **CALLBACK** |

### Idempotency

| Alan | Değer |
|---|---|
| Aynı taskRecordId retry | `IDEMPOTENT_REPLAY` |
| Bu taskRecordId için RDS instance sayısı | **1** |
| TKG request sayısı / RDS projection sayısı | 1 / 1 |

> Not: RDS'te farklı taskRecordId'lere sahip tarihsel `TKG_SIM_DYNAMIC_MOVE` görevleri idempotency ile ilgisizdir; kabul kriteri **bu koşunun kesin taskRecordId'si** için instance sayısının 1 olmasıdır.

### WorkSite kanıtı

| Alan | Değer |
|---|---|
| WorkSite sayısı (önce) | 22 |
| WorkSite sayısı (sonra) | 22 |
| Digest (önce) | `5807fe6a5b41446e6015036340531372e7482fda3dd83a34f77eae2b9ffbe349` |
| Digest (sonra) | `5807fe6a5b41446e6015036340531372e7482fda3dd83a34f77eae2b9ffbe349` |
| WorkSite mutasyonu | **YOK** |

### Acceptance-harness terminal-wait notu

İlk canlı koşu 180 saniyelik terminal bekleme penceresiyle çalıştı ve ters `AP20 → AP15` rotası bu pencere içinde tamamlanmadı; koşu, RDS görevi `1003`'e ulaşmadan önce geçici TKG API/PostgreSQL'i kapattı. Bu, bir **acceptance-harness terminal-wait-window sınırlamasıdır** — üründe veya RDS işlevselliğinde bir hata değildir (dinamik girdi tüketimi ve dispatch her iki koşuda da doğru çalıştı). Commissioning sırasında keşfedildi ve düzeltildi: `tests/phase_08c/run_live_rds_simulator_e2e.ps1` artık opsiyonel bir `TerminalTimeoutSeconds` parametresi alır (varsayılan **180** — Phase 08C davranışı değişmedi), ve `tests/phase_10a/run_live_dynamic_rds_e2e.ps1` bunu açıkça **600** olarak geçer. PowerShell 7, Phase 10A canlı harness'in desteklenen çalışma zamanıdır.

## Regression test sonuçları

| Suite | Sonuç |
|---|---:|
| .NET Release build | PASS, 0 warning / 0 error |
| Domain | 34/34 |
| Application | 58/58 |
| Auth | 14/14 |
| Phase 03 PostgreSQL | 25/25 |
| Phase 04 PostgreSQL/API | 19/19 |
| Phase 05 PostgreSQL/Admin API | 20/20 |
| Phase 08B PostgreSQL/API | 60 PASS + 1 live-opt-in skip |
| RDS script Node harness | 14/14 |
| Phase 10A artifact contract | 3/3 |
| Admin Web typecheck/lint/unit/build | PASS / PASS / 27/27 / PASS |
| Flutter format/analyze/test | no issues / 51/51 |
| Android debug APK | PASS |
| Flutter Web /operator/ build | PASS |

## Güvenlik kapıları

Canlı kabul, simulator-only ortamda gerçekleştirildi:

- Fiziksel robot bağlı değil; yalnız `sim_01` / `sim_02` (insan mimar tarafından RDS UI üzerinde doğrulandı).
- Dispatch yalnız açık opt-in ile etkin: `Rds__Enabled=true`, `Rds__DispatchEnabled=true`, `TKG_PDA_PHASE10A_SIMULATOR_SCENE_CONFIRMED=true`.
- `Rds__IntegrationKey` / `Rds__CallbackKey` yalnız process environment üzerinden sağlandı; repository'ye yazılmadı, log'lanmadı.
- WorkSite mutasyonu yok; production fork/load/unload davranışı yok; production WorkSite eşlemesi eklenmedi.
- Yalnız loopback HTTP RDS endpoint'i (`http://127.0.0.1:8080`) kabul edildi.

## Phase sınırı

Phase 10A burada durur. Sonraki ayrı çalışma, gerçek mapped physical RDS WorkSite'ları ve robot-modeline özgü aksiyonlarla production task'larını tasarlamalıdır.

### Henüz DEVREYE ALINMAMIŞ

- Fiziksel CBD15
- Production RDS WorkSite'ları
- Production slot → WorkSite ilişki kabulü
- ForkLoad / ForkUnload
- JackLoad / JackUnload
- binTask production davranışı
- BRING_EMPTY production Wind Task
- REMOVE_FULL production Wind Task
- REMOVE_EMPTY production Wind Task
- PLC
- SAP
