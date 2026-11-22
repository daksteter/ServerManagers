using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ServerManagerTool.Lib
{
    /// <summary>
    /// The mods the ASA server has downloaded from CurseForge. The server keeps the CurseForge details of each installed
    /// mod in its mod library (ShooterGame\Binaries\Win64\ShooterGame\ModsUserData\83374\library.json), so mod names are
    /// available without a CurseForge API key once the server has downloaded a mod (it does so at startup).
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
            /// The installed file (e.g. jvh-landscaping-windowsserver-181), which usually carries the mod's version.
            /// </summary>
            public string FileName { get; init; }

            /// <summary>
            /// When the installed file was published on CurseForge.
            /// </summary>
            public DateTime? FileDate { get; init; }

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

            // library.json has the CurseForge details of each installed mod; local\user_info.json only lists their IDs
            // (with load order and enabled state, next to the server's CurseForge sign-in token)
            foreach (var file in new[] { Path.Combine(folder, "library.json"), Path.Combine(folder, "local", "user_info.json") })
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

                    // only the installed mods are read
                    if (GetToken(JObject.Parse(json), "installedMods") is not JArray installedMods)
                        continue;

                    foreach (var entry in installedMods)
                    {
                        // keep the entry with the most details
                        var mod = ReadInstalledMod(entry as JObject);
                        if (mod != null && (!mods.TryGetValue(mod.ModId, out var known) || (known.Name == null && mod.Name != null)))
                            mods[mod.ModId] = mod;
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
        /// An installed mod entry: the CurseForge mod ("details") and file ("installedFile") records in library.json, or
        /// just "modId" in user_info.json. Property names are matched ignoring case (the server writes e.g. "iD", "uRL").
        /// </summary>
        private static InstalledMod ReadInstalledMod(JObject entry)
        {
            if (entry == null)
                return null;

            var details = GetToken(entry, "details") as JObject;
            var file = GetToken(entry, "installedFile") as JObject;

            var modId = GetLong(details, "id") ?? GetLong(file, "modId") ?? GetLong(entry, "modId");
            if (!modId.HasValue || modId.Value <= 0)
                return null;

            var fileName = GetString(file, "displayName") ?? GetString(file, "filename");
            if (fileName != null && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - 4);

            return new InstalledMod
            {
                ModId = modId.Value,
                Name = GetString(details, "name"),
                FileName = fileName,
                FileDate = GetDate(file, "fileDate"),
                WebsiteUrl = GetString(GetToken(details, "links") as JObject, "websiteUrl"),
            };
        }

        private static JToken GetToken(JObject obj, string name) => obj?.GetValue(name, StringComparison.OrdinalIgnoreCase);

        private static long? GetLong(JObject obj, string name)
        {
            var token = GetToken(obj, name);
            if (token == null)
                return null;

            return token.Type switch
            {
                JTokenType.Integer => token.Value<long>(),
                JTokenType.String when long.TryParse(token.Value<string>(), out var value) => value,
                _ => null,
            };
        }

        /// <summary>
        /// A date as the server writes them (2026.11.20-07.02.47).
        /// </summary>
        private static DateTime? GetDate(JObject obj, string name)
        {
            var value = GetString(obj, name);
            return DateTime.TryParseExact(value, "yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        }

        private static string GetString(JObject obj, string name)
        {
            var token = GetToken(obj, name);
            var value = token?.Type == JTokenType.String ? token.Value<string>() : null;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
