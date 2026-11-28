using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ServerManagerTool.Common.Utils
{
    /// <summary>
    /// Mod IDs as the server takes them in -mods= and -passivemods=: a CurseForge project ID, optionally with a suffix that
    /// a mod developer adds to test an approved but unpublished build ("928988-dev").
    /// </summary>
    public static class ModIdUtils
    {
        private static readonly Regex ModIdRegex = new Regex(@"^(\d+)(?:-[A-Za-z0-9]+)?$");

        // a curseforge.com/projects/<id> link, as accepted by the mod list editor
        private static readonly Regex ProjectLinkRegex = new Regex(@"^https?://(?:www\.)?curseforge\.com/projects/(\d+)/?$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Whether the text is a mod ID; <paramref name="projectId"/> is its CurseForge project ID (the number before
        /// any suffix), for the mod's name and page.
        /// </summary>
        public static bool TryParseModId(string text, out long projectId)
        {
            projectId = 0;
            var match = ModIdRegex.Match(text ?? string.Empty);
            return match.Success && long.TryParse(match.Groups[1].Value, out projectId) && projectId > 0;
        }

        /// <summary>
        /// The mod IDs in a text, separated by commas, semicolons or white space, as written ("928988-dev" keeps its
        /// suffix). A curseforge.com/projects/&lt;id&gt; link gives its project ID. Anything else goes to
        /// <paramref name="invalid"/>.
        /// </summary>
        public static List<string> ParseModIds(string text, out List<string> invalid)
        {
            var modIds = new List<string>();
            invalid = new List<string>();

            foreach (var entry in (text ?? string.Empty).Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = entry.Trim();
                var link = ProjectLinkRegex.Match(token);
                if (link.Success)
                    token = link.Groups[1].Value;

                if (TryParseModId(token, out _))
                    modIds.Add(token);
                else
                    invalid.Add(entry.Trim());
            }

            return modIds;
        }
    }
}
