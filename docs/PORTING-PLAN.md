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
| Query port is unused; servers list via Epic Online Services, so **A2S queries won't work** — use RCON (`ListPlayers`) | Verified live: the server binds UDP 27015 but never answers A2S; RCON `ListPlayers` works | Steam forums, ark.wiki.gg Server Browser |
| Server log (`ShooterGame\Saved\Logs\ShooterGame.log`) has `ARK Version: 98.9` near the top and `Server has completed startup and is now advertising for join` when ready | Verified live (ARK 98.9) | test server |
| Saves: per-map subfolder under `ShooterGame\Saved\SavedArks\<Map>\` (also with `AltSaveDirectoryName`) | Confirmed | ARK wiki |
| `.ark` world saves are **SQLite**; `.arkprofile` / `.arktribe` use a new UE5 layout | Confirmed by multiple parsers | AsaSavegameToolkit, arkparse, ark-sa-save-tools |
| Player IDs are EOS IDs (hex), not SteamID64 | Verified live: 32 hex digits, e.g. `0002b0e8…6db0.arkprofile` | test server |
| Save folder `SavedArks\TheIsland_WP\` holds `TheIsland_WP.ark`, `<EOS id>.arkprofile`, the server's own timestamped backups `TheIsland_WP_21.11.2026_23.28.20.arkrbf` and `TheIsland_WP_AntiCorruptionBackup.bak` | Verified live | test server |
| SOTF and PGM do not exist; maps use `_WP` names (`TheIsland_WP`, `ScorchedEarth_WP`, …) | Confirmed | — |
| Windows only (Linux needs Proton) | Confirmed | ARK wiki |

**Verify on a real ASA server before relying on it:** `ListPlayers` output with players connected (empty
server replies `No Players Connected`), which INI settings ASE had that ASA ignores,
`AllowedCheaterSteamIDs.txt` naming. (Verified: there is no `version.txt`.)

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
- [x] `GetServerArgs()` (`ServerProfile.cs`):
  - [x] `?Port=` → `-port=`; `?MaxPlayers=` → `-WinLiveMaxPlayers=`.
  - [x] `-ServerPlatform=` setting ("Players from": PC (Steam), PC (Microsoft Store), Xbox, PlayStation →
        `PC`, `WINGDK`, `XSX`, `PS5`, or `ALL` when all are ticked; default all, like the server). Replaces
        the `-crossplay`, `-epiconly`, `-PublicIPForEpic` checkboxes. At least one is required (validation).
  - [x] Options dropped by checking the **ASA server binary** for each option name (UE parses options as
        `TEXT("Name")` literals, so an option the server reads has its name in the exe as UTF-16; the wiki's
        ASE/ASA marks were not reliable enough). Not in the binary, so no longer passed and hidden in the UI:
        `?EventColorsChanceOverride`, `?NewYear1UTC/2UTC`, `-NewYearEvent`, `-UseBattlEye` (BattlEye is on
        unless `-NoBattlEye`), `-noantispeedhack`, `-speedhackbias`, `-nocombineclientmoves`, `-nomansky`,
        `-nomemorybias`, `-ServerAllowAnsel`, `-structurememopts`, `-UseStructureStasisGrid`,
        `-SecureSendArKPayload`, `-UseSecureSpawnRules`, `-epiconly`, `-PublicIPForEpic`, `-UseVivox`,
        `-newsaveformat`. Kept because the binary has them: `-log`, `-nosteamclient`, `-game`, `-server`,
        `-MinimumTimeBetweenInventoryRetrieval`, `-webalarm`, `-UseDynamicConfig`, `-ActiveEvent`, `-usestore`,
        `-d3d10`, `-sm4`, `-lowmemory`, `-NoHangDetection`, `-StasisKeepControllers`, `-UseItemDupeCheck`,
        cluster options, `-servergamelog*`, `-exclusivejoin`, `-NoDinos`, `-imprintlimit`, `-culture`, etc.
        Re-run the check after big server updates (scratchpad script `exe_options.py`; could become a test).
  - [x] Their profile properties still exist (old profiles load); delete them in the Phase 4 cleanup.
  - [ ] Options in the binary that the app doesn't offer yet: `-passivemods`, `-NoWildBabies`,
        `-GBUsageToForceRestart`, `-BattlEyeServerRecheck`, `-AlwaysTickDedicatedSkeletalMeshes` (M7).
  - [ ] `AdditionalArgs` are glued on raw — enforce "`?ServerAdminPassword` last" if it ever goes on the command line.
- [x] Ports: ASA binds no peer port (verified with netstat: only the game port, UDP 27015 and RCON), so the
      peer port is hidden and no longer gets a firewall rule. The server **does** open UDP 27015 (Steam)
      although it doesn't answer queries there, so the query port stays (field, validation, firewall) and
      `?QueryPort=` is passed again so each server on a machine can have its own.
- [x] Verified: the server honours `?QueryPort=` (started with 27016, it bound UDP 27016 instead of 27015).
      Ports it opens: game port (UDP), query port (UDP), RCON (TCP), plus two TCP ports on 127.0.0.1 only
      (8888 and one ephemeral) that need no firewall rule.
- [x] **Test:** restarted with the new arguments (`-ServerPlatform=ALL`, `?QueryPort=27016`, no dropped
      options): starts and is joinable as before; the app shows version 98.9 from the exe.
- [x] Startup alert text used `{port}` = query port → now the game port.
- [x] Install via SteamCMD `app_update 2430930` works (anonymous login, ~11.5 GB): test server at
      `Q:\asmdata\Servers\Server1`.
- [x] ASA has **no `version.txt`**. The server exe's version resource has an `ArkVersion` entry (`98.9`),
      plus `ProductVersion` `5.8.3-841389+++ARK1+Rel-1.97`. `ServerApp.GetServerVersion(installDirectory)`
      reads `ArkVersion` via `GetFileVersionInfo`/`VerQueryValue` (`VersionUtils.GetVersionResourceString`;
      .NET's `FileVersionInfo` returns nothing for this exe). Used for the installed and cached versions, and
      when a profile is loaded, so the version shows before the server has ever run. Update detection was
      already based on file times, not the version. `Config.VersionFile` is now unused (Phase 4).
- [x] Branch handling: nothing ASA-specific (Steam beta branches and passwords work the same way).
- [x] **Test:** installed a fresh ASA server through the app (`Q:\asmdata\Servers\Server2`) and started it:
      `RunServer.cmd` is correct, the server starts in ~1 minute (game port 7777/UDP, RCON 32330/TCP),
      session name and RCON settings come through from `GameUserSettings.ini`, and the app detects the
      process. Status stayed "Initializing" until M2 (A2S).
- [ ] Note: a new profile defaults to the next free `ServerN` folder and must be installed before Start
      is enabled (upstream behaviour); pointing it at an existing install also works.

### M2. Server status and players via RCON

A2S (QueryMaster) is used for status, player counts, the "Published" state, the stop-countdown
online check, and the Discord `info` command. Replace with process state + RCON.

- [x] `ServerStatusWatcher`: A2S checks and the dead external-API path removed. A running process counts
      as started (Running / Available) once it answers RCON `ListPlayers` (kept open between polls; also
      gives the player count) or, without RCON, once its log says it is advertising for join. The log
      also gives the running server's version (`ARK Version: x.y`).
- [x] "Published / publicly reachable": dropped. "Available" now means "finished starting and advertising";
      whether the server is reachable from the internet (port forwarding) is no longer checked.
- [x] `ServerRuntime.ProcessStatusUpdate`: player count from RCON, max players from the profile, version
      from the server log.
- [x] `ServerRuntime.RegisterForUpdates` no longer needs a query port or the machine's public IP.
- [x] `ServerApp.StopServer` online-player check → RCON (`ListPlayers`); no RCON → count down in full.
- [x] `DiscordBotHelper` `info` command → the app's own status (players via RCON).
- [x] `ServerRCON` `listplayers` regex accepts alphanumeric (EOS) IDs. Verify with a player connected.
- [x] RCON password no longer written to the profile debug log.
- [ ] RCON currently comes from `QueryMaster.Server.GetControl` (which also opens an A2S socket).
      Consider a small standalone Source-RCON client and retiring most of QueryMaster.
- [ ] Without RCON there is no player count — make that clear in the UI (or default RCON on for new profiles).
- [x] **Test:** with the test server running, the app reports Running / Available, 0 players, within one
      poll of starting, over a single RCON connection that stays open.
- [ ] **Test:** player count and RCON player list change when a player joins; stop with countdown skips
      the countdown on an empty server.

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

- [x] Removed (2,483 lines): `ServerProfile` SOTF and PGM regions with their arguments, load/save/reset/sync
      code; `PGMTerrain`, `PGM.gamedata`; the SOTF variants of the map / total conversion / branch / event
      lists (`GameData`, `IsSotF` in the game data model and files); the SOTF and PGM sections, their header
      templates and every SOTF/PGM trigger in the settings screen, main window, shutdown window and styles;
      the global "show SOTF / PGM section" options; the `ServerProfileCategory` / `ServerSettingsResetAction`
      values; settings `AppId_SotF`, `AppIdServer_SotF`, `AppPatchNotesUrlSotF`, `DefaultServerMap_PGM`,
      `SavedPGMRelativePath`, `DefaultPGMapName`, `Section{SOTF,PGM}Enabled`, `Section{SOTF,PGM}IsExpanded`.
- [ ] Their resource strings remain in the language files (pruned with all unused keys at the end of M3).

### M5. Saves, backups and player data

- [x] Save paths: `ServerProfile.GetProfileSavePath(installDirectory, altSaveDirectoryName, serverMap)` now
      ends in the map folder (`SavedArks\<map>`, or `Saved\<AltSaveDirectoryName>\<map>`); all callers updated
      (world file check, backup, restore, reset, player list / RCON parameters). The RCON window now passes the
      map and the player list window the alt save folder name (both were missing).
- [ ] Verify the `AltSaveDirectoryName` layout (`Saved\<alt>\<map>`, per the wiki) on a live server.
- [x] Backup: the world save, `.arkprofile`, `.arktribe` and `.arktributetribe` files are copied to a temp
      folder first (full sharing, so the server's writes are never blocked; retried if an SQLite `-journal`
      / `-wal` file appears or the file changes during the copy), then zipped as before. Verified live: the
      server does not hold the `.ark` open between saves, and has no journal file at rest.
- [x] Restore window lists the server's own backups (`<map>_<date>.arkrbf`, SQLite like the world save) next
      to the world save and the app's zip backups; restoring one copies it over `<map>.ark` (unchanged code).
      The server writes an `.arkrbf` on every save (autosave or `saveworld`; date in UTC) and prunes old ones
      itself (4 were kept on the test server).
- [x] **Test:** manual backup with the server running: it saved first, then wrote a zip with the world save
      and player file (CRCs OK, SQLite `integrity_check` = ok, no temp copies left). The restore window
      (server stopped) lists the world save, the zip and the `.arkrbf` backups. Restore itself not run yet.
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
