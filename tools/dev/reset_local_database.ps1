<#
.SYNOPSIS
    Resets the persistent local development database for TKG PDA.
.DESCRIPTION
    Stops the development stack and safely removes the local development PostgreSQL
    data directory located in $env:LOCALAPPDATA\TKG_PDA_DEV\postgres.
    Requires explicit confirmation unless -Force is specified.
    Never touches production or customer databases.
    Compatible with Windows PowerShell 5.1.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$localAppData = [Environment]::GetEnvironmentVariable('LOCALAPPDATA')
if ([string]::IsNullOrWhiteSpace($localAppData)) {
    $localAppData = Join-Path $env:USERPROFILE 'AppData\Local'
}
$devRoot   = Join-Path $localAppData 'TKG_PDA_DEV'
$pgDataDir = Join-Path $devRoot 'postgres'
$stateFile = Join-Path $devRoot 'state\stack_state.json'
$logsDir   = Join-Path $devRoot 'logs'

# Safety check: fail closed if directory is not strictly under LOCALAPPDATA\TKG_PDA_DEV
$resolvedDev = (Resolve-Path -LiteralPath $devRoot -ErrorAction SilentlyContinue)
if ($null -ne $resolvedDev) {
    $devPath = $resolvedDev.Path
    if (-not $devPath.StartsWith($localAppData, [StringComparison]::OrdinalIgnoreCase) -or `
        (Split-Path -Leaf $devPath) -ne 'TKG_PDA_DEV') {
        throw "Refusing unsafe reset target: $devPath"
    }
}

# Prompt confirmation unless -Force
if (-not $Force) {
    Write-Host "WARNING: This will stop the local stack and completely erase all local development" -ForegroundColor Yellow
    Write-Host "PostgreSQL database tables and state in: $pgDataDir" -ForegroundColor Yellow
    Write-Host ""
    $confirm = Read-Host -Prompt "Type 'RESET' to confirm deletion of local dev database"
    if ($confirm -ne 'RESET') {
        Write-Host "Reset cancelled by user." -ForegroundColor Green
        return
    }
}

# Stop stack first
Write-Host "Stopping running stack components..." -ForegroundColor Cyan
$stopScript = Join-Path $PSScriptRoot 'stop_local_stack.ps1'
if (Test-Path -LiteralPath $stopScript) {
    & $stopScript -PgBin $PgBin
}

# Ensure PostgreSQL process on this directory is dead
Start-Sleep -Seconds 1

# Delete PostgreSQL cluster data directory safely
if (Test-Path -LiteralPath $pgDataDir) {
    Write-Host "Removing local development PostgreSQL data ($pgDataDir)..." -ForegroundColor Cyan
    Remove-Item -LiteralPath $pgDataDir -Recurse -Force
}

# Clean state file
if (Test-Path -LiteralPath $stateFile) {
    Remove-Item -LiteralPath $stateFile -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "TKG LOCAL DEV DATABASE RESET COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "A fresh PostgreSQL cluster will be automatically initialized and seeded"
Write-Host "on the next run of: .\tools\dev\start_local_stack.ps1"
Write-Host "========================================" -ForegroundColor Green
