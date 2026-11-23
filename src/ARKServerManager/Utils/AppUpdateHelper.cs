using NLog;
using ServerManagerTool.Common.Utils;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ServerManagerTool.Utils
{
    /// <summary>
    /// Whether a newer version of the app has been released on GitHub.
    /// </summary>
    internal static class AppUpdateHelper
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// A release newer than the running app, and its page on GitHub.
        /// </summary>
        internal sealed record AppRelease(Version Version, string Url);

        /// <summary>
        /// The latest release, if it's newer than <paramref name="currentVersion"/>. Null when it isn't, and when GitHub
        /// can't be reached or doesn't answer as expected: the check is only a convenience.
        /// </summary>
        public static async Task<AppRelease> GetNewerReleaseAsync(string latestReleaseApiUrl, Version currentVersion)
        {
            if (string.IsNullOrWhiteSpace(latestReleaseApiUrl) || currentVersion is null)
                return null;

            try
            {
                using var timeout = new CancellationTokenSource(RequestTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, latestReleaseApiUrl);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var response = await NetworkUtils.HttpClient.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    // 404 before the first release, 403 when GitHub's hourly limit for this address is used up
                    _logger.Info($"{nameof(GetNewerReleaseAsync)} - GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(timeout.Token);
                var release = GetNewerRelease(json, currentVersion);
                _logger.Info($"{nameof(GetNewerReleaseAsync)} - {(release is null ? "no newer version" : $"version {release.Version} is available")}.");
                return release;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is JsonException || ex is InvalidOperationException || ex is UriFormatException)
            {
                _logger.Info($"{nameof(GetNewerReleaseAsync)} - couldn't check for a new version: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The release in GitHub's "latest release" reply, if it's newer than <paramref name="currentVersion"/>.
        /// </summary>
        internal static AppRelease GetNewerRelease(string json, Version currentVersion)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            // GitHub's "latest" is never a draft or a pre-release, but don't offer one if that changes
            if (IsTrue(root, "draft") || IsTrue(root, "prerelease"))
                return null;

            var version = ParseVersion(GetString(root, "tag_name"));
            var url = GetString(root, "html_url");

            // only ever open a GitHub page from the reply
            if (version is null || url is null || !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                return null;

            return version > Normalize(currentVersion) ? new AppRelease(version, url) : null;
        }

        /// <summary>
        /// A release tag ("v1.2.0") as a version; null if it isn't one (such as "v1.2.0-beta").
        /// </summary>
        internal static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var text = tag.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(1);

            return Version.TryParse(text, out var version) ? Normalize(version) : null;
        }

        // three parts, so the app's 1.1.0.0 and the tag's 1.1.0 are the same version
        private static Version Normalize(Version version) => new Version(version.Major, version.Minor, Math.Max(version.Build, 0));

        private static bool IsTrue(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        private static string GetString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
