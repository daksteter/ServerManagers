using ServerManagerTool.Common.Utils;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;

namespace ServerManagerTool.Common.ValidationRules
{
    /// <summary>
    /// A comma-separated list of mod IDs (-mods= / -passivemods=): CurseForge project IDs, each optionally with a
    /// developer's test suffix ("928988-dev").
    /// </summary>
    public class ModIdListValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var text = value as string;
            if (string.IsNullOrWhiteSpace(text))
                return ValidationResult.ValidResult;

            if (text.Contains(' '))
                return new ValidationResult(false, "Spaces are not permitted");

            return text.Split(',').All(modId => ModIdUtils.TryParseModId(modId, out _))
                ? ValidationResult.ValidResult
                : new ValidationResult(false, "Must be a comma-separated list of mod IDs, such as 928988 or 928988-dev");
        }
    }
}
