<#
Puts the drawing pad on an iPad (or any phone or tablet) over the internet, for when the venue network won't let
devices reach the laptop directly. Two Cloudflare quick tunnels (no account) give the laptop's local SpacetimeDB
server and the pad page public https addresses; Unity keeps using http://127.0.0.1:3000 and needs no changes.

    powershell -NoProfile -ExecutionPolicy Bypass -File tools/start-ipad-tunnel.ps1          # start, then leave it open
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/start-ipad-tunnel.ps1 -Stop    # stop a run left behind

Needs cloudflared (winget install --id Cloudflare.cloudflared) and Python 3. Start Play mode in Unity first, or let
this start the local drawing server itself. It prints the iPad's address, copies it to the clipboard and opens a QR
code for the iPad's camera. Then, in Unity: F4, type the room code the iPad shows, Join.

The addresses are public and change every run. The local server has no sync key, so the pad's "#key=ipad" is only
there to skip its key prompt. Ctrl+C closes the tunnels and the page server.
#>
param(
    [switch]$Stop,
    [int]$PagePort = 8080,
    # cloudflared's transport. 'auto' tries QUIC (UDP) and falls back to http2 (TCP); pass http2 if it hangs.
    [ValidateSet('auto', 'quic', 'http2')][string]$Protocol = 'auto'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot '.utmp\ipad'
$pidFile = Join-Path $runtimeRoot 'pids.txt'
$localServer = 'http://127.0.0.1:3000'
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Stop-Started {
    if (!(Test-Path -LiteralPath $pidFile)) { return }
    foreach ($id in Get-Content -LiteralPath $pidFile) {
        if (!$id) { continue }
        # A stale file's ids may belong to something else by now: only stop our own kinds of process.
        $process = Get-Process -Id ([int]$id) -ErrorAction SilentlyContinue
        if ($process -and $process.ProcessName -in @('cloudflared', 'python', 'python3')) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $pidFile -ErrorAction SilentlyContinue
}

function Test-Url([string]$url) {
    try { return (Invoke-WebRequest $url -TimeoutSec 4 -UseBasicParsing).StatusCode -eq 200 } catch { return $false }
}

function Test-PortInUse([int]$port) {
    $client = New-Object Net.Sockets.TcpClient
    try { $client.Connect('127.0.0.1', $port); return $true } catch { return $false } finally { $client.Dispose() }
}

# The processes write these logs while they run, so read them without locking them.
function Read-Shared([string]$path) {
    if (!(Test-Path -LiteralPath $path)) { return '' }
    try {
        $stream = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite, Delete')
        try { return (New-Object IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Dispose() }
    } catch { return '' }
}

function Find-Cloudflared {
    $command = Get-Command cloudflared -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    # winget installs here, but the PATH change only reaches PowerShell windows opened afterwards.
    foreach ($path in @("${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe", "$env:ProgramFiles\cloudflared\cloudflared.exe")) {
        if (Test-Path -LiteralPath $path) { return $path }
    }
    return $null
}

function Find-Python {
    # Run python.exe itself, never the 'py' launcher: stopping the launcher would leave its python child serving the
    # port. 'py' only finds it, ahead of 'python', which on many PCs is the Microsoft Store stub.
    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($py) {
        try {
            $exe = (& $py.Source -3 -c 'import sys; print(sys.executable)' | Select-Object -First 1).Trim()
            if ($exe -and (Test-Path -LiteralPath $exe)) { return $exe }
        } catch {}
    }
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python -and $python.Source -notlike '*WindowsApps*') { return $python.Source }
    return $null
}

function Start-Logged([string]$name, [string]$exe, [string[]]$arguments, [string]$workingDir) {
    $log = Join-Path $runtimeRoot "$name.log"
    Remove-Item -LiteralPath $log, "$log.out" -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $workingDir -WindowStyle Hidden `
        -RedirectStandardOutput "$log.out" -RedirectStandardError $log -PassThru
    Add-Content -LiteralPath $pidFile -Value $process.Id
    return @{ Name = $name; Process = $process; Log = $log }
}

function Start-Tunnel([string]$name, [int]$port) {
    return Start-Logged $name $cloudflared @('tunnel', '--no-autoupdate', '--protocol', $Protocol, '--url', "http://127.0.0.1:$port") $projectRoot
}

function Wait-TunnelUrl($tunnel) {
    for ($i = 0; $i -lt 120; $i++) {
        $text = Read-Shared $tunnel.Log
        if ($text -match 'https://[a-z0-9-]+\.trycloudflare\.com') { return $Matches[0] }
        if ($tunnel.Process.HasExited) {
            throw "The $($tunnel.Name) tunnel stopped: $((Read-Shared $tunnel.Log).Trim() -split "`n" | Select-Object -Last 3)"
        }
        Start-Sleep -Milliseconds 500
    }
    throw "No $($tunnel.Name) tunnel address after 60 s. The network may be blocking Cloudflare tunnels; try -Protocol http2. Log: $($tunnel.Log)"
}

