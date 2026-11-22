using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Lib;
using System.IO;

namespace ServerManagerTool.UnitTests
{
    /// <summary>
    /// The server's command line and the folders the app reads its saves from (checked against a live ASA server:
    /// docs\PORTING-PLAN.md, M1 and M5).
    /// </summary>
    [TestClass]
    public class ServerProfileUnitTest
    {
        private const string InstallDirectory = @"C:\ASA\Server1";

        [ClassInitialize]
        public static void ClassInitialize(TestContext _)
        {
            // no file watchers on the (nonexistent) install folder
            ServerProfile.EnableServerFilesWatcher = false;
        }

        private static ServerProfile NewProfile()
        {
            var profile = ServerProfile.FromDefaults();
            profile.InstallDirectory = InstallDirectory;
            profile.ServerMap = "TheIsland_WP";
            return profile;
        }

        #region Save folders

        [TestMethod]
        public void GetProfileSavePath_NoAlternateName_IsSavedArksMapFolder()
        {
            var path = ServerProfile.GetProfileSavePath(InstallDirectory, "", "TheIsland_WP");

            Assert.AreEqual(@"C:\ASA\Server1\ShooterGame\Saved\SavedArks\TheIsland_WP", path);
        }

        [TestMethod]
        public void GetProfileSavePath_AlternateName_ReplacesSavedArks()
        {
            // as the server lays it out: Saved\<alternate name>\<map>
            var path = ServerProfile.GetProfileSavePath(InstallDirectory, "AltTest", " TheIsland_WP ");

            Assert.AreEqual(@"C:\ASA\Server1\ShooterGame\Saved\AltTest\TheIsland_WP", path);
        }

        [TestMethod]
        public void GetProfileWorldSaveFile_IsMapNameDotArk()
        {
            var folder = ServerProfile.GetProfileSavePath(InstallDirectory, null, "Ragnarok_WP");

            Assert.AreEqual(Path.Combine(folder, "Ragnarok_WP.ark"), ServerProfile.GetProfileWorldSaveFile(folder, "Ragnarok_WP"));
        }

        #endregion

        #region Command line

        [TestMethod]
        public void GetServerArgs_StartsWithTheMapAndQuestionMarkOptions()
        {
            var profile = NewProfile();
            profile.QueryPort = 27016;

            var args = profile.GetServerArgs();

            Assert.StartsWith("TheIsland_WP?listen?QueryPort=27016?", args);
            // the ? options are one argument: no space before the first dash option
            Assert.IsLessThan(args.IndexOf(" -"), args.LastIndexOf('?'));
        }

        [TestMethod]
        public void GetServerArgs_PortAndPlayerLimit_AreDashOptions()
        {
            var profile = NewProfile();
            profile.ServerPort = 7779;
            profile.MaxPlayers = 20;

            var args = profile.GetServerArgs();

            // ASA ignores ?Port= and ?MaxPlayers=
            Assert.Contains(" -port=7779", args);
            Assert.Contains(" -WinLiveMaxPlayers=20", args);
            Assert.DoesNotContain("?Port=", args);
            Assert.DoesNotContain("?MaxPlayers=", args);
        }

        [TestMethod]
        public void GetServerArgs_AdminPassword_IsNotOnTheCommandLine()
        {
            var profile = NewProfile();
            profile.AdminPassword = "Secret123";

            var args = profile.GetServerArgs();

            // it goes in GameUserSettings.ini
            Assert.DoesNotContain("ServerAdminPassword", args);
            Assert.DoesNotContain("Secret123", args);
        }

        [TestMethod]
        public void GetServerArgs_AlternateSaveName_IsPassedOnlyWhenSet()
        {
            var profile = NewProfile();
            Assert.DoesNotContain("AltSaveDirectoryName", profile.GetServerArgs());

            profile.AltSaveDirectoryName = "AltTest";
            Assert.Contains("?AltSaveDirectoryName=AltTest", profile.GetServerArgs());
        }

        [TestMethod]
        public void GetServerArgs_ExtraQuestionMarkArgs_FollowTheAppsOwnWithoutASpace()
        {
            var profile = NewProfile();
            profile.AdditionalArgs = "  ?SettingName=Value -OptionName";

            var args = profile.GetServerArgs();

            // joined to the app's ? options (one argument), before the app's dash options
            Assert.Contains("?SettingName=Value -OptionName -port=", args);
            Assert.DoesNotContain(" ?SettingName", args);
            Assert.IsLessThan(args.IndexOf(" -"), args.LastIndexOf('?'));
        }

        [TestMethod]
        public void GetServerArgs_ExtraDashArgs_AreSeparatedByASpace()
        {
            var profile = NewProfile();
            profile.AdditionalArgs = "-OptionName";

            var args = profile.GetServerArgs();

            // after the app's ? options, as its own argument
            Assert.Contains(" -OptionName -port=", args);
            Assert.AreEqual(args.IndexOf(" -OptionName"), args.IndexOf(" -"));
        }

        [TestMethod]
        public void GetServerArgs_ModIds_AreCommaSeparatedInLoadOrder()
        {
            var profile = NewProfile();
            profile.ServerModIds = "937184; 123 ,456";
            profile.PassiveModIds = "789 , 1011";

            var args = profile.GetServerArgs();

            Assert.Contains(" -mods=937184,123,456", args);
            Assert.Contains(" -passivemods=789,1011", args);
        }

        [TestMethod]
        public void GetServerArgs_NoMods_NoModsOption()
        {
            var profile = NewProfile();
            profile.ServerModIds = " ";
            profile.PassiveModIds = "";

            var args = profile.GetServerArgs();

            Assert.DoesNotContain("-mods=", args);
            Assert.DoesNotContain("-passivemods=", args);
        }

        [TestMethod]
        public void GetServerPlatformArg_ListsTheChosenPlatforms()
        {
            var profile = NewProfile();
            profile.ServerPlatformPC = true;
            profile.ServerPlatformXbox = true;
            profile.ServerPlatformPlayStation = false;
            profile.ServerPlatformMicrosoftStore = false;
            Assert.AreEqual("PC+XSX", profile.GetServerPlatformArg());
            Assert.Contains(" -ServerPlatform=PC+XSX", profile.GetServerArgs());

            profile.ServerPlatformPlayStation = true;
            profile.ServerPlatformMicrosoftStore = true;
            Assert.AreEqual("ALL", profile.GetServerPlatformArg());

            profile.ServerPlatformPC = profile.ServerPlatformXbox = profile.ServerPlatformPlayStation = profile.ServerPlatformMicrosoftStore = false;
            Assert.IsNull(profile.GetServerPlatformArg());
            Assert.DoesNotContain("-ServerPlatform", profile.GetServerArgs());
        }

        [TestMethod]
        public void GetServerArgs_EndsWithTheStandardOptions()
        {
            var args = NewProfile().GetServerArgs();

            Assert.Contains(" -nosteamclient -game -server", args);
        }

        #endregion
    }
}
