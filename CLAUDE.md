# CLAUDE.md — MilkBarLauncher2

Fork of Milk Bar Launcher 2.0.1 (Breath of the Wild multiplayer for Cemu). Fixes and design
notes are in `MILKBAR2.md` — read it first. Upstream is archived (Nintendo DMCA'd the creator's
videos); its LICENSE is "All rights reserved", so this stays a **GitHub fork** (no Codeberg, no
re-licensing, no bundling of Nintendo-derived assets beyond what upstream already has).

- Fork: https://github.com/umin135/MilkBarLauncher2 (`origin`), upstream: `upstream`.
- Branches: `main` (= `milkbar2`, both at the fixes). Feature work on a branch, then fast-forward `main`.
- The user writes in Korean — **reply in Korean**.
- Commit/push only when the user asks; end commit messages with
  `Co-Authored-By: Claude <noreply@anthropic.com>`.

## Repo map
| Path | What |
|---|---|
| `C#/BOTWM.Server` | Server library (`MBL.Server.dll`): networking (`Server.cs`), packet (de)serialization (`JSONBuilder/`), sync state (`ServerClasses/`). |
| `C#/BOTW.DedicatedServer` | Console host (`MBL.DedicatedServer.exe`), reads `ServerConfig.ini`, `Gamemodes.json`. |
| `DLL/InjectDLL` | C++ DLL injected into Cemu: memory scanning, remote players (`Player.cpp`, `EquipmentAccess.h`), socket client (`Client.cpp`), main loop (`dllmain_Functions.cpp`), quest sync (`QuestSync.cpp`). |
| `WPF .NET 6/Breath of the Wild Multiplayer` | Launcher (WPF, .NET 8 at runtime): server browser, model select, `Source files/GameFilesModifier.cs` (builds the remote-player model, rewrites the local player's actor packs via `Resources/aapmLib.exe`). |
| `BNP Files` | Upstream BCML mod (Nintendo-derived data; leave as upstream ships it). |
| `Tools/` | Ours: `CustomModelBuilder` (MP_ models), `AampLibWrapper`, `NetTests` (split-packet tests), `IlDump` (prints string/call sequence per .NET method — handy to inspect release binaries). |

## Protocol facts (needed for any networking change)
- Client → server messages are fixed size: **ping (type byte 1) = 6144 B** (launcher), **connect 2 /
  update 3 / disconnect 4 = 7168 B** (DLL `sendBytes`). First byte = `MessageType`.
- Server → client: `[u16 little-endian length][payload]`.
- Strings: `[u8 byte length][UTF-8 bytes]` (server now uses UTF-8 byte counts; launcher-side
  `JSONBuilder` copies may still need the same audit).
- Request/response lockstep per client; `SerializationRate` (60) drives the client loop.

## Build (this PC)
- **InjectDLL**: `"A:\Program Files\Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" DLL\InjectDLL\InjectDLL.vcxproj -p:Configuration=Release -p:Platform=x64` → output goes to `..\..\Build\Resources\InjectDLL.dll` (relative to the project).
  A build of unmodified upstream source matched the 2.0.1 release.
- **MBL.Server**: `dotnet build -c Release` in `C#/BOTWM.Server`. The csproj uses NuGet
  (`MadMilkman.Ini`, `Newtonsoft.Json`) + a ProjectReference to `C#/BOTW.Logging`. Offline
  alternative used before: swap them for `<Reference HintPath>` to the DLLs in
  `D:\GameFolder\WIIU\MilkBar\MilkBarLauncher\DedicatedServer\`. Release method list was verified
  identical to 2.0.1.
- **Launcher / DedicatedServer**: not yet built from this repo (needs NuGet restore; launcher
  references BfresLibrary/SarcWrapper etc.). The launcher fix is currently deployed as an IL patch
  of the release DLL (ildasm/ilasm, see below), equivalent to the source change.
- **aapmLib.exe**: the checked-in exe is the OLD buggy build. Rebuild from
  `Resources/aapmLib.py`: `pip install oead pyinstaller` then `pyinstaller --onefile aapmLib.py`.
- Tools available: .NET SDKs 3.1–9, `ildasm` (`C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8.1 Tools\x64\ildasm.exe`),
  `ilasm` (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\ilasm.exe`), Python 3.8 venv with
  `oead`/`bcml` at `D:\Tools\botw-venv` (`Scripts\python.exe`), 7-Zip.

## Deployment on this PC (what the user actually plays with)
- Cemu 1.26.2: `D:\GameFolder\WIIU\CEMU_1.26.2` (JPN BotW v208 + DLC, Korean patch baked into the dump;
  update/DLC are junctions in `mlc01`). Graphic packs needed: BCML merged, `bcmlPatches\MilkBarLauncher`,
  Extended Memory, plus `MilkBar_Linkle` (personal Linkle model, CC BY-NC-ND — never commit it).
