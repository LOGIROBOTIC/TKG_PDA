# TKG PDA — Phase 10A Dynamic RDS Wind Task Contract

**Tarih:** 2026-08-24

**Kapsam:** RDS-native dynamic Wind Task input serializer doğrulaması ve simulator E2E

**DB migration:** Yok

**Production forklift workflow:** Başlatılmadı

## Sonuç

Phase 10A **LIVE ACCEPTANCE PENDING**. Tüm artifact, regression ve statik doğrulamalar geçti. Canlı RDS doğrulaması bekliyor.

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

## Manuel RDS UI kabulü

**LIVE ACCEPTANCE PENDING — Bu bölüm canlı koşu sonrasında doldurulacak.**

## TKG E2E kabulü

**LIVE ACCEPTANCE PENDING — Bu bölüm canlı koşu sonrasında doldurulacak.**

## Idempotency

**LIVE ACCEPTANCE PENDING**

## WorkSite kanıtı

**LIVE ACCEPTANCE PENDING**

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

**LIVE ACCEPTANCE PENDING**

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
