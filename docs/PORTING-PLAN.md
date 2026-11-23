# JVH Ark Ascended Server Manager — Porting Plan

Turning Bletch1971's ARK Server Manager (ARK: Survival Evolved, "ASE") into a manager for
**ARK: Survival Ascended ("ASA")**, keeping C# / WPF on the latest .NET.

- **Branch:** `main` on a private GitHub repo (`origin`); no release channel, and the self-updater is removed.
- **Upstream:** `upstream` = Bletch1971/ServerManagers. `master` there holds release binaries only;
  source lives on `source` / `dotnet-8-update`. The repo has **no LICENSE file** — get permission
  before distributing anything.
- **File/line references** below are as of commit `f273acaf` and will drift; search by symbol name.

---

## Status

| Phase | State | Commits |
|---|---|---|
| 1. Detach from upstream services, rebrand | Done | `5e2a0e75`, `995c49d1` |
| 2. .NET Framework 4.6.2 → .NET 11, latest packages | Done | `e52b007f`, `6c59861b`, `c80cb7ba`, `f273acaf` |
| 3. ASA support | M1–M8 done (M6: game data rebuilt; some engram data still missing) | `d554cb0a` plan; `dc58a01b`, `eedbd86a` M1; `0c1aef4d` maps; `ea7753e0`, `d91502ba` M3; `a6d0e38e` M2; `f4e015a2` M5; `d8450b5e` M4; `9609a942`; `9670bc09` M7; `6eb52bed` M8; `8ba195d7` M6; `c4dde3e0` player list; `0312f808`, `d08517cd` mod editor; `96da4227` RCON client; message mode |
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
| RCON `broadcast` doesn't reliably reach players (the on-screen message sometimes never shows); `serverchat` does | Seen by the user with another RCON client; no ASA-specific report found | user |
| Player IDs are EOS IDs (hex), not SteamID64 | Verified live: 32 hex digits, e.g. `0002b0e8…6db0.arkprofile` | test server |
| Save folder `SavedArks\TheIsland_WP\` holds `TheIsland_WP.ark`, `<EOS id>.arkprofile`, the server's own timestamped backups `TheIsland_WP_21.11.2026_23.28.20.arkrbf` and `TheIsland_WP_AntiCorruptionBackup.bak` | Verified live | test server |
| Ctrl+C in the server console makes ASA save and exit cleanly (`Closing by request`, then `Log file closed` in its log); a killed server just stops, unsaved, with no closing lines. Whether `saveworld` answers only once the save is done is assumed (commands run on the game thread), not measured on a large world | Seen in the test server's logs and save files (a manual Ctrl+C at 23:29 saved; the app's plain Stop at 22:48 did not) | test server |
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
  - [x] "`?ServerAdminPassword` last": nothing to enforce. The admin password is written to
        `GameUserSettings.ini`, never to the command line, and Server Args (`AdditionalArgs`) go after the app's
        own `?` options, so a `?ServerAdminPassword=` typed there is already last unless more `?` options follow it
        in the same box. The Server Args tooltip's example (`?MaxPlayers=5?QueryPort=9999`: ASA ignores the
        first, the app sets the second) is now a neutral `?SettingName=Value -OptionName`.
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
- [x] Messages default to server chat: the shutdown / update countdown, backup world-save, repeating MOTD and
      RCON window Save World / Destroy Wild Dinos messages go out as `serverchat` (the "Global" mode) instead
      of `broadcast`, which doesn't reliably reach players on ASA. Broadcast stays selectable in Settings (both
      RCON mode boxes; the tooltip recommends Global). One mapping (`ServerRcon.GetMessageCommand`) for every
      sender: the repeating MOTD and the RCON window buttons used to send the mode name itself
      (`global <message>`, not a command) in Global mode.
- [x] **Test:** stop with countdown (messages show in chat), the repeating MOTD (MOTD interval on), RCON window
      Save World.
- [x] Without RCON there is no player count — made clear in the UI: the status watcher reports why the count
      isn't known (`PlayerCountStatus`: RCON off, no RCON reply yet, password rejected), and the profile's status
      bar, the Server Monitor and the Shutdown window show "–" with a tooltip that says why and what to do; the
      Discord `info` reply adds the same note. The "player count changed" alert compares with the last known
      count, so an RCON reply missing for a poll (or while the server starts) no longer reports 0 players and
      back.
- [x] RCON is on by default for new profiles (and after resetting the Administration section): the player count,
      shutdown messages and the world save before a stop all need it. New profiles already get a generated admin
      password; saved profiles keep their own setting (the profile file always stores it). With automatic firewall
      management on, the RCON port is opened in Windows Firewall as before.
- [x] **Test:** a new profile has Enable RCON ticked; an existing profile is unchanged.
- [x] **Test:** RCON off → "–" with the RCON-off tooltip while the server runs; RCON on → "–" (no reply yet)
      while it starts, then the count. (A rejected password only happens if the server's ini is edited by hand:
      the profile writes the password it uses.)
- [x] **Test:** with the test server running, the app reports Running / Available, 0 players, within one
      poll of starting, over a single RCON connection that stays open.
- [x] **Test:** the player count went to 1 when a player joined; stop with countdown works.
- [x] **Test:** the RCON window's player list with a player connected (EOS ID parsing); stop skips the
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
- [x] Verified the `AltSaveDirectoryName` layout on a live server: started with `?AltSaveDirectoryName=AltTest`,
      the server created its world at `Saved\AltTest\TheIsland_WP\TheIsland_WP.ark` (an empty database at
      startup), and the save on Stop went there, with its `.arkrbf` backups; `SavedArks\TheIsland_WP` wasn't
      touched.
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
      (server stopped) lists the world save, the zip and the `.arkrbf` backups.
- [x] Restore deletes the world save's SQLite side files (`-journal`, `-wal`, `-shm`) along with the old save: one
      left by a crash would be applied to the restored file the next time it is opened. (ASA uses a rollback
      journal, `journal_mode = delete`, and leaves none at rest.)
- [x] **Test:** restore, server stopped. An `.arkrbf` (the server's own backup): 1 file, the world save is
      byte-identical to it. A zip, full restore: 3 files (world, `.arkprofile`, `.arktribe`), each identical to
      the zip's, with their saved times. Each restore first zipped the current state (both zips verified: the
      first holds the world as it was before the test). Restored world: SQLite `integrity_check` = ok, no side
      files left; the server started on it (Running / Available).
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
- [x] Engrams left out of the first pass (332, no unique wiki match): 13 added where the wiki's Craftables table
      has both level and points and the class is certain, matched through the item blueprint paths on the wiki
      (the saddles' code names: AngelFox = Solwyn, DevilFox = Malwyn, SnowDragon = Aureliax, SnowMonster = Ossidon)
      or the structure's folder and name: the Grand Tortugar, Gargantar and Umbra saddles (`Base`); Bloodforge, Cryo
      Hospital, Fabricated Crossbow, Holo-Decoy Grenade, Medical Stand, Warbench and the four saddles
      (`LostColony`). The wiki has no `engramClassname` values and no blueprint paths for most new ASA items, so
      the rest stay out: 109 premium cosmetic unlocks (`Packs/AAA`), 144 content-pack items (Steampunk, Wasteland,
      Frontier, Tides of Fortune), 17 skins and ghillie sets, and 49 engrams with no level / points on the wiki
      (Lost Colony armour, Tek items, new structure pieces; `ClipboardHammer` is probably the Template Hammer, and
      `LostColony_BunkerModule` isn't the Tek Bunker, which is listed). They can still be added by class name in the
      engram grid. Reading the values from the server's paks would need its property mappings.
- [x] Supply crate names: 73 of the 77 new crates renamed in the style of the existing ones. Beacons get their
      colour from the level (Island scale 25 / 35 / 45 / 60 = Blue / Purple / Yellow / Red, desert scale 30 / 45 /
      55 / 70; "Desert" for Astraeos's Scorched Earth crates, "Level 90 Beacon" where there's no known colour), cave
      crates by tier as before, and the wiki's names where it has them (Orbital Supply Drop Blue / Yellow / Red /
      Purple for the horde crates, Blue / Yellow / Red Cave Drop, Dragon Hoard, Treasure Chest). Parent classes say
      "(base class)"; the test crate `BuriedTreasureBase_Test` removed. Map spawners ("Bison") and drop inventories
      ("Drop Inventory - Carnivore Huge Acro") keep the names derived from their class names; the wiki has nothing
      better.
- [x] **Test:** creature, engram, item, crate and spawner lists show ASA content with the new mod colours; the
      app starts without game-data errors.
- [x] Server Monitor's Map column showed `TheIslandWP`: a `Label` reads "_" as an access-key marker, and the
      converter (`MapNameValueConverter`) only knew translated names. It now uses `GameData.FriendlyMapNameForClass`
      like the profile's map box ("The Island"), and the profile, server and map cells are `TextBlock`s, so
      underscores in names show and long names end in "…".

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
- [x] **Test:** settings page shows the new groups and options; changing a setting writes it to the INI / command
      line, and resetting the section removes it again. Checked with UiDriver on the test copy: "No wild babies"
      (launch option), the cosmetic whitelist URL (GameUserSettings) and "Disable photo mode" (Game.ini) were written
      on Save (`-NoWildBabies` in `RunServer.cmd` and the Show Command window, `CosmeticWhitelistOverride="…"` quoted
      like `BanListURL`, `bDisablePhotoMode=True`), and all three were gone after resetting Administration and Rules
      and saving again.
- [x] Found on the way: the settings page's check boxes and sliders had no accessible name (their label is a
      separate element), so screen readers read "check box". `CheckBoxAndTextBlock` and the `Annotated*Slider`
      controls (`ServerManager.Common\Controls`) now name them after their label.

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

- [x] `WebClient` / `HttpWebRequest` → `HttpClient` (8 sites were left after the earlier removals). The public IP
      check and the SteamCMD download share `NetworkUtils.HttpClient`; the IP check now tries its second URL when
      the first fails and gives up after 15 s. The Discord plugin's webhook post uses its own static client (same
      form data, same 5 s timeout). `ServicePointManager.SecurityProtocol` and its hidden setting are gone: they
      don't affect `HttpClient`. The unused `PerformCallToAPIAsync` is deleted.
- [x] Discord plugin: its update check, download button and patch-notes window are deleted (their URLs were blanked
      in M8, so the patch-notes button did nothing), with their settings, texts, the two icons and the
      `System.ServiceModel.Syndication` package. The app's own version feed window stays (hidden while no feed
      URL is set; it could show a GitHub releases feed later).
- [x] WFO2000 (40 getters, counted twice in the old build): each command getter now creates its `RelayCommand`
      once (`field ??=`). None captured state at creation, so behaviour is unchanged.
- [x] SYSLIB0051: the last legacy serialization constructor (`PluginException`) removed.
- [x] CS0618 `GetProfileConfigDir_Old`: removed with the code that used it, which on every save (and on deleting a
      profile) recursively deleted `Profiles\<profile name>`, a folder layout of very old versions. A profile named
      `.` therefore deleted the whole Profiles folder (reproduced on the test copy), and `..` would have deleted
      the data folder.
- [x] Dead code removed: the external status API path (M2), the `ClustersDir` setting (M3).
- [x] `ArkData` rewritten on AsaSavegameToolkit: its A2S client (`SSQLib`), `LoadOnlinePlayers*` and Steam lookups
      are gone.
- [x] Dead code removed: the `SteamServerDetail*` models, `SteamUtils.GetSteamProcess` and its settings
      (`SteamProcessName`, `SteamClientFile`).
- [x] Self-updater removed: the `ServerManager.Updater` project, the Upgrade buttons in the main window and Server
      Monitor, the version check that showed them (it polled an empty URL every 12 hours), its settings
      (`LatestASM*Url`, `UpdaterFile`, `UpdaterPrefix`, `UpdateCheckTime`) and its texts in every language.
- [x] Upstream's `.azure/` pipelines, Inno Setup script and `.github/workflows/msbuild.yml` deleted; the GitHub
      workflow would have run (and failed) on every push to the new repo. Releases are built with
      `tools\Release\Build-Release.ps1`.
- [x] Unit tests are self-contained; `dotnet test src\Server-Managers.slnx` runs 45, all passing.
  - `ZipUtilsUnitTest` builds its own zip in a temp folder (it read one from `D:\_smtest`).
  - `DiscordPluginUnitTest` asserted nothing (`Assert.IsTrue(true)`), posted to any webhooks in a local plugin
    config and opened modal windows. It now tests the webhook message (prefix, styles, truncation, encoding,
    `&`), the webhook URL and which config profiles get an alert, through methods split out of `HandleAlert`.
  - New `ARKServerManager.UnitTests`: the server command line (map and `?` options first, `-port=` /
    `-WinLiveMaxPlayers=`, extra Server Args placement, mod lists, platforms, no admin password) and the save
    folders, with and without an alternate save name (the layouts verified live in M1 and M5).
    `GlobalizerExtensions.GetResourceString` returns null without an application, so the game data loads in a
    test.
- [x] Considered: the solution is now `src\Server-Managers.slnx` (the redundant x64 / x86 platforms, which all
      mapped to Any CPU, dropped). Not done: nullable reference types (thousands of warnings across inherited
      code) and splitting `ServerProfile.cs` (7k lines) / `ServerSettingsControl` (11k lines); both are large
      changes with regression risk and nothing for users. Worth revisiting only when that code is reworked anyway.
- [x] UI self-testing: `tools\UiDriver` (FlaUI 5.0.0, UIA3; outside the solution) drives the app from a shell, one
      command per run (launch, windows, tree, find, click, set, get, select, tap, screenshot, close), through
      automation patterns rather than the real mouse. `Deploy-TestApp.ps1` sets up an isolated, non-elevated copy
      in `bin\UiTest` with its own settings and data folder (a normal process can't drive the elevated app, and
      the app reads its settings backup `userconfig.json` from its install folder, so a plain copy would use the
      real data folder). Checked on the main and Settings windows.
- [x] Accessibility gaps found with it (screen readers hit them too), fixed:
      - The "+" (new profile) button sat in the tab control's template, which WPF's tab control doesn't expose to
        UI Automation. The main window now uses `AccessibleTabControl` (`ServerManager.Common\Controls`), whose
        automation peer also lists the template's buttons; the button is named "New profile". Layout unchanged.
      - Combo box items read `ServerManagerTool.Common.Model.ComboBoxItem` and profile tabs
        `ServerManagerTool.Lib.Server` (WPF names data items by `ToString()`): `ComboBoxItem` (both copies, app
        and Discord plugin) now returns its display text, `Server` its profile name.
      - Checked with UiDriver: "New profile" found by name and clicked (a profile was added), combo items read
        "Normal" / "Minimized" / "Maximized", the tab reads the profile name.
- [x] Buttons without an accessible name: WPF names a button only by string content, so icon buttons and
      icon-plus-text buttons had none. `AutomationNames` (`ServerManager.Common\Lib`, registered at startup) binds
      each such button's name to the text shown in it, else its tooltip (so it follows Start / Stop and language
      changes); names set in XAML win. Hooked on `SizeChanged`: WPF raises `Loaded` only where an element has its
      own handler, so a class handler on it misses most buttons. Tooltips added where a button had neither text
      nor tooltip (Server Monitor Stop / Update split buttons, the two "..." folder pickers in Settings).
      `AutomationNamesUnitTest` covers the cases.
- [x] The profile view was invisible to UI Automation: the main window hosts the views in an `ItemsControl`,
      whose items WPF shows as empty data items. `ViewHostItemsControl` exposes the visible view instead (the
      main window went from 15 to 77 buttons in the tree). Checked with UiDriver on the main window, Settings
      and Server Monitor: every button has a name except the scroll bars' arrow buttons.
- [x] Libraries whose licenses conflict with a release, replaced:
      - Windows API Code Pack (Microsoft's own terms, which exclude combining it with copyleft code and ask for an
        indemnity): it was only used for file and folder pickers (14 dialogs in four files), now .NET's
        `Microsoft.Win32` `OpenFileDialog` / `SaveFileDialog` / `OpenFolderDialog`. Filters go through
        `FileDialogUtils.Filter`, which turns the old "label + `*.a,*.b`" pairs into Win32 filter strings.
      - NeXt.Vdf (GPL-3.0, built from source): only used to remove the beta key from SteamCMD's
        `appmanifest_<appid>.acf` on profile save, when the branch is cleared. Replaced by `VdfFile` / `VdfNode`
        (`ServerManager.Common\Lib`), a KeyValues text reader and writer that refuses what it doesn't understand
        (such as `[$WIN32]` conditions) instead of dropping it on save. It writes UTF-8 without a BOM as SteamCMD
        does; NeXt.Vdf wrote the manifest back as UTF-16. Its output matches the real ASA manifest byte for byte.
        The unused manifest reader (`SteamUtils.ReadSteamCmdAppManifestFile`, `SteamCmdAppManifest*` models) went
        with it. `VdfFileUnitTest` covers both.
- [x] WPFSharp.Globalizer: its license allows shipping the author's compiled releases in a non-competing project,
      but forbids publicly releasing builds compiled from a public copy of its source, which is what the repo did
      (`src\WPFSharp.Globalizer`, modified by Bletch). Now the published NuGet package, 1.2.4 (net5.0-windows build,
      same license text). Bletch's changes the app relied on are now app code, written against the package's
      public API (`GlobalizerExtensions.cs`):
      - `GetResourceString` (832 calls): an extension method on `GlobalizedApplication`, over
        `ResourceUtils.GetResourceString`.
      - English under every other language, so untranslated texts stay English: `LoadLanguage` loads `en-US.xaml`,
        then the language's file, through the manager's `LoadDictionariesFromFiles`. The package's
        `SwitchLanguage` loads only the language and throws for an unknown one. An unknown language falls back to
        English.
      - Styles: `LoadStyle` loads `Styles\Default.xaml` (the package looks for "Default Style.xaml") and lays a
        chosen style over it.
      - The language list in Settings is a plain combo box filled by the app. The package's
        `LanguageSelectionComboBox` calls the package's `SwitchLanguage`, which has no English fallback.
      Checked with UiDriver on the test copy: switching to German retitles Settings and translates the main window,
      the untranslated "New profile" stays English, the language names relist in German, and switching back
      works.

- [x] Stopping without a countdown saved nothing: the Stop buttons (`ServerRuntime.StopAsync`) sent Ctrl+C and
      killed the server if that couldn't be sent, and the Server Monitor's Stop menu and Discord's `stop`
      (`ServerApp`, process type Stop) skipped the world save and `doexit`. On the test server the 22:48 Stop left no
      save and no closing lines in the server log. Both now use `ServerProcessStopper`: RCON `saveworld` (waiting up
      to 2 minutes for the reply, through the new `RconClient.ReplyTimeout`), then `doexit`, a minute to exit; else
      Ctrl+C and a minute; else kill. Each step goes to `ServerManager_ServerStatus.log` (the `ServerRuntime` logger)
      or the profile's shutdown log. The status shows Stopped only once the process has exited.
      - `ProcessUtils.SendCtrlC`, from the old `SendStopAsync`: Ctrl+C sends are serialized (one console at a
        time), it reports the Windows error when Ctrl+C can't be sent, and it restores the standard handles.
        `AttachConsole` / `FreeConsole` left them pointing at closed handles, so the next `Process.Start` without
        the shell (SteamCMD with its output captured, as in Update after Stop) failed with "The handle is invalid".
      - `ServerApp`'s Ctrl+C and kill steps waited on `ts.Task.Result`, which blocks for as long as the server keeps
        running; they now check `HasExited`.
      - Checked against a stand-in server (RCON with a slow `saveworld`, Ctrl+C handling) driven by a console-less
        program, like the app: RCON stop in 2.3 s, Ctrl+C stop without RCON and with a wrong RCON password, kill
        after 60 s when Ctrl+C is ignored, and starting processes afterwards. Not yet on a real ASA server.

---

## Release checklist

What's left before a public release (not planned soon). The repo is on a private GitHub repo (`origin`, branch
`main`); Bletch's repo is `upstream`.

- [ ] **Permission from Bletch**, then a license. ARK Server Manager has no license file; README's License
      section says "not decided".
- [x] **Icons:** none of ARK Server Manager's 51 button and status images could ship. Traced in November 2026:
      - 18 were Windows 10 icons, copied pixel for pixel or with overlays drawn on; Microsoft doesn't allow its
        icons in other software.
      - Steam (Valve's icon), NoAvatar (Steam's default avatar), Donate (PayPal's button) and Discord (a recoloured
        Discord logo) are other companies' artwork.
      - The other 29 were added from 2018 to 2022 with no source or license recorded anywhere.

      All replaced by Fluent UI System Icons 1.1.347 (Microsoft, MIT), as vector images (`DrawingImage`) in
      `Resources\Icons.xaml` (the Discord plugin has its own), generated by `tools\Icons\Build-Icons.ps1` from the
      SVGs in `tools\Icons\fluent`. Outline icons in neutral grey; green for add, validate and start, red for
      delete, cancel and stop, amber for restart and warnings, blue for help; filled circles for the status lights.
      Fluent's 16 px designs where an icon is shown at 16 px; light versions on the main window's dark header (new
      `HeaderButtonStyle`, a soft highlight on hover); icons fade on disabled buttons. The profile tab's close button
      is an X instead of a delete icon. Unused images (Collapse, Expand, FolderDelete, NoAvatar) dropped. The Donate
      and Discord buttons, hidden since their links were blanked, removed with their settings and texts. The Discord
      plugin's window icon (upstream's black wrench) is now the app icon, which was already original (M8).
- [x] **Window title bars:** 19 windows (Settings, Find, Restore, mod list, game data, plugins, the Discord
      plugin's two and other dialogs) used `WindowStyle="ToolWindow"`, whose small close button Windows 11 draws
      against the window's edge. They now have the normal title bar, like the RCON window and Server Monitor, with
      the app icon. Resizable ones get maximize; the 14 without a taskbar button can't be minimized
      (`WindowUtils.CanMinimize="False"` clears `WS_MINIMIZEBOX`; Windows then shows that button greyed), since a
      minimized dialog would sit in a corner of the desktop with its owner blocked.
- [ ] **Updates after release:** there's no self-updater. Users update by extracting the new zip over the old
      folder: settings live outside it, and scheduled tasks (which store the exe's path) and the `Plugins` folder
      need the same folder. In-app updates can't work while the repo is private (downloads would need a token).
      Once public: first an update notice (ask GitHub for the latest release at startup, link to it); later,
      optionally, one-click updates, either a small updater (download, check the SHA-256, extract over the folder
      keeping `Plugins`, restart) or Velopack (MIT; but it replaces its install folder on every update, so plugins
      would have to move out of the app folder first, and scheduled tasks be recreated once).
      The release workflow (below) gives an update check what it needs: a GitHub release per version, tagged
      `v<version>`, with `JVH-Ark-Ascended-Server-Manager-<version>.zip` and its `.sha256`. The zip has a top
      folder named after the version, so an updater (or the README's "extract over the old folder") must copy that
      folder's contents.
- [x] **GitHub Actions** (both run on GitHub in November 2026: the first Build passed, and a trial Release run
      built the zip): `build.yml` builds and tests every push to `main` and
      every pull request (Markdown-only changes skipped; a newer push cancels a running build). `release.yml` runs
      on a `v*` tag: checks the tag against `<Version>` and that CHANGELOG.md has the version's section, tests,
      runs `Build-Release.ps1`, writes the `.sha256` and publishes the release with `gh release create` (no
      third-party actions). Run by hand, it's a trial build that keeps the zip as a workflow artifact for 7 days.
      `global.json` pins SDK 11.0.100 (latest feature band allowed). Check: the first Build run after pushing, and
      a trial Release run. A private repo has 2,000 free Actions minutes a month; Windows minutes count double.
- [x] **README:** describe the app properly for people finding the repo: what it does in more depth, screenshots,
      a setup walkthrough. Rewritten with grouped features, three screenshots in `docs/images` (main window, Dino
      Settings, Server Monitor; taken on the UiDriver test copy with demo profiles, an example IP from the
      documentation range and example install paths), a setup walkthrough (first start, profile, install, settings,
      ports, automation, Discord bot), day-to-day use, where things are kept and troubleshooting. The screenshots
      show uninstalled servers; ones of running servers could replace them later.
- [x] **Self-updater** removed (see Phase 4).
- [x] **Release build:** `tools\Release\Build-Release.ps1` publishes a clean Release build in its own folder
      (`--artifacts-path`, so a copy of the app running from `bin\Release` doesn't get in the way), adds README,
      CHANGELOG and THIRD-PARTY-NOTICES, refuses settings backups, the updater and `.pdb` files, checks the
      stamped version, and zips it into `artifacts\` with its SHA-256. The version is one `<Version>` in
      `src\Directory.Build.props` (1.1.0; the AssemblyInfo files no longer set one). It must stay above 1.0: user
      settings live in a folder per version, and `Config.Upgrade()` only copies them from a lower one.
- [x] **Manual test checklist** below, especially scheduled tasks (need admin), "Create shortcut" and the
      Discord bot. All ticked in November 2026.
- [ ] **CHANGELOG:** turn [Unreleased] into the first version, with its date.
- [x] Third-party licenses: Windows API Code Pack and NeXt.Vdf replaced, WPFSharp.Globalizer from its NuGet
      package, full license texts in THIRD-PARTY-NOTICES.md.
- [x] Conan Exiles leftovers removed: the projects in `6c59861b`; its installer script and an
      `InternalsVisibleTo` entry later.

---

## Manual test checklist (run after each milestone)

- [x] App starts (admin prompt, first-run data folder on a clean settings folder), main window shows. First run
      tested on the test copy with no data folder set (November 2026): the Data Folder Selection window offered the
      app's drive, Ok asked for confirmation and closed the app as it says; on the next start it created the
      folder (Profiles, Logs, SteamCMD), downloaded and installed SteamCMD (the `HttpClient` download), and
      showed the main window. The admin prompt is in daily use. The suggested folder name is now `asadata`
      (it was the original's `asmdata`, which an installed ARK Server Manager may already use).
- [x] Profiles: "+" adds, "x" deletes with confirmation, switching tabs keeps each tab's state. Tested on the test
      copy (November 2026): "+" added two profiles and the tab name follows the Profile box; a changed server
      name and game port stayed with their tab when switching (section open/closed states are app-wide by
      design); "x" asks first, No keeps the profile, Yes removes the tab and only that profile's file; the saved
      profile came back unchanged after a restart. All new profiles got ports 7777 / 27015 / 32330 and, until
      one was installed, the same install folder; now each takes the next ports and `ServerN` folder no other
      profile uses (checked on the test copy: three new profiles got 7777-7779, 27015-27017, 32330-32332 and
      Server1-3).
- [x] Folder pickers (install dir, backup dir), Help and other links open in the browser. Tested in the real app
      (November 2026): **Set Location...** and the backup folder button open the folder picker (the dialogs
      built into .NET), Help and the patch notes button open the ARK wiki.
- [x] Install/update server via SteamCMD; start, stop (with countdown), restart. Install, start and stop with a
      countdown in M2; in November 2026 the profile's Update button (SteamCMD, server back up a minute later),
      a restart with a 1-minute countdown, and a restart with "Update server after shutdown" (stop, SteamCMD,
      start) all worked, checked in the status and SteamCMD logs. Found on the way: the shutdown window's Stop
      button ignored the update and restart options (it just stopped the server); it's now unavailable while
      they're ticked.
- [x] Status and player count update; RCON window connects, auto-scrolls, kick/ban work. Tested in game
      (November 2026): the player count follows joins and leaves, chat scrolls, Kick disconnects, and Ban /
      Unban work online and offline (the server answers "Banned" / "Unbanned"; a banned player can't join).
      Found on the way: Ban Player and Unban Player were bound to commands that don't exist and did nothing.
- [x] Backup and restore a world save.
- [x] Scheduled tasks (auto-start, auto-update, auto-backup, auto-shutdown) are created and actually run
      (they must point at the `.exe`, not the `.dll`). Tested in November 2026: the backup, update and shutdown
      tasks run `JVH Ark Ascended Server Manager.exe -ab` / `-au` / `-as1<key>`; auto-start runs the profile's
      `RunServer.cmd` a minute after login. Each ran to the end (logs in the data folder's `Logs`): the backup
      saved and zipped; the update filled the new cache folder, then saved, stopped, backed up, updated and
      restarted the server; the shutdown saved, stopped, backed up and restarted it; auto-start started it. The
      first auto-start run had the server crash at second 0 (access violation in user32, crash report in
      `Saved\Crashes`); the second started normally, so a one-off for now. Found on the way: a task waiting
      for another one's server lock gave up when that one was ended early (the lock is handed over as
      abandoned); it now takes the lock over.
- [x] Server Monitor: "Create shortcut" produces a working desktop shortcut. Tested in November 2026: the button
      saves "JVH Ark Ascended Server Manager - Server Monitor.lnk" on the desktop, pointing at the `.exe` (not the
      `.dll`) with `-sm`, the `.exe`'s folder and icon; with the app closed it opened the Server Monitor on its own
      (after the admin prompt), and closing it quit the app.
- [x] Discord bot (if used) connects and answers `info`. Tested in a private Discord server (November 2026): it
      connects, and `help`, `info`, `status`, `list`, `stop` and `start` answer; `stop` waited for the running
      auto-backup, then stopped the server, and `start` started it. Found on the way: the bot didn't ask for the
      Message Content intent, so Discord sent it empty messages (it now does, and the intent must be on in the
      Developer Portal); a space after the prefix gave "Unknown command." (the default prefix is now `!asa`, typed
      `!asa status`); and `info`, `status` and the control commands always needed a profile ID or alias, which the
      help never showed (now not needed when the channel has one server, and the help shows the parameters).

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
