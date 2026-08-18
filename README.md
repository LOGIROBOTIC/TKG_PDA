# TKG PDA

TKG PDA, TKG Otomotiv'deki dokuz üretim makinesi/hücresi için AMR lojistik taleplerini yönetecek sistemin veri ve iş akışı temelidir. Bu repository; PostgreSQL veri mimarisini, müşteri Excel'lerinden üretilen izlenebilir master data'yı, normalize fiziksel yerleşimleri ve bunların doğrulama araçlarını içerir.

> Baseline durumu: Phase 01, 01.1, 02A ve 02B kabul edildi. **Phase 03 — Business Workflow Engine `agent/phase-03-workflow-engine` dalında uygulanmış ve review beklemektedir.** RDS/SEER entegrasyonu, HTTP API, UI, PLC, robot koordinatları, rota ve navigation profile üretimi bu phase'in kapsamında değildir.

## Mevcut baseline

| Katman | Durum | Sonuç |
|---|---|---|
| Phase 01 — Database Architecture | Tamamlandı | `auth`, `master`, `ops`, `integration`, `audit` şemaları |
| Phase 01.1 — Schema Consistency | Tamamlandı | Nullable planning uniqueness ve ortak layout/profile ilişkileri doğrulandı |
| Phase 02A — Textual Master Data | Tamamlandı | 161 part, 10 container type, 51 product group, 191 planning row |
| Phase 02B — Physical Layout Extraction | Tamamlandı | 21 layout, 24 variant assignment, 183 display slot |
| Phase 03 — Business Workflow Engine | Review bekliyor | `BRING_EMPTY`, `REMOVE_FULL`, `REMOVE_EMPTY`, reservation, idempotency ve variant-switch koruması |
| Phase 04 — Backend API + Auth | Başlamadı | Phase 03 domain/application çekirdeğini kullanacak |

Phase 02B'deki 183 `master.layout_slots` kaydının 172'si lojistik pozisyon, 11'i `slot_role = 'MACHINE'` olan makine/hücre bölgesidir. Bu 11 bölge yalnızca fiziksel ve ekran yerleşim referansıdır; gelecekte taşıma isteğinin `source_slot_id` veya `destination_slot_id` değeri olamaz. Şema ayrımı mevcut `slot_role` alanıyla yapabiliyor; bu nedenle baseline migration değiştirilmemiştir. Phase 03 doğrulaması bu invariant'ı uygulama katmanında zorunlu kılmalıdır.

## Repository yapısı

```text
db/migrations/                 PostgreSQL baseline şeması
db/seeds/generated/            Deterministik Phase 02A ve 02B seed'leri
data/generated/                Normalize JSON/CSV, issue raporları ve review PNG'leri
tools/phase_02a/               Metinsel Excel extraction pipeline'ı
tools/phase_02b/               Fiziksel layout extraction pipeline'ı
tests/                         Unit/regression ve fresh PostgreSQL testleri
docs/                          Mimari, ERD, phase raporları ve referanslar
src/TkgPda.Domain/             Saf workflow kuralları ve state machine
src/TkgPda.Application/        API'den bağımsız application service/port tanımları
src/TkgPda.Infrastructure.../  Transactional PostgreSQL workflow store
TKG.Pda.slnx                   .NET 10 solution
```

Müşterinin ham Excel, CAD, STEP ve görsel dosyaları yerel kaynak paketinde tutulur ve Git'e alınmaz. Repository'deki üretilmiş kayıtlar kaynak dosya adı, sheet/hücre aralığı ve SHA-256 provenance bilgilerini korur.

## Veritabanını kurma

PostgreSQL 18.x üzerinde uygulama sırası:

```powershell
psql -v ON_ERROR_STOP=1 -f db/migrations/001_initial_schema.sql
psql -v ON_ERROR_STOP=1 -f db/seeds/generated/002a_customer_textual_master_data.sql
psql -v ON_ERROR_STOP=1 -f db/seeds/generated/002b_customer_physical_layouts.sql
```

Seed dosyaları idempotent tasarlanmıştır. İkinci uygulama mantıksal DB durumunu değiştirmemelidir.

## Pipeline ve testler

Python bağımlılıkları ilgili phase klasörlerindeki `requirements.txt` dosyalarındadır.

```powershell
python tools/phase_02a/pipeline.py --project-root .
python -m unittest discover -s tests/phase_02a -p "test_*.py" -v

python tools/phase_02b/pipeline.py --project-root .
python -m unittest discover -s tests/phase_02b -p "test_*.py" -v

pwsh tests/phase_02b/run_postgres_integration.ps1

dotnet test TKG.Pda.slnx --configuration Release
pwsh tests/phase_03/run_postgres_integration.ps1
```

Phase 02B pipeline'ı fiziksel metre/milimetre veya robot/map koordinatı üretmez. `ui_x`, `ui_y`, `ui_width`, `ui_height` yalnızca PDA ekranında kullanılacak 0–1 normalize gösterim geometrisidir.

## Dokümantasyon

- [Dokümantasyon indeksi](docs/README.md)
- [Kabul edilmiş proje baseline'ı](docs/PROJECT_BASELINE.md)
- [Database ERD](docs/database/ERD.md)
- [Phase 01 notları](docs/PHASE_01_NOTES.md)
- [Phase 02A notları](docs/PHASE_02A_NOTES.md)
- [Phase 02B notları](docs/PHASE_02B_NOTES.md)
- [Phase 03 workflow engine notları](docs/PHASE_03_NOTES.md)
- [Database specification v0.1](docs/TKG_PDA_DATABASE_SPEC_v0.1.md)

## Bilinen veri kalitesi sınırları

- Phase 02A: 159 izlenebilir data issue; bunların 155'i warning, 4'ü info seviyesindedir.
- Çözülmemiş container kodları: `(blank)`, `1284`, `1285`, `285C`, `ARABA`, `KCK`, `ORT`.
- Phase 02B: 28 slotta part ilişkisi, 80 slotta container ilişkisi çözülememiştir.
- 183 slotun fiziksel footprint ölçüsü güvenilir kaynakta verilmediği için `NULL` bırakılmıştır.
- `67115BN050-10-HAOS` canonical koddur; HMTR görünümü source discrepancy olarak korunur.
- SEALER V1/V2 ortak layout kullanır; V3 için kanıt bulunmadığından layout ataması yapılmamıştır.

Ayrıntılı kararlar ve issue dağılımları [PROJECT_BASELINE.md](docs/PROJECT_BASELINE.md) ile phase raporlarında yer alır.
