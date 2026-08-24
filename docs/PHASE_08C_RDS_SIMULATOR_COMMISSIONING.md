# TKG PDA — Phase 08C RDS Wind Task Commissioning + Simulator E2E

**Tarih:** 2026-08-24

**Kapsam:** development-only `TKG_SIM_MOVE` Wind Task doğrulaması ve tek kontrollü TKG → RDS simulator E2E

**DB migration:** Yok

**Production forklift workflow:** Başlatılmadı

## Sonuç

Phase 08C **PASS**. Phase 08B PR #8 `main`e merge edildi (`be76a1fe249bfa98f8d20f01b64604c1a1d22358`) ve commissioning çalışması `agent/phase-08c-rds-simulator-commissioning` dalında yürütüldü.

Canlı kabul zinciri:

```text
TKG TransportRequest 4cd854a5-924c-464c-9fa9-644034f87146
  → integration.rds_tasks / aynı UUID
  → /script-api/tkg/create-transport
  → jj.createWindTask
  → TKG_SIM_MOVE
  → sim_01 / AP15 → AP20 / Wait + Wait
  → RDS 1000 → 1003
  → authenticated taskDone callback
  → TKG RUNNING → COMPLETED
```

## Görev tanımı

Sağlanan dosyanın izlenebilir kopyası: `rds/tasks/commissioning/TKG_SIM_MOVE.task`

Değişmemiş Downloads kaynak SHA-256: `1066f25dbd0f8ff9f7ec274e91df8a48ffd99b42c57cc3f98ca5b1c94dc96866`

Git text kopyası SHA-256 (terminal LF dahil): `b1124c87dea21200a52438e081bf312bc3c70bdab87aa43bd0b8de73b16458ad`

RDS Wind Task listesinde aynı ID/label/remark ile bulunan tanım açılarak blok ağacı doğrulandı:

```text
RootBp
└── CSelectAgvBp: sim_01, key route AP15
    ├── CAgvOperationBp: AP15, Wait
    └── CAgvOperationBp: AP20, Wait
```

### Parametre ve yeniden kullanım sınırı

1. `TKG_SIM_MOVE`, sabit `AP15 → AP20` hareketini kullanır.
2. Export edilen RDS task tanımında task-level `inputParams` yoktur (`inputParams: []`).
3. TKG commissioning template'i `fromPoint=AP15` ve `toPoint=AP20` değerlerini gönderdi; ancak mevcut sabit RDS task tanımında bu parametrelere başvuran expression bulunmadığı için task bu değerleri tüketmez.
4. Dinamik Wind Task parametre expression serialization biçimi **henüz kanıtlanmamıştır**; serializer biçimi tahmin edilmedi ve task değiştirilmedi.
5. Bu commissioning task'ı production `BRING_EMPTY`, `REMOVE_FULL` veya `REMOVE_EMPTY` workflow'u olarak **yeniden kullanılmamalıdır**. WorkSite, binTask, fork/jack, `LOAD`, `UNLOAD`, PLC, SAP veya SEER map/profile/route işlemi içermez.
6. Production task commissioning sonraki ayrı faza ertelenmiştir.

## Güvenlik kapıları

Çalıştırma öncesi ve sonrası:

| Kontrol | Sonuç |
|---|---|
| Simulator robotlar | `sim_01`, `sim_02` |
| Robot grubu | `g2` |
| Dispatchable | İkisi de evet |
| Fiziksel robot | Yok |
| Aktif commissioning task, dispatch öncesi | 0 |
| AP15 / AP20 | Canlı Monitor haritasında mevcut ve bağlı rota üzerinde |
| RDS task label validation | `VALIDATED`, `taskLabelExists=true` |
| WorkSite mutation | Yok |

Script editörü güvenlik incelemesinde görünür hale gelen development key çiftleri derhal iptal edildi. Canlı kabulte kullanılan son integration/callback anahtarları yalnız process/RDS runtime belleğinde tutuldu; repoya, dokümana veya test çıktısına yazılmadı.

## Manuel RDS UI kabulü

RDS UI üzerinden bir kez çalıştırılan manuel görev:

