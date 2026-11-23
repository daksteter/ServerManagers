# Changelog

All notable changes to JVH Ark Ascended Server Manager. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

JVH Ark Ascended Server Manager is a fork of [ARK Server Manager](https://github.com/Bletch1971/ServerManagers)
by Bletch (Brett Hewitson), with contributions from Lacoi and others, reworked for ARK: Survival Ascended.
Changes before the fork are in the original project's history.

## [Unreleased]

### Added

- **Update notice.** At startup the app asks GitHub for the latest release. When it's newer, "Version 1.2.0 is
  available" (for example) appears next to the version number in the main window and opens the release page. Turn it
  off in Settings with **Check for a new version at startup**. The patch-notes button next to the version is back too,
  and opens the Releases page.

## [1.1.0] - 2026-11-22

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
  - combo box items and profile tabs, read out by their text;
  - the settings page's check boxes and sliders, named after their labels.

  New tooltips on the Server Monitor's Stop and Update menus and on the folder pickers in Settings.
- **MIT license.** Anyone may use, change, fork and share the app, keeping the copyright and license notice.
  Bletch put ARK Server Manager under the MIT license too (November 2026), which makes this possible.

### Changed

- **New name and icon:** JVH Ark Ascended Server Manager. Scheduled tasks, firewall rules and the suggested data
  folder (`asadata`, was `asmdata`) use their own names, so they don't collide with an installed ARK Server
  Manager.
- **New icons throughout,** from Microsoft's Fluent UI System Icons: the Windows 11 style, sharp at any display
  scaling. Icons on disabled buttons fade, and the main window's header icons are light on its dark background.
- **Every window has the normal Windows title bar.** Settings, Find, Restore, the mod list and the other dialogs
  had a small tool-window title bar, whose close button Windows 11 draws against the window's edge. Resizable
  windows can now be maximized; dialogs without a taskbar button can't be minimized, so they can't get lost.
- **Runs on .NET 11** (was .NET Framework 4.6.2), with up-to-date libraries. Needs the .NET 11 Desktop Runtime.
- **Server status from RCON and the server log.** ASA servers don't answer Steam server queries.
  - "Running / Available" means the server has finished starting and is accepting players. Whether it can be
    reached from the internet is no longer checked.
  - The player count comes from RCON.
- **New profiles get their own ports.** Each port is the next one no other profile uses (game port 7777, 7778,
  and so on; the same for the Steam and RCON ports), so a second server can run next to the first without
  changing anything.
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
- **The Discord bot's default prefix is `!asa`** (was `asm!`), typed with a space: `!asa status`. A prefix ending in a
  letter or digit needs that space, so a message that merely starts with it (`!asap`) isn't taken for a command; after
  other prefixes the space is optional. The help lists the commands the way they're typed. A prefix already set in
  Settings is kept.
- **The Discord bot's commands don't need the server when the channel has only one:** `!asa stop` instead of
  `!asa stop <alias>`. With more than one, the reply asks for the alias or profile ID. The help shows each command's
  parameters, and a command given too many gets its usage in reply instead of "Parameter count does not match any
  command."

### Removed

- **Self-updater and online services:**
  - the self-updater;
  - the call-home to the original author's server;
  - the donation and Discord links;
  - the forum buttons;
  - the original release history.

  The Discord plugin no longer calls home, and its update check, download button and patch-notes button (which
  had nothing left to check) are gone.
- **ASE-only features:** Steam Workshop mods, total conversions, Survival of the Fittest, procedurally generated
  maps, and the Fjordur traversal buff.
- **ASE-only launch options and settings**, checked against the ASA server executable.
- **The Steam Web API key setting.** Nothing needs it any more.
- **The Conan Exiles manager.**

### Fixed

- **Saving a profile named `.` deleted the Profiles folder, with every profile in it.** An empty name did the same,
  and `..` would have tried to delete the whole data folder, including servers installed there. The cause was a
  cleanup of a folder layout from very old versions, which is gone.
- **RCON replies over about 4 KB, or arriving in pieces, were cut off.** The rest was then read as the reply to the
  next command.
- **In the "Global" message mode, some messages were never delivered.** The repeating message of the day and the
  RCON window's Save World and Destroy Wild Dinos messages were sent as an invalid command.
- **Restoring a world save now removes the old save's leftover SQLite journal** (left by a crash). Otherwise it
  would be applied to the restored world the next time it was opened, corrupting it.
- **New profiles added before any of them was installed all got the same install folder** (`Servers\Server1`).
  Each now gets the next folder that neither exists nor belongs to another profile.
- **Profile sync copied the cluster directory override even with cluster sync turned off.**
- **The server-started alert showed the query port instead of the game port.**
- **The Server Monitor showed ASA map names without their underscore** ("TheIslandWP"). It now shows the map's
  name ("The Island"), and underscores in profile and server names show too.
- **Stopping a server without a countdown didn't save the world.** The Stop buttons (in the profile, the Server
  Monitor and the shutdown window), the Server Monitor's Stop menu and the Discord bot's `stop` now save first:
  over RCON (`saveworld`, then `doexit`) when RCON is on, otherwise with Ctrl+C in the server's console, which ASA
  answers by saving. A server that still hasn't exited a minute later is killed, unsaved. Each step is logged in
  `ServerManager_ServerStatus.log`.
