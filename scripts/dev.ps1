# Starts the API and the Angular dev server together (Windows PowerShell 5.1 or later).
#
#   scripts\dev.ps1          restore, build, start both servers and wait; Ctrl+C stops both
#   scripts\dev.ps1 -Stop    stop servers that a previous run left behind
#
# The API is started from the built dll, not through "dotnet run", so that stopping it
# also frees the port. Both process trees are ended with taskkill.
param(
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$logsDir = Join-Path $repoRoot 'logs'
$apiPidFile = Join-Path $logsDir 'dev-api.pid'
$webPidFile = Join-Path $logsDir 'dev-web.pid'

# Ends a process and everything it started. A process that is already gone is not an error.
function Stop-ProcessTree {
    param([string]$ProcessId)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & cmd.exe /c "taskkill /PID $ProcessId /T /F >nul 2>&1"
    }
    catch {
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Stop-Tree {
    param([string]$PidFile)
    if (-not (Test-Path $PidFile)) {
        return
    }
    $text = (Get-Content $PidFile -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($text -match '^\d+$') {
        Stop-ProcessTree $text
    }
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

if ($Stop) {
    Stop-Tree $apiPidFile
    Stop-Tree $webPidFile
    Write-Host 'Stopped.'
    exit 0
}

if (-not (Test-Path $logsDir)) {
    New-Item -ItemType Directory -Path $logsDir | Out-Null
}

# A leftover run is stopped first so the ports are free.
Stop-Tree $apiPidFile
Stop-Tree $webPidFile

Push-Location $repoRoot
try {
    Write-Host 'Restoring and building the backend...'
    & dotnet restore backend/AdminDesk.sln
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet restore failed.'
    }
    & dotnet build backend/AdminDesk.sln --no-restore -nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet build failed.'
    }

    if (-not (Test-Path (Join-Path $repoRoot 'frontend\node_modules'))) {
        Write-Host 'Installing frontend packages...'
        Push-Location (Join-Path $repoRoot 'frontend')
        try {
            & npm.cmd ci
            if ($LASTEXITCODE -ne 0) {
                throw 'npm ci failed.'
            }
        }
        finally {
            Pop-Location
        }
    }

    $apiProject = Join-Path $repoRoot 'backend\src\AdminDesk.Api'
    $apiDll = Join-Path $apiProject 'bin\Debug\net10.0\AdminDesk.Api.dll'
    if (-not (Test-Path $apiDll)) {
        throw "Built API not found at $apiDll"
    }

    $env:ASPNETCORE_URLS = 'http://localhost:5080'
    $env:ASPNETCORE_CONTENTROOT = $apiProject
    $env:ASPNETCORE_ENVIRONMENT = 'Development'

    $apiProc = $null
    $webProc = $null
    try {
        $apiProc = Start-Process -FilePath 'dotnet' -ArgumentList ('"' + $apiDll + '"') -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $logsDir 'dev-api.out.log') `
            -RedirectStandardError (Join-Path $logsDir 'dev-api.err.log')
        Set-Content -Path $apiPidFile -Value $apiProc.Id

        Write-Host 'Waiting for the API on http://localhost:5080 ...'
        $ready = $false
        for ($i = 0; $i -lt 60; $i++) {
            if ($apiProc.HasExited) {
                break
            }
            try {
                $reply = Invoke-WebRequest -Uri 'http://localhost:5080/api/health' -UseBasicParsing -TimeoutSec 2
                if ($reply.StatusCode -eq 200) {
                    $ready = $true
                    break
                }
            }
            catch {
                Start-Sleep -Seconds 1
            }
        }
        if (-not $ready) {
            throw 'The API did not answer within 60 seconds. See logs\dev-api.out.log and logs\dev-api.err.log.'
        }

        Write-Host ''
        Write-Host 'API ready:  http://localhost:5080'
        Write-Host 'Web app:    http://localhost:4200  (starting now)'
        Write-Host 'The login screen has one-click demo role cards; the shared practice password is Demo@12345.'
        Write-Host 'Plain commands instead of this script:'
        Write-Host '  dotnet run --project backend/src/AdminDesk.Api'
        Write-Host '  npm start   (inside the frontend folder)'
        Write-Host 'Press Ctrl+C to stop both, or run scripts\dev.ps1 -Stop from another window.'
        Write-Host ''

        $webProc = Start-Process -FilePath 'npm.cmd' -ArgumentList 'start' -WorkingDirectory (Join-Path $repoRoot 'frontend') -NoNewWindow -PassThru
        Set-Content -Path $webPidFile -Value $webProc.Id
        Wait-Process -Id $webProc.Id
    }
    finally {
        if ($webProc) {
            Stop-ProcessTree $webProc.Id
        }
        if ($apiProc) {
            Stop-ProcessTree $apiProc.Id
        }
        Remove-Item $apiPidFile -Force -ErrorAction SilentlyContinue
        Remove-Item $webPidFile -Force -ErrorAction SilentlyContinue
    }
}
finally {
    Pop-Location
}
