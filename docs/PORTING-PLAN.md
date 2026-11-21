# JVH Ark Ascended Server Manager — Porting Plan

Turning Bletch1971's ARK Server Manager (ARK: Survival Evolved, "ASE") into a manager for
**ARK: Survival Ascended ("ASA")**, keeping C# / WPF on the latest .NET.

- **Branch:** `asa` (local only — no pushes, no release channel; the self-updater is intentionally off).
- **Upstream:** `origin` = Bletch1971/ServerManagers. `master` there holds release binaries only;
  source lives on `source` / `dotnet-8-update`. The repo has **no LICENSE file** — get permission
  before distributing anything.
- **File/line references** below are as of commit `f273acaf` and will drift; search by symbol name.

---

## Status

| Phase | State | Commits |
|---|---|---|
| 1. Detach from upstream services, rebrand | Done | `5e2a0e75`, `995c49d1` |
| 2. .NET Framework 4.6.2 → .NET 11, latest packages | Done | `e52b007f`, `6c59861b`, `c80cb7ba`, `f273acaf` |
| 3. ASA support | **Next** — milestones below | |
| 4. Cleanup / tech debt | Ongoing | |

What phases 1–2 changed is in the commit messages. Highlights that affect later work:

- Shared build settings live in `src/Directory.Build.props` (`net11.0-windows`).
- `ProcessUtils.OpenWithShell` must be used to open URLs/folders (plain `Process.Start(url)` throws on .NET).
- Use `Environment.ProcessPath` for "the app's .exe" (`Assembly.Location` is the `.dll` on .NET).
- Settings live in three places that must stay in sync: `Config.settings`, `Config.Designer.cs`, `App.config`.
- Conan Exiles projects and the upstream web API were removed.

---

## ASA facts this plan relies on