- Launcher in use: `D:\GameFolder\WIIU\MilkBar\MilkBarLauncher` — patched files have `.orig`
  backups; patched copies + sources + `INSTALL_PENDING.ps1` live in `D:\GameFolder\WIIU\MilkBar\patches`.
  `Resources\aapmLib.exe` there is a .NET wrapper (`Tools/AampLibWrapper`) running
  `aapmLib_fixed.py` with the Python in `aapmLib_fixed.cfg`, fallback `aapmLib.orig.exe`;
  logs: `%APPDATA%\BOTWM\Temp\aapmLib_fixed*.log`.
- Server: `...\DedicatedServer` (`ServerConfig.ini`: Hamachi IP `25.33.219.86`, port 5050,
  default gamemode "MasterCoop": quest + divine-beast sync off). Logs: `LatestLog.txt`, `Logs\`.
- BCML (Wii U, portable): `D:\GameFolder\WIIU\BCML` — run `BCML (Wii U).bat`, or scripts with
  `--portable` on the real command line (`remerge.py --portable`). After any BCML merge run
  `fix_cemu_settings.ps1` (normalizes Cemu `settings.xml`). A remerge wipes the launcher-generated
  `Jugador…`/`Player_Animation_NoFace` files; the launcher regenerates them on next connect.
- Friend install kit (no Milk Bar binaries inside; downloads the official 2.0.1 zip):
  `D:\GameFolder\WIIU\FriendKit\BotW_MP_Setup` → `D:\GameFolder\WIIU_Export\BotW_MP_FriendKit.7z`.
- Never replace files while Cemu / launcher / server are running (check `Get-Process Cemu,'Milk Bar Launcher',MBL.DedicatedServer`).

## Testing
- Network: start a copy of `DedicatedServer` with `ServerConfig.ini` `IP=127.0.0.1`, a spare port, then
  `python Tools/NetTests/pingtest.py <port> split` and `conntest.py <port> split <name>`
  (upstream server crashes on the split connect; fixed one logs "<name> joined").
- Game-side changes need the user to run Cemu; ask for `%APPDATA%\BOTWM\LatestLog.txt`,
  Cemu `log.txt`, and `crashdump\*.txt` (InjectDLL ships a PDB → symbolize offsets with dbghelp).

## Traps we already hit
- **Claude desktop app shell is MSIX-virtualized**: writes to `%APPDATA%`/`%LOCALAPPDATA%` from our
  shell go to a private copy the real apps never see. Anything the launcher/BCML must read from
  AppData has to be done by the user (or kept on `D:`).
- BCML 3.10.8 portable bug: the Rust side reads `<cwd>\bcml-data\bcml\settings.json`, Python reads
  `<cwd>\bcml-data\settings.json` → keep them as a hardlink. Rust also ignores Python's `sys.argv`,
  so `--portable` must be a real argument.
- BCML export fails with os error 183 if `graphicPacks\BreathOfTheWild_BCML` is a real folder.
- oead `Sarc`/`ParameterIO` views crash (segfault) if the source bytes object is garbage-collected — keep references.
- In bash, `printf 'D:\Tools\botw-venv'` turns `\b` into a backspace; write Windows paths with PowerShell or Python.
- PowerShell 5.1 + `$ErrorActionPreference='Stop'` turns native stderr into terminating errors.
- Wii U textures: mip 0 lives in `.Tex1`, the mip chain in `.Tex2`; a mod that replaces only `.Tex1`
  shows the vanilla mips as noise unless those names are removed from `.Tex2`.

## ToDo
1. **Rebuild `aapmLib.exe` from the fixed `aapmLib.py`** (PyInstaller + oead) so the fix works without
   a separate Python; then drop the wrapper from the deployment and the friend kit could ship it…
   only if redistribution of our own rebuilt tool is acceptable (it contains no upstream code beyond the I/O contract).
2. **Local NPC models still hang on load** ("Play as model" with `Npc_…`), even with correct `str64`
   values. Compare against the working `MP_` path (which keeps the Link armor setup): likely the
   `isNotLink=true` branch (all armor actors → NPC model, `Player_Animation_NoFace`) or missing RSTB
   for edited actor packs. Also a pause-menu crash was seen with an NPC (`PauseMenuPlayer` edits).
3. **Friend's Cemu crash after ~45 min** (2026-10-07 19:53, friend "AwerQxcv", ~70 ms ping): server
   log shows the client closed the connection (no server error). Need the friend's
   `%APPDATA%\BOTWM\Logs\*` (session before reconnect) and Cemu `crashdump\*.txt`. Their DLL is
   upstream, so also check whether a server-side change can avoid triggering client crashes.
4. **Build the launcher and DedicatedServer from this repo** (NuGet restore) and replace the
   IL-patched release binaries; keep the `CharacterModel`/NpcData behaviour identical.
5. Audit the launcher's own `JSONBuilder` (ping/connect parsing) for the same `string.Length`
   vs UTF-8 issue fixed on the server.
6. Optional: proper `MP_` model packaging (graphic pack + units file) and a Linkle-free sample, RSTB
   entries for new model folders if loading issues appear.
