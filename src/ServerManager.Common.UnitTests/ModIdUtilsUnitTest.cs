using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Common.Utils;
using ServerManagerTool.Common.ValidationRules;
using System.Globalization;

namespace ServerManagerTool.Common.UnitTests
{
    /// <summary>
    /// Mod IDs for -mods= / -passivemods=: CurseForge project IDs, optionally with a developer's test suffix.
    /// </summary>
    [TestClass]
    public class ModIdUtilsUnitTest
    {
        [TestMethod]
        [DataRow("928988", 928988L)]
        [DataRow("928988-dev", 928988L)]
        [DataRow("928988-Dev2", 928988L)]
        public void TryParseModId_ModId_ItsProjectId(string text, long projectId)
        {
            Assert.IsTrue(ModIdUtils.TryParseModId(text, out var parsed));
            Assert.AreEqual(projectId, parsed);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        [DataRow("dev")]
        [DataRow("-dev")]
        [DataRow("928988-")]
        [DataRow("928988 -dev")]
        [DataRow("928988-dev-2")]
        [DataRow("928988_dev")]
        [DataRow("0")]
        [DataRow("99999999999999999999")]
        public void TryParseModId_NotAModId_False(string text)
        {
            Assert.IsFalse(ModIdUtils.TryParseModId(text, out _));
        }

        [TestMethod]
        public void ParseModIds_IdsSuffixesAndLinks_KeptAsWritten()
        {
            var modIds = ModIdUtils.ParseModIds("928988, 928988-dev;https://www.curseforge.com/projects/931047/ 12345\nnope", out var invalid);

            CollectionAssert.AreEqual(new[] { "928988", "928988-dev", "931047", "12345" }, modIds);
            CollectionAssert.AreEqual(new[] { "nope" }, invalid);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("928988")]
        [DataRow("928988-dev")]
        [DataRow("928988,931047-dev,12345")]
        public void ModIdListValidationRule_ModIdList_Valid(string text)
        {
            Assert.IsTrue(new ModIdListValidationRule().Validate(text, CultureInfo.InvariantCulture).IsValid);
        }

        [TestMethod]
        [DataRow("928988, 931047")]
        [DataRow("928988,,931047")]
        [DataRow("928988,")]
        [DataRow("928988,mod")]
        [DataRow("https://www.curseforge.com/projects/928988")]
        public void ModIdListValidationRule_NotAModIdList_Invalid(string text)
        {
            Assert.IsFalse(new ModIdListValidationRule().Validate(text, CultureInfo.InvariantCulture).IsValid);
        }
    }
}
