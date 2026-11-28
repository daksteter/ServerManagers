using ServerManagerTool.Common.Utils;
using ServerManagerTool.Lib;
using ServerManagerTool.Lib.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
            var parsed = ModIdUtils.ParseModIds(modIds, out var invalid);
            foreach (var modId in parsed)
            {
                if (!Contains(modId))
                    this.Mods.Add(CreateItem(modId));
            }
            UpdatePositions();

            // anything in the profile that isn't a mod ID stays in the box (OK asks about it), instead of being dropped
            this.NewModIds = string.Join(" ", invalid);

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

        // "928988" and "928988-dev" are different entries (the published mod and a developer's test build)
        private bool Contains(string modId) => this.Mods.Any(m => string.Equals(m.ModId, modId, StringComparison.OrdinalIgnoreCase));

        private ModListItem CreateItem(string modId)
        {
            // name and page by project ID, so a developer's test build ("928988-dev") shows its mod's
            ModIdUtils.TryParseModId(modId, out var projectId);
            _installedMods.TryGetValue(projectId, out var installed);

            return new ModListItem
            {
                ModId = modId,
                Name = installed?.Name,
                DisplayName = installed?.Name ?? _globalizer.GetResourceString("ModList_NotDownloadedLabel"),
                FileName = installed?.FileName,
                FileDate = installed?.FileDate?.ToShortDateString(),
                Url = installed?.WebsiteUrl ?? CurseForgeModLibrary.GetProjectUrl(projectId),
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
            var modIds = ModIdUtils.ParseModIds(this.NewModIds, out var invalid);

            ModListItem lastAdded = null;
            foreach (var modId in modIds)
            {
                if (Contains(modId))
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
