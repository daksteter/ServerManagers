using System.Linq;
using System.Text.RegularExpressions;

namespace ServerManagerTool.Lib
{
    /// <summary>
    /// The RCON ListPlayers command. ASA servers don't answer Steam (A2S) player queries, so this is how online
    /// players are counted.
    /// </summary>
    public static class RconPlayerList
    {
        public const string Command = "ListPlayers";

        // One player per line ("0. Name, <player id>"), or "No Players Connected".
        private static readonly Regex PlayerLineRegex = new Regex(@"^\s*\d+\.\s+.+,\s*\S+\s*$", RegexOptions.Compiled);

        public static int CountPlayers(string response)
        {
            return (response ?? string.Empty).Split('\n').Count(line => PlayerLineRegex.IsMatch(line));
        }
    }
}
