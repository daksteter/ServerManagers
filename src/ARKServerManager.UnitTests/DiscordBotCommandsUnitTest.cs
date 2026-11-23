using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.DiscordBot;
using ServerManagerTool.DiscordBot.Delegates;
using ServerManagerTool.DiscordBot.Interfaces;
using ServerManagerTool.DiscordBot.Models;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServerManagerTool.UnitTests
{
    /// <summary>
    /// The Discord bot's commands as Discord.Net reads them, and how the help shows them.
    /// </summary>
    [TestClass]
    public class DiscordBotCommandsUnitTest
    {
        private sealed class TestBot : IServerManagerBot
        {
            public CancellationToken Token => CancellationToken.None;
            public Task RunAsync(DiscordBotConfig discordBotConfig, HandleCommandDelegate handleCommandCallback, HandleTranslationDelegate handleTranslationCallback, CancellationToken token) => Task.CompletedTask;
        }

        private static async Task<CommandService> GetCommandsAsync()
        {
            var services = new ServiceCollection()
                .AddSingleton(new DiscordBotConfig())
                .AddSingleton<IServerManagerBot>(new TestBot())
                .AddSingleton<HandleCommandDelegate>((commandType, serverId, channelId, profileIdOrAlias, token) => null)
                .BuildServiceProvider();

            var commands = new CommandService();
            await commands.AddModulesAsync(typeof(ServerManagerBot).Assembly, services);
            return commands;
        }

        private static CommandInfo GetCommand(CommandService commands, string name) =>
            commands.Commands.Single(c => c.Name == name && (name != "help" || c.Parameters.Count == 1));

        [TestMethod]
        [DataRow("info")]
        [DataRow("status")]
        [DataRow("backup")]
        [DataRow("restart")]
        [DataRow("shutdown")]
        [DataRow("start")]
        [DataRow("stop")]
        [DataRow("update")]
        public async Task ServerCommand_WithoutAServer_IsAccepted(string message)
        {
            var commands = await GetCommandsAsync();

            var search = commands.Search(message);
            Assert.IsTrue(search.IsSuccess);

            var parse = await search.Commands.Single().ParseAsync(null, search);
            Assert.IsTrue(parse.IsSuccess, parse.ErrorReason);
        }

        [TestMethod]
        public async Task ServerCommand_WithAServer_PassesItOn()
        {
            var commands = await GetCommandsAsync();

            var search = commands.Search("stop island");
            var parse = await search.Commands.Single().ParseAsync(null, search);

            Assert.IsTrue(parse.IsSuccess, parse.ErrorReason);
            Assert.AreEqual("island", parse.ArgValues.Single().BestMatch);
        }

        [TestMethod]
        [DataRow("stop", "!asa", "!asa stop [server]")]
        [DataRow("info", "!asa", "!asa info [server]")]
        [DataRow("list", "!asa", "!asa list")]
        [DataRow("help", "!asa", "!asa help <command>")]
        [DataRow("status", "asm!", "asm!status [server]")]
        public async Task Usage_ShowsTheCommandAsTyped(string name, string prefix, string expected)
        {
            var commands = await GetCommandsAsync();

            Assert.AreEqual(expected, CommandPrefixHelper.Usage(prefix, GetCommand(commands, name)));
        }
    }
}
