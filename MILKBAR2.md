# MilkBar2

Working fork of [Milk Bar Launcher](https://github.com/MilkBarModding/MilkBarLauncher) 2.0.1
(Breath of the Wild multiplayer for Cemu, Wii U v208). Branch `milkbar2` holds the fixes below
on top of upstream `main` (22d5184).

> **License:** upstream is "Copyright (c) 2024. All rights reserved." with no open-source
> license. Keep this fork private until the original authors grant permission/a license.

## Fixes

### Server (`C#/BOTWM.Server`)
| Problem | Effect | Fix |
|---|---|---|
| `handleClient` appended the whole 10 240-byte receive buffer (`data.AddRange(info.ToList())`) instead of the bytes actually received. | Any message split by TCP (high ping, relayed Hamachi) was corrupted: "Error receiving message", bad data forwarded to other players, and a split connect message crashed the whole server (`ArgumentOutOfRangeException` in `ServerData.GetPlayer`). | `Server.cs`: append only received bytes and cut exactly one message (client messages are fixed size: ping = 6 144 B, connect/update/disconnect = 7 168 B); leftovers are kept for the next message. |
| String length prefixes used `string.Length` (UTF-16 chars) while the bytes were UTF-8. | Any non-ASCII player name (e.g. Korean) shifted the packet; the client read `SerializationRate = 0` and Cemu crashed (divide by zero). | `JSONBuilder.cs`: all prefixes use `Encoding.UTF8.GetByteCount`. |

Verified with a local test server: split connect message → original server crashed, fixed server
accepted it; "우민" now serializes as `06 EC 9A B0 EB AF BC` (was `02 …`).

### Dedicated server (`C#/BOTW.DedicatedServer`)
| Problem | Fix |
|---|---|
| `CopyAppdataFiles` stripped the prefix `BOTW.DedicatedServer.AppdataFiles.`, but the resources are named `BOTWM.…`, so `%APPDATA%\BOTWM\QuestFlagsNames.txt` was never created and the server failed on first start unless the launcher had been opened before. | Strip everything up to `AppdataFiles.` regardless of namespace. |

### Client DLL (`DLL/InjectDLL`)
| Problem | Fix |
|---|---|
| `Client::receiveBytes` did `memcpy(…, received - 2)`; when the server closed the socket (`recv` ≤ 0) this became a huge copy → Cemu crash. It also assumed the length prefix arrived in the first packet. | Read exactly `[u16 length][payload]`, stop cleanly on a closed connection (output stays zeroed). |
| `1000 / SerializationRate` with corrupt/empty data → divide by zero. | Guard: fall back to 60. |
| Custom player models could only be single-unit NPC models (no armor sync). | `EquipmentAccess::SetArmor(folder, head, upper, lower)` + `Player.cpp`: a model string starting with `MP_` uses the Jugador armor setup in folder `<name>_MP` (`MP_X@HHH-UUU-LLL` forces a fixed outfit). |

### Launcher (`WPF .NET 6`)
| Problem | Fix |
|---|---|
| `Resources/aapmLib.py` (python `aamp`, `set_param(name, str)`) rewrote every edited AAMP value as a plain string. ModelList `Folder`/`UnitName` must be `str64`, so "Play as model" was ignored (still Link) or caused an infinite load. | New `aapmLib.py` on `oead`: keeps each parameter's original type, repairs already-damaged `Folder`/`UnitName`/`BindBone`/`AttentionUser`, processes items independently (failures logged to `%APPDATA%\BOTWM\Temp\aapmLib_fixed_detail.log`), reads JSON with BOM. **Rebuild `Resources/aapmLib.exe`** from it (`pip install oead pyinstaller`, `pyinstaller --onefile aapmLib.py`); the checked-in exe is still the old build. `Tools/AampLibWrapper` is an alternative .NET exe that runs the script with an existing Python and falls back to the old exe. |
| Only `Npc_` models replaced the local player. | `ServerBrowserModel.connectToServer`: `MP_` models call `ModifyGameROMPlayerModel(model, model, isNotLink: false)` so Link's armor actors stay; `aapmLib.py` then maps every `Armor_NNN_*` / `Armor_Default_*` piece to `MP_<name>_MP` (`MP_Armor_…` units listed in `<model>_MP.units.txt` next to aapmLib) and `Armor_Default_Extra_*` to `MP_<name>`. |

## Custom `MP_` player models

A custom model needs two model folders (shipped e.g. as a Cemu graphic pack under `content/Model`):

* `MP_<name>.sbfres/.Tex1/.Tex2` – small local-player model: unit `MP_<name>` (body) plus
  `Armor_Default_Extra_00/01`.
* `MP_<name>_MP.sbfres/.Tex1/.Tex2` – remote/armor model with the same 113 unit names as the
  launcher-generated `Jugador1ModelNameLongForASpecificReason` (Head/Chest/EmptyModel/MP_Armor_*).

Add `{"Name": "<shown name>", "Folder": "MP_<name>"}` to `Resources/NpcData.json` and put
`MP_<name>_MP.units.txt` (one unit name per line) next to aapmLib.

`Tools/CustomModelBuilder` builds both folders by running the launcher's own
`GameFilesModifier.CreateModifiedModel()` on a fake dump (vanilla armor + the mod's overrides,
`prep_custom_model.py`), then `split` / `local` / `texprune` / `mipfix`:
Wii U textures take mip 0 from `.Tex1` and the mip chain from `.Tex2`, so every texture a mod
replaced in `.Tex1` without shipping its own `.Tex2` must be removed from `.Tex2` (otherwise the
vanilla mips show through as noise). New model folders have no RSTB entry; that has been fine so
far.

## Known issues / ideas
* Local NPC models ("Play as model" with `Npc_…`) still hang on load in testing.
* Pausing with a local NPC model crashed once (PauseMenuPlayer also gets the model).
* Upstream clients (unpatched DLL) still crash if the server drops them; the server fix removes
  the usual trigger.
