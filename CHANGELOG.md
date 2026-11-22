# Changelog

All notable changes to JVH Ark Ascended Server Manager. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

JVH Ark Ascended Server Manager is a fork of [ARK Server Manager](https://github.com/Bletch1971/ServerManagers)
by Bletch (Brett Hewitson), with contributions from Lacoi and others, reworked for ARK: Survival Ascended.
Changes before the fork are in the original project's history.

## [Unreleased]

First release for ARK: Survival Ascended (ASA). Servers for ARK: Survival Evolved (ASE) are no longer supported.

### Added

- **ASA dedicated server support.** Installs, updates and runs the ASA server (Steam app 2430930) through
  SteamCMD, with the launch arguments ASA expects: the game port as `-port=`, the player limit as
  `-WinLiveMaxPlayers=`, and a query port for each server.
- **Crossplay.** "Players from" (PC through Steam, PC through the Microsoft Store, Xbox, PlayStation) sets
  `-ServerPlatform=`.
- **Server version** read from the server executable (ASA has no `version.txt`), shown even before the server
  has run.
- **Maps:** the ten official ASA maps (The Island, Scorched Earth, The Center, Aberration, Extinction, Ragnarok,
  Valguero, Lost Colony, Genesis, Astraeos). Mod maps can still be typed in.
- **ASA-only settings,** each written only when changed from the server default:
  - cryopods, the Cryo Hospital, outposts, implant respawn cooldown, PvP mounted weapons, train cars and the
    Wandering King Titan;
  - the soft tame limit, Cosmo ammo, Armadoggo and Veilwyn cooldowns, burrow decay and Gacha caves;
  - bunkers, the Bloodforge, Tek wireless crafting and generator limits;
  - photo mode, the tribe tower bonus, Valguero memorial names, speed leveling, a cosmetic whitelist and an
    admin list URL;
  - the launch options `-passivemods`, `-NoWildBabies`, `-GBUsageToForceRestart`,
    `-AlwaysTickDedicatedSkeletalMeshes`, `-DisableCustomCosmetics`, `-DoCustomCosmeticValidation` and
    `-disabledinonetrangescaling`.
- **Game data rebuilt for ASA:** creatures, items, engrams, map spawners, supply crates and drop inventories,
  from the installed server and the ARK wiki. Lost Colony, Astraeos and Bob's Tall Tales content is included.
- **CurseForge mods.** Mod IDs (and passive mod IDs) are CurseForge project IDs passed with `-mods=` /
  `-passivemods=`; the server downloads and updates mods itself.
- **Mod list editor.** Add mod IDs or curseforge.com links, remove mods, change the load order and open a mod's
  CurseForge page. Each mod's name, installed file and date show once the server has downloaded it; they're read
  from the server's own mod library, so no CurseForge API key is needed.
- **Built-in RCON client** for status, the player count, the RCON window, shutdown messages and world saves.
  Replies of any length are read in full, and a wrong admin password is reported with a hint instead of failing
  silently.
- **Player and tribe details from ASA save files** (`.arkprofile`, `.arktribe`, and the world save for `-usestore`
  servers): platform name, character, level, tribe, members and tamed creature count. Used by the Player List,
  the profile and tribe windows, and the RCON window's offline players. Files are read without ever blocking
  the server.
- **Player names in the admin, exclusive-join and whitelist lists,** taken from the players' save files (ASA uses
  Epic Online Services IDs, which Steam doesn't know).
- **Restore from the server's own backups** (`.arkrbf`), listed in the Restore window next to the app's zip
  backups.
- **Accessibility.** Screen readers can now reach:
  - every button, named from its text or tooltip;
  - the "+" (new profile) button and the profile settings;
  - combo box items and profile tabs, read out by their text.

  New tooltips on the Server Monitor's Stop and Update menus and on the folder pickers in Settings.

### Changed

- **New name and icon:** JVH Ark Ascended Server Manager. Scheduled tasks and firewall rules use their own
  names, so they don't collide with an installed ARK Server Manager.
- **Runs on .NET 11** (was .NET Framework 4.6.2), with up-to-date libraries. Needs the .NET 11 Desktop Runtime.
- **Server status from RCON and the server log.** ASA servers don't answer Steam server queries.
  - "Running / Available" means the server has finished starting and is accepting players. Whether it can be
    reached from the internet is no longer checked.
  - The player count comes from RCON.
- **RCON is on by default for new profiles.** The player count, shutdown messages and the world save before a
  stop all need it. New profiles get a generated admin password.
- **A dash and an explanatory tooltip instead of "0"** when the player count can't be read (RCON off, no reply
  yet, or password rejected). The "player count changed" alert no longer fires when RCON misses a reply.
