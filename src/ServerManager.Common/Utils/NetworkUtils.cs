using NLog;
using ServerManagerTool.Common.Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace ServerManagerTool.Common.Utils
{
    public static class NetworkUtils
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static List<NetworkAdapterEntry> GetAvailableIPV4NetworkAdapters()
        {
            List<NetworkAdapterEntry> adapters = new List<NetworkAdapterEntry>();
            
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach(var ifc in interfaces)
            {
                var ipProperties = ifc.GetIPProperties();
                if (ipProperties != null)
                {
                    adapters.AddRange(ipProperties.UnicastAddresses.Select(a => a.Address)
                                                                   .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                                                                   .Select(a => new NetworkAdapterEntry(a, ifc.Description)));
                }
            }

            return adapters;
        }

        public static NetworkAdapterEntry GetPreferredIP(IEnumerable<NetworkAdapterEntry> adapters)
        {
            //
            // Try for a 192.168. address first
            //
            var preferredIp = adapters.FirstOrDefault(a => a.IPAddress.StartsWith("192.168."));
            if (preferredIp == null)
            {
                //
                // Try a 10.0 address next
                //
                preferredIp = adapters.FirstOrDefault(a => a.IPAddress.StartsWith("10.0."));
                if (preferredIp == null)
                {
                    // 
                    // Sad.  Just take the first.
                    //
                    preferredIp = adapters.FirstOrDefault();
                }
            }

            return preferredIp;
        }

        /// <summary>
        /// The app's one HTTP client (HttpClient is meant to be shared: one per request exhausts sockets).
        /// </summary>
        public static HttpClient HttpClient { get; } = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"JVHArkAscendedServerManager/{typeof(NetworkUtils).Assembly.GetName().Version?.ToString(3)}");
            return client;
        }

        private static readonly TimeSpan PublicIPCheckTimeout = TimeSpan.FromSeconds(15);

        public static string DiscoverPublicIP()
        {
            // on the thread pool, so blocking here can't deadlock a caller on the UI thread
            return Task.Run(DiscoverPublicIPAsync).GetAwaiter().GetResult();
        }

        public static async Task<string> DiscoverPublicIPAsync()
        {
            foreach (var url in new[] { CommonConfig.Default.PublicIPCheckUrl1, CommonConfig.Default.PublicIPCheckUrl2 })
            {
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                try
                {
                    using var timeout = new CancellationTokenSource(PublicIPCheckTimeout);
                    var publicIP = (await HttpClient.GetStringAsync(url, timeout.Token)).Trim();
                    if (IPAddress.TryParse(publicIP, out _))
                    {
                        return publicIP;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"{nameof(DiscoverPublicIPAsync)} - Exception checking for public ip at {url}. {ex.Message}");
                }
            }

            return String.Empty;
        }
    }
}
