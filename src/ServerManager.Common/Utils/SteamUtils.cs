using NLog;
using ServerManagerTool.Common.Lib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace ServerManagerTool.Common.Utils
{
    public static class SteamUtils
    {
        private const string KEYWORK_QUIT = "+quit";

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Removes the beta branch recorded in the UserConfig section of a SteamCMD app manifest
        /// (appmanifest_&lt;appid&gt;.acf), so that SteamCMD goes back to the public branch. Returns whether the file
        /// was changed.
        /// </summary>
        public static bool ClearAppManifestBetaKeys(string manifestFile)
        {
            if (string.IsNullOrWhiteSpace(manifestFile) || !File.Exists(manifestFile))
                return false;

            var manifest = VdfFile.Load(manifestFile);
            var userConfig = manifest["UserConfig"];
            if (userConfig == null || !userConfig.IsSection)
                return false;

            var removed = userConfig.Children.RemoveAll(key => string.Equals(key.Key, "betakey", StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return false;

            VdfFile.Save(manifest, manifestFile);
            return true;
        }

        public static Process GetSteamProcess()
        {
            if (string.IsNullOrWhiteSpace(CommonConfig.Default.SteamClientFile) || !File.Exists(CommonConfig.Default.SteamClientFile))
                return null;

            // Find the server process.
            var expectedPath = IOUtils.NormalizePath(CommonConfig.Default.SteamClientFile);
            var runningProcesses = Process.GetProcessesByName(CommonConfig.Default.SteamProcessName);

            Process process = null;
            foreach (var runningProcess in runningProcesses)
            {
                var runningPath = ProcessUtils.GetMainModuleFilepath(runningProcess.Id);
                if (string.Equals(expectedPath, runningPath, StringComparison.OrdinalIgnoreCase))
                {
                    process = runningProcess;
                    break;
                }
            }

            return process;
        }

        public static string BuildSteamCmdArguments(bool removeQuit, string argumentString)
        {
            if (string.IsNullOrWhiteSpace(argumentString))
                return argumentString;

            var newArgumentString = argumentString.TrimEnd(' ');

            if (newArgumentString.ToLower().EndsWith(KEYWORK_QUIT) && removeQuit)
                return newArgumentString.Substring(0, newArgumentString.Length - KEYWORK_QUIT.Length);
            else if (!newArgumentString.ToLower().EndsWith(KEYWORK_QUIT) && !removeQuit)
                return newArgumentString += $" {KEYWORK_QUIT}";

            return newArgumentString.TrimEnd(' ');
        }

        public static string BuildSteamCmdArguments(bool removeQuit, string argumentFormatString, params string[] argumentValues)
        {
            if (string.IsNullOrWhiteSpace(argumentFormatString) || argumentValues == null || argumentValues.Length == 0)
                return argumentFormatString;

            var argumentString = string.Format(argumentFormatString, argumentValues);
            return BuildSteamCmdArguments(removeQuit, argumentString);
        }

        public static List<int> GetExitStatusList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<int>();
            }

            return new List<int>(Array.ConvertAll(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), int.Parse));
        }
    }
}
