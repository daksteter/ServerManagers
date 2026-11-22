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
| 3. ASA support | M1–M8 done (M6: game data rebuilt; some engram data still missing) | `d554cb0a` plan; `dc58a01b`, `eedbd86a` M1; `0c1aef4d` maps; `ea7753e0`, `d91502ba` M3; `a6d0e38e` M2; `f4e015a2` M5; `d8450b5e` M4; `9609a942`; `9670bc09` M7; `6eb52bed` M8; `8ba195d7` M6; `c4dde3e0` player list; `0312f808`, `d08517cd` mod editor; RCON client |
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
| Profiles: `MyData` → `PlayerDataID`, `UniqueID` (EOS), `PlayerName`, `MyPlayerCharacterConfig.PlayerCharacterName`, `TribeID`; level = 1 + `CharacterStatusComponent_ExtraCharacterLevel`. Tribes: `TribeData` → `TribeName`, `TribeID`, `OwnerPlayerDataId` (UInt32), `MembersPlayerDataID`, `NumTribeDinos`. With `-usestore` both live in the world save's `GameModeCustomBytes` | Verified (test server profile, toolkit samples) | AsaSavegameToolkit |
| RCON (Source protocol): one reply per command (no output → `Server received, But no response!!`); wrong password → auth reply ID -1, then the connection is closed; two packets in one write, or an empty response packet, make the server stop answering on that connection | Verified live (probe against the test server) | test server |
| Player IDs are EOS IDs (hex), not SteamID64 | Verified live: 32 hex digits, e.g. `0002b0e8…6db0.arkprofile` | test server |
| Save folder `SavedArks\TheIsland_WP\` holds `TheIsland_WP.ark`, `<EOS id>.arkprofile`, the server's own timestamped backups `TheIsland_WP_21.11.2026_23.28.20.arkrbf` and `TheIsland_WP_AntiCorruptionBackup.bak` | Verified live | test server |
| SOTF and PGM do not exist; maps use `_WP` names (`TheIsland_WP`, `ScorchedEarth_WP`, …) | Confirmed | — |
| Windows only (Linux needs Proton) | Confirmed | ARK wiki |

**Verified on the test server:** there is no `version.txt`; `ListPlayers` replies `No Players Connected` when empty
and the player count went to 1 when a player joined; which INI settings ASE had that ASA ignores (M7).
**Still to verify:** the admin whitelist file is `AllowedCheaterAccountIDs.txt` in ASA (wiki; the name isn't a
literal in the server binary, so it can't be confirmed there). The app uses that name since M8.

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
  - [x] Their profile properties, reset/sync lines and hidden controls were deleted in M3 (old profiles still
        load; unknown keys are ignored).
  - [x] Options in the binary that the app didn't offer: `-passivemods`, `-NoWildBabies`,
        `-GBUsageToForceRestart`, `-AlwaysTickDedicatedSkeletalMeshes` added in M7. `-BattlEyeServerRecheck`
        not added (undocumented).
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
      `Q:\asmdata\Servers\Server1` (later deleted; the test server is now `Server2`).
- [x] ASA has **no `version.txt`**. The server exe's version resource has an `ArkVersion` entry (`98.9`),
      plus `ProductVersion` `5.8.3-841389+++ARK1+Rel-1.97`. `ServerApp.GetServerVersion(installDirectory)`
      reads `ArkVersion` via `GetFileVersionInfo`/`VerQueryValue` (`VersionUtils.GetVersionResourceString`;
      .NET's `FileVersionInfo` returns nothing for this exe). Used for the installed and cached versions, and
      when a profile is loaded, so the version shows before the server has ever run. Update detection was
      already based on file times, not the version. `Config.VersionFile` was removed in M3.
- [x] Branch handling: nothing ASA-specific (Steam beta branches and passwords work the same way).
- [x] **Test:** installed a fresh ASA server through the app (`Q:\asmdata\Servers\Server2`) and started it:
      `RunServer.cmd` is correct, the server starts in ~1 minute (game port 7777/UDP, RCON 32330/TCP),
      session name and RCON settings come through from `GameUserSettings.ini`, and the app detects the
      process. Status stayed "Initializing" until M2 (A2S).
- Note: a new profile defaults to the next free `ServerN` folder and must be installed before Start
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
- [x] `ServerRCON` `listplayers` regex accepts alphanumeric (EOS) IDs.
- [x] RCON password no longer written to the profile debug log.
- [x] Own Source-RCON client (`ServerManager.Common\Lib\RconClient.cs`, .NET sockets only, no package) replaces
      QueryMaster's; the QueryMaster project (3,466 lines: A2S, GoldSource, master server, logs) and its unit
      tests (which queried real IPs) are deleted. Used by the status watcher, the RCON window, the stop
      countdown / save / broadcasts (`ServerApp`) and the MOTD (`ServerRuntime`).
      - Fixes: replies are read by their length (QueryMaster read once into a 4,110-byte buffer, so longer or
        fragmented replies were cut off and the rest read as the next reply); a wrong password is reported
        (`RconAuthenticationException`, logged with a hint to check the admin password; no pointless retries)
        instead of passing silently; no unused A2S socket per connection; the RCON window closes its
        connection when it closes.
      - Commands are sent strictly one at a time (one write per packet, no Nagle buffering): the ASA server
        stops answering on a connection that gets two packets in one write, or the empty "end of reply" packet
        some Source clients use, so a reply split over packets is joined from what has already arrived.
      - `RconClientUnitTest`: a fake server on loopback (auth, wrong password, a reply over 4 KB, a reply in
        fragments, a reply over two packets, timeout, several threads). Also checked against the test server.
- [x] **Test:** status and player count, RCON window (commands, chat, player list, kick), broadcast and MOTD,
      stop with countdown and save, all over the new client.
- [ ] Without RCON there is no player count — make that clear in the UI (or default RCON on for new profiles).
- [x] **Test:** with the test server running, the app reports Running / Available, 0 players, within one
      poll of starting, over a single RCON connection that stays open.
- [x] **Test:** the player count went to 1 when a player joined; stop with countdown works.
- [ ] **Test:** the RCON window's player list with a player connected (EOS ID parsing); stop skips the
      countdown on an empty server.

### M3. Mods: Steam Workshop → CurseForge

~4,500 lines of Workshop code become unnecessary because the ASA server manages mods itself.

Decision: **plain CurseForge IDs** (no CurseForge API for now).

- [x] Profile: `ServerModIds` (comma-separated; Int64 validation fits CurseForge IDs) is emitted as `-mods=`
      and no longer written to GUS `ActiveMods`; `-TotalConversionMod` is no longer emitted.
- [x] Switched off the Workshop paths: mod update in `ServerApp.UpgradeLocal` and `ServerRuntime.UpgradeAsync`,
      the mod cache (`GetModList`), the 15-minute Steam mod status poll, and the Workshop checks in `Validate`.
- [x] Hidden: total conversion row, mod details / mod update buttons, out-of-date mod panel, global mod update
      options, Server Monitor "Update mods", and the "Steam Web API key missing" banner.
- [x] Deleted the Workshop code: `ModUtils`, `ModDetailsWindow`, `WorkshopFilesWindow`, `ModDetail*` and the
      Workshop / published-file models, `SteamUtils` mod queries, the mod parts of `ServerApp.UpgradeLocal` /
      `UpdateFiles`, `UpdateModCache`, `GetModList`, the mod cache step of the auto-update, `ServerRuntime`
      mod status (counts, timer, alert) and the mod part of `UpgradeAsync` (no more `updateMods` parameters),
      total conversions and official mods (profile, game data, UI), the Server Monitor "Update mods" command,
      `ServerUpdateType.Mods`, the global mod update options and mod alert, and the Steam Web API key banner.
      The auto-update's parallel branch loop now gets one `ServerApp` per branch (it shared one variable).
- [x] Also removed the ASE-only launch options hidden in M1 (properties, reset/sync, controls) and the peer port.
- [x] Removed 22 app settings nothing referenced any more (Workshop/mod options, the Steam-query status URL and
      delay, `VersionFile`, and eight that were already unused upstream).
- [x] The Steam Web API key is gone (with the `ArkData` replacement, M5): the setting and its global settings row,
      upstream's built-in key (`DefaultSteamAPIKey` in App.config), `SteamUtils.GetSteamUserDetails` and its models.
- [x] `ModDetailsWindow` replacement: a mod list editor (`ModListWindow`), opened from the edit button next to
      Mod IDs and next to Passive mod IDs. Add (IDs separated by commas / spaces, or `curseforge.com/projects/<id>`
      links), remove, move up / down (the order is the load order), open the mod's CurseForge page
      (`curseforge.com/projects/<id>` redirects to it); OK writes the list back to the box.
      - Names, installed files and file dates come from the server's own mod library
        (`ShooterGame\Binaries\Win64\ShooterGame\ModsUserData\83374\library.json`, `installedMods`: CurseForge
        `details` + `installedFile` records, property names in odd case such as `iD`, `uRL`, `filename`; dates
        as `2026.11.20-07.02.47`), so no CurseForge API key is needed; a mod's name shows once the server has
        downloaded it. `local\user_info.json` only lists the installed mod IDs (with load order, enabled state and
        the server's CurseForge sign-in token); only its `installedMods` is read, as a fallback.
      - The mods are downloaded to `ShooterGame\Binaries\Win64\ShooterGame\Mods\83374\<modId>_<fileId>\`.
- [x] **Test:** edit the list (add, remove, reorder, OK / Cancel).
- [x] **Test:** with a mod the server has downloaded (JVH Landscaping), the editor shows its name. The reader
      was fixed against the test server's real `library.json` (the first version read only `user_info.json`,
      which has no names).
- [x] Optional: `-passivemods=` support (M7: "Passive mod IDs" below Mod IDs).
- [x] Game data files no longer contain `TotalConversions` / `OfficialMods` arrays (M6).

### M4. Remove SOTF and PGM

- [x] Removed (2,483 lines): `ServerProfile` SOTF and PGM regions with their arguments, load/save/reset/sync
      code; `PGMTerrain`, `PGM.gamedata`; the SOTF variants of the map / total conversion / branch / event
      lists (`GameData`, `IsSotF` in the game data model and files); the SOTF and PGM sections, their header
      templates and every SOTF/PGM trigger in the settings screen, main window, shutdown window and styles;
      the global "show SOTF / PGM section" options; the `ServerProfileCategory` / `ServerSettingsResetAction`
      values; settings `AppId_SotF`, `AppIdServer_SotF`, `AppPatchNotesUrlSotF`, `DefaultServerMap_PGM`,
      `SavedPGMRelativePath`, `DefaultPGMapName`, `Section{SOTF,PGM}Enabled`, `Section{SOTF,PGM}IsExpanded`.
- [x] Their resource strings were pruned at the end of M3. Left on purpose: `Mod_PGM` and the `*_PGM_C`
      spawn-entry names, which are game class names looked up from game data (they go with M6).

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
- [x] The player list was hidden at first (ASE-only `ArkData`), then brought back on **AsaSavegameToolkit 1.0.2**
      (NuGet, MIT): `ArkData` keeps its `DataContainer` / `PlayerData` / `TribeData` API but reads the ASA
      `.arkprofile` / `.arktribe` files with the toolkit (the old byte-search parser, the Steam lookups and
      `SSQLib` are deleted).
      - Player: EOS ID (= file name), platform name (as saved at the last login), character name, level,
        in-game player ID, tribe. Tribe: ID, name, owner, member IDs, tamed creature count.
      - Files are read with full sharing (never block the server) and cached until they change, so the
        5-second refresh only re-reads what the server wrote since. A profile that can't be parsed shows as
        "< corrupted profile >".
      - `-usestore` servers keep profiles and tribes in the world save (`custom` table, `GameModeCustomBytes`):
        read from a temp copy of the `.ark`, only after the server has saved again.
      - Back: Player List button (server settings), Server Monitor player-list button when RCON is off,
        RCON "View Profile" / "View Tribe", and the RCON window's offline players from the save files (online
        players without a profile yet — joined since the last save — are no longer dropped).
      - Checked against the test server's profile and the toolkit's sample profiles / tribes / `-usestore`
        save (levels, tribe and owner links, embedded data), then live with a tribe on the test server.
- [x] Admin / exclusive-join / whitelist lists: names come from the players' profile files (`<id>.arkprofile`)
      instead of the Steam Web API (which doesn't know EOS IDs); IDs without a profile show `<not available>`
      (also on `-usestore` servers, whose profiles have no files).
- [x] Only the Windows builds of the toolkit's native SQLite library are copied to the output (~30 MB saved).
- [x] **Test:** Player List window (server settings, and Server Monitor with RCON off), profile window
      (explorer link), the RCON window's offline players (shown with Players → Filter → Show Offline; the
      default filter is online only) and "View Profile", admin names in Server Files.
- [x] **Test:** with a tribe created on the test server: tribe window (name, owner, members).

### M6. Maps and game data

- [x] Map list: the ten playable maps found in the installed server's pak tables of contents —
      `TheIsland_WP`, `ScorchedEarth_WP`, `TheCenter_WP`, `Aberration_WP`, `Extinction_WP`, `Ragnarok_WP`,
      `Valguero_WP`, `LostColony_WP`, `Genesis_WP`, `Astraeos_WP` (in `SurvivalEvolved.gamedata`; ASE map
      entries removed from the other files). Mod maps can still be typed into the editable map box.
- [x] Game data rebuilt for ASA (`GameData/*.gamedata`, version 2.0.0) from three sources: the installed server's
      pak directory index (which classes exist — 207,568 asset paths, read from the `.utoc` files), the existing
      ASE data (names, engram levels/points, tameability for classes that carried over) and the ARK wiki's Cargo
      tables `Items`, `Creatures`, `Entities`, `Craftables` plus its Creature IDs and Engram class names pages
      (names, categories, tameability, engram levels/points for new content). Scripts in the scratchpad:
      `utoc_index.py`, `asa_gamedata.py`.
  - Kept: every ASE class that exists in ASA (310 creatures, 481 engrams, 986 items, 203 spawners, 101 crates,
    27 drop inventories). Dropped: classes ASA doesn't have (Primitive Plus entirely; ASE structure engrams
    replaced by the integrated S+ ones; a few map-specific creatures, spawners and inventories).
  - Added: 253 creatures (wiki entries marked as in ASA whose class exists on the server), 593 items (wiki items
    whose class exists), 71 engrams (only where the wiki gives level and points for a unique match), 151 map
    spawners, 77 supply crates and 151 drop inventories (names derived from the class names).
  - New creature name tags come from the wiki's Creature IDs page for base creatures (it agrees with the ASE data
    for 131 of 137 base creatures); variants get none, so their spawn-weight cells stay hidden.
  - Files: `SurvivalEvolved.gamedata` → `Base.gamedata`; new `LostColony`, `Astraeos`, `BobsTallTales`;
    removed `PrimitivePlus`, `CrystalIsles`, `Fjordur`, `Genesis2`, `LostIsland`; `OfficialMods` /
    `TotalConversions` arrays removed. Item categories normalised (e.g. `Resource` → `Resources`).
  - The build now deletes `.gamedata` files from the output that are no longer in the project (the app loads
    every file in the folder, so stale ones would come back).
- [x] Mod groups: labels and row colours for Lost Colony, Astraeos, Bob's Tall Tales (and the missing Valguero
      label); Primitive Plus, Crystal Isles, Genesis 2, Lost Island, Fjordur and PGM removed.
- [x] Resource keys: 758 class-name strings for classes no longer in the game data pruned in all languages.
- [x] `GameDataApplication` filter (`"ark"`) still fits (ASA is "ark" too); the GameData window's forum link went in M8.
- [ ] 332 new ASA engrams aren't listed (no reliable level / points: mostly Bob's Tall Tales pack items, Lost
      Colony gear and skins). They can still be overridden by class name in the engram grid.
- [ ] Names for the new spawners, supply crates and drop inventories are derived from class names (e.g.
      "Lost Loot Chest Cave T1"); improve when better data turns up.
- [x] **Test:** creature, engram, item, crate and spawner lists show ASA content with the new mod colours; the
      app starts without game-data errors.

### M7. Settings review (`ServerProfile.cs`, 451 properties / 305 INI keys)

Method: every INI key and launch option the manager writes was checked against the ASA server binary (key names
appear as UTF-16 `GConfig` lookups or ASCII reflection names) and against the ARK wiki's server configuration page,
whose raw wikitext marks each option `inASA` / `inASE`. Map-specific keys read by map blueprints can't be found in the
exe (the content paks are compressed), so the maps the server ships with were read from the pak directory indexes:
The Island, Scorched Earth, The Center, Aberration, Extinction, Astraeos, Ragnarok, Valguero, Lost Colony, Genesis
(no Fjordur, Crystal Isles, Lost Island or Genesis 2).

- [x] Removed (not in the ASA binary, not listed for ASA): Fjordur traversal buff (+ its "Fjordur settings" group),
      `AllowIntegratedSPlusStructures`, NPC network stasis range scaling (override + 3 keys), `bDisableDinoBreeding`.
- [x] Kept, contrary to the first plan: Extinction event, Genesis, Hexagons, Ragnarok, cryopod nerf — their maps ship
      with ASA and the binary still reads the keys. Genesis 2 world buffs etc. are still read by the binary; kept.
- [x] `BanListURL` default → ASA's official list `https://cdn2.arkdedicated.com/asa/BanList.txt` (from the binary).
- [x] Added ASA-only settings (wiki `inASA = Yes`, `inASE = No`), all written only when changed from the server
      default (`WriteIfNotValue`), or nullable (checkbox + slider) where the default isn't known:
  - Launch options (Administration > Server options): `-NoWildBabies`, `-disabledinonetrangescaling`,
    `-DisableCustomCosmetics`, `-DoCustomCosmeticValidation`, `-AlwaysTickDedicatedSkeletalMeshes`,
    `-GBUsageToForceRestart=` (nullable); `-passivemods=` (Maps and mods, below Mod IDs).
  - GameUserSettings: `CosmeticWhitelistOverride`, `AdminListURL` (Server options); cryopods (fridge on saddles,
    enemy check, fridge requirement), Cryo Hospital (4), outposts (3 nullable + sigil multiplier), implant respawn
    cooldown, PvP mounted weapons, max train cars, Wandering King Titan spawn time (Rules); soft tame limit (3),
    Cosmo ammo (2), Armadoggo / Veilwyn cooldowns, burrow decay, Gacha caves (Dinos); bunkers (10), Bloodforge (3)
    (Structures).
  - Game.ini: photo mode (2), tribe tower bonus, Valguero memorial names (Rules); `bAllowSpeedLeveling` (Dinos);
    Tek wireless crafting (5), generator limits (2, nullable) (Structures).
  - Not added: vendor/debug options (`-EnableSteelShield`, `-NitradoQueryPort`, `-NoAI`, `-NoDinosExcept*`, thread
    flags), `UseCharacterTracker` (undocumented), `CheatTeleportLocations` (list editor needed).
- [x] Fix: profile sync copied `ClusterDirOverride` even with cluster sync off (duplicate line outside the `if`).
- [x] Profile sync and section resets include the new settings; removed settings are gone from both.
- Note: new strings are en-US only (other languages fall back to English).
- [ ] **Test:** settings page shows the new groups and options; changing a setting writes it to the INI / command
      line, and resetting the section removes it again.

### M8. Links, text and branding

- [x] Links: `AppPatchNotesUrl` → the ARK wiki's Patch Notes page (current ASA patch first); `HelpUrl` → the
      wiki's Dedicated server setup; `SteamWebAPIKeyHelpUrl` → steamcommunity.com/dev; `DiscordBotHelpUrl` →
      Discord's getting-started guide. (`AppPatchNotesUrlSotF` went in M4.)
- [x] Removed the ASM-forum buttons and settings (`GameDataUrl`, `ServerManagerPluginUrl`): no forum exists for
      this app. The app's own patch-notes buttons (main window, Server Monitor) hide when no release feed or
      patch-notes URL is configured (none is), like the Donate / Discord buttons.
- [x] Deleted Bletch's release history (`VersionFeed.xml` / `VersionFeedBeta.xml`, app and Discord plugin).
- [x] Discord plugin detached from upstream: it reported the machine's public IP to the upstream API every 12 hours
      (`CallHomeAsync`) and checked / downloaded plugin updates from Bletch1971's GitHub. The call-home is
      removed, the update and feed URLs are blank, and the update check skips an empty URL.
- [x] Admin whitelist file → `AllowedCheaterAccountIDs.txt` (ASA's name, per the wiki); an existing
      `AllowedCheaterSteamIDs.txt` written by earlier versions is renamed when the profile's server files load.
- [x] Wording (en-US): RCON help and other texts say player ID instead of Steam ID; the server name tooltip no
      longer mentions Steam. Unused keys (`RCON_Menu_ViewSteamProfile`, `ProfileValidation_TCModSteamError`, the
      forum tooltips) and regions emptied by earlier removals pruned in all languages. No "Survival Evolved" /
      "ARK Server Manager" text was left in the English strings or the code.
- [x] App icon: an original icon (wrench over a three-toed track on a teal tile) replaces the ASE ARK-logo icon
      (`Art/favicon.ico`, 16–256 px; drawn by a script, no ARK / Wildcard artwork).
- Note: the other languages' RCON help still says "steam id" (translations not updated).
- [x] **Test:** the new icon shows on the window and taskbar; Help opens the wiki; the server patch notes button
      opens the wiki's Patch Notes; the main window and Server Monitor have no patch-notes button; Game Data and
      Plugins windows have no forum button; admins added in Server Files land in `AllowedCheaterAccountIDs.txt`.

---

## Phase 4 — Cleanup and tech debt

- [ ] `WebClient` / `HttpWebRequest` (SYSLIB0014, ~30 sites) → shared `HttpClient`.
- [ ] WFO2000 analyzer warnings (82): command getters that create a new instance on every call.
- [ ] SYSLIB0051 legacy serialization constructors on 3 exception types.
- [x] Dead code removed: the external status API path (M2), the `ClustersDir` setting (M3).
- [x] `ArkData` rewritten on AsaSavegameToolkit: its A2S client (`SSQLib`), `LoadOnlinePlayers*` and Steam lookups
      are gone.
- [ ] Dead code left: `SteamServerDetail*` models, `SteamUtils.GetSteamProcess`.
- [ ] Self-updater (`ServerManager.Updater`): disabled; its single-exe swap logic doesn't fit .NET's
      multi-file output. Remove it, or rework if a release channel is ever wanted.
- [ ] `.azure/` and `.github/` CI configs are upstream's (Azure DevOps, Inno Setup installer that still
      builds `ARK Server Manager.exe`) — delete or replace.
- [ ] Unit tests: `ZipUtilsUnitTest` needs `D:\_smtest`, `DiscordPluginUnitTest` touches plugin config
      (`QueryMaster.UnitTests`, which queried real IPs, went with QueryMaster; `RconClientUnitTest` is
      self-contained). Make the rest self-contained or delete them; add tests for argument building and path logic.
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

1. **CurseForge API key** — names of downloaded mods now come from the server's own mod library, so a key
   would only add names of mods the server hasn't downloaded yet, and update info. Not needed for now.
2. ~~**Player list**~~ — decided: hidden in M5, then brought back on AsaSavegameToolkit.
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
- Save format: [AsaSavegameToolkit (NuGet)](https://www.nuget.org/packages/AsaSavegameToolkit)
  ([source](https://github.com/flintthatchwood/AsaSavegameToolkit)),
  [arkparser (PyPI)](https://pypi.org/project/arkparser/), [ark-sa-save-tools](https://github.com/Kakoen/ark-sa-save-tools)