function Wait-Public([string]$url) {
    for ($i = 0; $i -lt 20; $i++) {
        if (Test-Url $url) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

# A run left behind (closed window, crash) still holds the ports.
Stop-Started
if ($Stop) {
    Write-Output '[iPad] Stopped the tunnels and the page server.'
    exit 0
}

try {
    $cloudflared = Find-Cloudflared
    if (!$cloudflared) { throw 'cloudflared not found. Run: winget install --id Cloudflare.cloudflared  (then open a new PowerShell window).' }
    $python = Find-Python
    if (!$python) { throw 'Python 3 not found (py or python on PATH).' }

    if (!(Test-Url "$localServer/v1/ping")) {
        Write-Output '[iPad] The local drawing server is not running; starting it (this can take a minute the first time)...'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'start-local-board.ps1') | Out-Host
        if (!(Test-Url "$localServer/v1/ping")) { throw 'The local drawing server did not start. See .utmp\spacetime\setup.log.' }
    }
    if (Test-PortInUse $PagePort) { throw "Port $PagePort is already in use. Close what's using it, or pass -PagePort 8081." }

    Write-Output '[iPad] Serving the drawing pad and opening two Cloudflare tunnels...'
    $page = Start-Logged 'page' $python @('-m', 'http.server', "$PagePort", '--bind', '127.0.0.1') (Join-Path $PSScriptRoot 'whiteboard')
    $serverTunnel = Start-Tunnel 'server-tunnel' 3000
    $pageTunnel = Start-Tunnel 'page-tunnel' $PagePort
    $serverUrl = Wait-TunnelUrl $serverTunnel
    $pageUrl = Wait-TunnelUrl $pageTunnel
    if ($page.Process.HasExited) { throw "The page server stopped: $(Read-Shared $page.Log)" }

    $ipadUrl = "$pageUrl/index.html?mode=board&server=$([uri]::EscapeDataString($serverUrl))#key=ipad"

    # New tunnel names take a few seconds to resolve. This laptop may cache a too-early miss for a while, so a failure
    # here is only a warning: the iPad usually works anyway.
    Write-Output '[iPad] Waiting for the tunnels to come up...'
    $serverUp = Wait-Public "$serverUrl/v1/ping"
    $pageUp = Wait-Public "$pageUrl/index.html"
    if (!$serverUp -or !$pageUp) {
        Write-Warning 'This laptop cannot reach the tunnels yet. Try the iPad anyway; if it says offline, wait a minute and reload.'
    }

    try { Set-Clipboard -Value $ipadUrl } catch {}
    $qrPage = Join-Path $runtimeRoot 'ipad.html'
    $urlJson = ConvertTo-Json $ipadUrl
    @"
<!doctype html><meta charset="utf-8"><title>ThinkInk iPad pad</title>
<style>body{font:18px system-ui,sans-serif;display:grid;place-items:center;gap:16px;margin:40px;text-align:center}
code{word-break:break-all;max-width:640px;font-size:13px}</style>
<h2>Scan with the iPad's camera</h2><div id="qr"></div><code id="url"></code>
<p>Then in Unity: F4, type the room code the iPad shows, Join.</p>
<script src="https://cdnjs.cloudflare.com/ajax/libs/qrcodejs/1.0.0/qrcode.min.js"></script>
<script>const url = $urlJson; document.getElementById('url').textContent = url;
new QRCode(document.getElementById('qr'), { text: url, width: 360, height: 360 });</script>
"@ | Set-Content -LiteralPath $qrPage -Encoding UTF8
    Start-Process $qrPage

    Write-Output ''
    Write-Output '[iPad] Ready. Open this on the iPad (it is on the clipboard, and the QR code page is open):'
    Write-Output "    $ipadUrl"
    Write-Output '[iPad] Then in Unity (Play mode): F4, type the room code the iPad shows, Join.'
    Write-Output '[iPad] Reloading the iPad page makes a new room: join its new code in Unity.'
    Write-Output '[iPad] Leave this window open. Ctrl+C closes the tunnels.'

    $watched = @($page, $serverTunnel, $pageTunnel)
    while ($true) {
        foreach ($item in $watched) {
            if ($item.Process.HasExited) {
                throw "$($item.Name) stopped; run this script again for new addresses. Log: $($item.Log)"
            }
        }
        Start-Sleep -Seconds 2
    }
} catch {
    Write-Output "[iPad] Failed: $_"
    exit 1
} finally {
    Stop-Started
}
