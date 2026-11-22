using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ServerManagerTool.Lib.ViewModel
{
    /// <summary>
    /// A mod in the mod list editor.
    /// </summary>
    public class ModListItem : INotifyPropertyChanged
    {
        private int _index;
        private bool _isFirst;
        private bool _isLast;

        public long ModId { get; init; }

        /// <summary>
        /// The mod's name, or null if the server hasn't downloaded the mod yet.
        /// </summary>
        public string Name { get; init; }

        public string DisplayName { get; init; }

        public string Version { get; init; }

        public string Url { get; init; }

        public bool IsInstalled => Name != null;

        /// <summary>
        /// The position in the list (1 = first).
        /// </summary>
        public int Index
        {
            get => _index;
            set => Set(ref _index, value);
        }

        public bool IsFirst
        {
            get => _isFirst;
            set => Set(ref _isFirst, value);
        }

        public bool IsLast
        {
            get => _isLast;
            set => Set(ref _isLast, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
