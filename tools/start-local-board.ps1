param([switch]$Publish)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot '.utmp\spacetime'
$binRoot = Join-Path $runtimeRoot 'bin'
$cli = Join-Path $binRoot 'spacetimedb-cli.exe'
$cliRoot = Join-Path $runtimeRoot 'root'
$serverUrl = 'http://127.0.0.1:3000'
$modulePath = Join-Path $projectRoot 'server\spacetimedb'
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null

# Multiple Play presses must not start duplicate setup/publish processes.
try {
    $setupLock = [IO.File]::Open((Join-Path $runtimeRoot 'setup.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
} catch [IO.IOException] {
    Write-Output '[LocalBoard] Setup is already running.'
    exit 0
}

# Unity starts this hidden and can't read its output across a domain reload, so it logs to a file.
Start-Transcript -LiteralPath (Join-Path $runtimeRoot 'setup.log') | Out-Null
try {
    if (!(Test-Path -LiteralPath $cli)) {
        Write-Output '[LocalBoard] Downloading the official SpacetimeDB 2.13.0 local runtime...'
        $archive = Join-Path $runtimeRoot 'runtime.zip'
        Invoke-WebRequest 'https://github.com/clockworklabs/SpacetimeDB/releases/download/v2.13.0/spacetime-x86_64-pc-windows-msvc.zip' -OutFile $archive -UseBasicParsing
        Expand-Archive -LiteralPath $archive -DestinationPath $binRoot -Force
    }

    $online = $false
    try {
        $online = (Invoke-WebRequest "$serverUrl/v1/ping" -TimeoutSec 2 -UseBasicParsing).StatusCode -eq 200
    } catch {}
    if (!$online) {
        $serverArguments = @('--root-dir', $cliRoot, 'start', '--listen-addr', '127.0.0.1:3000',
            '--data-dir', (Join-Path $runtimeRoot 'data'), '--page_pool_max_size', '268435456', '--non-interactive')
        # Start-Process joins its argument list; quote the two filesystem arguments for paths with spaces.
        $serverArguments[1] = '"' + $cliRoot + '"'
        $serverArguments[6] = '"' + (Join-Path $runtimeRoot 'data') + '"'
        $serverProcess = Start-Process -FilePath $cli -ArgumentList $serverArguments -WorkingDirectory $projectRoot -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $runtimeRoot 'server.log') -RedirectStandardError (Join-Path $runtimeRoot 'server-error.log') -PassThru
        $serverProcess.Id | Set-Content (Join-Path $runtimeRoot 'server.pid')
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            try {
                $online = (Invoke-WebRequest "$serverUrl/v1/ping" -TimeoutSec 1 -UseBasicParsing).StatusCode -eq 200
            } catch {}
            if ($online) { break }
            if ($serverProcess.HasExited) {
                $reason = Get-Content -LiteralPath (Join-Path $runtimeRoot 'server-error.log') -Tail 5 -ErrorAction SilentlyContinue
                throw "Local drawing server exited (is something else using port 3000?). $($reason -join ' ')"
            }
            Start-Sleep -Milliseconds 250
        }
        if (!$online) { throw 'Local drawing server did not become ready on port 3000.' }
    }

    $hasModule = $false
    try {
        $hasModule = (Invoke-WebRequest "$serverUrl/v1/database/thinkink/schema?version=9" -TimeoutSec 3 -UseBasicParsing).StatusCode -eq 200
    } catch {}
    if ($Publish -or !$hasModule) {
        if (!(Test-Path (Join-Path $modulePath 'node_modules'))) {
            Push-Location $modulePath
            try {
                & npm.cmd ci --no-audit --no-fund | Out-Host
                if ($LASTEXITCODE -ne 0) { throw 'Could not install drawing-server dependencies. Node.js is required.' }
            } finally { Pop-Location }
        }
        & $cli --root-dir $cliRoot publish thinkink --server $serverUrl --module-path $modulePath --anonymous --yes --no-config | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Could not publish the local drawing module.' }
    }
    Write-Output '[LocalBoard] Ready at http://127.0.0.1:3000. Create a room in Unity; no ThinkInk key is needed.'
} catch {
    Write-Output "[LocalBoard] Failed: $_"
    exit 1
} finally {
    Stop-Transcript | Out-Null
    $setupLock.Dispose()
}