| Alan | Kanıt |
|---|---|
| Task record | `30e7f8aa-3d5e-4f59-bb84-0b998f879d5d` |
| Label | `TKG_SIM_MOVE` |
| AGV | `sim_01` |
| Son durum | `Finished` / `1003` |
| Süre | 54 saniye |
| Ended reason | `[RootBp]End Of Task` |
| Fiziksel sonuç | sim_01 AP15 üzerinden AP20'ye ulaştı |

## TKG E2E kabulü

Fresh PostgreSQL 18.3, baseline migration/seeds ve gerçek ASP.NET Core API ile tam olarak bir TKG talebi oluşturuldu.

| Alan | Sonuç |
|---|---|
| TransportRequest UUID | `4cd854a5-924c-464c-9fa9-644034f87146` |
| RDS taskRecordId | `4cd854a5-924c-464c-9fa9-644034f87146` |
| Kimlik eşitliği | PASS |
| TKG durumları | `QUEUED → RUNNING → COMPLETED` |
| RDS durumları | `1000 → 1003` |
| Label | `TKG_SIM_MOVE` |
| AGV | `sim_01` |
| Ended reason | `[RootBp]End Of Task` |
| Callback audit | 1 × `RDS_CALLBACK_APPLIED` |
| TKG request adedi | 1 |
| TKG RDS projection adedi | 1 |
| RDS instance adedi / UUID | 1 |

RDS block kanıtı: `RootBp`, `CSelectAgvBp` ve iki `CAgvOperationBp` kaydının tamamı `1003` ile bitti.

## Idempotency

- Aynı HTTP idempotency key + aynı payload, yeni TKG talebi üretmeden aynı UUID'yi döndürdü.
- Aynı RDS `taskRecordId` ile `/script-api/tkg/create-transport` tekrar çağrısı `IDEMPOTENT_REPLAY` ve `idempotentReplay=true` döndürdü.
- Retry sonrasında RDS task instance sayısı aynı UUID için tam olarak 1 kaldı.

## WorkSite kanıtı

Commissioning görevi WorkSite kullanmadı. RDS WorkSite projection alanları (`siteId`, `filled`, `content`, `locked`, `disabled`, `syncFailed`) test öncesi/sonrası sıralı digest ile karşılaştırıldı:

| Alan | Önce | Sonra |
|---|---:|---:|
| WorkSite adedi | 22 | 22 |
| SHA-256 | `d89af98f38fba35fbb4edc24254aaf853e42fff4a211a15ff033255fb2c46463` | aynı |

Sonuç: WorkSite state değişmedi.

## Harness ve temizlik

Canlı kabul script'i `tests/phase_08c/run_live_rds_simulator_e2e.ps1`:

- yalnız loopback `http://127.0.0.1:8080` RDS'yi kabul eder;
- RDS UI'da yalnız `sim_01/sim_02` bulunduğuna dair ayrı, açık simulator-scene opt-in'i ister;
- callback portunu açıkça `5000` ile sınırlar;
- fresh PostgreSQL 18.3 cluster oluşturur;
- `001 + 002a + 002b` yükler;
- geçici identity, runtime state ve commissioning-only task template oluşturur;
- gerçek API login/transport boundary'sini kullanır;
- terminal/callback/query/idempotency/WorkSite değişmezliği kanıtını doğrular;
- sonunda API'yi, PostgreSQL'i ve geçici klasörü temizler.

İlk üç harness denemesi dispatch öncesinde sırasıyla client encoding, PowerShell chain ve çözüm dosyası adı hatalarında fail-closed durdu; hiçbirinde RDS create çağrısı yapılmadı. Başarılı koşu tek TKG TransportRequest üretti.

İkinci disposable cancellation görevi opsiyonel olduğu için çalıştırılmadı. Zorunlu create/callback/reconciliation/idempotent-retry kabulü temiz biçimde geçtiği için yeni robot hareketi eklenmedi.

## Phase sınırı

Phase 08C burada durur. Sonraki ayrı çalışma, gerçek mapped physical RDS WorkSite'ları ve robot-modeline özgü aksiyonları kullanarak üretim task'larını tasarlamalıdır:

- `BRING_EMPTY`
- `REMOVE_FULL`
- `REMOVE_EMPTY`

Gerçek forklift `LOAD/UNLOAD`, WorkSite/binTask, SEER navigation, PLC ve SAP bu fazda başlamamıştır.
