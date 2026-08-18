# TKG PDA Dokümantasyon İndeksi

Bu dizin, kabul edilmiş Phase 01–04 baseline'ının mimari kararlarını, veri üretim zincirini ve doğrulama kanıtlarını içerir. Phase 05 Admin Configuration API feature branch üzerinde uygulanmış ve review beklemektedir.

## Başlangıç noktaları

- [PROJECT_BASELINE.md](PROJECT_BASELINE.md) — mevcut kapsam, sayılar, kararlar, riskler ve phase durumu
- [database/ERD.md](database/ERD.md) — GitHub uyumlu Mermaid mimari ve ER diyagramları
- [TKG_PDA_DATABASE_SPEC_v0.1.md](TKG_PDA_DATABASE_SPEC_v0.1.md) — özgün database specification

## Uygulama ve doğrulama raporları

- [PHASE_01_NOTES.md](PHASE_01_NOTES.md) — ilk şema ve Phase 01.1 tutarlılık düzeltmeleri
- [PHASE_02A_NOTES.md](PHASE_02A_NOTES.md) — textual master data pipeline'ı ve tam data issue raporu
- [PHASE_02B_NOTES.md](PHASE_02B_NOTES.md) — fiziksel layout extraction, review görüntüleri ve integration test sonucu
- [PHASE_03_NOTES.md](PHASE_03_NOTES.md) — transport state machine, reservation/concurrency ve idempotency davranışı
- [PHASE_04_NOTES.md](PHASE_04_NOTES.md) — JWT, RBAC, cihaz bağlama, machine permission ve HTTP sözleşmesi
- [PHASE_05_ADMIN_CONFIGURATION_API.md](PHASE_05_ADMIN_CONFIGURATION_API.md) — Admin Web auth, provisioning, configuration lifecycle, concurrency ve audit
- [Seed workflow](../db/seeds/README.md) — deterministik seed üretme ve DB'ye uygulama sırası

## Referanslar

- [Customer reference policy](references/customer/README.md) — ham kaynakların kullanım ve değişmezlik kuralları
- [RDS API reference](references/rds/RdsApi_en.md) — sonraki entegrasyon aşamaları için müşteri tarafından sağlanan API dokümanı

## Canonical dosyalar

- Şema: `db/migrations/001_initial_schema.sql`
- Phase 02A seed: `db/seeds/generated/002a_customer_textual_master_data.sql`
- Phase 02B seed: `db/seeds/generated/002b_customer_physical_layouts.sql`
- Normalize çıktı: `data/generated/phase_02a/` ve `data/generated/phase_02b/`

Yerel çalışma klasöründe bulunabilecek eski kök-level prompt/specification/migration kopyaları Git'e alınmaz. Geliştirme ve yayın için yalnızca yukarıdaki canonical yollar kullanılmalıdır.
