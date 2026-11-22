using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArkData
{
    /// <summary>
    /// The players and tribes of a map's save folder: its .arkprofile and .arktribe files and, for servers started with
    /// -usestore, the profiles and tribes kept in the world save.
    /// </summary>
    public class DataContainer
    {
        /// <summary>
        /// The player profiles, including unreadable ones (empty <see cref="PlayerData.PlayerId"/>).
        /// </summary>
        public List<PlayerData> Players { get; } = new List<PlayerData>();

        public List<TribeData> Tribes { get; } = new List<TribeData>();

        public static Task<DataContainer> CreateAsync(string saveFolder, string worldSaveFile = null, CancellationToken token = default)
        {
            return Task.Run(() => Create(saveFolder, worldSaveFile, token), token);
        }

        /// <param name="saveFolder">The folder with the map's world save, profile and tribe files.</param>
        /// <param name="worldSaveFile">The world save, to also read the profiles and tribes stored in it (-usestore); null to skip.</param>
        public static DataContainer Create(string saveFolder, string worldSaveFile = null, CancellationToken token = default)
        {
            var container = new DataContainer();

            if (!string.IsNullOrWhiteSpace(saveFolder) && Directory.Exists(saveFolder))
            {
                var currentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in GetFiles(saveFolder, DataFileDetails.PlayerFileExtension))
                {
                    token.ThrowIfCancellationRequested();
                    currentFiles.Add(file);

                    var player = SaveFileReader.ReadPlayer(file);
                    if (player != null)
                        container.Players.Add(player);
                }

                foreach (var file in GetFiles(saveFolder, DataFileDetails.TribeFileExtension))
                {
                    token.ThrowIfCancellationRequested();
                    currentFiles.Add(file);

                    var tribe = SaveFileReader.ReadTribe(file);
                    if (tribe != null)
                        container.Tribes.Add(tribe);
                }

                if (!string.IsNullOrWhiteSpace(worldSaveFile))
                    currentFiles.Add(Path.GetFullPath(worldSaveFile));

                SaveFileReader.ForgetMissingFiles(saveFolder, currentFiles);
            }

            if (!string.IsNullOrWhiteSpace(worldSaveFile))
            {
                token.ThrowIfCancellationRequested();

                // files win over the copies in the world save (with -BackupTransferPlayerDatas the server writes both)
                var (players, tribes) = SaveFileReader.ReadStoredData(worldSaveFile);
                container.Players.AddRange(players.Where(p => !container.Players.Any(f => string.Equals(f.PlayerId, p.PlayerId, StringComparison.OrdinalIgnoreCase))));
                container.Tribes.AddRange(tribes.Where(t => !container.Tribes.Any(f => f.Id == t.Id)));
            }

            container.LinkPlayerTribe();
            return container;
        }

        /// <summary>
        /// Reads one profile file (cached until the file changes).
        /// </summary>
        /// <returns>The profile; null if the file doesn't exist; an empty <see cref="PlayerData.PlayerId"/> if it can't be read.</returns>
        public static PlayerData ReadPlayer(string profileFile) => SaveFileReader.ReadPlayer(profileFile);

        private static IEnumerable<string> GetFiles(string folder, string extension)
        {
            // the extension check also keeps out e.g. .arktributetribe files
            return Directory.EnumerateFiles(folder, $"*{extension}")
                .Where(f => string.Equals(Path.GetExtension(f), extension, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath);
        }

        /// <summary>
        /// Links the players to their tribes and the tribes to their owner and members.
        /// </summary>
        private void LinkPlayerTribe()
        {
            foreach (var player in Players)
            {
                player.Tribe = player.TribeId.HasValue ? Tribes.FirstOrDefault(t => t.Id == player.TribeId.Value) : null;
                player.OwnedTribes = Tribes.Where(t => t.OwnerId.HasValue && t.OwnerId.Value == player.CharacterId && player.CharacterId != 0).ToList();
            }

            foreach (var tribe in Tribes)
            {
                tribe.Owner = tribe.OwnerId.HasValue ? Players.FirstOrDefault(p => p.CharacterId == tribe.OwnerId.Value && p.CharacterId != 0) : null;
                tribe.Players = Players.Where(p => p.TribeId == tribe.Id).ToList();
            }
        }
    }
}
