using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace ServerManagerTool.Lib
{
    /// <summary>
    /// The mods the ASA server has downloaded from CurseForge. The server keeps the CurseForge details of each installed
    /// mod in its mod library (ShooterGame\Binaries\Win64\ShooterGame\ModsUserData\83374\local\user_info.json), so mod
    /// names are available without a CurseForge API key once the server has downloaded a mod (it does so at startup).
    /// </summary>
    public static class CurseForgeModLibrary
    {
        /// <summary>
        /// CurseForge's game ID for ARK: Survival Ascended.
        /// </summary>
        public const int GameId = 83374;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public sealed class InstalledMod
        {
            public long ModId { get; init; }

            public string Name { get; init; }

            /// <summary>
            /// The installed file's display name (usually the mod's version).
            /// </summary>
            public string Version { get; init; }

            public string WebsiteUrl { get; init; }
        }

        /// <summary>
        /// The mod's CurseForge page (CurseForge redirects project IDs to the page).
        /// </summary>
        public static string GetProjectUrl(long modId) => $"https://www.curseforge.com/projects/{modId}";

        /// <summary>
        /// The mods in the server's mod library, by mod ID. Empty if the server hasn't downloaded any mods yet.
        /// </summary>
        public static Dictionary<long, InstalledMod> LoadInstalledMods(string installDirectory)
        {
            var mods = new Dictionary<long, InstalledMod>();
            if (string.IsNullOrWhiteSpace(installDirectory))
                return mods;

            var folder = Path.Combine(installDirectory, Config.Default.ServerBinaryRelativePath, "ShooterGame", "ModsUserData", GameId.ToString());

            // user_info.json is the current library; library.json is the one older server versions migrated from
            foreach (var file in new[] { Path.Combine(folder, "local", "user_info.json"), Path.Combine(folder, "library.json") })
            {
                if (!File.Exists(file))
                    continue;

                try
                {
                    string json;
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream))
                    {
                        json = reader.ReadToEnd();
                    }

                    // the file also holds the server's CurseForge sign-in token: only the installed mods are read
                    if (JObject.Parse(json)["installedMods"] is not JArray installedMods)
                        continue;

                    foreach (var entry in installedMods)
                    {
                        var mod = ReadInstalledMod(entry as JObject);
                        if (mod != null && !mods.ContainsKey(mod.ModId))
                            mods.Add(mod.ModId, mod);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"{nameof(LoadInstalledMods)} - could not read {file}: {ex.Message}");
                }
            }

            return mods;
        }

        /// <summary>
        /// An installed mod entry: the CurseForge mod ("details") and file ("installedFile") records.
        /// </summary>
        private static InstalledMod ReadInstalledMod(JObject entry)
        {
            if (entry == null)
                return null;

            var details = entry["details"] as JObject ?? entry["mod"] as JObject;
            var file = entry["installedFile"] as JObject ?? entry["file"] as JObject;

            var modId = GetLong(details, "id") ?? GetLong(file, "modId") ?? GetLong(entry, "modId") ?? GetLong(entry, "id");
            if (!modId.HasValue || modId.Value <= 0)
                return null;

            return new InstalledMod
            {
                ModId = modId.Value,
                Name = GetString(details, "name") ?? GetString(entry, "name"),
                Version = GetString(file, "displayName") ?? GetString(file, "fileName"),
                WebsiteUrl = GetString(details?["links"] as JObject, "websiteUrl"),
            };
        }

        private static long? GetLong(JObject obj, string name)
        {
            var token = obj?[name];
            if (token == null)
                return null;

            return token.Type switch
            {
                JTokenType.Integer => token.Value<long>(),
                JTokenType.String when long.TryParse(token.Value<string>(), out var value) => value,
                _ => null,
            };
        }

        private static string GetString(JObject obj, string name)
        {
            var token = obj?[name];
            var value = token?.Type == JTokenType.String ? token.Value<string>() : null;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
