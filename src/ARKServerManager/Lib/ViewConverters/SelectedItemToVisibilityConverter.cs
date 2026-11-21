using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ServerManagerTool.Lib.ViewModel
{
    /// <summary>
    /// Visible when the two bound values are the same object (e.g. an item and the selected item), otherwise Collapsed.
    /// Used to keep one view per tab alive while showing only the selected one.
    /// </summary>
    public class SelectedItemToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return values != null && values.Length == 2 && values[0] != null && ReferenceEquals(values[0], values[1])
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
