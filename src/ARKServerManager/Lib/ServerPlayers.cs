using ArkData;
using NLog;
using ServerManagerTool.Common.Model;
using ServerManagerTool.Common.Utils;
using ServerManagerTool.Enums;
using ServerManagerTool.Lib.ViewModel.RCON;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ServerManagerTool.Lib
{
    public class ServerPlayers : DependencyObject
    {
        private const int PLAYER_LIST_INTERVAL = 5000;

        public event EventHandler PlayersCollectionUpdated;

        public static readonly DependencyProperty PlayersProperty = DependencyProperty.Register(nameof(Players), typeof(SortableObservableCollection<PlayerInfo>), typeof(ServerPlayers), new PropertyMetadata(null));
        public static readonly DependencyProperty CountPlayersProperty = DependencyProperty.Register(nameof(CountPlayers), typeof(int), typeof(ServerPlayers), new PropertyMetadata(0));
        public static readonly DependencyProperty CountInvalidPlayersProperty = DependencyProperty.Register(nameof(CountInvalidPlayers), typeof(int), typeof(ServerPlayers), new PropertyMetadata(0));

        private readonly ConcurrentDictionary<string, PlayerInfo> _players = new ConcurrentDictionary<string, PlayerInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly object _updatePlayerCollectionLock = new object();
        private CancellationTokenSource _cancellationTokenSource = null;
        private PlayerListParameters _playerListParameters;

        private Logger _allLogger;
        private Logger _eventLogger;
        private Logger _debugLogger;
        private Logger _errorLogger;
        private bool _disposed = false;

        public ServerPlayers(PlayerListParameters parameters)
        {
            this.Players = new SortableObservableCollection<PlayerInfo>();

            _playerListParameters = parameters;

            _allLogger = App.GetProfileLogger(_playerListParameters.ProfileId, "PlayerList_All", LogLevel.Info, LogLevel.Info);
            _eventLogger = App.GetProfileLogger(_playerListParameters.ProfileId, "PlayerList_Event", LogLevel.Info, LogLevel.Info);
            _debugLogger = App.GetProfileLogger(_playerListParameters.ProfileId, "PlayerList_Debug", LogLevel.Trace, LogLevel.Debug);
            _errorLogger = App.GetProfileLogger(_playerListParameters.ProfileId, "PlayerList_Error", LogLevel.Error, LogLevel.Fatal);

            UpdatePlayersAsync().DoNotWait();
        }

        public void Dispose()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
            }
            _disposed = true;
        }

        public SortableObservableCollection<PlayerInfo> Players
        {
            get { return (SortableObservableCollection<PlayerInfo>)GetValue(PlayersProperty); }
            set { SetValue(PlayersProperty, value); }
        }

        public int CountPlayers
        {
            get { return (int)GetValue(CountPlayersProperty); }
            set { SetValue(CountPlayersProperty, value); }
        }

        public int CountInvalidPlayers
        {
            get { return (int)GetValue(CountInvalidPlayersProperty); }
            set { SetValue(CountInvalidPlayersProperty, value); }
        }

        private void LogEvent(LogEventType eventType, string message)
        {
            switch (eventType)
            {
                case LogEventType.All:
                    _allLogger?.Info(message);
                    return;

                case LogEventType.Event:
                    _eventLogger?.Info(message);
                    return;
            }
        }

        protected void OnPlayerCollectionUpdated()
        {
            PlayersCollectionUpdated?.Invoke(this, EventArgs.Empty);
        }

        private async Task UpdatePlayersAsync()
        {
            if (_disposed)
                return;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            await UpdatePlayerDetailsAsync(_cancellationTokenSource.Token)
                .ContinueWith(async t1 =>
                {
                    await TaskUtils.RunOnUIThreadAsync(() =>
                    {
                        UpdatePlayerCollection();
                    });
                }, TaskContinuationOptions.NotOnCanceled)
                .ContinueWith(t2 =>
                {
                    var cancelled = _cancellationTokenSource.IsCancellationRequested;
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;

                    if (!cancelled)
                        Task.Delay(PLAYER_LIST_INTERVAL).ContinueWith(t3 => UpdatePlayersAsync());
                });
        }

        private async Task UpdatePlayerDetailsAsync(CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(_playerListParameters.InstallDirectory))
                return;

            var savedPath = ServerProfile.GetProfileSavePath(_playerListParameters.InstallDirectory, _playerListParameters.AltSaveDirectoryName, _playerListParameters.ServerMap);
            var worldSaveFile = _playerListParameters.UseStore ? ServerProfile.GetProfileWorldSaveFile(savedPath, _playerListParameters.ServerMap) : null;
            DataContainer dataContainer = null;

            try
            {
                // load the player data from the files.
                dataContainer = await DataContainer.CreateAsync(savedPath, worldSaveFile, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _errorLogger?.Error($"{nameof(UpdatePlayerDetailsAsync)} - Error: CreateAsync. {ex.Message}\r\n{ex.StackTrace}");
                return;
            }

            token.ThrowIfCancellationRequested();

            var (adminIds, whitelistIds) = await GetAdminAndWhitelistIdsAsync();

            await Task.Run(() =>
            {
                foreach (var playerData in dataContainer.Players)
                {
                    token.ThrowIfCancellationRequested();

                    var id = playerData.PlayerId;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        var validPlayer = new PlayerInfo()
                        {
                            PlayerId = playerData.PlayerId,
                            PlayerName = playerData.PlayerName,
                            IsValid = true,
                        };

                        _players.AddOrUpdate(id, validPlayer, (k, v) => { v.PlayerName = playerData.PlayerName; v.IsValid = true; return v; });
                    }
                    else
                    {
                        id = Path.GetFileNameWithoutExtension(playerData.Filename);
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            var invalidPlayer = new PlayerInfo()
                            {
                                PlayerId = id,
                                PlayerName = "< corrupted profile >",
                                IsValid = false,
                            };

                            _players.AddOrUpdate(id, invalidPlayer, (k, v) => { v.PlayerName = "< corrupted profile >"; v.IsValid = false; return v; });
                        }
                        else
                        {
                            _debugLogger?.Debug($"{nameof(UpdatePlayerDetailsAsync)} - Error: corrupted profile.\r\n{playerData.Filename}.");
                            continue;
                        }
                    }

                    if (_players.TryGetValue(id, out PlayerInfo player) && player != null)
                    {
                        player.UpdateData(playerData);
                        player.IsAdmin = adminIds.Contains(player.PlayerId);
                        player.IsWhitelisted = whitelistIds.Contains(player.PlayerId);
                    }
                }

                // remove any players that no longer have a profile.
                var profileIds = new HashSet<string>(dataContainer.Players.Select(pd => string.IsNullOrWhiteSpace(pd.PlayerId) ? Path.GetFileNameWithoutExtension(pd.Filename) : pd.PlayerId).Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
                foreach (var droppedPlayer in _players.Values.Where(p => !profileIds.Contains(p.PlayerId)).ToArray())
                {
                    _players.TryRemove(droppedPlayer.PlayerId, out _);
                }
            }, token);
        }

        private async Task<(HashSet<string> AdminIds, HashSet<string> WhitelistIds)> GetAdminAndWhitelistIdsAsync()
        {
            var adminIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var whitelistIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // the lists belong to the profile (a DependencyObject), so read them on the UI thread
            await TaskUtils.RunOnUIThreadAsync(() =>
            {
                var profile = _playerListParameters?.Server?.Profile;
                adminIds.UnionWith(profile?.ServerFilesAdmins?.Select(u => u.PlayerId).Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>());
                whitelistIds.UnionWith(profile?.ServerFilesWhitelisted?.Select(u => u.PlayerId).Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>());
            });

            return (adminIds, whitelistIds);
        }

        private void UpdatePlayerCollection()
        {
            lock (_updatePlayerCollectionLock)
            {
                this.Players = new SortableObservableCollection<PlayerInfo>(_players.Values);
                this.CountPlayers = this.Players.Count;
                this.CountInvalidPlayers = this.Players.Count(p => !p.IsValid);

                OnPlayerCollectionUpdated();
            }
        }
    }
}