- **The Discord bot didn't see commands typed with its prefix.** It didn't ask Discord for the Message Content
  intent, without which Discord sends bots empty message text. Turn on **Message Content Intent** on the bot's
  page in the Discord Developer Portal; without it Discord now refuses the bot's connection.
- **The Discord bot answered "Unknown command." when there was a space after the prefix** (`asm! help`); only
  `asm!help` worked.
- **The app crashed on start when its data folder's drive was gone:** after moving to another PC, removing a drive
  or changing its letter. It now says the data folder can't be found and opens the data folder window (Cancel quits,
  so you can connect the drive and start again). Scheduled tasks exit with code 1012 instead of waiting on a message.
- **A scheduled task waiting for another one to finish with the same server gave up with an error** if that one
  was ended early (from Task Scheduler, or by a crash). It now takes over and carries on.
- **The RCON window's Ban Player and Unban Player did nothing** (they were bound to commands that don't exist).
  Kick, ban and unban now also work for a player whose save file hasn't been read yet; they need only the player
  ID.
- **The shutdown window's Stop button ignored "Update server after shutdown" and "Restart server after
  shutdown."** With either ticked, clicking Stop just stopped the server. Stop is now unavailable while they're
  ticked; its tooltip points to Restart (or Shutdown) with a countdown of 0 instead.
- **A stopped server could show as Stopped while it was still shutting down,** and a shutdown could wait forever for
  a server that didn't exit.
- **Updating a server right after stopping it could fail with "The handle is invalid".**
- **Going back to the public branch rewrote SteamCMD's app manifest as UTF-16.** It's now written as UTF-8, as
  SteamCMD writes it.

### Known limitations

- Some ASA engrams aren't in the engram list: content-pack items, cosmetic unlocks and skins, and 49 others the ARK
  wiki has no unlock level or engram points for (such as Lost Colony armour and new Tek items). They can still be
  overridden by class name.
- Names of the new map spawners and drop inventories are derived from their class names.
- New and changed texts are in English only; other languages fall back to English for them.
- Changing firewall rules and scheduled tasks needs the app to run as administrator.

### For developers

- `tools/UiDriver`: a command-line UI Automation driver (FlaUI) and a script that sets up an isolated test copy
  of the app, for checking the UI from a shell.
- `tools/Release/Build-Release.ps1` builds a release zip: a clean build with the README, changelog and notices,
  checked for settings backups and leftovers. The version is set once, in `src/Directory.Build.props`.
- `tools/Icons/Build-Icons.ps1` generates the icon resources (`Resources/Icons.xaml`) from the Fluent UI System
  Icons SVGs kept in `tools/Icons/fluent`. The old icons, whose origins and licenses couldn't be established, are
  gone.
- The self-updater project and upstream's Azure DevOps, Inno Setup and GitHub build files are gone.
- Two libraries whose licenses conflict with a release were replaced: the Windows API Code Pack (file and folder
  pickers now use the dialogs built into .NET) and NeXt.Vdf (GPL; SteamCMD's manifest is now read and written by
  the app's own code).
- WPFSharp.Globalizer comes from its published NuGet package (1.2.4), as its license requires for a public
  release, instead of a modified copy of its source. The changes the app relied on (English under other languages,
  resource strings, style loading, the language list) are now in the app.
- Unit tests for the RCON client, the accessible names, the manifest reader, the server's command line, the save
  folders, new profiles' ports and install folders, zip extraction, the missing data folder check, the Discord bot's
  command prefix and the Discord plugin's webhook messages. They need no network, server or fixed folders: `dotnet test src\Server-Managers.slnx`.
- The solution file is `src\Server-Managers.slnx` (the newer format); `global.json` pins the .NET 11 SDK.
- GitHub Actions: every push to `main` is built and tested; pushing a version tag (`v1.2.0`) publishes a GitHub
  release with the zip, its SHA-256 and the changelog section as notes.
- Web requests use one shared `HttpClient` instead of the obsolete `WebClient`; the analyzer and obsolete-API
  warnings are cleared, and leftover dead code (Steam server-detail models, a Steam process lookup) is gone.
- `docs/PORTING-PLAN.md`: what was changed for ASA and why, with the facts it relies on.
