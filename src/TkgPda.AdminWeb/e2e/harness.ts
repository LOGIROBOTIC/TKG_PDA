import { execFileSync, spawnSync } from 'node:child_process'
import { randomBytes, pbkdf2Sync } from 'node:crypto'
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync, openSync } from 'node:fs'
import { createServer } from 'node:net'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

/**
 * Shared configuration and helpers for the Phase 06.1 real-browser acceptance harness.
 *
 * The harness provisions a throwaway PostgreSQL 18 cluster, applies the authoritative
 * SQL baseline + Phase 02A/02B seeds, seeds a single ADMIN identity, and runs the real
 * ASP.NET Core API. Playwright then drives real Chromium against the React client,
 * whose Vite server proxies /api to this API. Nothing here is production configuration:
 * runtime credentials and signing material are randomly generated during global setup,
 * written only to the ignored handoff file, and removed by global teardown.
 */

const here = path.dirname(fileURLToPath(import.meta.url))
export const adminWebRoot = path.resolve(here, '..')
export const repoRoot = path.resolve(here, '../../..')

export const API_URL = 'http://127.0.0.1:5099'
export const PG_PORT = 55450
export const PG_DATABASE = 'tkg_pda_phase06_e2e'
export const PG_BIN = process.env.TKG_PG_BIN ?? 'C:\\Program Files\\PostgreSQL\\18\\bin'
export const TEMP_DIR_NAME = '.codex-pg-phase06-e2e'
export const TEMP_ROOT = path.join(repoRoot, TEMP_DIR_NAME)
export const RUNTIME_FILE = path.join(here, '.e2e-runtime.json')

export const ADMIN_USERNAME = 'e2e.admin'

export const createRuntimeSecrets = () => ({
  adminPassword: `Phase06!${randomBytes(24).toString('base64url')}`,
  jwtSigningKey: randomBytes(48).toString('base64url'),
})

export const buildConnectionString = () => [
  'Host=127.0.0.1',
  `Port=${PG_PORT}`,
  `Database=${PG_DATABASE}`,
  'Username=postgres',
  'Pooling=true',
  'Maximum Pool Size=50',
].join(';')

export interface RuntimeInfo {
  apiUrl: string
  apiPid: number
  pgPort: number
  pgDatabase: string
  pgBin: string
  tempRoot: string
  dataDir: string
  adminUsername: string
  adminPassword: string
}

export const pgTool = (name: string, pgBin = PG_BIN) => path.join(pgBin, `${name}.exe`)

export const psqlArgs = () => [
  '-X', '-h', '127.0.0.1', '-p', String(PG_PORT), '-U', 'postgres', '-d', PG_DATABASE, '-v', 'ON_ERROR_STOP=1',
]

/** Run a psql command against the throwaway cluster and return stdout. */
export const psql = (sql: string): string =>
  execFileSync(pgTool('psql'), [...psqlArgs(), '-Atc', sql], {
    encoding: 'utf8',
    env: { ...process.env, PGPASSWORD: 'trust' },
  })

/**
 * Produce a password hash byte-compatible with the API's Pbkdf2PasswordHasher
 * ($tkg-pbkdf2-sha512$v=1$i=210000$<salt>$<hash>, SHA512, 16-byte salt, 32-byte hash).
 */
export const hashPassword = (password: string): string => {
  const iterations = 210_000
  const salt = randomBytes(16)
  const hash = pbkdf2Sync(password, salt, iterations, 32, 'sha512')
  return `$tkg-pbkdf2-sha512$v=1$i=${iterations}$${salt.toString('base64')}$${hash.toString('base64')}`
}

export const writeRuntime = (info: RuntimeInfo) => writeFileSync(RUNTIME_FILE, JSON.stringify(info, null, 2), 'utf8')

export const readRuntime = (): RuntimeInfo => {
  if (!existsSync(RUNTIME_FILE)) {
    throw new Error(`E2E runtime file missing (${RUNTIME_FILE}). Run via "npm run test:e2e" so global setup provisions the API/PostgreSQL harness.`)
  }
  return JSON.parse(readFileSync(RUNTIME_FILE, 'utf8')) as RuntimeInfo
}

export const removeRuntime = () => {
  if (existsSync(RUNTIME_FILE)) rmSync(RUNTIME_FILE)
}

export const ensureLogFile = (): number => {
  const logDir = path.join(TEMP_ROOT, 'logs')
  mkdirSync(logDir, { recursive: true })
  return openSync(path.join(logDir, 'api.log'), 'a')
}

/** Guarded recursive delete: only ever removes the dedicated temp cluster directory. */
export const removeTempRoot = () => {
  if (!existsSync(TEMP_ROOT)) return
  const resolved = path.resolve(TEMP_ROOT)
  if (!resolved.startsWith(path.resolve(repoRoot)) || path.basename(resolved) !== TEMP_DIR_NAME) {
    throw new Error(`Refusing unsafe cleanup target: ${resolved}`)
  }
  rmSync(resolved, { recursive: true, force: true })
}

export const stopPostgres = (dataDir: string, pgBin = PG_BIN) => {
  if (!existsSync(dataDir)) return
  spawnSync(pgTool('pg_ctl', pgBin), ['-D', dataDir, '-m', 'fast', '-w', 'stop'], { encoding: 'utf8' })
}

/** Kill a process tree on Windows. Best effort — never throws. */
export const killProcessTree = (pid: number) => {
  if (!pid) return
  spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { encoding: 'utf8' })
}

export const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** Fail without disturbing another process when a fixed local harness port is occupied. */
export const assertPortAvailable = (port: number): Promise<void> => new Promise((resolve, reject) => {
  const server = createServer()
  server.once('error', () => reject(new Error(`E2E harness port ${port} is already in use; no process was stopped.`)))
  server.listen(port, '127.0.0.1', () => server.close(() => resolve()))
})
