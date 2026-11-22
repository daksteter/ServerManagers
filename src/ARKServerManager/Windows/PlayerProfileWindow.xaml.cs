using ArkData;
using ServerManagerTool.Common.Lib;
using ServerManagerTool.Common.Utils;
using ServerManagerTool.Lib.ViewModel.RCON;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using WPFSharp.Globalizer;

namespace ServerManagerTool
{
    /// <summary>
    /// Interaction logic for PlayerProfileWindow.xaml
    /// </summary>
    public partial class PlayerProfileWindow : Window
    {
        private readonly GlobalizedApplication _globalizer = GlobalizedApplication.Instance;

        public PlayerProfileWindow(PlayerInfo player, String serverFolder)
        {
            InitializeComponent();
            WindowUtils.RemoveDefaultResourceDictionary(this, Config.Default.DefaultGlobalizationFile);

            this.Player = player;
            this.ServerFolder = serverFolder;
            this.DataContext = this;
        }

        public PlayerInfo Player
        {
            get;
            private set;
        }

        public String ServerFolder
        {
            get;
            private set;
        }

        public PlayerData PlayerData => Player?.PlayerData;

        public TribeData TribeData => Player?.PlayerData?.Tribe;

        public String CreatedDate => PlayerData?.FileCreated?.ToString("G");

        public Boolean IsTribeOwner => PlayerData != null && TribeData != null && TribeData.OwnerId == PlayerData.CharacterId;

        // profiles and tribes kept in the world save (-usestore) have no file to show
        public String PlayerLink => String.IsNullOrWhiteSpace(PlayerData?.File) ? null : $"/select,\"{PlayerData.File}\"";

        public String TribeLink => String.IsNullOrWhiteSpace(TribeData?.File) ? null : $"/select,\"{TribeData.File}\"";

        public String TribeOwner => TribeData != null && TribeData.Owner != null ? $"{TribeData.Owner.CharacterName} ({TribeData.Owner.PlayerName})" : null;

        public String UpdatedDate => PlayerData?.FileUpdated?.ToString("G");

        public String WindowTitle => String.Format(_globalizer.GetResourceString("Profile_WindowTitle_Player"), Player.PlayerName);

        public ICommand ExplorerLinkCommand
        {
            get
            {
                return new RelayCommand<String>(
                    execute: (action) =>
                    {
                        if (String.IsNullOrWhiteSpace(action)) return;
                        Process.Start("explorer.exe", action);
                    },
                    canExecute: (action) => true
                );
            }
        }
    }
}
