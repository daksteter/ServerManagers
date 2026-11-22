# JVH Ark Ascended Server Manager

A Windows app for running **ARK: Survival Ascended** dedicated servers: install and update them through
SteamCMD, edit their settings, start and stop them, and keep an eye on them while they run.

It is a fork of [ARK Server Manager](https://github.com/Bletch1971/ServerManagers) by Bletch, reworked for
ARK: Survival Ascended. It doesn't support ARK: Survival Evolved servers.

## Features

- **Install and update** the server (Steam app 2430930) through SteamCMD, including beta branches. Run several
  servers on one machine, each with its own profile.
- **Edit the settings** in `GameUserSettings.ini`, `Game.ini` and the launch options, including the ASA-only ones,
  on one settings page per server.
- **Mods** by CurseForge project ID. The server downloads and updates them itself, and a mod list editor shows
  each mod's name and installed version.
- **Status and players:** whether each server is running and ready, and how many players are online (through
  RCON).
- **RCON window:** chat, the player list with profiles and tribes, kick and ban, and server commands.
- **Shutdowns with a countdown** and messages to players, a world save first, and optional restart and update.
- **Backups and restore** of world saves and player files, including the server's own backups.
- **Scheduled tasks** for automatic start, update, backup and shutdown.
- **Server Monitor:** all servers in one window.
- **Optional Discord bot** to check and control servers from Discord.

## Requirements

- Windows 10 or 11, 64-bit.
- The [.NET 11 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/11.0) (x64).
- About 12 GB of disk space per server, plus room for saves and backups.
- Administrator rights to create firewall rules and scheduled tasks. The app offers to restart as administrator
  when it starts.

## Getting started

1. Start the app and choose a data folder. Servers, profiles, backups and logs go there.
2. Click **+** to add a server profile, then **Install** to download the server.
3. Set the server name, passwords, map and any other settings, then click **Save**.
4. Click **Start**. The status changes to **Running / Available** once the server is ready for players.

Players connect through the game port (UDP 7777 by default), so forward that port on your router to make the
server reachable from the internet. RCON (TCP 32330 by default) is on for new profiles. The app uses it on the
same machine, so it doesn't need to be forwarded.

## Building from source

You need the .NET 11 SDK on Windows.

```powershell
dotnet build src\ARKServerManager -c Release
```

The app is built to `src\ARKServerManager\bin\Release\net11.0-windows`. Don't zip that folder for others: a copy
of the app that has been run writes its settings backups (`userconfig.json`, `commonconfig.json`) into it.

To make a release zip, commit your changes and run:

```powershell
tools\Release\Build-Release.ps1                  # version from src\Directory.Build.props
tools\Release\Build-Release.ps1 -Version 1.2.0
```

It makes a clean build in its own folder, adds this README, the changelog and the third-party notices, checks
that no settings backup, updater or debug symbols got in, and writes
`artifacts\JVH-Ark-Ascended-Server-Manager-<version>.zip`. The version must stay above 1.0, or the app won't
carry users' settings over from earlier builds.

- `docs/PORTING-PLAN.md` explains what was changed for ASA and why.
- `tools/UiDriver` can drive the app's UI from a shell for testing.

## Changes

See [CHANGELOG.md](CHANGELOG.md).

## Credits

- **[ARK Server Manager](https://github.com/Bletch1971/ServerManagers)** by Bletch (Brett Hewitson), which this
  app is built on, with contributions from Lacoi and others.
- **Game data** (creature, item and engram names and engram levels) includes content from the
  [ARK Wiki](https://ark.wiki.gg/) by its contributors, licensed under
  [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/).
- **Third-party libraries:** see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

Not decided yet. The original ARK Server Manager has no license file.

## Disclaimer

ARK: Survival Ascended is a trademark of Studio Wildcard. This project is not affiliated with or endorsed by
Studio Wildcard or Snail Games.
