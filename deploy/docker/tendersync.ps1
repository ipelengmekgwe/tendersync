<#
.SYNOPSIS
TenderSync Docker Compose helper (PLAN.md §15). The runbook is docs/runbooks/local.md.

.DESCRIPTION
  init        Write deploy/docker/.env with random secrets (JWT key, admin password, document master key).
  up          Build and start everything (runs init first when .env is missing), then wait until it is healthy.
  infra       Start only PostgreSQL and Seq, for `dotnet run` and `npm run dev` on this machine.
  status      Containers, health and the URLs.
  logs [svc]  Follow logs (all services, or one: api, worker, web, migrate, postgres, seq).
  ingest [n]  One-shot ingest of the last n days (default 7) in a throwaway Worker container.
  seed-demo   Write the demo tenders used by the dashboard e2e tests (Development only, so not with -Prod).
  down        Stop and remove the containers; data volumes stay.
  reset       Stop and DELETE the database, documents and logs (asks first; -Force skips the question).

  -Prod uses docker-compose.yml alone (Production settings, secrets from .env required). Without it the
  docker-compose.override.yml development settings apply.

.EXAMPLE
./deploy/docker/tendersync.ps1 up
.EXAMPLE
./deploy/docker/tendersync.ps1 logs worker
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('init', 'up', 'infra', 'status', 'logs', 'ingest', 'seed-demo', 'down', 'reset', 'help')]
    [string]$Command = 'help',
    [Parameter(Position = 1)]
    [string]$Argument,
    [switch]$Prod,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$Dir = $PSScriptRoot
$EnvFile = Join-Path $Dir '.env'

function New-Secret([int]$Bytes) {
    $buffer = New-Object byte[] $Bytes
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    [Convert]::ToBase64String($buffer)
}

function Read-EnvFile {
    $values = @{}
    if (Test-Path $EnvFile) {
        foreach ($line in Get-Content $EnvFile) {
            if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim() }
        }
    }
    $values
}

function Get-Setting([hashtable]$Values, [string]$Name, [string]$Default) {
    if ($Values.ContainsKey($Name) -and $Values[$Name]) { $Values[$Name] } else { $Default }
}

function Invoke-Compose([string[]]$ComposeArgs) {
    $files = @('-f', (Join-Path $Dir 'docker-compose.yml'))
    if (-not $Prod) { $files += @('-f', (Join-Path $Dir 'docker-compose.override.yml')) }
    & docker compose --project-directory $Dir @files @ComposeArgs
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($ComposeArgs -join ' ') failed (exit $LASTEXITCODE)." }
}

function Initialize-EnvFile {
    if ((Test-Path $EnvFile) -and -not $Force) {
        Write-Host "$EnvFile already exists; leaving it alone (-Force overwrites it, and a new master key makes stored documents unreadable)."
        return
    }
    $password = (New-Secret 18).Replace('+', '-').Replace('/', '_')
    $lines = Get-Content (Join-Path $Dir '.env.example') | ForEach-Object {
        switch -Regex ($_) {
            '^Jwt__SigningKey=' { "Jwt__SigningKey=$(New-Secret 48)"; break }
            '^Seed__AdminEmail=' { 'Seed__AdminEmail=admin@tendersync.local'; break }
            '^Seed__AdminPassword=' { "Seed__AdminPassword=$password"; break }
            '^TENDERSYNC_MASTER_KEY=' { "TENDERSYNC_MASTER_KEY=$(New-Secret 32)"; break }
            default { $_ }
        }
    }
    Set-Content -Path $EnvFile -Value $lines -Encoding utf8
    Write-Host "Wrote $EnvFile with random secrets."
    Write-Host "  First admin: admin@tendersync.local / $password (used by the production-like stack; the dev stack signs anyone in)."
    Write-Host "  Back up TENDERSYNC_MASTER_KEY from that file: encrypted company documents cannot be read without it."
}

function Wait-Healthy([int]$Seconds = 300) {
    $settings = Read-EnvFile
    $api = "http://localhost:$(Get-Setting $settings 'API_PORT' '5000')/health/ready"
    $web = "http://localhost:$(Get-Setting $settings 'WEB_PORT' '3000')/login"
    $deadline = (Get-Date).AddSeconds($Seconds)
    foreach ($url in @($api, $web)) {
        while ($true) {
            try {
                $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
                if ($response.StatusCode -eq 200) { Write-Host "OK   $url"; break }
            } catch { }
            if ((Get-Date) -gt $deadline) { throw "Timed out waiting for $url. See: ./deploy/docker/tendersync.ps1 logs" }
            Start-Sleep -Seconds 3
        }
    }
}

function Show-Urls {
    $s = Read-EnvFile
    Write-Host ''
    Write-Host "Dashboard  http://localhost:$(Get-Setting $s 'WEB_PORT' '3000')"
    Write-Host "API        http://localhost:$(Get-Setting $s 'API_PORT' '5000')/scalar"
    Write-Host "Hangfire   http://localhost:$(Get-Setting $s 'WORKER_PORT' '5001')/hangfire  (Admin token: /hangfire?access_token=...)"
    Write-Host "Seq        http://localhost:$(Get-Setting $s 'SEQ_PORT' '5341')"
}

switch ($Command) {
    'init' { Initialize-EnvFile }
    'up' {
        if (-not (Test-Path $EnvFile)) { Initialize-EnvFile }
        Invoke-Compose @('up', '-d', '--build')
        Wait-Healthy
        Show-Urls
    }
    'infra' { Invoke-Compose @('up', '-d', 'postgres', 'seq') }
    'status' { Invoke-Compose @('ps'); Show-Urls }
    'logs' { Invoke-Compose (@('logs', '-f', '--tail', '200') + @($Argument | Where-Object { $_ })) }
    'ingest' {
        $days = if ($Argument) { $Argument } else { '7' }
        Invoke-Compose @('run', '--rm', 'worker', '--ingest-days', $days)
    }
    'seed-demo' {
        if ($Prod) { throw 'seed-demo writes demo data and only runs in Development (drop -Prod).' }
        Invoke-Compose @('run', '--rm', 'worker', '--seed-demo')
    }
    'down' { Invoke-Compose @('down') }
    'reset' {
        if (-not $Force) {
            $answer = Read-Host 'This deletes the database, all documents and the logs of this stack. Type yes to continue'
            if ($answer -ne 'yes') { Write-Host 'Nothing deleted.'; return }
        }
        Invoke-Compose @('down', '--volumes')
    }
    default { Get-Help $PSCommandPath -Detailed }
}
