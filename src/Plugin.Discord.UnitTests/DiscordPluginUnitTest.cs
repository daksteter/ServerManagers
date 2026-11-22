using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Plugin.Common;
using ServerManagerTool.Plugin.Discord;
using System.Collections.Specialized;
using System.Linq;
using System.Web;

namespace Plugin.Discord.UnitTests
{
    /// <summary>
    /// The webhook message the plugin builds for an alert, and which config profiles receive it. Nothing is sent:
    /// the tests call the formatting and selection directly.
    /// </summary>
    [TestClass]
    public class DiscordPluginUnitTest
    {
        private const string WebhookUrl = "https://discord.com/api/webhooks/1/token";

        private static ConfigProfile NewConfigProfile(params string[] profileNames)
        {
            var configProfile = new ConfigProfile { DiscordWebhookUrl = WebhookUrl };
            foreach (var name in profileNames)
                configProfile.ProfileNames.Add(new ProfileNameValue(name));
            configProfile.AlertTypes.Add(new AlertTypeValue(AlertType.Startup));
            return configProfile;
        }

        // the form fields, decoded (the post data starts with '&')
        private static NameValueCollection Fields(string postData) => HttpUtility.ParseQueryString(postData.TrimStart('&'));

        [TestMethod]
        public void BuildPostData_PlainMessage_IsTheContentOnly()
        {
            var postData = DiscordPlugin.BuildPostData(new ConfigProfile(), "Server 1", "The server has been started.");

            var fields = Fields(postData);
            Assert.AreEqual("The server has been started.", fields["content"]);
            Assert.IsNull(fields["tts"]);
            Assert.IsNull(fields["username"]);
        }

        [TestMethod]
        public void BuildPostData_UnicodeAndLineBreaks_SurviveEncoding()
        {
            var message = "Требуется перезагрузка сервера.\r\n\r\nServer restart in 5 minutes (S+) 731604991.";

            var fields = Fields(DiscordPlugin.BuildPostData(new ConfigProfile(), "Server 1", message));

            Assert.AreEqual(message, fields["content"]);
        }

        [TestMethod]
        public void BuildPostData_Ampersands_AreReplaced()
        {
            var configProfile = new ConfigProfile { PrefixMessageWithProfileName = true, DiscordBotName = "Bots & Co" };

            var fields = Fields(DiscordPlugin.BuildPostData(configProfile, "Tom & Jerry", "Saved & stopped"));

            Assert.AreEqual("(Tom _ Jerry) Saved _ stopped", fields["content"]);
            Assert.AreEqual("Bots _ Co", fields["username"]);
        }

        [TestMethod]
        public void BuildPostData_ProfileNamePrefix_OnlyWhenEnabledAndNamed()
        {
            var prefixed = new ConfigProfile { PrefixMessageWithProfileName = true };

            Assert.AreEqual("(Server 1) Started", Fields(DiscordPlugin.BuildPostData(prefixed, "Server 1", "Started"))["content"]);
            Assert.AreEqual("Started", Fields(DiscordPlugin.BuildPostData(prefixed, "", "Started"))["content"]);
            Assert.AreEqual("Started", Fields(DiscordPlugin.BuildPostData(new ConfigProfile(), "Server 1", "Started"))["content"]);
        }

        [TestMethod]
        public void BuildPostData_LongMessage_IsCutTo1980Characters()
        {
            var message = new string('x', 2500);

            var content = Fields(DiscordPlugin.BuildPostData(new ConfigProfile(), "Server 1", message))["content"];

            Assert.HasCount(1980, content);
            Assert.EndsWith("xxx...", content);
        }

        [TestMethod]
        public void BuildPostData_Styles_WrapTheMessageInOrder()
        {
            var configProfile = new ConfigProfile { MessageCodeBlock = true, MessageBold = true, MessageItalic = true, MessageUnderlined = true };

            var content = Fields(DiscordPlugin.BuildPostData(configProfile, "Server 1", "Started"))["content"];

            Assert.AreEqual("__***```Started```***__", content);
        }

        [TestMethod]
        public void BuildPostData_TextToSpeechAndBotName_AreSentWhenSet()
        {
            var configProfile = new ConfigProfile { DiscordUseTTS = true, DiscordBotName = "ASA Bot" };

            var fields = Fields(DiscordPlugin.BuildPostData(configProfile, "Server 1", "Started"));

            Assert.AreEqual("True", fields["tts"]);
            Assert.AreEqual("ASA Bot", fields["username"]);
            Assert.AreEqual("Started", fields["content"]);
        }

        [TestMethod]
        public void GetWebhookPostUrl_TrimsSpacesAndTrailingSlash()
        {
            Assert.AreEqual($"{WebhookUrl}?wait=true", DiscordPlugin.GetWebhookPostUrl($"  {WebhookUrl}/ "));
            Assert.AreEqual($"{WebhookUrl}?wait=true", DiscordPlugin.GetWebhookPostUrl(WebhookUrl));
        }

        [TestMethod]
        public void SelectConfigProfiles_MatchesEnabledProfilesForTheAlertAndServer()
        {
            var match = NewConfigProfile("Server 1");
            var otherServer = NewConfigProfile("Server 2");
            var disabled = NewConfigProfile("Server 1");
            disabled.IsEnabled = false;
            var noWebhook = NewConfigProfile("Server 1");
            noWebhook.DiscordWebhookUrl = " ";
            var otherAlert = NewConfigProfile("Server 1");
            otherAlert.AlertTypes.Clear();
            otherAlert.AlertTypes.Add(new AlertTypeValue(AlertType.Error));

            var selected = DiscordPlugin.SelectConfigProfiles(new[] { match, otherServer, disabled, noWebhook, otherAlert }, AlertType.Startup, "server 1");

            Assert.HasCount(1, selected);
            Assert.AreSame(match, selected.Single());
        }

        [TestMethod]
        public void SelectConfigProfiles_NoProfiles_SelectsNothing()
        {
            Assert.IsEmpty(DiscordPlugin.SelectConfigProfiles(null, AlertType.Startup, "Server 1"));
        }
    }
}
