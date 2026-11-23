using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Common.Utils;
using System;
using System.IO;
using System.Linq;

namespace ServerManagerTool.Common.UnitTests
{
    /// <summary>
    /// Recognizing a data folder that can't be there any more (its drive is gone).
    /// </summary>
    [TestClass]
    public class IOUtilsUnitTest
    {
        [TestMethod]
        public void IsOnMissingDrive_DriveThatDoesNotExist_True()
        {
            var letter = "ZYXWVUTSRQPONMLKJIHGFED".FirstOrDefault(c => !Directory.Exists($"{c}:\\"));
            if (letter == default)
                Assert.Inconclusive("Every drive letter is in use.");

            Assert.IsTrue(IOUtils.IsOnMissingDrive($"{letter}:\\asadata"));
        }

        [TestMethod]
        public void IsOnMissingDrive_ExistingFolder_False()
        {
            Assert.IsFalse(IOUtils.IsOnMissingDrive(Path.GetTempPath()));
        }

        [TestMethod]
        public void IsOnMissingDrive_MissingFolderOnAnExistingDrive_False()
        {
            // the folder can still be created
            Assert.IsFalse(IOUtils.IsOnMissingDrive(Path.Combine(Path.GetTempPath(), $"asadata-{Guid.NewGuid():N}")));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("  ")]
        public void IsOnMissingDrive_NotSet_False(string folder)
        {
            Assert.IsFalse(IOUtils.IsOnMissingDrive(folder));
        }
    }
}
