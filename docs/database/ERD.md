# TKG PDA Database ERD

Bu belge, `db/migrations/001_initial_schema.sql` ile kabul edilmiş Phase 01–02B baseline'ını gösterir. Okunabilirlik için model beş Mermaid diyagramına ayrılmıştır. Diyagramlarda teknik audit timestamp alanlarının bir bölümü gösterilmez; FK yönleri ve domain sınırları migration ile aynıdır.

> Phase 03 workflow engine kabul edilip `main` dalına alınmıştır. Phase 04 authenticated API mevcut `auth`, `ops` ve `audit` tablolarını migration değiştirmeden kullanır; workflow mutasyonlarını `TransportWorkflowService` üzerinden yürütür. `integration` tabloları hâlâ gelecekteki RDS akışı içindir; robot koordinatı, rota veya navigation profile üretilmez.

## 1. Şema sınırları

```mermaid
flowchart LR
    AUTH[auth<br/>users, roles, permissions]
    MASTER[master<br/>customer data and configuration]
    OPS[ops<br/>runtime state and transport requests]
    INTEGRATION[integration<br/>RDS boundary]
    AUDIT[audit<br/>devices and change history]

    AUTH --> MASTER
    AUTH --> OPS
    MASTER --> OPS
    MASTER --> INTEGRATION
    OPS --> INTEGRATION
    AUTH --> AUDIT
    OPS --> AUDIT
```

## 2. Kimlik, makine ve textual master data

```mermaid
erDiagram
    AUTH_USERS ||--o{ AUTH_USER_ROLES : receives
    AUTH_ROLES ||--o{ AUTH_USER_ROLES : grants
    AUTH_USERS ||--o{ AUTH_USER_MACHINE_PERMISSIONS : receives
    MASTER_MACHINES ||--o{ AUTH_USER_MACHINE_PERMISSIONS : scopes

    MASTER_MACHINES ||--o{ MASTER_VARIANTS : has
    MASTER_VARIANTS ||--o{ MASTER_PRODUCT_GROUPS : owns
    MASTER_PARTS ||--o{ MASTER_PRODUCT_GROUPS : is_main_part
    MASTER_PRODUCT_GROUPS ||--o{ MASTER_PRODUCT_GROUP_PARTS : contains
    MASTER_PARTS ||--o{ MASTER_PRODUCT_GROUP_PARTS : participates
    MASTER_CONTAINER_TYPES o|--o{ MASTER_PRODUCT_GROUP_PARTS : defaults

    MASTER_VARIANTS ||--o{ MASTER_PART_PLANNING_METRICS : scopes
    MASTER_PRODUCT_GROUPS o|--o{ MASTER_PART_PLANNING_METRICS : groups
    MASTER_PARTS ||--o{ MASTER_PART_PLANNING_METRICS : plans
    MASTER_CONTAINER_TYPES o|--o{ MASTER_PART_PLANNING_METRICS : uses

    AUTH_USERS {
        uuid id PK
        varchar username UK
        text password_hash
        boolean is_active
    }
    AUTH_ROLES {
        uuid id PK
        varchar code UK
        boolean is_system
    }
    AUTH_USER_ROLES {
        uuid user_id PK,FK
        uuid role_id PK,FK
    }
    AUTH_USER_MACHINE_PERMISSIONS {
        uuid user_id PK,FK
        uuid machine_id PK,FK
        boolean can_view
        boolean can_operate
        boolean can_change_variant
        boolean can_manage_slots
    }
    MASTER_MACHINES {
        uuid id PK
        varchar code UK
        varchar display_name
    }
    MASTER_VARIANTS {
        uuid id PK
        uuid machine_id FK
        varchar code
    }
    MASTER_PARTS {
        uuid id PK
        varchar part_code UK
        text description
    }
    MASTER_CONTAINER_TYPES {
        uuid id PK
        varchar code UK
        varchar category
        integer length_mm
        integer width_mm
        integer height_mm
    }
    MASTER_PRODUCT_GROUPS {
        uuid id PK
        uuid variant_id FK
        uuid main_part_id FK
        varchar name
    }
    MASTER_PRODUCT_GROUP_PARTS {
        uuid id PK
        uuid product_group_id FK
        uuid part_id FK
        uuid container_type_id FK
        numeric lot_quantity
    }
    MASTER_PART_PLANNING_METRICS {
        uuid id PK
        uuid variant_id FK
        uuid product_group_id FK
        uuid part_id FK
        uuid container_type_id FK
        numeric lot_quantity
        numeric agv_moves_per_day
        text source_note
    }
```