- **Messages go out as server chat by default:** shutdown countdowns, backup notices, the repeating message of the
  day and the RCON window's messages. Broadcasts don't always reach players on ASA; Broadcast can still be chosen
  in Settings.
- **Per-map save folders** (`SavedArks\<map>`, or `Saved\<alternate save name>\<map>`), as ASA uses.
- **Backups copy the world save and player files before zipping,** so a running server is never blocked and the
  backup is consistent (ASA world saves are databases).
- **The admin whitelist file is `AllowedCheaterAccountIDs.txt`,** ASA's name. An `AllowedCheaterSteamIDs.txt`
  from earlier versions is renamed.
- **The ban list defaults to ASA's official list.**
- **Help and patch notes open the ARK wiki.**
- **Player IDs instead of Steam IDs** in the English texts.
- **The RCON window's player list reads ASA player IDs,** and its "View Profile" / "View Tribe" show ASA players
  and tribes.
- **The Discord bot's `info` command** reports the status and player count the app already tracks.

### Removed

- **Self-updater and online services:**
  - the self-updater;
  - the call-home to the original author's server;
  - the donation and Discord links;
  - the forum buttons;
  - the original release history.

  The Discord bot plugin no longer calls home or checks the original repository for updates.
- **ASE-only features:** Steam Workshop mods, total conversions, Survival of the Fittest, procedurally generated
  maps, and the Fjordur traversal buff.
- **ASE-only launch options and settings**, checked against the ASA server executable.
- **The Steam Web API key setting.** Nothing needs it any more.
- **The Conan Exiles manager.**

### Fixed

- **RCON replies over about 4 KB, or arriving in pieces, were cut off.** The rest was then read as the reply to the
  next command.
- **In the "Global" message mode, some messages were never delivered.** The repeating message of the day and the
  RCON window's Save World and Destroy Wild Dinos messages were sent as an invalid command.
- **Restoring a world save now removes the old save's leftover SQLite journal** (left by a crash). Otherwise it
  would be applied to the restored world the next time it was opened, corrupting it.
- **Profile sync copied the cluster directory override even with cluster sync turned off.**
- **The server-started alert showed the query port instead of the game port.**
- **Stopping a server without a countdown didn't save the world.** The Stop buttons (in the profile, the Server
  Monitor and the shutdown window), the Server Monitor's Stop menu and the Discord bot's `stop` now save first:
  over RCON (`saveworld`, then `doexit`) when RCON is on, otherwise with Ctrl+C in the server's console, which ASA
  answers by saving. A server that still hasn't exited a minute later is killed, unsaved. Each step is logged in
  `ServerManager_ServerStatus.log`.
- **A stopped server could show as Stopped while it was still shutting down,** and a shutdown could wait forever for
  a server that didn't exit.
- **Updating a server right after stopping it could fail with "The handle is invalid".**
- **Going back to the public branch rewrote SteamCMD's app manifest as UTF-16.** It's now written as UTF-8, as
  SteamCMD writes it.

### Known limitations

- 332 newer ASA engrams (mostly Bob's Tall Tales and Lost Colony items) aren't in the engram list yet. They can
  still be overridden by class name.
- Names of the new map spawners, supply crates and drop inventories are derived from their class names.
- New and changed texts are in English only; other languages fall back to English for them.
- Changing firewall rules and scheduled tasks needs the app to run as administrator.

### For developers

- `tools/UiDriver`: a command-line UI Automation driver (FlaUI) and a script that sets up an isolated test copy
  of the app, for checking the UI from a shell.
- `tools/Release/Build-Release.ps1` builds a release zip: a clean build with the README, changelog and notices,
  checked for settings backups and leftovers. The version is set once, in `src/Directory.Build.props`.
- The self-updater project and upstream's Azure DevOps, Inno Setup and GitHub build files are gone.
- Two libraries whose licenses conflict with a release were replaced: the Windows API Code Pack (file and folder
  pickers now use the dialogs built into .NET) and NeXt.Vdf (GPL; SteamCMD's manifest is now read and written by
  the app's own code).
- WPFSharp.Globalizer comes from its published NuGet package (1.2.4), as its license requires for a public
  release, instead of a modified copy of its source. The changes the app relied on (English under other languages,
  resource strings, style loading, the language list) are now in the app.
- Unit tests for the RCON client, the accessible names and the manifest reader.
- `docs/PORTING-PLAN.md`: what was changed for ASA and why, with the facts it relies on.
