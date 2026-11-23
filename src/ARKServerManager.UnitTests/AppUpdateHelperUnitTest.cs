using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Utils;
using System;
using System.Text.Json;

namespace ServerManagerTool.UnitTests
{
    /// <summary>
    /// Reading GitHub's "latest release" reply and deciding whether it's newer than the running app.
    /// </summary>
    [TestClass]
    public class AppUpdateHelperUnitTest
    {
        private static readonly Version Running = new Version(1, 1, 0, 0);

        private static string Reply(string tag, string url = null, bool draft = false, bool prerelease = false) =>
            JsonSerializer.Serialize(new
            {
                tag_name = tag,
                html_url = url ?? $"https://github.com/daksteter/ServerManagers/releases/tag/{tag}",
                name = $"JVH Ark Ascended Server Manager {tag}",
                draft,
                prerelease,
            });

        [TestMethod]
        [DataRow("v1.2.0", "1.2.0")]
        [DataRow("V1.1.1", "1.1.1")]
        [DataRow("v2.0", "2.0.0")]
        [DataRow(" v1.2.0 ", "1.2.0")]
        [DataRow("1.2.0", "1.2.0")]
        public void ParseVersion_ReleaseTag_Version(string tag, string expected)
        {
            Assert.AreEqual(Version.Parse(expected), AppUpdateHelper.ParseVersion(tag));
        }

        [TestMethod]
        [DataRow("v1.2.0-beta")]
        [DataRow("latest")]
        [DataRow("v1")]
        [DataRow("")]
        [DataRow(null)]
        public void ParseVersion_NotAVersion_Null(string tag)
        {
            Assert.IsNull(AppUpdateHelper.ParseVersion(tag));
        }

        [TestMethod]
        public void GetNewerRelease_NewerVersion_ReturnsItsPage()
        {
            var release = AppUpdateHelper.GetNewerRelease(Reply("v1.2.0"), Running);

            Assert.IsNotNull(release);
            Assert.AreEqual(new Version(1, 2, 0), release.Version);
            Assert.AreEqual("https://github.com/daksteter/ServerManagers/releases/tag/v1.2.0", release.Url);
        }

        [TestMethod]
        [DataRow("v1.1.0")] // the running version: 1.1.0.0 and 1.1.0 are the same
        [DataRow("v1.0.9")]
        public void GetNewerRelease_SameOrOlderVersion_Null(string tag)
        {
            Assert.IsNull(AppUpdateHelper.GetNewerRelease(Reply(tag), Running));
        }

        [TestMethod]
        public void GetNewerRelease_DraftOrPrerelease_Null()
        {
            Assert.IsNull(AppUpdateHelper.GetNewerRelease(Reply("v1.2.0", draft: true), Running));
            Assert.IsNull(AppUpdateHelper.GetNewerRelease(Reply("v1.2.0", prerelease: true), Running));
        }

        [TestMethod]
        [DataRow("https://example.com/download")]
        [DataRow("http://github.com/daksteter/ServerManagers/releases/tag/v1.2.0")]
        [DataRow("file:///C:/Windows/notepad.exe")]
        public void GetNewerRelease_PageNotOnGitHub_Null(string url)
        {
            Assert.IsNull(AppUpdateHelper.GetNewerRelease(Reply("v1.2.0", url), Running));
        }

        [TestMethod]
        [DataRow("{}")]
        [DataRow("[]")]
        [DataRow("{\"message\": \"Not Found\"}")]
        [DataRow("{\"tag_name\": 12, \"html_url\": null}")]
        public void GetNewerRelease_UnexpectedReply_Null(string json)
        {
            Assert.IsNull(AppUpdateHelper.GetNewerRelease(json, Running));
        }
    }
}