Planning satırlarında grup nullable'dır. Uniqueness iki partial index ile sağlanır:

- grup varsa `(variant_id, product_group_id, part_id)`,
- grup yoksa `(variant_id, part_id)`.

## 3. Layout, navigation ve task template modeli

```mermaid
erDiagram
    MASTER_MACHINES ||--o{ MASTER_VARIANTS : has
    MASTER_MACHINES ||--o{ MASTER_LAYOUTS : owns
    MASTER_MACHINES ||--o{ MASTER_NAVIGATION_PROFILES : owns

    MASTER_VARIANTS ||--o{ MASTER_VARIANT_LAYOUT_ASSIGNMENTS : selects
    MASTER_LAYOUTS ||--o{ MASTER_VARIANT_LAYOUT_ASSIGNMENTS : shared_by
    MASTER_VARIANTS ||--o{ MASTER_VARIANT_NAV_ASSIGNMENTS : selects
    MASTER_NAVIGATION_PROFILES ||--o{ MASTER_VARIANT_NAV_ASSIGNMENTS : shared_by

    MASTER_LAYOUTS ||--o{ MASTER_LAYOUT_SLOTS : contains
    MASTER_CONTAINER_TYPES o|--o{ MASTER_LAYOUT_SLOTS : default_for
    MASTER_LAYOUTS ||--o{ MASTER_SLOT_GROUPS : defines
    MASTER_SLOT_GROUPS ||--o{ MASTER_SLOT_GROUP_MEMBERS : contains
    MASTER_LAYOUT_SLOTS ||--o{ MASTER_SLOT_GROUP_MEMBERS : joins

    MASTER_VARIANTS ||--o{ MASTER_TASK_TEMPLATES : configures
    MASTER_PRODUCT_GROUPS o|--o{ MASTER_TASK_TEMPLATES : specializes
    MASTER_TASK_TYPES ||--o{ MASTER_TASK_TEMPLATES : types
    MASTER_SLOT_GROUPS o|--o{ MASTER_TASK_TEMPLATES : source_group
    MASTER_SLOT_GROUPS o|--o{ MASTER_TASK_TEMPLATES : destination_group

    MASTER_MACHINES {
        uuid id PK
        varchar code UK
    }
    MASTER_VARIANTS {
        uuid id PK
        uuid machine_id FK
        varchar code
    }
    MASTER_LAYOUTS {
        uuid id PK
        uuid machine_id FK
        varchar code
        integer version
        varchar status
    }
    MASTER_VARIANT_LAYOUT_ASSIGNMENTS {
        uuid variant_id PK,FK
        uuid layout_id PK,FK
        uuid machine_id FK
        boolean is_default
    }
    MASTER_NAVIGATION_PROFILES {
        uuid id PK
        uuid machine_id FK
        varchar code
        integer version
        varchar status
        jsonb config_json
    }
    MASTER_VARIANT_NAV_ASSIGNMENTS {
        uuid variant_id PK,FK
        uuid navigation_profile_id PK,FK
        uuid machine_id FK
        boolean is_default
    }
    MASTER_LAYOUT_SLOTS {
        uuid id PK
        uuid layout_id FK
        varchar code
        varchar slot_role
        uuid default_container_type_id FK
        numeric ui_x
        numeric ui_y
        numeric ui_width
        numeric ui_height
    }
    MASTER_CONTAINER_TYPES {
        uuid id PK
        varchar code UK
    }
    MASTER_SLOT_GROUPS {
        uuid id PK
        uuid layout_id FK
        varchar code
        varchar purpose
    }
    MASTER_SLOT_GROUP_MEMBERS {
        uuid slot_group_id PK,FK
        uuid layout_slot_id PK,FK
        integer priority
    }
    MASTER_PRODUCT_GROUPS {
        uuid id PK
        uuid variant_id FK
    }
    MASTER_TASK_TYPES {
        uuid id PK
        varchar code UK
    }
    MASTER_TASK_TEMPLATES {
        uuid id PK
        uuid variant_id FK
        uuid product_group_id FK
        uuid task_type_id FK
        uuid source_slot_group_id FK
        uuid destination_slot_group_id FK
    }
```