| Fact | Confidence | Source |
|---|---|---|
| Server is Steam app **2430930**, exe `ShooterGame\Binaries\Win64\ArkAscendedServer.exe` | Confirmed | ARK wiki: Dedicated server setup |
| Game port must be passed as `-port=` (`?Port=` is ignored, defaults to 7777) | Confirmed | ARK wiki |
| Mods: `-mods=<id1>,<id2>` with **CurseForge project IDs**; server downloads/updates them at startup. `ActiveMods`, `-automanagedmods`, `-MapModID`, `-GameModIds`, `-ActiveEvent` are obsolete | Confirmed | ARK wiki: Server configuration |
| Crossplay: `-ServerPlatform=PC+XSX+PS5` (or `ALL`); `-crossplay` / `-epiconly` obsolete | Confirmed (values from wiki summary) | ARK wiki |
| If `?ServerAdminPassword=` is on the command line it must be the **last** `?` option | Confirmed | ARK wiki |
| Max players: `-WinLiveMaxPlayers=N`; `?MaxPlayers=` is ignored | Strong (forum + guides) | Steam forums, wikily.gg |
| Query port is unused; servers list via Epic Online Services, so **A2S queries won't work** — use RCON (`ListPlayers`) | Strong (forums), verify live | Steam forums, ark.wiki.gg Server Browser |
| Saves: per-map subfolder under `ShooterGame\Saved\SavedArks\<Map>\` (also with `AltSaveDirectoryName`) | Confirmed | ARK wiki |
| `.ark` world saves are **SQLite**; `.arkprofile` / `.arktribe` use a new UE5 layout | Confirmed by multiple parsers | AsaSavegameToolkit, arkparse, ark-sa-save-tools |
| Player IDs are EOS IDs (hex), not SteamID64 | Likely, verify live | — |
| SOTF and PGM do not exist; maps use `_WP` names (`TheIsland_WP`, `ScorchedEarth_WP`, …) | Confirmed | — |
| Windows only (Linux needs Proton) | Confirmed | ARK wiki |

**Verify on a real ASA server before relying on it:** `ListPlayers` output format, whether `version.txt`
exists in the install, which INI settings ASE had that ASA ignores, `AllowedCheaterSteamIDs.txt` naming.

---

## Phase 3 — ASA support

Ordered so each milestone leaves the app runnable. Tick boxes as commits land.

### M1. Server identity, install and launch

- [x] App IDs: `AppIdServer` 376030 → 2430930, `AppId` 346110 → 2399830 (ASA client; only used for the
      `steam_appid.txt` check and Workshop). Remove `AppId_SotF` / `AppIdServer_SotF` in M4.
- [x] Exe / process: `ServerExe` → `ArkAscendedServer.exe`, `ServerProcessName` → `ArkAscendedServer`.
- [x] Verified on a real install: ASA's `Binaries\Win64\steam_appid.txt` holds `2399830`, so the
      `ServerProfile.Validate` app-ID check passes with the new `AppId`.
- [x] Process detection: `ServerStatusWatcher.GetServerProcessStatus` now matches `-port=<game port>`
      (`ServerCommandLineArgsMatchFormat`, plus `?MultiHome=` when an IP is set) instead of `?QueryPort=`.
- [ ] `GetServerArgs()` (`ServerProfile.cs` ~4091–4459):
  - [x] `?Port=` → `-port=`; drop `?QueryPort=`; `?MaxPlayers=` → `-WinLiveMaxPlayers=`.
  - [ ] Replace `-crossplay`, `-epiconly`, `-PublicIPForEpic`, `-UseDynamicConfig` with a
        `-ServerPlatform=` setting (new profile property + UI).
  - [ ] Drop ASE-only flags: `-TotalConversionMod`, `-ActiveEvent`, SOTF block, `-d3d10`, `-sm4`,
        `-nomansky`, `-ServerAllowAnsel`, `-UseVivox`, `-DisableRailgunPVP`, `-newsaveformat`,
        `-usestore`, `-webalarm` (verify each against the ASA option list).
  - [ ] Keep: cluster args, BattlEye (`-NoBattlEye` still valid), `-log`, `-culture`, `-servergamelog`, etc.
  - [ ] `AdditionalArgs` are glued on raw — enforce "`?ServerAdminPassword` last" if it ever goes on the command line.
- [ ] Ports: remove the query port from UI/validation/firewall; check whether ASA still uses the peer
      port (port+1) and drop it if not. Firewall rules: `ServerRuntime.StartAsync` ~527–554.
- [x] Startup alert text used `{port}` = query port → now the game port.
- [x] Install via SteamCMD `app_update 2430930` works (anonymous login, ~11.5 GB): test server at
      `Q:\asmdata\Servers\Server1`.
- [ ] ASA has **no `version.txt`** (upstream read the installed version from it; `GetServerVersion` returns
      an empty version when missing). Use the Steam build ID from `steamapps\appmanifest_2430930.acf`,
      or the version the server reports, instead.
- [ ] Check branch handling against the ASA depot.
- [ ] **Test:** install a fresh ASA server through the app, start it, confirm status shows Running and
      the generated `RunServer.cmd` / command line is correct.

### M2. Server status and players via RCON

A2S (QueryMaster) is used for status, player counts, the "Published" state, the stop-countdown
online check, and the Discord `info` command. Replace with process state + RCON.

- [ ] `ServerStatusWatcher` (~280–383): drop local/public A2S checks and the dead external-API path;
      Running = process alive + RCON reachable (when RCON is enabled), player list from `ListPlayers`.
- [ ] Decide what replaces "Published / publicly reachable" (probably drop it — no public query to ask).
- [ ] `ServerRuntime.ProcessStatusUpdate` (~239–442): player count, max players, version (was parsed
      from the A2S server name).
- [ ] `ServerRuntime.RegisterForUpdates` (~483–493) silently skips servers without a public IP — remove that dependency.
- [ ] `ServerApp.StopServer` (~436–486) online-player check → RCON.
- [ ] `DiscordBotHelper` `info` command (~163–176) → RCON.
- [ ] `ServerRCON` `listplayers` regex (~345) only accepts numeric IDs → accept EOS IDs (verify format live).
- [ ] RCON currently comes from `QueryMaster.Server.GetControl` (which also opens an A2S socket).
      Consider a small standalone Source-RCON client and retiring most of QueryMaster.
- [ ] RCON must be enabled for status to work — make that clear in the UI / validation.

### M3. Mods: Steam Workshop → CurseForge

~4,500 lines of Workshop code become unnecessary because the ASA server manages mods itself.

Decision: **plain CurseForge IDs** (no CurseForge API for now).

- [x] Profile: `ServerModIds` (comma-separated; Int64 validation fits CurseForge IDs) is emitted as `-mods=`
      and no longer written to GUS `ActiveMods`; `-TotalConversionMod` is no longer emitted.
- [x] Switched off the Workshop paths: mod update in `ServerApp.UpgradeLocal` and `ServerRuntime.UpgradeAsync`,
      the mod cache (`GetModList`), the 15-minute Steam mod status poll, and the Workshop checks in `Validate`.
- [x] Hidden: total conversion row, mod details / mod update buttons, out-of-date mod panel, global mod update
      options, Server Monitor "Update mods", and the "Steam Web API key missing" banner.
- [ ] Delete the now-dead code: `Utils/ModUtils.cs`, `WorkshopFilesWindow`, Workshop models in `ServerManager.Common/Model`,
      `SteamUtils` mod/Workshop queries, mod sections of `ServerApp.UpgradeLocal` / `UpdateFiles` /
      `UpdateModCache` / `GetModList`, `ServerRuntime.UpdateModStatus` / mod part of `UpgradeAsync`,
      mod validation in `ServerProfile.Validate` (~5043–5281), mod update settings and UI.
- [ ] Remove the Steam Web API key requirement (MainWindow red banner; `DisplayModInformation` gating).
- [ ] `ModDetailsWindow`: rework into a simple CurseForge ID list editor. Optional: show names via the
      CurseForge API (needs an API key — decision below).
- [ ] Optional: `-passivemods=` support.

### M4. Remove SOTF and PGM

- [ ] `ServerProfile` SOTF region (~3700–3828), PGM region (~3593–3617) and their Save/Load/Reset/Sync code.
- [ ] `Lib/Model/PGMTerrain.cs`, `GameData/PGM.gamedata`, SOTF/PGM branches in `GameData.cs` / `GameDataUtils.IsSotF`.
- [ ] UI: SOTF/PGM expanders and every expander trigger in `ServerSettingsControl.xaml`; `GlobalSettingsControl`
      section toggles; `ShutdownWindow` SOTF bits; `ServerProfileCategory` / `ServerSettingsResetAction` enum values.
- [ ] Settings (`SectionSOTFEnabled`, `SectionPGMEnabled`, SOTF app IDs, PGM paths) and resource keys.
- [ ] Note: loading a SOTF/PGM profile flips global section flags (`ServerProfile.cs` ~4554–4558).

### M5. Saves, backups and player data

- [ ] Save paths: `ServerProfile.GetProfileSavePath` (~7342) has no per-map subfolder. Add the map to the
      path and update callers (`Server.cs`, `ServerApp` save/world-file getters and backup,
      `ServerPlayers`, `ServerRCON`, `PlayerListWindow`, `RCONWindow`, `WorldSaveRestoreWindow`,
      `RestoreSaveFiles`). `PlayerListParameters` needs the map name.
- [ ] Backup/restore (`ServerApp.CreateServerBackupArchiveFile` ~2114–2318, `RestoreSaveFiles` ~5296–5436):
      new file layout; SQLite `.ark` must not be copied mid-write (check for `-wal` / `-shm` files).
- [x] Decision: **hide the player list for now.** Hidden: Player List button (server settings), Server Monitor
      player-list button when RCON is off, RCON "View Profile" / "View Tribe"; the RCON window's save-file
      player enrichment (`ServerRCON.UpdatePlayerDetailsAsync`) is switched off.
- [ ] Later: replace `ArkData` (ASE binary format + Steam name lookups) with **AsaSavegameToolkit**
      (NuGet, MIT, .NET 8+, reads `.ark` / `.arkprofile` / `.arktribe`). Steam name lookups
      (`DataContainerAsync.LoadSteamAsync`, cheater/whitelist lists) won't work with EOS IDs.
- [ ] Delete unused `ArkData/SSQLib` (~1,000 lines).

### M6. Maps and game data

- [x] Map list: the ten playable maps found in the installed server's pak tables of contents —
      `TheIsland_WP`, `ScorchedEarth_WP`, `TheCenter_WP`, `Aberration_WP`, `Extinction_WP`, `Ragnarok_WP`,
      `Valguero_WP`, `LostColony_WP`, `Genesis_WP`, `Astraeos_WP` (in `SurvivalEvolved.gamedata`; ASE map
      entries removed from the other files). Mod maps can still be typed into the editable map box.
- [ ] `GameData/*.gamedata` (14 files, ~22k lines of ASE creatures/engrams/items/crates/maps) must be
      rebuilt for ASA. Decide the source (manual, community data, or extracted from the server).
      Start with maps only; ship ASE content files disabled until replaced.
- [ ] Resource keys: map names, ~2,300 class-name keys × 9 languages.
- [ ] `GameDataApplication` filter (`"ark"`), the GameData window and its forum link.

### M7. Settings review (`ServerProfile.cs`, 451 properties / 305 INI keys)

- [ ] Remove clearly ASE-only settings: Extinction event, Genesis/Gen2, Hexagons, Ragnarok section,
      Fjordur, Crystal Isles, cryopod nerf (verify), command-line-only ASE flags, `BanListURL` default
      (`arkdedicated.com`).
- [ ] Add ASA-only settings worth exposing (`-ServerPlatform`, `-passivemods`, `-DisableCustomCosmetics`,
      `-GBUsageToForceRestart`, ASA-era INI keys — research list).
- [ ] Fix: `ClusterDirOverride` is synced unconditionally in profile sync (~6429, duplicate line outside the `if`).
- [ ] Profile sync (`ProfileSyncWindow`, `SyncSettings` ~6287–7066) — trim sections for removed features.

### M8. Links, text and branding

- [ ] Patch-notes URLs (`AppPatchNotesUrl` → ASA changelog; remove `AppPatchNotesUrlSotF`).
- [ ] Old ASM forum help links (`HelpUrl`, `GameDataUrl`, `ServerManagerPluginUrl`, `DiscordBotHelpUrl`,
      `SteamWebAPIKeyHelpUrl`) → ASA docs or remove.
- [ ] Remaining "Survival Evolved" / ASE wording in resource strings.
- [ ] `VersionFeed.xml` / `VersionFeedBeta.xml` (Bletch's release history) — replace or remove.
- [ ] App icon / artwork (still ASE ARK art).

---

## Phase 4 — Cleanup and tech debt

- [ ] `WebClient` / `HttpWebRequest` (SYSLIB0014, ~30 sites) → shared `HttpClient`.
- [ ] WFO2000 analyzer warnings (82): command getters that create a new instance on every call.
- [ ] SYSLIB0051 legacy serialization constructors on 3 exception types; CA2200 rethrows in ArkData.
- [ ] Dead code: external status API path, `ClustersDir` setting, `SteamServerDetail*` models,
      `SteamUtils.GetSteamProcess`, `LoadOnlinePlayers*`.
- [ ] Self-updater (`ServerManager.Updater`): disabled; its single-exe swap logic doesn't fit .NET's
      multi-file output. Remove it, or rework if a release channel is ever wanted.
- [ ] `.azure/` and `.github/` CI configs are upstream's (Azure DevOps, Inno Setup installer that still
      builds `ARK Server Manager.exe`) — delete or replace.
- [ ] Unit tests: only `Plugin.Common.UnitTests` is safe to run. `QueryMaster.UnitTests` queries real IPs
      (incl. a public one), `ZipUtilsUnitTest` needs `D:\_smtest`, `DiscordPluginUnitTest` touches plugin config.
      Make these self-contained or delete them; add tests for argument building and path logic.
- [ ] Consider .slnx, nullable reference types, and splitting `ServerProfile.cs` (7k lines) / `ServerSettingsControl` (11k lines).

---

## Manual test checklist (run after each milestone)

- [ ] App starts (admin prompt, first-run data folder on a clean settings folder), main window shows.
- [ ] Profiles: "+" adds, "x" deletes with confirmation, switching tabs keeps each tab's state.
- [ ] Folder pickers (install dir, backup dir), Help and other links open in the browser.
- [ ] Install/update server via SteamCMD; start, stop (with countdown), restart.
- [ ] Status and player count update; RCON window connects, auto-scrolls, kick/ban work.
- [ ] Backup and restore a world save.
- [ ] Scheduled tasks (auto-start, auto-update, auto-backup, auto-shutdown) are created and actually run
      (they must point at the `.exe`, not the `.dll`).
- [ ] Server Monitor: "Create shortcut" produces a working desktop shortcut.
- [ ] Discord bot (if used) connects and answers `info`.

---

## Decisions needed

1. **CurseForge API key** — show mod names/update info in the UI (needs a free key from CurseForge),
   or keep mods as a plain ID list? Recommendation: plain IDs first, API later.
2. **Player list** — adopt AsaSavegameToolkit now, or hide the player/tribe windows until later?
   Recommendation: hide first (M5), integrate the toolkit as a separate step.
3. **Game data source** for ASA creatures/engrams/items (M6).
4. **Keep or drop** the Discord bot, Discord plugin and plugin system.
5. **License / permission** from the original author before sharing builds.

## Sources

- ARK wiki — [Dedicated server setup](https://ark.wiki.gg/wiki/Dedicated_server_setup),
  [Server configuration](https://ark.wiki.gg/wiki/Server_configuration),
  [Server Browser](https://ark.wiki.gg/wiki/Server_Browser)
- Max players: [Steam discussion](https://steamcommunity.com/app/2399830/discussions/0/3881599433115394720),
  [wikily.gg ASA dedicated server guide](https://wikily.gg/ark-survival-ascended/dedicated-server)
- Query port / EOS: [Steam discussion](https://steamcommunity.com/app/2399830/discussions/0/4343238874469046643)
- Save format: [AsaSavegameToolkit (NuGet)](https://www.nuget.org/packages/AsaSavegameToolkit),
  [arkparser (PyPI)](https://pypi.org/project/arkparser/), [ark-sa-save-tools](https://github.com/Kakoen/ark-sa-save-tools)
