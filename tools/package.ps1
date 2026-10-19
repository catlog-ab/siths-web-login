<#
.SYNOPSIS
  Paketerar kortprogram och webbläsartillägg för en organisation.

.DESCRIPTION
  Läser en deployment-fil (se deployments/example.json) och skapar i dist/<hostName>/:
    extension/                     tillägget, klart att läsa in okomprimerat eller zippa till Chrome Web Store
    extension.zip                  samma sak som zip
    SithsWebLogin-<hostName>.zip   SithsWebLogin.exe + siths-host.json, för användarna

  Exe-filen byggs inte om här: samma (signerade) SithsWebLogin.exe används av alla organisationer.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Deployment deployments\foretaget.json
  powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Deployment deployments\foretaget.json -Exe C:\signerat\SithsWebLogin.exe
#>
param(
    [Parameter(Mandatory = $true)][string]$Deployment,
    [string]$Exe
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $root 'native-host\bin\SithsWebLogin.exe' }
if (-not (Test-Path $Exe)) { throw "Hittar inte $Exe. Kör native-host\build.cmd först." }

$d = Get-Content $Deployment -Raw -Encoding UTF8 | ConvertFrom-Json

# --- Kontroller ---
if ($d.hostName -notmatch '^[a-z0-9_]+(\.[a-z0-9_]+)*$') { throw "hostName '$($d.hostName)' är ogiltigt." }
$store = [ordered]@{}
if ($d.extension.store) {
    foreach ($b in 'chrome', 'edge') { if ($d.extension.store.$b) { $store[$b] = [string]$d.extension.store.$b } }
}
# Butiks-id:n måste också få prata med kortprogrammet.
$allIds = @(@($d.extension.ids) + @($store.Values) | Where-Object { $_ } | Select-Object -Unique)
if ($allIds.Count -eq 0) { throw 'extension.ids saknas.' }
foreach ($id in $allIds) { if ($id -notmatch '^[a-p]{32}$') { throw "Ogiltigt tilläggs-id: $id" } }
if (-not $d.sites.matchPatterns) { throw 'sites.matchPatterns saknas.' }
[void][regex]::new($d.sites.originPattern)
$heartbeat = if ($d.heartbeatSeconds) { [int]$d.heartbeatSeconds } else { 30 }
if ($heartbeat -lt 30) { throw 'heartbeatSeconds kan inte vara under 30 (Chromes minsta alarm-intervall).' }

$utf8 = New-Object Text.UTF8Encoding $false
$out = Join-Path $root "dist\$($d.hostName)"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

# --- Tillägget ---
$ext = Join-Path $out 'extension'
Copy-Item -Recurse (Join-Path $root 'extension') $ext

$m = Get-Content (Join-Path $ext 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$m.name = $d.extension.name
$m.version = $d.extension.version
$m.action.default_title = $d.extension.name
$m.host_permissions = @($d.sites.matchPatterns)
$m.content_scripts[0].matches = @($d.sites.matchPatterns)
if ($d.extension.key) { $m.key = $d.extension.key }
else { $m.PSObject.Properties.Remove('key') }   # Chrome Web Store tilldelar id:t
[IO.File]::WriteAllText((Join-Path $ext 'manifest.json'), ($m | ConvertTo-Json -Depth 10), $utf8)

$cfg = [ordered]@{
    displayName      = $d.displayName
    hostName         = $d.hostName
    originPattern    = $d.sites.originPattern
    apiBase          = $(if ($d.api.base) { $d.api.base } else { '/api/siths/' })
    apiSuffix        = $(if ($null -ne $d.api.suffix) { $d.api.suffix } else { '.php' })
    heartbeatSeconds = $heartbeat
    downloadUrl      = $(if ($d.downloadUrl) { [string]$d.downloadUrl } else { $null })
}
$configJs = @"
// Genererad av tools/package.ps1 från $(Split-Path -Leaf $Deployment). Ändra i deployment-filen, inte här.
export const CONFIG = $($cfg | ConvertTo-Json);

const originRe = new RegExp(CONFIG.originPattern);

export function isSiteOrigin(origin) {
  return typeof origin === "string" && originRe.test(origin);
}
"@
[IO.File]::WriteAllText((Join-Path $ext 'config.js'), $configJs, $utf8)
# Butiksversionen: utan "key" (Chrome Web Store och Edge Add-ons tilldelar id). Den uppackade
# mappen ovan behåller en ev. nyckel så att utvecklingsinstallationen får samma id som förut.
$storeDir = Join-Path $out 'extension-store'
Copy-Item -Recurse $ext $storeDir
$sm = Get-Content (Join-Path $storeDir 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$sm.PSObject.Properties.Remove('key')
[IO.File]::WriteAllText((Join-Path $storeDir 'manifest.json'), ($sm | ConvertTo-Json -Depth 10), $utf8)
Compress-Archive -Path (Join-Path $storeDir '*') -DestinationPath (Join-Path $out 'extension.zip')
Remove-Item -Recurse -Force $storeDir

# --- Kortprogrammet ---
$hostDir = Join-Path $out 'host'
New-Item -ItemType Directory -Force $hostDir | Out-Null
Copy-Item $Exe (Join-Path $hostDir 'SithsWebLogin.exe')
$hostCfg = [ordered]@{
    hostName            = $d.hostName
    displayName         = $d.displayName
    allowedExtensionIds = $allIds
}
if ($store.Count -gt 0) { $hostCfg.storeExtensions = $store }
[IO.File]::WriteAllText((Join-Path $hostDir 'siths-host.json'), ($hostCfg | ConvertTo-Json), $utf8)
$zip = Join-Path $out "SithsWebLogin-$($d.hostName).zip"
Compress-Archive -Path (Join-Path $hostDir '*') -DestinationPath $zip
Remove-Item -Recurse -Force $hostDir

$sig = Get-AuthenticodeSignature $Exe
Write-Host "Klart: $out"
Write-Host "  extension\      (läs in okomprimerat vid test)"
Write-Host "  extension.zip   (ladda upp till Chrome Web Store / Edge Add-ons)"
Write-Host "  $(Split-Path -Leaf $zip)"
if ($sig.Status -ne 'Valid') { Write-Warning "SithsWebLogin.exe är inte signerad ($($sig.Status)). Smart App Control och AppLocker kan blockera den." }
