using NLog;
using ServerManagerTool.Common;
using ServerManagerTool.Common.Enums;
using ServerManagerTool.Common.Lib;
using ServerManagerTool.Common.Model;
using ServerManagerTool.Common.Utils;
using ServerManagerTool.Lib.Model;
using ServerManagerTool.Plugin.Common;
using ServerManagerTool.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WPFSharp.Globalizer;

namespace ServerManagerTool.Lib
{
    public class ServerRuntime : DependencyObject, IDisposable
    {
        private const int DIRECTORIES_PER_LINE = 200;
        private const int RCON_MAXRETRIES = 3;

        public event EventHandler StatusUpdate;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static readonly GlobalizedApplication _globalizer = GlobalizedApplication.Instance;
        private readonly List<PropertyChangeNotifier> profileNotifiers = new List<PropertyChangeNotifier>();
        private Process serverProcess;
        private IAsyncDisposable updateRegistration;
        private int lastKnownPlayerCount;
        private System.Timers.Timer motdIntervalTimer = new System.Timers.Timer(3600000);
        private RconClient _rconConsole = null;

        #region Properties

        public static readonly DependencyProperty AvailabilityProperty = DependencyProperty.Register(nameof(Availability), typeof(AvailabilityStatus), typeof(ServerRuntime), new PropertyMetadata(AvailabilityStatus.Unknown));
        public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(ServerStatus), typeof(ServerRuntime), new PropertyMetadata(ServerStatus.Unknown));
        public static readonly DependencyProperty StatusStringProperty = DependencyProperty.Register(nameof(StatusString), typeof(string), typeof(ServerRuntime), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty MaxPlayersProperty = DependencyProperty.Register(nameof(MaxPlayers), typeof(int), typeof(ServerRuntime), new PropertyMetadata(0));
        public static readonly DependencyProperty PlayersProperty = DependencyProperty.Register(nameof(Players), typeof(int), typeof(ServerRuntime), new PropertyMetadata(0));
        public static readonly DependencyProperty PlayerCountStatusProperty = DependencyProperty.Register(nameof(PlayerCountStatus), typeof(PlayerCountStatus), typeof(ServerRuntime), new PropertyMetadata(PlayerCountStatus.Known));
        public static readonly DependencyProperty PlayersStringProperty = DependencyProperty.Register(nameof(PlayersString), typeof(string), typeof(ServerRuntime), new PropertyMetadata("0"));
        public static readonly DependencyProperty PlayersTooltipProperty = DependencyProperty.Register(nameof(PlayersTooltip), typeof(string), typeof(ServerRuntime), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(nameof(Version), typeof(Version), typeof(ServerRuntime), new PropertyMetadata(new Version()));
        public static readonly DependencyProperty ProfileSnapshotProperty = DependencyProperty.Register(nameof(ProfileSnapshot), typeof(ServerProfileSnapshot), typeof(ServerRuntime), new PropertyMetadata(null));

        public AvailabilityStatus Availability
        {
            get { return (AvailabilityStatus)GetValue(AvailabilityProperty); }
            protected set { SetValue(AvailabilityProperty, value); }
        }

        public ServerStatus Status
        {
            get { return (ServerStatus)GetValue(StatusProperty); }
            protected set { SetValue(StatusProperty, value); }
        }

        public string StatusString
        {
            get { return (string)GetValue(StatusStringProperty); }
            protected set { SetValue(StatusStringProperty, value); }
        }

        public int MaxPlayers
        {
            get { return (int)GetValue(MaxPlayersProperty); }
            protected set { SetValue(MaxPlayersProperty, value); }
        }

        public int Players
        {
            get { return (int)GetValue(PlayersProperty); }
            protected set { SetValue(PlayersProperty, value); }
        }

        /// <summary>
        /// Whether <see cref="Players"/> is known: the count comes from RCON, so without an RCON reply it isn't.
        /// </summary>
        public PlayerCountStatus PlayerCountStatus
        {
            get { return (PlayerCountStatus)GetValue(PlayerCountStatusProperty); }
            protected set { SetValue(PlayerCountStatusProperty, value); }
        }

        /// <summary>
        /// The player count to show: the number, or a dash when it isn't known.
        /// </summary>
        public string PlayersString
        {
            get { return (string)GetValue(PlayersStringProperty); }
            protected set { SetValue(PlayersStringProperty, value); }
        }

        /// <summary>
        /// What the player count means, or why it isn't known.
        /// </summary>
        public string PlayersTooltip
        {
            get { return (string)GetValue(PlayersTooltipProperty); }
            protected set { SetValue(PlayersTooltipProperty, value); }
        }

        public Version Version
        {
            get { return (Version)GetValue(VersionProperty); }
            protected set { SetValue(VersionProperty, value); }
        }

        public ServerProfileSnapshot ProfileSnapshot
        {
            get { return (ServerProfileSnapshot)GetValue(ProfileSnapshotProperty); }
            set { SetValue(ProfileSnapshotProperty, value); }
        }


        #endregion

        public void Dispose()
        {
            if (this.motdIntervalTimer != null)
            {
                this.motdIntervalTimer.Stop();
                this.motdIntervalTimer.Elapsed -= async (sender, e) => await HandleMotDIntervalTimer();
                this.motdIntervalTimer.Dispose();
                this.motdIntervalTimer = null;
            }

            this.updateRegistration?.DisposeAsync().DoNotWait();
        }

        public Task AttachToProfile(ServerProfile profile)
        {
            AttachToProfileCore(profile);
            GetProfilePropertyChanges(profile);
            return TaskUtils.FinishedTask;
        }

        private void AttachToProfileCore(ServerProfile profile)
        {
            UnregisterForUpdates();

            this.ProfileSnapshot = ServerProfileSnapshot.Create(profile);

            // setup the MotD timer
            this.motdIntervalTimer = new System.Timers.Timer(this.ProfileSnapshot.MOTDInterval * 60 * 1000);
            this.motdIntervalTimer.Elapsed += async (sender, e) => await HandleMotDIntervalTimer();

            // The installed server exe knows its own version; fall back to the last one recorded for the profile.
            var installedVersion = ServerApp.GetServerVersion(profile.InstallDirectory);
            if (installedVersion > new Version(0, 0))
            {
                this.Version = installedVersion;
            }
            else if (Version.TryParse(profile.LastInstalledVersion, out Version lastInstalled))
            {
                this.Version = lastInstalled;
            }

            UpdatePlayersString();
            RegisterForUpdates();
        }

        private void GetProfilePropertyChanges(ServerProfile profile)
        {
            foreach(var notifier in profileNotifiers)
            {
                notifier.Dispose();
            }

            profileNotifiers.Clear();
            profileNotifiers.AddRange(PropertyChangeNotifier.GetNotifiers(
                profile,
                new[] {
                    ServerProfile.ProfileNameProperty,
                    ServerProfile.InstallDirectoryProperty,
                    ServerProfile.ServerPortProperty,
                    ServerProfile.QueryPortProperty,
                    ServerProfile.ServerIPProperty,
                    ServerProfile.MaxPlayersProperty,
                    ServerProfile.RCONEnabledProperty,
                    ServerProfile.RCONPortProperty,

                    ServerProfile.ServerPasswordProperty,
                    ServerProfile.AdminPasswordProperty,

                    ServerProfile.ServerMapProperty,
                    ServerProfile.ServerModIdsProperty,

                    ServerProfile.MOTDIntervalProperty,
                },
                (s, p) =>
                {
                    if (Status == ServerStatus.Stopped || Status == ServerStatus.Uninstalled || Status == ServerStatus.Unknown || Status == ServerStatus.Updating)
                    {
                        AttachToProfileCore(profile);
                    }
                }));
        }

        private void GetServerEndpoints(out IPEndPoint localEndPoint, out IPEndPoint rconEndPoint)
        {
            localEndPoint = null;
            rconEndPoint = null;

            if (!ushort.TryParse(this.ProfileSnapshot.ServerPort.ToString(), out _))
            {
                _logger.Error($"Port is out of range ({this.ProfileSnapshot.ServerPort})");
                return;
            }

            localEndPoint = new IPEndPoint(this.ProfileSnapshot.ServerIPAddress, this.ProfileSnapshot.ServerPort);

            // ASA has no query port and doesn't answer Steam queries; the status watcher uses RCON instead (when enabled).
            if (this.ProfileSnapshot.RCONEnabled && ushort.TryParse(this.ProfileSnapshot.RCONPort.ToString(), out _))
            {
                var rconAddress = IPAddress.Any.Equals(this.ProfileSnapshot.ServerIPAddress) ? IPAddress.Loopback : this.ProfileSnapshot.ServerIPAddress;
                rconEndPoint = new IPEndPoint(rconAddress, this.ProfileSnapshot.RCONPort);
            }
        }

        public string GetServerExe() => Path.Combine(this.ProfileSnapshot.InstallDirectory, Config.Default.ServerBinaryRelativePath, Config.Default.ServerExe);

        public string GetServerLauncherFile() => Path.Combine(this.ProfileSnapshot.InstallDirectory, Config.Default.ServerConfigRelativePath, Config.Default.LauncherFile);

        private void ProcessStatusUpdate(IAsyncDisposable registration, ServerStatusUpdate update)
        {
            if (!ReferenceEquals(registration, this.updateRegistration))
            {
                return;
            }

            TaskUtils.RunOnUIThreadAsync(() =>
            {
                var oldStatus = this.Status;
                var oldAvailability = this.Availability;

                switch (update.Status)
                {
                    case WatcherServerStatus.Unknown:
                        if (oldStatus != ServerStatus.Updating)
                        {
                            UpdateServerStatus(
                                ServerStatus.Unknown, 
                                AvailabilityStatus.Unknown, 
                                false);
                        }

                        if (this.motdIntervalTimer != null && this.motdIntervalTimer.Enabled)
                        {
                            this.motdIntervalTimer.Stop();
                        }

                        break;

                    case WatcherServerStatus.NotInstalled:
                        if (oldStatus != ServerStatus.Updating)
                        {
                            UpdateServerStatus(
                                ServerStatus.Uninstalled, 
                                AvailabilityStatus.Unavailable, 
                                false);
                        }

                        if (this.motdIntervalTimer != null && this.motdIntervalTimer.Enabled)
                        {
                            this.motdIntervalTimer.Stop();
                        }

                        break;

                    case WatcherServerStatus.Stopped:
                        if (oldStatus != ServerStatus.Updating)
                        {
                            UpdateServerStatus(
                                ServerStatus.Stopped, 
                                AvailabilityStatus.Unavailable, 
                                oldStatus == ServerStatus.Initializing || oldStatus == ServerStatus.Running || oldStatus == ServerStatus.Stopping);
                        }

                        if (this.motdIntervalTimer != null && this.motdIntervalTimer.Enabled)
                        {
                            this.motdIntervalTimer.Stop();
                        }

                        break;

                    case WatcherServerStatus.Initializing:
                        if (oldStatus != ServerStatus.Stopping)
                        {
                            UpdateServerStatus(
                                ServerStatus.Initializing, 
                                AvailabilityStatus.Unavailable, 
                                oldStatus != ServerStatus.Initializing && oldStatus != ServerStatus.Unknown);
                        }

                        if (this.motdIntervalTimer != null && this.motdIntervalTimer.Enabled)
                        {
                            this.motdIntervalTimer.Stop();
                        }

                        break;

                    case WatcherServerStatus.LocalSuccess:
                        if (oldStatus != ServerStatus.Stopping)
                        {
                            UpdateServerStatus(
                                ServerStatus.Running,
                                AvailabilityStatus.LocalOnly,
                                oldStatus != ServerStatus.Running && oldStatus != ServerStatus.Unknown);

                            if (this.ProfileSnapshot.MOTDIntervalEnabled && this.motdIntervalTimer != null && !this.motdIntervalTimer.Enabled)
                            {
                                this.motdIntervalTimer.Start();
                            }
                        }
                        else
                        {
                            if (this.motdIntervalTimer != null && this.motdIntervalTimer.Enabled)
                            {
                                this.motdIntervalTimer.Stop();
                            }
                        }
                        break;

                    case WatcherServerStatus.ExternalSkipped:
                        if (oldStatus != ServerStatus.Stopping)
                        {
                            UpdateServerStatus(
                                ServerStatus.Running,
                                oldAvailability >= AvailabilityStatus.PublicOnly ? AvailabilityStatus.PublicOnly : AvailabilityStatus.LocalOnly,
                                oldStatus != ServerStatus.Running && oldStatus != ServerStatus.Unknown);

                            if (this.ProfileSnapshot.MOTDIntervalEnabled && this.motdIntervalTimer != null && !this.motdIntervalTimer.Enabled)
                            {
                                this.motdIntervalTimer.Start();
                            }
                        }
                        else
                        {
                            if (this.motdIntervalTimer != null && this.motdIntervalTimer.Enabled)
                            {
                                this.motdIntervalTimer.Stop();
                            }
                        }
                        break;

                    case WatcherServerStatus.ExternalSuccess:
                        if (oldStatus != ServerStatus.Stopping)
                        {
                            UpdateServerStatus(
                                ServerStatus.Running,
                                AvailabilityStatus.PublicOnly,
                                oldStatus != ServerStatus.Running && oldStatus != ServerStatus.Unknown);

                            if (this.ProfileSnapshot.MOTDIntervalEnabled && this.motdIntervalTimer != null && !this.motdIntervalTimer.Enabled)
                            {
                                this.motdIntervalTimer.Start();
                            }
                        }
                        else
                        {
                            if (this.motdIntervalTimer != null && this.motdIntervalTimer.Enabled)
                            {
                                this.motdIntervalTimer.Stop();
                            }
                        }
                        break;

                    case WatcherServerStatus.Published:
                        if (oldStatus != ServerStatus.Stopping)
                        {
                            UpdateServerStatus(
                                ServerStatus.Running, 
                                AvailabilityStatus.Available, 
                                oldStatus != ServerStatus.Running && oldStatus != ServerStatus.Unknown);

                            if (this.ProfileSnapshot.MOTDIntervalEnabled && this.motdIntervalTimer != null && !this.motdIntervalTimer.Enabled)
                            {
                                this.motdIntervalTimer.Start();
                            }
                        }
                        else
                        {
                            if (this.motdIntervalTimer != null && this.motdIntervalTimer.Enabled)
                            {
                                this.motdIntervalTimer.Stop();
                            }
                        }
                        break;

                    default:
                        if (this.motdIntervalTimer != null && this.motdIntervalTimer.Enabled)
                        {
                            this.motdIntervalTimer.Stop();
                        }

                        break;
                }

                this.Players = update.OnlinePlayerCount;
                this.PlayerCountStatus = update.PlayerCountStatus;
                this.MaxPlayers = this.ProfileSnapshot.MaxPlayerCount;
                UpdatePlayersString();

                // compared with the last count that was known, so RCON dropping out for a poll (or not answering yet
                // while the server starts) isn't reported as everyone leaving and coming back
                if (update.PlayerCountStatus == PlayerCountStatus.Known && update.OnlinePlayerCount != this.lastKnownPlayerCount)
                {
                    this.lastKnownPlayerCount = update.OnlinePlayerCount;
                    PluginHelper.Instance.ProcessAlert(AlertType.OnlinePlayerCountChanged, this.ProfileSnapshot.ProfileName, $"{Config.Default.Alert_OnlinePlayerCountChange} {this.Players} / {this.MaxPlayers}");
                }

                if (update.Version != null)
                {
                    // Reported in the server log ("ARK Version: x.y").
                    this.Version = update.Version;
                }

                this.serverProcess = update.Process;

                StatusUpdate?.Invoke(this, EventArgs.Empty);
            }).DoNotWait();
        }

        private void RegisterForUpdates()
        {
            if (this.updateRegistration == null)
            {
                GetServerEndpoints(out IPEndPoint localEndPoint, out IPEndPoint rconEndPoint);
                if (localEndPoint == null)
                    return;

                this.updateRegistration = ServerStatusWatcher.Instance.RegisterForUpdates(this.ProfileSnapshot.InstallDirectory, this.ProfileSnapshot.ProfileId, localEndPoint, rconEndPoint, this.ProfileSnapshot.RCONPassword, ProcessStatusUpdate);
            }
        }

        private void UnregisterForUpdates()
        {
            this.motdIntervalTimer.Stop();
            this.motdIntervalTimer.Elapsed -= async (sender, e) => await HandleMotDIntervalTimer();

            this.updateRegistration?.DisposeAsync().DoNotWait();
            this.updateRegistration = null;
        }


        public Task StartAsync()
        {
            if(!Environment.Is64BitOperatingSystem)
            {
                MessageBox.Show("The server requires a 64-bit operating system to run. Your operating system is 32-bit and therefore JVH Ark Ascended Server Manager cannot start the server. You may still load and save profiles and settings files for use on other machines.", "64-bit OS Required", MessageBoxButton.OK, MessageBoxImage.Error);
                return TaskUtils.FinishedTask;
            }

            switch(this.Status)
            {
                case ServerStatus.Running:
                case ServerStatus.Initializing:
                case ServerStatus.Stopping:
                    _logger.Debug($"{nameof(StartAsync)} - Server {this.ProfileSnapshot.ProfileName} already running.");
                    return TaskUtils.FinishedTask;
            }

            UnregisterForUpdates();

            var serverExe = GetServerExe();
            var launcherExe = GetServerLauncherFile();

            if (Config.Default.ManageFirewallAutomatically)
            {
                if (SecurityUtils.IsAdministrator())
                {
                    // ASA uses no peer port (game port + 1).
                    var ports = new List<int>() { this.ProfileSnapshot.ServerPort, this.ProfileSnapshot.QueryPort };
                    if (this.ProfileSnapshot.RCONEnabled)
                    {
                        ports.Add(this.ProfileSnapshot.RCONPort);
                    }

                    if (!FirewallUtils.CreateFirewallRules(serverExe, ports, $"{Config.Default.FirewallRulePrefix} {this.ProfileSnapshot.ServerName}", description: "", group: Config.Default.FirewallRulePrefix.Replace(":", "")))
                    {
                        var result = MessageBox.Show("Failed to automatically set firewall rules. If you are running custom firewall software, you may need to set your firewall rules manually. You may turn off automatic firewall management in Settings.\r\n\r\nWould you like to continue running the server anyway?", "Automatic Firewall Management Error", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result == MessageBoxResult.No)
                        {
                            return TaskUtils.FinishedTask;
                        }
                    }
                }
                else
                {
                    var result = MessageBox.Show("Failed to automatically set firewall rules. You must be running the server manager as an administrator to manage the firewall rules.\r\n\r\nWould you like to continue running the server anyway?", "Automatic Firewall Management Error", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.No)
                    {
                        return TaskUtils.FinishedTask;
                    }
                }
            }

            CheckServerWorldFileExists();
            UpdateServerStatus(ServerStatus.Initializing, false);

            try
            {
                var startInfo = new ProcessStartInfo()
                {
                    FileName = launcherExe
                };

                var process = Process.Start(startInfo);
                process.EnableRaisingEvents = true;
            }
            catch (Win32Exception ex)
            {
                UpdateServerStatus(ServerStatus.Unknown, AvailabilityStatus.Unavailable, false);
                throw new FileNotFoundException($"Unable to find {Config.Default.LauncherFile} at {launcherExe}. Server Install Directory: {this.ProfileSnapshot.InstallDirectory}", launcherExe, ex);
            }
            finally
            {
                RegisterForUpdates();
            }
            
            return TaskUtils.FinishedTask;            
        }

        /// <summary>
        /// Stops the server without a countdown, saving the world first (see <see cref="ServerProcessStopper"/>).
        /// </summary>
        public async Task StopAsync()
        {
            if (this.Status != ServerStatus.Running && this.Status != ServerStatus.Initializing)
                return;

            var process = this.serverProcess;
            var profile = this.ProfileSnapshot;
            if (process == null || profile == null)
                return;

            UpdateServerStatus(ServerStatus.Stopping, AvailabilityStatus.Unavailable, true);

            bool exited;
            try
            {
                _logger.Info($"Stopping {profile.ProfileName} (PID {process.Id}).");
                exited = await Task.Run(() => ServerProcessStopper.Stop(process, profile, message => _logger.Info($"{profile.ProfileName}: {message}")));
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warn($"{nameof(StopAsync)} - {profile.ProfileName}: {ex.Message}");
                exited = false;
            }

            await TaskUtils.RunOnUIThreadAsync(() =>
            {
                if (exited)
                {
                    CheckServerWorldFileExists();
                    UpdateServerStatus(ServerStatus.Stopped, AvailabilityStatus.Unavailable, false);
                }
                else
                {
                    // still running, or unknown: the status watcher reports which on its next check
                    _logger.Error($"{profile.ProfileName}: the server didn't stop.");
                    UpdateServerStatus(ServerStatus.Unknown, AvailabilityStatus.Unknown, false);
                }
            });
        }

        public async Task<bool> UpgradeAsync(CancellationToken cancellationToken, bool updateServer, BranchSnapshot branch, bool validate, ProgressDelegate progressCallback)
        {
            if (updateServer && !Environment.Is64BitOperatingSystem)
            {
                var result = MessageBox.Show("The server requires a 64-bit operating system to run. Your operating system is 32-bit and therefore JVH Ark Ascended Server Manager will be unable to start the server, but you may still install it or load and save profiles and settings files for use on other machines.\r\n\r\nDo you wish to continue?", "64-bit OS Required", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    return false;
                }
            }

            try
            {
                await StopAsync();

                bool isNewInstallation = this.Status == ServerStatus.Uninstalled;

                UpdateServerStatus(ServerStatus.Updating, false);

                // Run the SteamCMD to install the server
                var steamCmdFile = SteamCmdUpdater.GetSteamCmdFile(Config.Default.DataDir);
                if (string.IsNullOrWhiteSpace(steamCmdFile) || !File.Exists(steamCmdFile))
                {
                    progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} ***********************************");
                    progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} ERROR: SteamCMD could not be found. Expected location is {steamCmdFile}");
                    progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} ***********************************");
                    return false;
                }

                // record the start time of the process, this is used to determine if any files changed in the download process.
                var startTime = DateTime.Now;

                var gotNewVersion = false;
                var downloadSuccessful = false;
                var success = false;

                if (updateServer)
                {
                    // *********************
                    // Server Update Section
                    // *********************

                    var branchInfo = ServerApp.GetBranchInfo(branch?.AppIdServer, branch?.BranchName);

                    progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} Starting server update.");
                    progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} Server branch: {branchInfo}.");
                    progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} Profile name: {this.ProfileSnapshot.ProfileName}.");

                    // create the branch arguments
                    var steamCmdInstallServerBetaArgs = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(branch?.BranchName))
                    {
                        steamCmdInstallServerBetaArgs.AppendFormat(Config.Default.SteamCmdInstallServerBetaNameArgsFormat, branch.BranchName);
                        if (!string.IsNullOrWhiteSpace(branch?.BranchPassword))
                        {
                            steamCmdInstallServerBetaArgs.Append(" ");
                            steamCmdInstallServerBetaArgs.AppendFormat(Config.Default.SteamCmdInstallServerBetaPasswordArgsFormat, branch?.BranchPassword);
                        }
                    }

                    // Check if this is a new server installation.
                    if (isNewInstallation && Config.Default.AutoUpdate_EnableUpdate && !string.IsNullOrWhiteSpace(Config.Default.AutoUpdate_CacheDir))
                    {
                        var cacheFolder = ServerApp.GetServerCacheFolder(branch?.AppIdServer, branch?.BranchName);

                        // check if the auto-update facility is enabled and the cache folder defined.
                        if (!string.IsNullOrWhiteSpace(cacheFolder) && Directory.Exists(cacheFolder))
                        {
                            // Auto-Update enabled and cache foldler exists.
                            progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} Installing server from local cache...may take a while to copy all the files.");

                            // Install the server files from the cache.
                            var installationFolder = this.ProfileSnapshot.InstallDirectory;
                            int count = 0;
                            await Task.Run(() =>
                                ServerApp.DirectoryCopy(cacheFolder, installationFolder, true, Config.Default.AutoUpdate_UseSmartCopy, (p, m, n) =>
                                {
                                    count++;
                                    progressCallback?.Invoke(0, ".", count % DIRECTORIES_PER_LINE == 0);
                                }), cancellationToken);
                        }
                    }

                    progressCallback?.Invoke(0, "", true);
                    progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} Updating server from steam.");

                    downloadSuccessful = !Config.Default.SteamCmdRedirectOutput;
                    DataReceivedEventHandler serverOutputHandler = (s, e) =>
                    {
                        var dataValue = e.Data ?? string.Empty;
                        progressCallback?.Invoke(0, dataValue);
                        if (!gotNewVersion && dataValue.Contains("downloading,"))
                        {
                            gotNewVersion = true;
                        }
                        if (dataValue.StartsWith("Success!"))
                        {
                            downloadSuccessful = true;
                        }
                    };

                    var steamCmdRemoveQuit = CommonConfig.Default.SteamCmdRemoveQuit && !Config.Default.SteamCmdRedirectOutput;
                    var steamCmdArgs = SteamUtils.BuildSteamCmdArguments(steamCmdRemoveQuit, Config.Default.SteamCmdInstallServerArgsFormat, Config.Default.SteamCmd_AnonymousUsername, this.ProfileSnapshot.InstallDirectory, this.ProfileSnapshot.AppIdServer, steamCmdInstallServerBetaArgs.ToString(), validate ? "validate" : string.Empty);
                    var workingDirectory = Config.Default.DataDir;

                    var SteamCmdIgnoreExitStatusCodes = SteamUtils.GetExitStatusList(Config.Default.SteamCmdIgnoreExitStatusCodes);

                    success = await ServerUpdater.UpgradeServerAsync(steamCmdFile, steamCmdArgs, workingDirectory, null, null, this.ProfileSnapshot.InstallDirectory, SteamCmdIgnoreExitStatusCodes, Config.Default.SteamCmdRedirectOutput ? serverOutputHandler : null, cancellationToken, steamCmdRemoveQuit ? ProcessWindowStyle.Normal : ProcessWindowStyle.Minimized);
                    if (success && downloadSuccessful)
                    {
                        progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} Finished server update.");

                        if (Directory.Exists(this.ProfileSnapshot.InstallDirectory))
                        {
                            if (!Config.Default.SteamCmdRedirectOutput)
                                // check if any of the server files have changed.
                                gotNewVersion = ServerApp.HasNewServerVersion(this.ProfileSnapshot.InstallDirectory, startTime);

                            progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} New server version - {gotNewVersion.ToString().ToUpperInvariant()}.");

                            // update the version number of the server.
                            this.Version = ServerApp.GetServerVersion(this.ProfileSnapshot.InstallDirectory);

                            progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} Server version: {this.Version}\r\n");
                        }
                    }
                    else
                    {
                        success = false;
                        progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} ****************************");
                        progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} ERROR: Failed server update.");
                        progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} ****************************");
                        progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} Check steamcmd logs for more information why the server update failed.\r\n");

                        if (Config.Default.SteamCmdRedirectOutput)
                            progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} If the server update keeps failing try disabling the '{_globalizer.GetResourceString("GlobalSettings_SteamCmdRedirectOutputLabel")}' option in the settings window.\r\n");
                    }
                }
                else
                    success = true;

                progressCallback?.Invoke(0, $"{SteamCmdUpdater.OUTPUT_PREFIX} Finished upgrade process.");
                return success;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            finally
            {
                UpdateServerStatus(ServerStatus.Stopped, false);
            }
        }


        private void CheckServerWorldFileExists()
        {
            var serverApp = new ServerApp()
            {
                BackupWorldFile = false,
                DeleteOldBackupFiles = false,
                SendAlerts = false,
                SendEmails = false,
                OutputLogs = false
            };
            serverApp.CheckServerWorldFileExists(ProfileSnapshot);
        }

        public void DeleteFirewallRules()
        {
            if (Config.Default.ManageFirewallAutomatically)
            {
                if (SecurityUtils.IsAdministrator())
                {
                    var serverExe = GetServerExe();

                    FirewallUtils.DeleteFirewallRules(serverExe);
                }
            }
        }

        public void UpdateServerStatus(ServerStatus serverStatus, bool sendAlert)
        {
            var availability = Availability;

            switch (serverStatus)
            {
                case ServerStatus.Stopped:
                case ServerStatus.Stopping:
                case ServerStatus.Uninstalled:
                case ServerStatus.Updating:
                    availability = AvailabilityStatus.Unavailable;
                    break;
                case ServerStatus.Unknown:
                    availability = AvailabilityStatus.Unknown;
                    sendAlert = false;
                    break;
            }

            UpdateServerStatus(serverStatus, availability, sendAlert);
        }

        public void UpdateServerStatus(ServerStatus serverStatus, AvailabilityStatus availabilityStatus, bool sendAlert)
        {
            this.Status = serverStatus;
            this.Availability = availabilityStatus;

            UpdateServerStatusString();

            if (!string.IsNullOrWhiteSpace(Config.Default.Alert_ServerStatusChange) && sendAlert)
                PluginHelper.Instance.ProcessAlert(AlertType.ServerStatusChange, this.ProfileSnapshot.ProfileName, $"{Config.Default.Alert_ServerStatusChange} {StatusString}");
        }

        public void UpdateServerStatusString()
        {
            StatusString = GetServerStatusString(Status);
            UpdatePlayersString();
        }

        private void UpdatePlayersString()
        {
            PlayersString = GetPlayersString(Players, PlayerCountStatus);
            PlayersTooltip = GetPlayersTooltip(PlayerCountStatus);
        }

        public static string GetPlayersString(int players, PlayerCountStatus playerCountStatus)
        {
            return playerCountStatus == PlayerCountStatus.Known ? players.ToString() : "–";
        }

        public static string GetPlayersTooltip(PlayerCountStatus playerCountStatus)
        {
            switch (playerCountStatus)
            {
                case PlayerCountStatus.RconDisabled:
                    return _globalizer.GetResourceString("ServerSettings_PlayersRconDisabledTooltip");
                case PlayerCountStatus.NoRconReply:
                    return _globalizer.GetResourceString("ServerSettings_PlayersNoRconReplyTooltip");
                case PlayerCountStatus.RconPasswordRejected:
                    return _globalizer.GetResourceString("ServerSettings_PlayersRconPasswordRejectedTooltip");
                default:
                    return _globalizer.GetResourceString("ServerSettings_PlayersTooltip");
            }
        }

        public static string GetServerStatusString(ServerStatus status)
        {
            switch (status)
            {
                case ServerStatus.Initializing:
                    return _globalizer.GetResourceString("ServerSettings_RuntimeStatusInitializingLabel");
                case ServerStatus.Running:
                    return _globalizer.GetResourceString("ServerSettings_RuntimeStatusRunningLabel");
                case ServerStatus.Stopped:
                    return _globalizer.GetResourceString("ServerSettings_RuntimeStatusStoppedLabel");
                case ServerStatus.Stopping:
                    return _globalizer.GetResourceString("ServerSettings_RuntimeStatusStoppingLabel");
                case ServerStatus.Uninstalled:
                    return _globalizer.GetResourceString("ServerSettings_RuntimeStatusUninstalledLabel");
                case ServerStatus.Unknown:
                    return _globalizer.GetResourceString("ServerSettings_RuntimeStatusUnknownLabel");
                case ServerStatus.Updating:
                    return _globalizer.GetResourceString("ServerSettings_RuntimeStatusUpdatingLabel");
                default:
                    return _globalizer.GetResourceString("ServerSettings_RuntimeStatusUnknownLabel");
            }
        }

        public void DisableMotDIntervalTimer()
        {
            var snapshot = this.ProfileSnapshot;
            snapshot.MOTDIntervalEnabled = false;
            this.ProfileSnapshot = snapshot;

            this.motdIntervalTimer.Stop();
        }

        private async Task HandleMotDIntervalTimer()
        {
            await TaskUtils.RunOnUIThreadAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(this.ProfileSnapshot.MOTD))
                    return;

                if (this.ProfileSnapshot.RCONEnabled)
                {
                    try
                    {
                        await SendMessageAsync(this.ProfileSnapshot.MOTD, CancellationToken.None);
                        _logger.Debug($"Message of the Day shown.");
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        CloseRconConsole();
                    }
                }

                await Task.Delay(1);
            });
        }

        private void CloseRconConsole()
        {
            if (_rconConsole != null)
            {
                _rconConsole.Dispose();
                _rconConsole = null;

                Task.Delay(1000).Wait();
            }
        }

        /// <returns>false if the server rejected the RCON password (retrying won't help).</returns>
        private bool SetupRconConsole()
        {
            CloseRconConsole();

            if (this.ProfileSnapshot == null || !this.ProfileSnapshot.RCONEnabled)
                return true;

            try
            {
                var endPoint = new IPEndPoint(this.ProfileSnapshot.ServerIPAddress, this.ProfileSnapshot.RCONPort);
                _rconConsole = RconClient.Connect(endPoint, this.ProfileSnapshot.RCONPassword);
            }
            catch (RconAuthenticationException ex)
            {
                _logger.Warn($"{nameof(SetupRconConsole)} - {ex.Message}");
                _rconConsole = null;
                return false;
            }
            catch (Exception)
            {
                _rconConsole = null;
            }

            return true;
        }

        private async Task<bool> SendCommandAsync(string command, bool retryIfFailed)
        {
            if (this.ProfileSnapshot == null || !this.ProfileSnapshot.RCONEnabled)
                return false;
            if (string.IsNullOrWhiteSpace(command))
                return false;

            int retries = 0;
            int rconRetries = 0;
            int maxRetries = retryIfFailed ? RCON_MAXRETRIES : 1;

            while (retries < maxRetries && rconRetries < RCON_MAXRETRIES)
            {
                if (!SetupRconConsole())
                    break;

                if (_rconConsole == null)
                {
                    rconRetries++;
                }
                else
                {
                    rconRetries = 0;

                    try
                    {
                        _rconConsole.SendCommand(command);

                        return true;
                    }
                    catch (Exception)
                    {
                    }

                    retries++;
                }
            }

            return false;
        }

        private async Task<bool> SendMessageAsync(string message, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var sent = await SendCommandAsync($"{ServerRcon.GetMessageCommand(Config.Default.RCON_MessageCommand)} {message}", false);

            if (sent)
            {
                try
                {
                    Task.Delay(Config.Default.SendMessageDelay, token).Wait(token);
                }
                catch { }
            }

            return sent;
        }
    }
}
