import { existsSync, readFileSync } from 'node:fs'
import { RUNTIME_FILE, killProcessTree, removeRuntime, removeTempRoot, stopPostgres, type RuntimeInfo } from './harness'

export default async function globalTeardown(): Promise<void> {
  if (!existsSync(RUNTIME_FILE)) return
  let runtime: RuntimeInfo
  try {
    runtime = JSON.parse(readFileSync(RUNTIME_FILE, 'utf8')) as RuntimeInfo
  } catch {
    removeRuntime()
    return
  }

  console.log('[e2e] stopping the API process…')
  killProcessTree(runtime.apiPid)

  console.log('[e2e] stopping PostgreSQL and removing the throwaway cluster…')
  stopPostgres(runtime.dataDir, runtime.pgBin)
  removeTempRoot()
  removeRuntime()
}
