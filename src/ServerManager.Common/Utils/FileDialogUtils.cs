using System;
using System.Linq;

namespace ServerManagerTool.Common.Utils
{
    public static class FileDialogUtils
    {
        /// <summary>
        /// Builds a filter for the Windows file dialogs, such as "Profile (*.profile, *.ini)|*.profile;*.ini", from a
        /// label and a list of patterns or extensions separated by commas or semicolons ("*.profile,*.ini", "*.json",
        /// "csv").
        /// </summary>
        public static string Filter(string label, string patterns)
        {
            var list = (patterns ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.StartsWith("*", StringComparison.Ordinal) ? p : "*." + p.TrimStart('.'))
                .ToArray();
            if (list.Length == 0)
                list = new[] { "*.*" };

            return $"{label} ({string.Join(", ", list)})|{string.Join(";", list)}";
        }
    }
}
