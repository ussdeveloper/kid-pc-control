# Kid PC Control

Parental control for **Windows 11** — Admin panel + Kid service on the local network.

Repository: https://github.com/ussdeveloper/kid-pc-control

## Features (v0.1 foundation)

- Installer/setup asks **Admin** or **Kid**
- Kid: Windows Service + tray icon + local admin-password override (disable limits / add time / until end of day)
- LAN discovery (UDP) so Admin sees Kid devices
- Dark Windows 11 style UI
- Auto-update check against GitHub Releases (ETag, 6h interval, no embedded PAT)

## Build

```powershell
dotnet build KidPcControl.sln -c Release
```

Publish for installer payload:

```powershell
$out = "installer/payload"
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
dotnet publish src/KidPcControl.Admin/KidPcControl.Admin.csproj -c Release -r win-x64 --self-contained true -o $out
dotnet publish src/KidPcControl.Service/KidPcControl.Service.csproj -c Release -r win-x64 --self-contained true -o $out
dotnet publish src/KidPcControl.Tray/KidPcControl.Tray.csproj -c Release -r win-x64 --self-contained true -o $out
dotnet publish src/KidPcControl.Agent/KidPcControl.Agent.csproj -c Release -r win-x64 --self-contained true -o $out
dotnet publish src/KidPcControl.Setup/KidPcControl.Setup.csproj -c Release -r win-x64 --self-contained true -o $out
```

Then compile `installer/KidPcControl.iss` with Inno Setup (optional).

## Dev run

1. Run `KidPcControl.Setup` as Administrator → choose Kid, set name + password  
2. Or run `KidPcControl.Service` (console) + `KidPcControl.Tray`  
3. On another machine/session run `KidPcControl.Admin` — Kid should appear in the list within a few seconds  

Policy/status live in `%ProgramData%\KidPcControl\`.

## Release

Push a tag `vX.Y.Z` — GitHub Actions builds a zip release asset.

## Roadmap

- gRPC policy push Admin → Kid  
- App whitelist enforcement  
- URL regex filter + on-screen block message  
- Live screen preview  
- Silent apply of downloaded updates  
