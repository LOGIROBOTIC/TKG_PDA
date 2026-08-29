<#
.SYNOPSIS
    Stops the TKG PDA local development stack.
.DESCRIPTION
    Safely terminates the API, Admin Web dev server, Flutter Web dev server,
    and PostgreSQL cluster owned by the local stack runner.
    Leaves persistent PostgreSQL data intact.
    Compatible with Windows PowerShell 5.1.
#>

[CmdletBinding()]
param(
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$KeepPostgres
)

$ErrorActionPreference = 'Continue'

$localAppData = [Environment]::GetEnvironmentVariable('LOCALAPPDATA')
if ([string]::IsNullOrWhiteSpace($localAppData)) {
    $localAppData = Join-Path $env:USERPROFILE 'AppData\Local'
}
$devRoot   = Join-Path $localAppData 'TKG_PDA_DEV'
$pgDataDir = Join-Path $devRoot 'postgres'
$stateFile = Join-Path $devRoot 'state\stack_state.json'

$stoppedProcesses = @()

if (Test-Path -LiteralPath $stateFile) {
    try {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        
        # Stop API process
        if ($state.apiPid) {
            $proc = Get-Process -Id $state.apiPid -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Host "Stopping API process (PID $($state.apiPid))..." -ForegroundColor Cyan
                Stop-Process -Id $state.apiPid -Force -ErrorAction SilentlyContinue
                $stoppedProcesses += "API (PID $($state.apiPid))"
            }
        }

        # Stop Admin Web dev server
        if ($state.adminPid) {
            $proc = Get-Process -Id $state.adminPid -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Host "Stopping Admin Web dev server (PID $($state.adminPid))..." -ForegroundColor Cyan
                # Kill process tree on Windows via taskkill to stop node/vite child processes
                & taskkill.exe /PID $state.adminPid /T /F 2>$null | Out-Null
                $stoppedProcesses += "Admin Web (PID $($state.adminPid))"
            }
        }

        # Stop Flutter Operator Web dev server
        if ($state.operatorPid) {
            $proc = Get-Process -Id $state.operatorPid -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Host "Stopping Flutter Operator dev server (PID $($state.operatorPid))..." -ForegroundColor Cyan
                & taskkill.exe /PID $state.operatorPid /T /F 2>$null | Out-Null
                $stoppedProcesses += "Operator Web (PID $($state.operatorPid))"
            }
        }

        if ($state.pgDataDir) {
            $pgDataDir = $state.pgDataDir
        }
    } catch {
        Write-Warning "Could not parse state file: $($_.Exception.Message)"
    }
}

# Stop PostgreSQL unless -KeepPostgres is requested
if (-not $KeepPostgres) {
    $pgCtl = Join-Path $PgBin 'pg_ctl.exe'
    if ((Test-Path -LiteralPath $pgCtl) -and (Test-Path -LiteralPath $pgDataDir)) {
        Write-Host "Stopping PostgreSQL development cluster at $pgDataDir..." -ForegroundColor Cyan
        & $pgCtl -D $pgDataDir -m fast -w stop 2>$null | Out-Null
        $stoppedProcesses += "PostgreSQL cluster ($pgDataDir)"
    }
}

# Remove state file
if (Test-Path -LiteralPath $stateFile) {
    Remove-Item -LiteralPath $stateFile -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "TKG LOCAL DEV STACK STOPPED" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
if ($stoppedProcesses.Count -gt 0) {
    Write-Host "Stopped components:"
    foreach ($item in $stoppedProcesses) {
        Write-Host "  - $item"
    }
} else {
    Write-Host "No active development stack processes were found."
}
Write-Host "Persistent data remains intact in: $pgDataDir" -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Green