Assignment tabloları fiziksel layout/navigation profile ile üretim variant'ı arasında zorunlu 1:1 bağı önler. Aynı machine kapsamındaki bir layout birden fazla variant tarafından paylaşılabilir.

### Slot rolü güvenlik sınırı

`MASTER_LAYOUT_SLOTS.slot_role` değeri `MACHINE`, `STAGING`, `EMPTY_STORAGE`, `FULL_STORAGE`, `WAREHOUSE`, `PICKUP`, `DROP`, `SHARED` veya `OTHER` olabilir. Phase 02B baseline'ında 172 lojistik slot ve 11 `MACHINE` bölgesi vardır.

`MACHINE` bölgeleri yalnızca ekran/fiziksel referanstır. Aşağıdaki kural Phase 03'te domain validation ile uygulanmalıdır:

```text
transport source/destination slot => slot_role != 'MACHINE'
```

Migration bu bakım turunda değiştirilmemiştir.

## 4. Runtime, RDS boundary ve audit

```mermaid
erDiagram
    MASTER_MACHINES ||--o| OPS_MACHINE_RUNTIME_STATE : has_state
    MASTER_VARIANTS o|--o{ OPS_MACHINE_RUNTIME_STATE : active_variant
    MASTER_LAYOUTS o|--o{ OPS_MACHINE_RUNTIME_STATE : active_layout
    MASTER_NAVIGATION_PROFILES o|--o{ OPS_MACHINE_RUNTIME_STATE : active_navigation
    AUTH_USERS o|--o{ OPS_MACHINE_RUNTIME_STATE : selected_by

    MASTER_LAYOUT_SLOTS ||--o| OPS_SLOT_RUNTIME_STATE : has_state
    MASTER_CONTAINER_TYPES o|--o{ OPS_SLOT_RUNTIME_STATE : current_container
    MASTER_PARTS o|--o{ OPS_SLOT_RUNTIME_STATE : current_part

    AUTH_USERS o|--o{ OPS_TRANSPORT_REQUESTS : requests
    MASTER_MACHINES ||--o{ OPS_TRANSPORT_REQUESTS : scopes
    MASTER_VARIANTS ||--o{ OPS_TRANSPORT_REQUESTS : scopes
    MASTER_LAYOUTS ||--o{ OPS_TRANSPORT_REQUESTS : uses
    MASTER_NAVIGATION_PROFILES o|--o{ OPS_TRANSPORT_REQUESTS : uses
    MASTER_TASK_TYPES ||--o{ OPS_TRANSPORT_REQUESTS : types
    MASTER_LAYOUT_SLOTS o|--o{ OPS_TRANSPORT_REQUESTS : source_or_destination
    OPS_TRANSPORT_REQUESTS ||--o{ OPS_TRANSPORT_REQUEST_EVENTS : records
    AUTH_USERS o|--o{ OPS_TRANSPORT_REQUEST_EVENTS : acts

    MASTER_NAVIGATION_PROFILES o|--o{ INTEGRATION_RDS_WORKSITES : scopes
    MASTER_LAYOUT_SLOTS ||--o| INTEGRATION_SLOT_RDS_MAPPING : maps
    INTEGRATION_RDS_WORKSITES ||--o| INTEGRATION_SLOT_RDS_MAPPING : maps
    OPS_TRANSPORT_REQUESTS ||--o| INTEGRATION_RDS_TASKS : mirrors

    AUTH_USERS o|--o{ AUDIT_AUDIT_LOGS : acts
    AUDIT_CLIENT_DEVICES o|--o{ AUDIT_AUDIT_LOGS : emits

    OPS_MACHINE_RUNTIME_STATE {
        uuid machine_id PK,FK
        uuid active_variant_id FK
        uuid active_layout_id FK
        uuid active_navigation_id FK
        bigint row_version
    }
    OPS_SLOT_RUNTIME_STATE {
        uuid layout_slot_id PK,FK
        varchar occupancy_state
        varchar load_state
        uuid current_container_type_id FK
        uuid current_part_id FK
        bigint row_version
    }
    OPS_TRANSPORT_REQUESTS {
        uuid id PK
        bigint request_no UK
        varchar idempotency_key UK
        uuid machine_id FK
        uuid variant_id FK
        uuid layout_id FK
        uuid task_type_id FK
        uuid source_slot_id FK
        uuid destination_slot_id FK
        varchar status
    }
    OPS_TRANSPORT_REQUEST_EVENTS {
        bigint id PK
        uuid transport_request_id FK
        uuid actor_user_id FK
        varchar event_type
        varchar old_status
        varchar new_status
    }
    INTEGRATION_RDS_WORKSITES {
        uuid id PK
        uuid navigation_profile_id FK
        varchar rds_site_id
        integer last_filled
        varchar last_agv_id
        jsonb raw_state_json
    }
    INTEGRATION_SLOT_RDS_MAPPING {
        uuid layout_slot_id PK,FK
        uuid rds_worksite_id FK
    }
    INTEGRATION_RDS_TASKS {
        uuid id PK
        uuid transport_request_id UK,FK
        varchar rds_task_record_id UK
        varchar rds_task_label
        varchar out_order_no
        varchar agv_id
        integer rds_status
    }
    AUDIT_CLIENT_DEVICES {
        uuid id PK
        varchar device_code UK
        varchar device_type
    }
    AUDIT_AUDIT_LOGS {
        bigint id PK
        uuid actor_user_id FK
        uuid device_id FK
        varchar action
        varchar entity_type
        varchar entity_id
        uuid correlation_id
    }
    AUTH_USERS {
        uuid id PK
    }
    MASTER_MACHINES {
        uuid id PK
    }
    MASTER_VARIANTS {
        uuid id PK
    }
    MASTER_LAYOUTS {
        uuid id PK
    }
    MASTER_NAVIGATION_PROFILES {
        uuid id PK
    }
    MASTER_LAYOUT_SLOTS {
        uuid id PK
        varchar slot_role
    }
    MASTER_CONTAINER_TYPES {
        uuid id PK
    }
    MASTER_PARTS {
        uuid id PK
    }
    MASTER_TASK_TYPES {
        uuid id PK
    }
```

