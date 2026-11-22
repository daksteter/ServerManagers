using AsaSavegameToolkit;
using AsaSavegameToolkit.Plumbing.Properties;
using AsaSavegameToolkit.Plumbing.Readers;
using AsaSavegameToolkit.Plumbing.Records;
using AsaSavegameToolkit.Porcelain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ArkData
{
    /// <summary>
    /// Reads ASA player profiles and tribes with AsaSavegameToolkit. Files are opened with full sharing, so the server's
    /// writes are never blocked, and what was read is cached until the file changes: refreshing a player list only re-reads
    /// the files the server has written since.
    /// </summary>
    internal static class SaveFileReader
    {
        // A file written in the last few seconds may still be being written: use what was read, but read it again next time.
        private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(5);

        private sealed class CacheEntry
        {
            public DateTime LastWriteTimeUtc { get; init; }
            public long Length { get; init; }
            public object Data { get; init; }
        }

        private sealed class StoredData
        {
            public static readonly StoredData Empty = new StoredData();

            public List<PlayerData> Players { get; } = new List<PlayerData>();
            public List<TribeData> Tribes { get; } = new List<TribeData>();
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> FileCache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object StoredDataLock = new object();

        /// <summary>
        /// Reads a profile file. Returns null if the file doesn't exist (or can't be opened and was never read), and a
        /// profile with an empty <see cref="PlayerData.PlayerId"/> if it can't be parsed.
        /// </summary>
        public static PlayerData ReadPlayer(string file) => (ReadCached(file, ParsePlayerFile) as PlayerData)?.Copy();

        /// <summary>
        /// Reads a tribe file. Returns null if the file doesn't exist or can't be read.
        /// </summary>
        public static TribeData ReadTribe(string file) => (ReadCached(file, ParseTribeFile) as TribeData)?.Copy();

        /// <summary>
        /// Drops the cached files of a folder that are not in <paramref name="currentFiles"/> (deleted since).
        /// </summary>
        public static void ForgetMissingFiles(string folder, ISet<string> currentFiles)
        {
            var fullFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var file in FileCache.Keys)
            {
                if (string.Equals(Path.GetDirectoryName(file), fullFolder, StringComparison.OrdinalIgnoreCase) && !currentFiles.Contains(file))
                    FileCache.TryRemove(file, out _);
            }
        }

        /// <summary>
        /// The profiles and tribes a server started with -usestore keeps in its world save. The world save is copied to a
        /// temp file first (it is an SQLite database the server may be writing), and only when it has changed since the
        /// last read.
        /// </summary>
        public static (IEnumerable<PlayerData> Players, IEnumerable<TribeData> Tribes) ReadStoredData(string worldSaveFile)
        {
            StoredData data;
            lock (StoredDataLock)
            {
                data = ReadStoredDataCached(worldSaveFile);
            }
            return (data.Players.Select(p => p.Copy()), data.Tribes.Select(t => t.Copy()));
        }

        private static object ReadCached(string file, Func<byte[], FileInfo, object> parse)
        {
            var info = new FileInfo(file);
            if (!info.Exists)
                return null;

            var lastWriteTimeUtc = info.LastWriteTimeUtc;
            var length = info.Length;
            FileCache.TryGetValue(info.FullName, out var cached);
            if (cached != null && cached.LastWriteTimeUtc == lastWriteTimeUtc && cached.Length == length)
                return cached.Data;

            byte[] bytes;
            try
            {
                bytes = ReadAllBytesShared(info.FullName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // deleted or locked right now: keep showing what was read last time
                return cached?.Data;
            }

            var data = parse(bytes, info);

            info.Refresh();
            if (info.Exists && info.LastWriteTimeUtc == lastWriteTimeUtc && info.Length == length && DateTime.UtcNow - lastWriteTimeUtc > SettleTime)
                FileCache[info.FullName] = new CacheEntry { LastWriteTimeUtc = lastWriteTimeUtc, Length = length, Data = data };

            return data;
        }

        private static byte[] ReadAllBytesShared(string file)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static object ParsePlayerFile(byte[] bytes, FileInfo info)
        {
            // the file is named after the player's EOS ID; an unreadable profile keeps an empty ID
            var player = ParsePlayer(bytes, info.FullName);
            if (player != null)
                player.PlayerId = Path.GetFileNameWithoutExtension(info.Name);
            else
                player = new PlayerData { PlayerId = string.Empty };

            player.File = info.FullName;
            player.Filename = info.Name;
            player.FileCreated = info.CreationTime;
            player.FileUpdated = info.LastWriteTime;
            return player;
        }

        private static object ParseTribeFile(byte[] bytes, FileInfo info)
        {
            var tribe = ParseTribe(bytes, info.FullName);
            if (tribe == null)
                return null;

            // the file is named after the tribe ID
            if (tribe.Id == 0 && int.TryParse(Path.GetFileNameWithoutExtension(info.Name), out var tribeId))
                tribe.Id = tribeId;

            tribe.File = info.FullName;
            tribe.Filename = info.Name;
            tribe.FileCreated = info.CreationTime;
            tribe.FileUpdated = info.LastWriteTime;
            return tribe;
        }

        /// <returns>The profile, or null if it can't be parsed.</returns>
        private static PlayerData ParsePlayer(byte[] bytes, string sourceName)
        {
            try
            {
                GameObjectRecord record;
                using (var archive = new AsaArchive(NullLogger.Instance, bytes, sourceName))
                {
                    record = ArkProfileRecord.Read(archive, Guid.Empty);
                }

                var player = Player.Create(record, null);
                if (string.IsNullOrWhiteSpace(player.UniqueNetId) && !player.PlayerDataId.HasValue)
                    return null;

                return new PlayerData
                {
                    PlayerId = player.UniqueNetId?.ToLowerInvariant() ?? string.Empty,
                    PlayerName = player.PlayerName ?? string.Empty,
                    CharacterId = player.PlayerDataId ?? 0L,
                    CharacterName = player.CharacterName ?? string.Empty,
                    Level = (int)Math.Clamp(player.Level, 0L, int.MaxValue),
                    TribeId = player.TribeId != 0 ? player.TribeId : null,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <returns>The tribe, or null if it can't be parsed.</returns>
        private static TribeData ParseTribe(byte[] bytes, string sourceName)
        {
            try
            {
                GameObjectRecord record;
                using (var archive = new AsaArchive(NullLogger.Instance, bytes, sourceName))
                {
                    record = ArkTribeRecord.Read(archive, Guid.Empty);
                }

                IList<Property> properties = record.Properties;
                if (GetValue(properties, "TribeData") is List<Property> tribeProperties)
                    properties = tribeProperties;

                var tribe = new TribeData
                {
                    Id = (int)(GetInteger(properties, "TribeID") ?? 0L),
                    Name = GetValue(properties, "TribeName") as string ?? string.Empty,
                    OwnerId = GetInteger(properties, "OwnerPlayerDataID"),
                    TamedDinoCount = (int)(GetInteger(properties, "NumTribeDinos") ?? 0L),
                };

                if (GetValue(properties, "MembersPlayerDataID") is IList members)
                {
                    foreach (var member in members)
                    {
                        if (ToInteger(member) is long memberId)
                            tribe.MemberIds.Add(memberId);
                    }
                }

                return tribe;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static StoredData ReadStoredDataCached(string worldSaveFile)
        {
            var info = new FileInfo(worldSaveFile);
            if (!info.Exists)
                return StoredData.Empty;

            var lastWriteTimeUtc = info.LastWriteTimeUtc;
            var length = info.Length;
            FileCache.TryGetValue(info.FullName, out var cached);
            var cachedData = cached?.Data as StoredData;
            if (cachedData != null && cached.LastWriteTimeUtc == lastWriteTimeUtc && cached.Length == length)
                return cachedData;

            // a save in progress: try again on the next refresh
            if (File.Exists($"{info.FullName}-journal") || File.Exists($"{info.FullName}-wal"))
                return cachedData ?? StoredData.Empty;

            var tempFile = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(info.Name)}_{Guid.NewGuid():N}{info.Extension}");
            try
            {
                using (var source = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var destination = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(destination);
                }

                info.Refresh();
                if (info.LastWriteTimeUtc != lastWriteTimeUtc || info.Length != length)
                    return cachedData ?? StoredData.Empty;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // locked or changed: keep what was read last time and try again on the next refresh
                DeleteTempFile(tempFile);
                return cachedData ?? StoredData.Empty;
            }

            var data = new StoredData();
            try
            {
                GameModeCustomBytesRecord customBytes;
                using (var reader = new AsaSaveReader(tempFile, NullLogger.Instance, new AsaReaderSettings()))
                {
                    customBytes = reader.ReadGameModeCustomBytes(default);
                }

                foreach (var entry in customBytes?.Profiles ?? new List<EmbeddedProfileEntry>())
                {
                    var player = ParsePlayer(entry.RawBlob, info.Name);
                    if (string.IsNullOrWhiteSpace(player?.PlayerId) || data.Players.Any(p => p.PlayerId == player.PlayerId))
                        continue;

                    player.FileUpdated = info.LastWriteTime;
                    data.Players.Add(player);
                }
                foreach (var entry in customBytes?.Tribes ?? new List<EmbeddedTribeEntry>())
                {
                    var tribe = ParseTribe(entry.RawBlob, info.Name);
                    if (tribe == null)
                        continue;
                    if (tribe.Id == 0)
                        tribe.Id = unchecked((int)entry.TribeId);
                    if (tribe.Id == 0 || data.Tribes.Any(t => t.Id == tribe.Id))
                        continue;

                    tribe.FileUpdated = info.LastWriteTime;
                    data.Tribes.Add(tribe);
                }
            }
            catch (Exception)
            {
                // a world save this version of the toolkit can't read: don't copy it again until the server saves again
                data = StoredData.Empty;
            }
            finally
            {
                // pooled connections keep the temp file open
                SqliteConnection.ClearAllPools();
                DeleteTempFile(tempFile);
            }

            FileCache[info.FullName] = new CacheEntry { LastWriteTimeUtc = lastWriteTimeUtc, Length = length, Data = data };
            return data;
        }

        private static void DeleteTempFile(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
                // left in the temp folder
            }
        }

        private static object GetValue(IList<Property> properties, string name)
        {
            foreach (var property in properties)
            {
                if (property?.Tag != null && property.Tag.ArrayIndex == 0 && string.Equals(property.Tag.Name.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property.GetValue();
            }
            return null;
        }

        private static long? GetInteger(IList<Property> properties, string name) => ToInteger(GetValue(properties, name));

        private static long? ToInteger(object value)
        {
            return value switch
            {
                int i => i,
                uint u => u,
                long l => l,
                ulong ul => unchecked((long)ul),
                short s => s,
                ushort us => us,
                byte b => b,
                sbyte sb => sb,
                _ => null,
            };
        }
    }
}
