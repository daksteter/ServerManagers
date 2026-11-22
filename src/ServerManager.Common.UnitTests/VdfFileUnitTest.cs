using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Common.Lib;
using ServerManagerTool.Common.Utils;

namespace ServerManager.Common.Tests
{
    /// <summary>
    /// <see cref="VdfFile"/> and <see cref="SteamUtils.ClearAppManifestBetaKeys"/> on files laid out like SteamCMD's
    /// app manifests, and <see cref="FileDialogUtils.Filter"/>.
    /// </summary>
    [TestClass]
    public class VdfFileUnitTest
    {
        // Laid out exactly as SteamCMD writes it: tabs, two tabs between key and value, LF line ends.
        private const string Manifest =
            "\"AppState\"\n" +
            "{\n" +
            "\t\"appid\"\t\t\"2430930\"\n" +
            "\t\"name\"\t\t\"ARK Survival Ascended Dedicated Server\"\n" +
            "\t\"LauncherPath\"\t\t\"C:\\\\SteamCMD\\\\steamcmd.exe\"\n" +
            "\t\"InstalledDepots\"\n" +
            "\t{\n" +
            "\t\t\"2430931\"\n" +
            "\t\t{\n" +
            "\t\t\t\"manifest\"\t\t\"1234567890123456789\"\n" +
            "\t\t\t\"size\"\t\t\"11811160064\"\n" +
            "\t\t}\n" +
            "\t}\n" +
            "\t\"UserConfig\"\n" +
            "\t{\n" +
            "\t\t\"BetaKey\"\t\t\"preview\"\n" +
            "\t\t\"language\"\t\t\"english\"\n" +
            "\t}\n" +
            "\t\"MountedConfig\"\n" +
            "\t{\n" +
            "\t\t\"BetaKey\"\t\t\"preview\"\n" +
            "\t}\n" +
            "}\n";

        [TestMethod]
        public void Parse_SteamCmdManifest_ReadsKeysAndWritesTheSameText()
        {
            var root = VdfFile.Parse(Manifest);

            Assert.AreEqual("AppState", root.Key);
            Assert.AreEqual("2430930", root["appid"].Value);
            Assert.AreEqual(@"C:\SteamCMD\steamcmd.exe", root["launcherpath"].Value);
            Assert.AreEqual("11811160064", root["InstalledDepots"]["2430931"]["size"].Value);
            Assert.AreEqual("preview", root["UserConfig"]["betakey"].Value);
            Assert.IsFalse(root["appid"].IsSection);
            Assert.IsTrue(root["UserConfig"].IsSection);
            Assert.IsNull(root["missing"]);

            Assert.AreEqual(Manifest, VdfFile.Serialize(root));
        }

        [TestMethod]
        public void Parse_CommentsUnquotedStringsEscapesAndCrLf_AreRead()
        {
            var text =
                "// written by hand\r\n" +
                "Root\r\n" +
                "{\r\n" +
                "    plain    value // trailing comment\r\n" +
                "    \"quote\" \"say \\\"hi\\\"\"\r\n" +
                "    \"lines\" \"a\\nb\\tc\"\r\n" +
                "    \"empty\" \"\"\r\n" +
                "    \"url\" \"https://example.com/a\"\r\n" +
                "    \"same\" \"1\"\r\n" +
                "    \"same\" \"2\"\r\n" +
                "    \"Section\" { }\r\n" +
                "}\r\n";

            var root = VdfFile.Parse(text);

            Assert.AreEqual("Root", root.Key);
            Assert.AreEqual("value", root["plain"].Value);
            Assert.AreEqual("say \"hi\"", root["quote"].Value);
            Assert.AreEqual("a\nb\tc", root["lines"].Value);
            Assert.AreEqual(string.Empty, root["empty"].Value);
            Assert.AreEqual("https://example.com/a", root["url"].Value);
            CollectionAssert.AreEqual(new[] { "1", "2" }, root.Children.Where(c => c.Key == "same").Select(c => c.Value).ToArray());
            Assert.AreEqual(0, root["section"].Children.Count);

            // Escapes are written back, and the written text reads the same again.
            var written = VdfFile.Serialize(root);
            StringAssert.Contains(written, "\"quote\"\t\t\"say \\\"hi\\\"\"\n");
            StringAssert.Contains(written, "\"lines\"\t\t\"a\\nb\\tc\"\n");
            Assert.AreEqual(written, VdfFile.Serialize(VdfFile.Parse(written)));
        }

        [TestMethod]
        public void Parse_MalformedOrUnsupported_Throws()
        {
            var bad = new[]
            {
                "",
                "\"AppState\"",
                "\"AppState\"\n{\n\t\"appid\"\t\t\"1\"\n",
                "\"AppState\"\n{\n\t\"appid\"\t\t\"1\n}\n",
                "\"AppState\"\n{\n\t\"appid\"\n}\n",
                "\"AppState\"\n{\n\t\"appid\"\t\t\"1\" [$WIN32]\n}\n",
                "\"AppState\"\n{\n}\n\"Second\"\n{\n}\n",
                "{\n}\n",
            };

            foreach (var text in bad)
                Assert.ThrowsExactly<FormatException>(() => VdfFile.Parse(text), text);
        }

        [TestMethod]
        public void ClearAppManifestBetaKeys_RemovesUserConfigBetaKeyOnly()
        {
            var file = Path.Combine(Path.GetTempPath(), $"appmanifest_{Guid.NewGuid():N}.acf");
            try
            {
                File.WriteAllText(file, Manifest, new UTF8Encoding(false));

                Assert.IsTrue(SteamUtils.ClearAppManifestBetaKeys(file));

                var bytes = File.ReadAllBytes(file);
                Assert.AreNotEqual(0xEF, bytes[0], "written without a byte order mark");
                Assert.AreEqual(Manifest.Replace("\t\t\"BetaKey\"\t\t\"preview\"\n\t\t\"language\"", "\t\t\"language\""), Encoding.UTF8.GetString(bytes));

                Assert.IsFalse(SteamUtils.ClearAppManifestBetaKeys(file), "nothing left to remove");
                Assert.IsFalse(SteamUtils.ClearAppManifestBetaKeys(file + ".missing"));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        public void ClearAppManifestBetaKeys_Utf16File_IsReadAndRewrittenAsUtf8()
        {
            // The library used before wrote the manifest back as UTF-16 with a byte order mark.
            var file = Path.Combine(Path.GetTempPath(), $"appmanifest_{Guid.NewGuid():N}.acf");
            try
            {
                File.WriteAllText(file, Manifest, Encoding.Unicode);

                Assert.IsTrue(SteamUtils.ClearAppManifestBetaKeys(file));

                var root = VdfFile.Parse(Encoding.UTF8.GetString(File.ReadAllBytes(file)));
                Assert.IsNull(root["UserConfig"]["BetaKey"]);
                Assert.AreEqual("english", root["UserConfig"]["language"].Value);
                Assert.AreEqual("preview", root["MountedConfig"]["BetaKey"].Value);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        public void Filter_BuildsWindowsDialogFilters()
        {
            Assert.AreEqual("Profile (*.profile, *.ini)|*.profile;*.ini", FileDialogUtils.Filter("Profile", "*.profile,*.ini"));
            Assert.AreEqual("JSON Files (*.json)|*.json", FileDialogUtils.Filter("JSON Files", "*.json"));
            Assert.AreEqual("CSV Files (*.csv, *.txt)|*.csv;*.txt", FileDialogUtils.Filter("CSV Files", " csv ; .txt "));
            Assert.AreEqual("All (*.*)|*.*", FileDialogUtils.Filter("All", null));
        }
    }
}
