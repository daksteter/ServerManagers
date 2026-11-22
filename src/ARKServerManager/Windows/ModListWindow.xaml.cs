using ServerManagerTool.Common.Utils;
using ServerManagerTool.Lib;
using ServerManagerTool.Lib.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using WPFSharp.Globalizer;

namespace ServerManagerTool
{
    /// <summary>
    /// Edits a list of CurseForge mod IDs (the profile's mods or passive mods): add, remove, reorder. Names and installed
    /// versions come from the server's mod library (<see cref="CurseForgeModLibrary"/>).
    /// </summary>
    public partial class ModListWindow : Window
    {
        // a CurseForge project ID, or a curseforge.com/projects/<id> link
        private static readonly Regex ModIdRegex = new Regex(@"^(?:https?://(?:www\.)?curseforge\.com/projects/)?(\d+)/?$", RegexOptions.IgnoreCase);

        private readonly GlobalizedApplication _globalizer = GlobalizedApplication.Instance;
        private readonly Dictionary<long, CurseForgeModLibrary.InstalledMod> _installedMods;

        public static readonly DependencyProperty NewModIdsProperty = DependencyProperty.Register(nameof(NewModIds), typeof(string), typeof(ModListWindow), new PropertyMetadata(string.Empty));

        /// <param name="title">The window title.</param>
        /// <param name="modIds">The comma-separated mod IDs to edit.</param>
        /// <param name="installDirectory">The server's install folder, for the names of the mods it has downloaded.</param>
        public ModListWindow(string title, string modIds, string installDirectory)
        {
            InitializeComponent();
            WindowUtils.RemoveDefaultResourceDictionary(this, Config.Default.DefaultGlobalizationFile);

            this.Title = title;
            _installedMods = CurseForgeModLibrary.LoadInstalledMods(installDirectory);

            this.Mods = new ObservableCollection<ModListItem>();
            foreach (var modId in ParseModIds(modIds, out _))
            {
                if (!this.Mods.Any(m => m.ModId == modId))
                    this.Mods.Add(CreateItem(modId));
            }
            UpdatePositions();

            this.DataContext = this;
        }

        public ObservableCollection<ModListItem> Mods { get; }

        /// <summary>
        /// The edited list, comma-separated (as the profile stores it).
        /// </summary>
        public string ModIds => string.Join(",", this.Mods.Select(m => m.ModId));

        public string NewModIds
        {
            get { return (string)GetValue(NewModIdsProperty); }
            set { SetValue(NewModIdsProperty, value); }
        }

        /// <summary>
        /// The mod IDs in a text (separated by commas, semicolons or white space); anything else goes to <paramref name="invalid"/>.
        /// </summary>
        private static List<long> ParseModIds(string text, out List<string> invalid)
        {
            var modIds = new List<long>();
            invalid = new List<string>();

            foreach (var token in (text ?? string.Empty).Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = ModIdRegex.Match(token.Trim());
                if (match.Success && long.TryParse(match.Groups[1].Value, out var modId) && modId > 0)
                    modIds.Add(modId);
                else
                    invalid.Add(token.Trim());
            }

            return modIds;
        }

        private ModListItem CreateItem(long modId)
        {
            _installedMods.TryGetValue(modId, out var installed);

            return new ModListItem
            {
                ModId = modId,
                Name = installed?.Name,
                DisplayName = installed?.Name ?? _globalizer.GetResourceString("ModList_NotDownloadedLabel"),
                FileName = installed?.FileName,
                FileDate = installed?.FileDate?.ToShortDateString(),
                Url = installed?.WebsiteUrl ?? CurseForgeModLibrary.GetProjectUrl(modId),
            };
        }

        private void UpdatePositions()
        {
            for (var index = 0; index < this.Mods.Count; index++)
            {
                this.Mods[index].Index = index + 1;
                this.Mods[index].IsFirst = index == 0;
                this.Mods[index].IsLast = index == this.Mods.Count - 1;
            }
        }

        private void AddMods_Click(object sender, RoutedEventArgs e)
        {
            var modIds = ParseModIds(this.NewModIds, out var invalid);

            ModListItem lastAdded = null;
            foreach (var modId in modIds)
            {
                if (this.Mods.Any(m => m.ModId == modId))
                    continue;

                lastAdded = CreateItem(modId);
                this.Mods.Add(lastAdded);
            }
            UpdatePositions();

            if (lastAdded != null)
                this.ModsGrid.ScrollIntoView(lastAdded);

            // leave what couldn't be added in the box, to correct it
            this.NewModIds = string.Join(" ", invalid);
            if (invalid.Count > 0)
            {
                var message = string.Format(_globalizer.GetResourceString("ModList_InvalidIdsLabel"), string.Join(", ", invalid));
                MessageBox.Show(message, _globalizer.GetResourceString("ModList_InvalidIdsTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            this.NewModIdsTextBox.Focus();
        }

        private void MoveModUp_Click(object sender, RoutedEventArgs e) => MoveMod(sender, -1);

        private void MoveModDown_Click(object sender, RoutedEventArgs e) => MoveMod(sender, 1);

        private void MoveMod(object sender, int offset)
        {
            if ((sender as FrameworkElement)?.DataContext is not ModListItem mod)
                return;

            var index = this.Mods.IndexOf(mod);
            var newIndex = index + offset;
            if (index < 0 || newIndex < 0 || newIndex >= this.Mods.Count)
                return;

            this.Mods.Move(index, newIndex);
            UpdatePositions();
            this.ModsGrid.SelectedItem = mod;
        }

        private void RemoveMod_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ModListItem mod)
                return;

            this.Mods.Remove(mod);
            UpdatePositions();
        }

        private void Mod_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            if ((sender as Hyperlink)?.DataContext is ModListItem mod && !string.IsNullOrWhiteSpace(mod.Url))
                ProcessUtils.OpenWithShell(mod.Url);

            e.Handled = true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // IDs typed in the box but not added yet
            if (!string.IsNullOrWhiteSpace(this.NewModIds))
            {
                AddMods_Click(sender, e);
                if (!string.IsNullOrWhiteSpace(this.NewModIds))
                    return;
            }

            this.DialogResult = true;
            Close();
        }
    }
}
