# TKG PDA Dokümantasyon İndeksi

Bu dizin, tamamlanmış Phase 01–09 baseline'ının mimari kararlarını, veri üretim zincirini ve doğrulama kanıtlarını içerir. Phase 08B güvenli RDS execution sınırını uygular; Phase 08C bu sınırı development-only `TKG_SIM_MOVE` ile canlı simulator üzerinde doğrular; Phase 09 aynı Flutter operator istemcisini Android ve Windows Web/PWA hedeflerinde dondurur.

## Başlangıç noktaları

- [PROJECT_BASELINE.md](PROJECT_BASELINE.md) — mevcut kapsam, sayılar, kararlar, riskler ve phase durumu
- [database/ERD.md](database/ERD.md) — GitHub uyumlu Mermaid mimari ve ER diyagramları
- [TKG_PDA_DATABASE_SPEC_v0.1.md](TKG_PDA_DATABASE_SPEC_v0.1.md) — özgün database specification
- [commissioning/CUSTOMER_ACCEPTANCE_AUDIT.md](commissioning/CUSTOMER_ACCEPTANCE_AUDIT.md) — kaynak/doğruluk ve Operator kabul matrisi
- [commissioning/CUSTOMER_SOURCE_RECONCILIATION.md](commissioning/CUSTOMER_SOURCE_RECONCILIATION.md) — tüm customer kaynakları için A/B/C gap uzlaştırması ve kanıt matrisi
- [commissioning/CUSTOMER_DATA_REQUEST.md](commissioning/CUSTOMER_DATA_REQUEST.md) — müşteri, saha ve RDS için açık bilgi talep listesi
- [WP_33_OPERATOR_UX_V2_SHARED_QUEUE.md](WP_33_OPERATOR_UX_V2_SHARED_QUEUE.md) — mantıksal kasa eylemleri, atomik Remove-All ve global Operator kuyruğu
- [commissioning/PRODUCTION_COMMISSIONING_CHECKLIST.md](commissioning/PRODUCTION_COMMISSIONING_CHECKLIST.md) — physical robot öncesi fail-closed devreye alma kapıları
- [commissioning/RDS_COMMISSIONING_DISCOVERY.md](commissioning/RDS_COMMISSIONING_DISCOVERY.md) — ayrı RDSCore yüzeyi, kapalı read allowlist'i ve commissioning karşılaştırması
- [commissioning/RDS_COMMISSIONING_SNAPSHOT_SCHEMA.md](commissioning/RDS_COMMISSIONING_SNAPSHOT_SCHEMA.md) — deterministik snapshot alanları ve redaction sözleşmesi
- [commissioning/RDS_FIRST_SNAPSHOT_RUNBOOK.md](commissioning/RDS_FIRST_SNAPSHOT_RUNBOOK.md) — ilk simulator/local/production salt-okunur RDSCore snapshot'ı için çalıştırılmamış güvenlik runbook'u
- [integration/RDSCORE_NATIVE_ORDER_CONTRACT.md](integration/RDSCORE_NATIVE_ORDER_CONTRACT.md) — kapalı Native Order portu, sealed order/block kimliği, response semantiği ve güvenli terminate sözleşmesi

## Uygulama ve doğrulama raporları

- [PHASE_01_NOTES.md](PHASE_01_NOTES.md) — ilk şema ve Phase 01.1 tutarlılık düzeltmeleri
- [PHASE_02A_NOTES.md](PHASE_02A_NOTES.md) — textual master data pipeline'ı ve tam data issue raporu
- [PHASE_02B_NOTES.md](PHASE_02B_NOTES.md) — fiziksel layout extraction, review görüntüleri ve integration test sonucu
- [PHASE_03_NOTES.md](PHASE_03_NOTES.md) — transport state machine, reservation/concurrency ve idempotency davranışı
- [PHASE_04_NOTES.md](PHASE_04_NOTES.md) — JWT, RBAC, cihaz bağlama, machine permission ve HTTP sözleşmesi
- [PHASE_05_ADMIN_CONFIGURATION_API.md](PHASE_05_ADMIN_CONFIGURATION_API.md) — Admin Web auth, provisioning, configuration lifecycle, concurrency ve audit
- [PHASE_06_ADMIN_WEB.md](PHASE_06_ADMIN_WEB.md) — React UI mimarisi, normalize layout editörü, hata UX'i ve frontend testleri
- [PHASE_07A_PDA_RDS_CONTRACT_READINESS.md](PHASE_07A_PDA_RDS_CONTRACT_READINESS.md) — PDA operational context, action ve persisted execution projection kabulü
- [PHASE_07B_FLUTTER_PDA.md](PHASE_07B_FLUTTER_PDA.md) — device-bound Flutter PDA mimarisi, operatör akışları, idempotency/offline güvenliği ve kabul kanıtı
- [PHASE_08A_LIVE_RDS_READINESS.md](PHASE_08A_LIVE_RDS_READINESS.md) — canlı salt-okunur RDS kanıtı, WorkSite aynası, admin mapping ve readiness
- [PHASE_08B_RDS_EXECUTION_INTEGRATION.md](PHASE_08B_RDS_EXECUTION_INTEGRATION.md) — script güvenliği, dispatch/reconciliation/callback/cancel, load-state ve canlı gate sonucu
- [PHASE_08C_RDS_SIMULATOR_COMMISSIONING.md](PHASE_08C_RDS_SIMULATOR_COMMISSIONING.md) — Wind Task doğrulaması, manuel simulator koşusu ve tek TKG UUID ile canlı E2E/idempotency kanıtı
- [PHASE_09_MULTIPLATFORM_OPERATOR.md](PHASE_09_MULTIPLATFORM_OPERATOR.md) — Android + Web/PWA ortak Flutter mimarisi, browser güvenliği ve gerçek E2E kanıtı
- [OPERATOR_WEB_DEPLOYMENT.md](OPERATOR_WEB_DEPLOYMENT.md) — `/operator/` build/hosting, PWA, kiosk, provisioning, update ve rollback rehberi
- [RDS_PDA_INTEGRATION_SPEC.md](RDS_PDA_INTEGRATION_SPEC.md) — Phase 08 için normative TKG/RDS/PDA sorumluluk ve entegrasyon sözleşmesi
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
