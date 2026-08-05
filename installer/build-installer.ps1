# Build KidPcControl-Setup-vX.Y.Z.exe
# Requires: .NET 8 SDK, Inno Setup 6

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = "0.1.0"
$props = Join-Path $root "Directory.Build.props"
if (Test-Path $props) {
  $m = Select-String -Path $props -Pattern "<Version>([^<]+)</Version>" | Select-Object -First 1
  if ($m) { $version = $m.Matches[0].Groups[1].Value }
}

$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found." }

$out = Join-Path $root "installer\payload"
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $out, (Join-Path $root "installer\output") | Out-Null

$projects = @(
  "src\KidPcControl.Admin\KidPcControl.Admin.csproj",
  "src\KidPcControl.Service\KidPcControl.Service.csproj",
  "src\KidPcControl.Tray\KidPcControl.Tray.csproj",
  "src\KidPcControl.Agent\KidPcControl.Agent.csproj",
  "src\KidPcControl.Setup\KidPcControl.Setup.csproj"
)

foreach ($p in $projects) {
  Write-Host "Publishing $p"
  dotnet publish $p -c Release -r win-x64 --self-contained true -o $out
  if ($LASTEXITCODE -ne 0) { throw "Publish failed: $p" }
}

# Sync version in iss
$iss = Join-Path $root "installer\KidPcControl.iss"
(Get-Content $iss -Raw) -replace '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$version`"" | Set-Content $iss -NoNewline

Push-Location (Join-Path $root "installer")
& $iscc "KidPcControl.iss"
Pop-Location

$setup = Join-Path $root "installer\output\KidPcControl-Setup-v$version.exe"
if (-not (Test-Path $setup)) { throw "Installer not created: $setup" }

Copy-Item $setup (Join-Path $root "KidPcControl-Setup-v$version.exe") -Force
Write-Host ""
Write-Host "Installer ready:"
Write-Host "  $setup"
Write-Host "  $(Join-Path $root "KidPcControl-Setup-v$version.exe")"
