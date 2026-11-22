using System;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Common.Utils;

namespace ServerManager.Common.Tests
{
    /// <summary>
    /// Extracting parts of a backup zip, as the restore window does: the world files at the top, and the mod data
    /// folders under SaveGames. Each test builds its own zip in a temporary folder.
    /// </summary>
    [TestClass]
    public class ZipUtilsUnitTest
    {
        private string _folder;
        private string _zipFile;

        [TestInitialize]
        public void CreateZip()
        {
            _folder = Path.Combine(Path.GetTempPath(), "ZipUtilsUnitTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _zipFile = Path.Combine(_folder, "backup.zip");

            using var zip = ZipFile.Open(_zipFile, ZipArchiveMode.Create);
            void Add(string entryName) { using var writer = new StreamWriter(zip.CreateEntry(entryName).Open()); writer.Write(entryName); }

            Add("TheIsland_WP.ark");
            Add("0002b0e895544f1498ac92f47b596db0.arkprofile");
            Add("1502673240.arktribe");
            zip.CreateEntry("SaveGames/");
            Add("SaveGames/a.sav");
            Add("SaveGames/b.sav");
            Add("SaveGames/ModA/c.sav");
            Add("SaveGames/ModA/d.sav");
            Add("SaveGames/ModA/e.sav");
            Add("SaveGames/AwesomeTeleporters/f.sav");
        }

        [TestCleanup]
        public void DeleteZip()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        [TestMethod]
        public void ExtractFiles_TopFolderOnly_ExtractsTheWorldFiles()
        {
            var destination = Path.Combine(_folder, "out", "SavedArks");

            var count = ZipUtils.ExtractFiles(_zipFile, destination, recurseFolders: false);

            Assert.AreEqual(3, count);
            Assert.IsTrue(File.Exists(Path.Combine(destination, "TheIsland_WP.ark")));
            Assert.IsFalse(Directory.Exists(Path.Combine(destination, "SaveGames")));
        }

        [TestMethod]
        public void ExtractFiles_Folder_ExtractsItsFilesAndSubfolders()
        {
            var destination = Path.Combine(_folder, "out");

            var count = ZipUtils.ExtractFiles(_zipFile, destination, sourceFolder: "SaveGames", recurseFolders: true);

            Assert.AreEqual(6, count);
            Assert.IsTrue(File.Exists(Path.Combine(destination, "SaveGames", "ModA", "c.sav")));
            Assert.IsFalse(File.Exists(Path.Combine(destination, "TheIsland_WP.ark")));
        }

        [TestMethod]
        public void ExtractFiles_NestedFolder_ExtractsOnlyIt()
        {
            var count = ZipUtils.ExtractFiles(_zipFile, Path.Combine(_folder, "out"), sourceFolder: "AwesomeTeleporters/", recurseFolders: true);

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void DoesFolderExist_FindsTopAndNestedFolders()
        {
            Assert.IsTrue(ZipUtils.DoesFolderExist(_zipFile, "SaveGames"));          // has a folder entry
            Assert.IsTrue(ZipUtils.DoesFolderExist(_zipFile, "AwesomeTeleporters")); // only in file paths
            Assert.IsFalse(ZipUtils.DoesFolderExist(_zipFile, "SavedArks"));
        }

        [TestMethod]
        public void DoesFileExist_MatchesTheFileNameInAnyFolder()
        {
            Assert.IsTrue(ZipUtils.DoesFileExist(_zipFile, "theisland_wp.ark"));
            Assert.IsTrue(ZipUtils.DoesFileExist(_zipFile, "f.sav"));
            Assert.IsFalse(ZipUtils.DoesFileExist(_zipFile, "Ragnarok_WP.ark"));
        }
    }
}
