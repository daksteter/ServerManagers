using System.Windows;

namespace ServerManagerTool.Common.Model
{
    public class ComboBoxItem : DependencyObject
    {
        public static readonly DependencyProperty ValueMemberProperty = DependencyProperty.Register(nameof(ValueMember), typeof(string), typeof(ComboBoxItem), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty DisplayMemberProperty = DependencyProperty.Register(nameof(DisplayMember), typeof(string), typeof(ComboBoxItem), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty GroupMemberProperty = DependencyProperty.Register(nameof(GroupMember), typeof(string), typeof(ComboBoxItem), new PropertyMetadata(string.Empty));

        public ComboBoxItem()
        {
        }

        public ComboBoxItem(string valueMember, string displayMember)
        {
            ValueMember = valueMember;
            DisplayMember = displayMember;
        }

        public ComboBoxItem(string valueMember, string displayMember, string groupMember)
        {
            ValueMember = valueMember;
            DisplayMember = displayMember;
            GroupMember = groupMember;
        }

        public string ValueMember
        {
            get { return (string)GetValue(ValueMemberProperty); }
            set { SetValue(ValueMemberProperty, value); }
        }

        public string DisplayMember
        {
            get { return (string)GetValue(DisplayMemberProperty); }
            set { SetValue(DisplayMemberProperty, value); }
        }

        public string GroupMember
        {
            get { return (string)GetValue(GroupMemberProperty); }
            set { SetValue(GroupMemberProperty, value); }
        }

        /// <summary>
        /// The text shown. WPF uses it as the item's name for screen readers and UI Automation, and for
        /// finding an item by typing its first letters.
        /// </summary>
        public override string ToString() => string.IsNullOrEmpty(DisplayMember) ? ValueMember : DisplayMember;

        public ComboBoxItem Duplicate()
        {
            return new ComboBoxItem
            {
                DisplayMember = this.DisplayMember,
                ValueMember = this.ValueMember,
                GroupMember = this.GroupMember,
            };
        }
    }
}
