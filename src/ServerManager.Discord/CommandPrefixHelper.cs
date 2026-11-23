using Discord.Commands;
using System;
using System.Linq;

namespace ServerManagerTool.DiscordBot
{
    /// <summary>
    /// Finding the command after the bot's prefix ("!asa status") and showing the prefix in the help.
    /// </summary>
    public static class CommandPrefixHelper
    {
        /// <summary>
        /// Whether the message starts with the prefix, and where the command after it starts. Case doesn't matter, and
        /// spaces after the prefix (or in the setting, around it) are skipped. A prefix ending in a letter or digit must
        /// be followed by a space ("!asa status"), so a word that merely starts with it ("!asap") isn't taken for a command.
        /// </summary>
        public static bool TryGetCommandStart(string message, string prefix, out int commandStart)
        {
            commandStart = 0;
            prefix = prefix?.Trim();
            if (string.IsNullOrEmpty(prefix) || message is null || !message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            if (char.IsLetterOrDigit(prefix[^1]) && message.Length > prefix.Length && !char.IsWhiteSpace(message[prefix.Length]))
                return false;

            commandStart = SkipSpaces(message, prefix.Length);
            return true;
        }

        /// <summary>
        /// The first position at or after the index that isn't white space. Discord.Net doesn't find a command whose text
        /// starts with a space; it answers "Unknown command."
        /// </summary>
        public static int SkipSpaces(string text, int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
            return index;
        }

        /// <summary>
        /// The prefix as written before a command in the help: "!asa status", or "asm!status" for a prefix ending in a symbol.
        /// </summary>
        public static string ForDisplay(string prefix)
        {
            prefix = prefix?.Trim() ?? string.Empty;
            return prefix.Length > 0 && char.IsLetterOrDigit(prefix[^1]) ? prefix + " " : prefix;
        }

        /// <summary>
        /// How the command is typed, with its parameters: "!asa stop [server]", "!asa help <command>".
        /// </summary>
        public static string Usage(string prefix, CommandInfo command)
        {
            return ForDisplay(prefix) + command.Aliases[0] + string.Concat(command.Parameters.Select(p => p.IsOptional ? $" [{p.Name}]" : $" <{p.Name}>"));
        }
    }
}
