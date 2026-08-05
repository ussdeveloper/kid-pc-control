# Kid PC Control

Parental control for **Windows 11** — Admin tray + Kid service on the local network.

Repository: https://github.com/ussdeveloper/kid-pc-control

## v0.2

- Admin **tray** + autostart
- **LAN control API** (policy push, block/unblock, apps, URLs, screen JPEG)
- Schedule + max continuous use + device block enforcement
- App whitelist enforcement
- URL regex filter via local proxy + on-screen message
- Live screen preview in Admin
- GitHub Release ships `KidPcControl-Setup-vX.Y.Z.exe`

## Build installer

```powershell
.\installer\build-installer.ps1
```

Output: `KidPcControl-Setup-v0.2.0.exe`

## Usage

1. Install on Kid PC → Setup → **Kid** (name + admin password)  
2. Install on parent PC → Setup → **Admin** (stays in tray)  
3. Open Admin panel from tray → select Kid → send policy / block / preview  

Policy & status: `%ProgramData%\KidPcControl\`

## Notes

- URL filtering uses a local HTTP proxy (`127.0.0.1:47893`) when regex rules exist; HTTPS sites are logged by Host/CONNECT best-effort.
- Screen preview is periodic JPEG from the Kid user session (Agent), not a full video codec.
- Whitelist empty = all apps allowed; non-empty = only listed process names (plus system essentials).
