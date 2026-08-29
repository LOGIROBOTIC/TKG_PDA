# TKG PDA — Local Development Stack

This directory contains PowerShell scripts to start, stop, and reset a persistent local development stack for manual operator and administrator testing.

## Overview

The local development stack allows engineers to run and interact with the full TKG PDA system locally without depending on temporary integration-test harnesses or cloud infrastructure.

```
┌─────────────────────────────────────────────────────────────┐
│                    Local Development Stack                  │
│                                                             │
│  React Admin Web               Flutter Operator Web (PWA)   │
│  http://127.0.0.1:4173         http://127.0.0.1:7357        │
│             │                               │               │
│             └───────────────┬───────────────┘               │
│                             ▼                               │
│                   ASP.NET Core API                          │
│                   http://127.0.0.1:5000                     │
│                             │                               │
│             ┌───────────────┴───────────────┐               │
│             ▼                               ▼               │
│     Dedicated PostgreSQL                Local RDS           │
│     127.0.0.1:55450 / tkg_pda_dev       READ-ONLY Mode      │
│     (persistent AppData)                http://127.0.0.1:8080
└─────────────────────────────────────────────────────────────┘
```

## Ports and Endpoints

| Component | Endpoint | Description |
|---|---|---|
| **PostgreSQL** | `127.0.0.1:55450` | Dedicated local database `tkg_pda_dev` |
| **Backend API** | `http://127.0.0.1:5000` | ASP.NET Core API and health endpoints |
| **Admin Web** | `http://127.0.0.1:4173` | React / TypeScript Administration UI |
| **Operator Web** | `http://127.0.0.1:7357` | Flutter Web Operator Client (Chrome) |
| **Local RDS** | `http://127.0.0.1:8080` | SEER RDS (Read-Only queries, dispatch disabled) |

## Persistent Data Location

All local database files, logs, and process state are stored **outside the Git repository** in:

```text
$env:LOCALAPPDATA\TKG_PDA_DEV\
├── postgres\     # PostgreSQL cluster files (persisted between runs)
├── logs\         # stdout/stderr logs for API, Admin Web, Flutter, Postgres
└── state\        # stack_state.json with active process PIDs
```

The Git repository remains clean and contains no generated database files.

## Quick Start

### 1. Start the Stack

```powershell
powershell -ExecutionPolicy Bypass -File tools\dev\start_local_stack.ps1
```

Options:
- `-Password <string>`: Provide the local development password non-interactively (otherwise prompts securely on first bootstrap).
- `-RdsMode <ReadOnly|Off>`: Defaults to `ReadOnly`.
- `-NoOperator`: Starts API and Admin Web without launching Flutter Operator.
- `-NoAdmin`: Starts API and Operator Web without launching Admin Web.
- `-PgBin <path>`: Override PostgreSQL 18 binary directory (default: `C:\Program Files\PostgreSQL\18\bin`).

On initial startup, you will be prompted for a password for all local development accounts. The password is encrypted using TKG's standard PBKDF2-SHA512 hasher (210,000 iterations) directly into PostgreSQL; plaintext passwords are never logged or stored.

### 2. Stop the Stack

```powershell
powershell -ExecutionPolicy Bypass -File tools\dev\stop_local_stack.ps1
```

Stops the API, Admin Web dev server, Operator Web process, and PostgreSQL cluster cleanly. Database data is preserved.

### 3. Reset Database

```powershell
powershell -ExecutionPolicy Bypass -File tools\dev\reset_local_database.ps1
```

Stops the stack and removes the local development database cluster, allowing a clean bootstrap on the next startup.

## Development Accounts

All accounts use the local development password configured during bootstrap:

| Username | Role | Full Name | Primary Purpose |
|---|---|---|---|
| `dev-admin` | `ADMIN` | Development Administrator | Admin Web management, layout editing, user management |
| `dev-supervisor` | `SUPERVISOR` | Development Supervisor | Line-side operations, variant switching |
| `dev-line` | `LINE_OPERATOR` | Development Line Operator | Slot operations, transport requests |
| `dev-warehouse` | `WAREHOUSE_OPERATOR` | Development Warehouse Operator | Warehouse supply / Bring-Empty operations |

## Development Client Devices

Pre-registered device codes for testing device-bound operator authentication:

| Device Code | Terminal Name | Associated Role |
|---|---|---|
| `DEV-SUPERVISOR` | Development Supervisor Terminal | `dev-supervisor` |
| `DEV-LINE` | Development Line Terminal | `dev-line` |
| `DEV-WAREHOUSE` | Development Warehouse Terminal | `dev-warehouse` |

## Preconfigured Operational State (Machine SH-01-A)

Upon bootstrap, machine `SH-01-A` is initialized with:
- **Active Variant**: `1` (Published Default Layout)
- **Active Navigation Profile**: `DEV-RDS-SIM` (`Development RDS Simulator`)
- **Initial Test Slot States**:
  - **Slot 1**: `VACANT` + `load = UNKNOWN` (Ready to test `BRING_EMPTY`)
  - **Slot 2**: `OCCUPIED` + `load = EMPTY` (Ready to test `Kasa Doldu` / `REMOVE_EMPTY`)
  - **Slot 3**: `OCCUPIED` + `load = FULL` (Ready to test `Kasa Boş` / `REMOVE_FULL`)

## Safety and Security Invariants

- **PowerShell 5.1 Compatible**: Uses instance-based cryptography (`RandomNumberGenerator.Create()`, `Rfc2898DeriveBytes`) for Windows PowerShell 5.1 compatibility.
- **RDS Dispatch Disabled**: `Rds__DispatchEnabled=false` by default to prevent unintended robot movements during UI testing.
- **No Production Mutation**: The stack only targets port 55450 in `$env:LOCALAPPDATA\TKG_PDA_DEV`.
- **JWT Key In-Memory**: Fresh 48-byte Base64 signing key generated in-memory on every startup.
