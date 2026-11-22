using ServerManagerTool.Plugin.Common;
using ServerManagerTool.Plugin.Discord.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;

namespace ServerManagerTool.Plugin.Discord
{
    public sealed class DiscordPlugin : IAlertPlugin, IBeta
    {
        private const int MAX_MESSAGE_LENGTH = 1980; // 2000 minus some formatting characters

        // one client for every webhook call (HttpClient is meant to be shared)
        private static readonly HttpClient _httpClient = new HttpClient();

        private Object lockObject = new Object();

        public DiscordPlugin()
        {
            BetaEnabled = false;
        }

        private DiscordPluginConfig PluginConfig
        {
            get;
            set;
        }

        public bool BetaEnabled
        {
            get;
            set;
        }

        public bool Enabled => true;

        public string ConfigFile => Path.Combine(PluginHelper.PluginFolder, Config.Default.ConfigFile);

        public string PluginCode => Config.Default.PluginCode;

        public string PluginName => Config.Default.PluginName;

        public Version PluginVersion
        {
            get
            {
                try
                {
                    return Assembly.GetExecutingAssembly().GetName().Version;
                }
                catch
                {
                    return new Version();
                }
            }
        }

        public bool HasConfigForm => true;

        public string LanguageCode
        {
            get
            {
                var assembly = typeof(PluginHelper).Assembly;
                if (assembly != null)
                {
                    var pluginHelperType = assembly.GetType(typeof(PluginHelper).FullName, false, true);
                    if (pluginHelperType != null)
                    {
                        var property = pluginHelperType.GetProperty(nameof(LanguageCode));
                        if (property != null)
                        {
                            return property.GetValue(PluginHelper.Instance).ToString();
                        }
                    }
                }

                return "en-US";
            }
        }

        public void HandleAlert(AlertType alertType, string profileName, string alertMessage)
        {
            if (string.IsNullOrWhiteSpace(alertMessage))
                return;

            lock (lockObject)
            {
                foreach (var configProfile in SelectConfigProfiles(PluginConfig.ConfigProfiles, alertType, profileName))
                {
                    HandleAlert(configProfile, alertType, profileName, alertMessage);
                }
            }
        }

        /// <summary>
        /// The enabled config profiles with a webhook that take this alert type for this server profile.
        /// </summary>
        internal static List<ConfigProfile> SelectConfigProfiles(IEnumerable<ConfigProfile> configProfiles, AlertType alertType, string profileName)
        {
            return (configProfiles ?? Enumerable.Empty<ConfigProfile>())
                .Where(cp => cp.IsEnabled
                          && cp.AlertTypes.Any(pn => pn.Value.Equals(alertType))
                          && cp.ProfileNames.Any(pn => pn.Value.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                          && !string.IsNullOrWhiteSpace(cp.DiscordWebhookUrl))
                .ToList();
        }

        /// <summary>
        /// The form data posted to the webhook: the message with the config profile's prefix and styles, and the
        /// optional text-to-speech flag and bot name.
        /// </summary>
        internal static string BuildPostData(ConfigProfile configProfile, string profileName, string alertMessage)
        {
            // remove any bad characters
            var formattedProfileName = profileName?.Replace("&", "_") ?? string.Empty;
            var formattedAlertMessage = alertMessage?.Replace("&", "_") ?? string.Empty;

            // check if we need to add the profile name to the message
            if (configProfile.PrefixMessageWithProfileName && !string.IsNullOrWhiteSpace(formattedProfileName))
                formattedAlertMessage = $"({formattedProfileName}) {formattedAlertMessage}";

            // check if the message is too long
            if (formattedAlertMessage.Length > MAX_MESSAGE_LENGTH)
                formattedAlertMessage = $"{formattedAlertMessage.Substring(0, MAX_MESSAGE_LENGTH - 3)}...";

            // check if we need to apply any styles to the message
            if (configProfile.MessageCodeBlock)
                formattedAlertMessage = $"```{formattedAlertMessage}```";
            if (configProfile.MessageBold)
                formattedAlertMessage = $"**{formattedAlertMessage}**";
            if (configProfile.MessageItalic)
                formattedAlertMessage = $"*{formattedAlertMessage}*";
            if (configProfile.MessageUnderlined)
                formattedAlertMessage = $"__{formattedAlertMessage}__";
            formattedAlertMessage = HttpUtility.UrlEncode(formattedAlertMessage);

            var postData = string.Empty;

            if (configProfile.DiscordUseTTS)
                postData += $"&tts={configProfile.DiscordUseTTS}";
            if (!string.IsNullOrWhiteSpace(configProfile.DiscordBotName))
                postData += $"&username={configProfile.DiscordBotName.Replace("&", "_")}";
            postData += $"&content={formattedAlertMessage}";

            return postData;
        }

        /// <summary>
        /// The webhook URL to post to: trimmed, without a trailing slash, asking Discord to wait for the message.
        /// </summary>
        internal static string GetWebhookPostUrl(string webhookUrl)
        {
            var url = (webhookUrl ?? string.Empty).Trim();
            if (url.EndsWith("/"))
                url = url.Substring(0, url.Length - 1);
            return $"{url}?wait=true";
        }

        internal void HandleAlert(ConfigProfile configProfile, AlertType alertType, string profileName, string alertMessage)
        {
            if (configProfile == null || string.IsNullOrWhiteSpace(configProfile.DiscordWebhookUrl) || string.IsNullOrWhiteSpace(alertMessage))
                return;

            var postData = BuildPostData(configProfile, profileName, alertMessage);

            try
            {
                var data = Encoding.UTF8.GetBytes(postData);

                var content = new ByteArrayContent(data);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                using var request = new HttpRequestMessage(HttpMethod.Post, GetWebhookPostUrl(configProfile.DiscordWebhookUrl)) { Content = content };
                using var timeout = new CancellationTokenSource(Config.Default.RequestTimeout);

                using var httpResponse = _httpClient.Send(request, timeout.Token);
                using var reader = new StreamReader(httpResponse.Content.ReadAsStream(timeout.Token));
                var responseString = reader.ReadToEnd();
                if (httpResponse.StatusCode == HttpStatusCode.OK)
                {
                    Debug.WriteLine($"{nameof(HandleAlert)}\r\nResponse: {responseString}");
                }
                else
                {
                    Debug.WriteLine($"{nameof(HandleAlert)}\r\n{httpResponse.StatusCode}: {responseString}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR: {nameof(HandleAlert)}\r\n{ex.Message}");
            }
        }

        public void Initialize()
        {
            LoadConfig();
        }

        internal void BackupConfig()
        {
            if (!File.Exists(ConfigFile))
                return;

            var backupFile = Path.ChangeExtension(ConfigFile, "bak");
            File.Copy(ConfigFile, backupFile, true);
        }

        internal void LoadConfig()
        {
            PluginConfig = null;

            if (File.Exists(ConfigFile))
            {
                PluginConfig = JsonUtils.DeserializeFromFile<DiscordPluginConfig>(ConfigFile);
            }
            else
            {
                PluginConfig = new DiscordPluginConfig();
                SaveConfig();
            }

            if (PluginConfig is null)
            {
                throw new Exception("Profile config file could not be loaded.");
            }

            PluginConfig.CommitChanges();
        }

        public void OpenConfigForm(Window owner)
        {
            var window = new ConfigWindow(this, PluginConfig);
            window.Owner = owner;
            window.ShowDialog();

            LoadConfig();
        }

        internal void SaveConfig()
        {
            JsonUtils.SerializeToFile(PluginConfig, ConfigFile);
            PluginConfig.CommitChanges();
        }
    }
}
