using ServerManagerTool.Common.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using WPFSharp.Globalizer;

namespace ServerManagerTool
{
    /// <summary>
    /// What the app needs on top of the published WPFSharp.Globalizer package: resource strings, English under every
    /// other language, and the default style under a chosen one.
    /// </summary>
    public static class GlobalizerExtensions
    {
        public const string FallbackLanguage = "en-US";
        public const string FallbackStyle = "Default";

        /// <summary>
        /// The string resource with this key, with "\r" and "\n" escapes turned into line breaks; null when there's
        /// none.
        /// </summary>
        public static string GetResourceString(this GlobalizedApplication application, string key) =>
            ResourceUtils.GetResourceString(application.Resources, key);

        /// <summary>
        /// Switches to a language: the English texts are loaded first and the language's own over them, so texts
        /// it hasn't translated stay in English. A language that isn't installed falls back to English.
        /// </summary>
        public static void LoadLanguage(this GlobalizedApplication application, string language)
        {
            if (string.IsNullOrWhiteSpace(language) || !AvailableLanguages.Instance.Contains(language))
                language = FallbackLanguage;

            var manager = application.GlobalizationManager;
            var files = new[] { FallbackLanguage, language }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(code => Path.Combine(manager.DefaultPath, code, $"{code}.xaml"))
                .Where(File.Exists)
                .ToList();
            if (files.Count == 0)
                return;

            var culture = new CultureInfo(language);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            foreach (var dictionary in manager.MergedDictionaries.OfType<GlobalizationResourceDictionary>().ToList())
                manager.MergedDictionaries.Remove(dictionary);

            manager.FileNames = files;
            manager.LoadDictionariesFromFiles(files);
            manager.NotifyResourceDictionaryChanged(new ResourceDictionaryChangedEventArgs
            {
                ResourceDictionaryPaths = files,
                ResourceDictionaryNames = files.Select(Path.GetFileNameWithoutExtension).ToList(),
            });
        }

        /// <summary>
        /// Loads the default style and, when another style is chosen and installed, that style over it.
        /// </summary>
        public static void LoadStyle(this GlobalizedApplication application, string style)
        {
            var manager = application.StyleManager;

            // SwitchStyle replaces every style that isn't linked to a language.
            manager.SwitchStyle(Path.Combine(manager.DefaultPath, $"{FallbackStyle}.xaml"));

            if (string.IsNullOrWhiteSpace(style) || style.Equals(FallbackStyle, StringComparison.OrdinalIgnoreCase))
                return;

            var file = Path.Combine(manager.DefaultPath, $"{style}.xaml");
            if (File.Exists(file) && manager.LoadFromFile(file) is StyleResourceDictionary dictionary)
            {
                manager.MergedDictionaries.Add(dictionary);
                AvailableStyles.Instance.SelectedStyle = style;
            }
        }

        /// <summary>The installed languages, each with its name in the current language when the texts have one.</summary>
        public static IEnumerable<(string Code, string Name)> GetLanguages(this GlobalizedApplication application) =>
            AvailableLanguages.Instance.Select(code => (code, application.GetResourceString(code) ?? new CultureInfo(code).DisplayName));
    }
}