`integration.rds_worksites.navigation_profile_id` bilinçli olarak nullable tutulmuştur. Global/warehouse WorkSite domain kararı verilmeden constraint değiştirilmemiştir. Phase 02A/02B seed'leri RDS WorkSite veya RDS task kaydı üretmez.

## 5. Deterministik veri üretim zinciri

```mermaid
flowchart TD
    RAW[Local customer source package<br/>Git ignored and byte-preserved]
    P02A[Phase 02A pipeline<br/>textual extraction and normalization]
    AJSON[Normalized JSON and CSV<br/>source manifest and data issues]
    ASQL[002a textual master-data seed]
    P02B[Phase 02B pipeline<br/>physical display-layout extraction]
    BJSON[Normalized layouts and slots<br/>provenance and data issues]
    REVIEW[21 deterministic review PNGs]
    BSQL[002b physical-layout seed]
    SCHEMA[001 initial schema]
    PG[(Fresh PostgreSQL 18.x)]

    RAW --> P02A
    P02A --> AJSON
    P02A --> ASQL
    RAW --> P02B
    AJSON --> P02B
    P02B --> BJSON
    P02B --> REVIEW
    P02B --> BSQL
    SCHEMA --> PG
    ASQL --> PG
    BSQL --> PG
```

DB uygulama sırası `001_initial_schema.sql` → `002a_customer_textual_master_data.sql` → `002b_customer_physical_layouts.sql` şeklindedir. Seed'ler tekrar uygulanabilir ve ikinci uygulama mantıksal veritabanı durumunu değiştirmez.
