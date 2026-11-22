using NLog;
using ServerManagerTool.Common.Enums;
using ServerManagerTool.Common.Lib;
using ServerManagerTool.Common.Utils;
using ServerManagerTool.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ServerManagerTool.Lib
{
    /// <summary>
    /// Polls the status of each registered server.
    /// ASA servers don't answer Steam (A2S) queries; they are listed through Epic Online Services instead. So a running
    /// server process counts as started once it answers RCON, which also gives the player count, or, without RCON,
    /// once its log says it is advertising for players to join.
    /// </summary>
    public class ServerStatusWatcher
    {
        private const int RCON_TIMEOUT = 3000;
        private const string SERVER_LOG_FOLDER = "Logs";
        private const string SERVER_LOG_FILE = "ShooterGame.log";
        private const string SERVER_LOG_STARTUP_COMPLETE = "Server has completed startup and is now advertising for join";

        // Every server log line starts with a UTC timestamp: [2026.11.21-22.44.00:664]
        private static readonly Regex LogTimestampRegex = new Regex(@"^\[(\d{4}\.\d{2}\.\d{2}-\d{2}\.\d{2}\.\d{2}):\d{3}\]", RegexOptions.Compiled);
        private static readonly Regex LogVersionRegex = new Regex(@"ARK Version: (\d+(?:\.\d+)+)", RegexOptions.Compiled);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly List<ServerStatusUpdateRegistration> _serverRegistrations = new List<ServerStatusUpdateRegistration>();
        private readonly ActionBlock<Func<Task>> _eventQueue;

        static ServerStatusWatcher()
        {
            Instance = new ServerStatusWatcher();
        }

        private ServerStatusWatcher()
        {
            _eventQueue = new ActionBlock<Func<Task>>(async f => await f.Invoke(), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });
            _eventQueue.Post(DoUpdateAsync);
        }

        public static ServerStatusWatcher Instance
        {
            get;
            private set;
        }

        public IAsyncDisposable RegisterForUpdates(string installDirectory, string profileId, IPEndPoint localEndpoint, IPEndPoint rconEndpoint, string rconPassword, Action<IAsyncDisposable, ServerStatusUpdate> updateCallback)
        {
            var registration = new ServerStatusUpdateRegistration
            {
                InstallDirectory = installDirectory,
                ProfileId = profileId,
                GamePort = localEndpoint.Port,
                LocalEndpoint = localEndpoint,
                RconEndpoint = rconEndpoint,
                RconPassword = rconPassword,
                UpdateCallback = updateCallback,
            };

            registration.UnregisterAction = async () =>
                {
                    var tcs = new TaskCompletionSource<bool>();
                    _eventQueue.Post(() =>
                    {
                        if(_serverRegistrations.Contains(registration))
                        {
                            Logger.Debug($"{nameof(RegisterForUpdates)} Removing registration for {registration.LocalEndpoint}");
                            _serverRegistrations.Remove(registration);
                        }
                        CloseRcon(registration);
                        tcs.TrySetResult(true);
                        return Task.FromResult(true);
                    });

                    await tcs.Task;
                };

            _eventQueue.Post(() =>
                {
                    if (!_serverRegistrations.Contains(registration))
                    {
                        Logger.Debug($"{nameof(RegisterForUpdates)} Adding registration for {registration.LocalEndpoint}");
                        _serverRegistrations.Add(registration);
                    }
                    return Task.FromResult(true);
                }
            );

            return registration;
        }

        private Task DoUpdateAsync()
        {
            try
            {
                foreach (var registration in _serverRegistrations)
                {
                    var statusUpdate = new ServerStatusUpdate();

                    try
                    {
                        statusUpdate = GetServerStatus(registration);

                        PostServerStatusUpdate(registration, statusUpdate);
                    }
                    catch (Exception ex)
                    {
                        // We don't want to stop other registration queries or break the ActionBlock
                        Logger.Error($"{nameof(DoUpdateAsync)} - Exception in local update. {ex.Message}\r\n{ex.StackTrace}");
                        Debugger.Break();
                    }
                    finally
                    {
                        var players = statusUpdate.PlayerCountStatus == PlayerCountStatus.Known ? statusUpdate.OnlinePlayerCount.ToString() : $"unknown ({statusUpdate.PlayerCountStatus})";
                        Logger.Info($"{nameof(DoUpdateAsync)} {registration.LocalEndpoint}: {statusUpdate.Status}, players: {players}");
                    }
                }
            }
            finally
            {
                Task.Delay(Config.Default.ServerStatusWatcher_LocalStatusQueryDelay)
                    .ContinueWith(_ => _eventQueue.Post(DoUpdateAsync))
                    .DoNotWait();
            }

            return Task.CompletedTask;
        }

        private static ServerStatusUpdate GetServerStatus(ServerStatusUpdateRegistration registration)
        {
            //
            // First check the process status
            //
            var processStatus = GetServerProcessStatus(registration, out Process process);
            switch (processStatus)
            {
                case ServerProcessStatus.NotInstalled:
                    CloseRcon(registration);
                    return new ServerStatusUpdate { Status = WatcherServerStatus.NotInstalled };

                case ServerProcessStatus.Stopped:
                    CloseRcon(registration);
                    return new ServerStatusUpdate { Status = WatcherServerStatus.Stopped };

                case ServerProcessStatus.Unknown:
                    CloseRcon(registration);
                    return new ServerStatusUpdate { Status = WatcherServerStatus.Unknown };

                case ServerProcessStatus.Running:
                    break;

                default:
                    Debugger.Break();
                    break;
            }

            var rconAnswered = TryGetRconPlayerCount(registration, out int onlinePlayerCount);
            UpdateServerLogStatus(registration, process);

            return new ServerStatusUpdate
            {
                Process = process,
                // Published shows as Running / Available: the server has finished starting and is advertising itself
                // for players to join. Whether it can be reached from the internet (port forwarding etc.) is no longer
                // checked, because that check relied on Steam queries.
                Status = rconAnswered || registration.LogStartupComplete ? WatcherServerStatus.Published : WatcherServerStatus.Initializing,
                OnlinePlayerCount = onlinePlayerCount,
                PlayerCountStatus = rconAnswered ? PlayerCountStatus.Known
                    : registration.RconEndpoint == null ? PlayerCountStatus.RconDisabled
                    : registration.RconPasswordRejected ? PlayerCountStatus.RconPasswordRejected
                    : PlayerCountStatus.NoRconReply,
                Version = registration.LogVersion,
            };
        }

        private static ServerProcessStatus GetServerProcessStatus(ServerStatusUpdateRegistration updateContext, out Process serverProcess)
        {
            serverProcess = null;
            if (string.IsNullOrWhiteSpace(updateContext.InstallDirectory))
            {
                return ServerProcessStatus.NotInstalled;
            }

            var serverExePath = Path.Combine(updateContext.InstallDirectory, Config.Default.ServerBinaryRelativePath, Config.Default.ServerExe);
            if(!File.Exists(serverExePath))
            {
                return ServerProcessStatus.NotInstalled;
            }

            //
            // The server appears to be installed, now determine if it is running or stopped.
            //
            try
            {
                foreach (var process in Process.GetProcessesByName(Config.Default.ServerProcessName))
                {
                    var commandLine = ProcessUtils.GetCommandLineForProcess(process.Id)?.ToLower();

                    if (commandLine != null &&
                        (commandLine.StartsWith(serverExePath, StringComparison.OrdinalIgnoreCase) || commandLine.StartsWith($"\"{serverExePath}\"", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Does this match our server exe and port?
                        var serverArgMatch = string.Format(Config.Default.ServerCommandLineArgsMatchFormat, updateContext.GamePort).ToLower();
                        if (commandLine.Contains(serverArgMatch))
                        {
                            // Was an IP set on it?
                            var anyIpArgMatch = string.Format(Config.Default.ServerCommandLineArgsIPMatchFormat, string.Empty).ToLower();
                            if (commandLine.Contains(anyIpArgMatch))
                            {
                                // If we have a specific IP, check for it.
                                var ipArgMatch = string.Format(Config.Default.ServerCommandLineArgsIPMatchFormat, updateContext.LocalEndpoint.Address.ToString()).ToLower();
                                if (!commandLine.Contains(ipArgMatch))
                                {
                                    // Specific IP set didn't match
                                    continue;
                                }

                                // Specific IP matched
                            }

                            // Either specific IP matched or no specific IP was set and we will claim this is ours.

                            process.EnableRaisingEvents = true;
                            if (process.HasExited)
                            {
                                return ServerProcessStatus.Stopped;
                            }

                            serverProcess = process;
                            return ServerProcessStatus.Running;
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Logger.Error($"{nameof(GetServerProcessStatus)}. {ex.Message}\r\n{ex.StackTrace}");
            }

            return ServerProcessStatus.Stopped;
        }

        /// <summary>
        /// Sends ListPlayers over RCON and counts the players in the reply. The RCON connection stays open between polls.
        /// </summary>
        /// <returns>true if the server answered.</returns>
        private static bool TryGetRconPlayerCount(ServerStatusUpdateRegistration registration, out int playerCount)
        {
            playerCount = 0;
            if (registration.RconEndpoint == null)
            {
                return false;
            }

            try
            {
                if (registration.Rcon == null || !registration.Rcon.IsConnected)
                {
                    CloseRcon(registration);
                    registration.Rcon = RconClient.Connect(registration.RconEndpoint, registration.RconPassword ?? string.Empty, RCON_TIMEOUT);
                    registration.RconPasswordRejected = false;
                }

                playerCount = RconPlayerList.CountPlayers(registration.Rcon.SendCommand(RconPlayerList.Command));
                return true;
            }
            catch (RconAuthenticationException ex)
            {
                // the server is up, but without the right password there is no player count
                if (!registration.RconPasswordRejected)
                    Logger.Warn($"{nameof(TryGetRconPlayerCount)} - {ex.Message} Check the profile's admin password.");
                registration.RconPasswordRejected = true;
                CloseRcon(registration);
                return false;
            }
            catch (Exception ex)
            {
                // Expected while the server is starting, before its RCON port opens.
                Logger.Debug($"{nameof(TryGetRconPlayerCount)} - No RCON reply from {registration.RconEndpoint}. {ex.Message}");
                CloseRcon(registration);
                return false;
            }
        }

        private static void CloseRcon(ServerStatusUpdateRegistration registration)
        {
            try
            {
                registration.Rcon?.Dispose();
            }
            catch (Exception)
            {
                // Nothing useful to do if the connection is already broken.
            }

            registration.Rcon = null;
        }

        /// <summary>
        /// Reads the running server's log for its version and for the line that says startup is complete.
        /// The results are kept per process, so the log is no longer read once startup is complete.
        /// </summary>
        private static void UpdateServerLogStatus(ServerStatusUpdateRegistration registration, Process process)
        {
            if (registration.LogProcessId != process.Id)
            {
                registration.LogProcessId = process.Id;
                registration.LogStartupComplete = false;
                registration.LogVersion = null;
            }

            if (registration.LogStartupComplete)
            {
                return;
            }

            var logFile = Path.Combine(registration.InstallDirectory, Config.Default.SavedRelativePath, SERVER_LOG_FOLDER, SERVER_LOG_FILE);

            try
            {
                if (!File.Exists(logFile))
                {
                    return;
                }

                var processStartUtc = process.StartTime.ToUniversalTime();

                using (var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    // The server renames the previous run's log when it starts, so until then this can be an older log.
                    // The first line's timestamp tells whether it belongs to the running process.
                    var match = LogTimestampRegex.Match(reader.ReadLine() ?? string.Empty);
                    if (!match.Success ||
                        !DateTime.TryParseExact(match.Groups[1].Value, "yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime logOpenedUtc) ||
                        logOpenedUtc < processStartUtc.AddSeconds(-5))
                    {
                        return;
                    }

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (registration.LogVersion == null)
                        {
                            var versionMatch = LogVersionRegex.Match(line);
                            if (versionMatch.Success && Version.TryParse(versionMatch.Groups[1].Value, out Version version))
                            {
                                registration.LogVersion = version;
                            }
                        }

                        if (line.Contains(SERVER_LOG_STARTUP_COMPLETE, StringComparison.Ordinal))
                        {
                            registration.LogStartupComplete = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"{nameof(UpdateServerLogStatus)} - Failed reading {logFile}. {ex.Message}");
            }
        }

        private void PostServerStatusUpdate(ServerStatusUpdateRegistration registration, ServerStatusUpdate statusUpdate)
        {
            _eventQueue.Post(() =>
            {
                if (_serverRegistrations.Contains(registration))
                {
                    try
                    {
                        registration.UpdateCallback(registration, statusUpdate);
                    }
                    catch (Exception ex)
                    {
                        DebugUtils.WriteFormatThreadSafeAsync("Exception during server status update callback: {0}\n{1}", ex.Message, ex.StackTrace).DoNotWait();
                    }
                }
                return TaskUtils.FinishedTask;
            });
        }
    }
}
