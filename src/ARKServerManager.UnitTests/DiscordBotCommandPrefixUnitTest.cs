using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.DiscordBot;
using ServerManagerTool.DiscordBot.Models;
using ServerManagerTool.DiscordBot.Modules;
using System.Threading.Tasks;

namespace ServerManagerTool.UnitTests
{
    /// <summary>
    /// How the Discord bot finds the command after its prefix, and how the help shows the prefix.
    /// </summary>
    [TestClass]
    public class DiscordBotCommandPrefixUnitTest
    {
        [TestMethod]
        [DataRow("!asa status", "!asa", "status")]
        [DataRow("!ASA status", "!asa", "status")]
        [DataRow("!asa   status", "!asa", "status")]
        [DataRow("!asa status", "!asa ", "status")]
        [DataRow("!asa info island", "!asa", "info island")]
        [DataRow("asm!status", "asm!", "status")]
        [DataRow("asm! status", "asm!", "status")]
        public void TryGetCommandStart_MessageStartingWithThePrefix_FindsTheCommand(string message, string prefix, string command)
        {
            Assert.IsTrue(CommandPrefixHelper.TryGetCommandStart(message, prefix, out var commandStart));
            Assert.AreEqual(command, message[commandStart..]);
        }

        [TestMethod]
        [DataRow("!asap, the server's down", "!asa")]
        [DataRow("!asastatus", "!asa")]
        [DataRow("status", "!asa")]
        [DataRow("see !asa status", "!asa")]
        [DataRow("!asa status", "")]
        [DataRow("", "!asa")]
        public void TryGetCommandStart_NoPrefix_NotACommand(string message, string prefix)
        {
            Assert.IsFalse(CommandPrefixHelper.TryGetCommandStart(message, prefix, out _));
        }

        [TestMethod]
        [DataRow("!asa", "!asa ")]
        [DataRow("!asa ", "!asa ")]
        [DataRow("asm!", "asm!")]
        public void ForDisplay_ShowsHowToTypeTheCommand(string prefix, string expected)
        {
            Assert.AreEqual(expected, CommandPrefixHelper.ForDisplay(prefix));
        }

        [TestMethod]
        [DataRow("!asa help", "!asa")]
        [DataRow("!asa  help", "!asa")]
        [DataRow("asm! help", "asm!")]
        public async Task CommandService_SpaceAfterThePrefix_FindsTheCommand(string message, string prefix)
        {
            var commands = new CommandService();
            using var services = new ServiceCollection().AddSingleton(new DiscordBotConfig()).BuildServiceProvider();
            await commands.AddModuleAsync<HelpModule>(services);

            Assert.IsTrue(CommandPrefixHelper.TryGetCommandStart(message, prefix, out var commandStart));

            Assert.IsTrue(commands.Search(message[commandStart..]).IsSuccess);
            // what the bot did before: a space after the prefix made Discord.Net answer "Unknown command."
            Assert.IsFalse(commands.Search(message[prefix.Length..]).IsSuccess);
        }
    }
}
