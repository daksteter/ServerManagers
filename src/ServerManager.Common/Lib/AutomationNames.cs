using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace ServerManagerTool.Common.Lib
{
    /// <summary>
    /// Gives every button without an accessible name one: the text shown in it, or else its tooltip. WPF names a
    /// button only by string content, so icon buttons and buttons whose content is a panel (icon plus text) have no
    /// name for screen readers and UI Automation, only the tooltip as help text.
    /// </summary>
    public static class AutomationNames
    {
        private static bool _registered;

        /// <summary>
        /// Call once at startup; applies to every window the process shows.
        /// </summary>
        public static void Register()
        {
            if (_registered)
                return;

            _registered = true;
            // SizeChanged, not Loaded: WPF raises Loaded only where an element has its own Loaded handler, so a class
            // handler misses most buttons; every button gets a SizeChanged when it is first laid out (and a hidden one
            // when it is first shown)
            EventManager.RegisterClassHandler(typeof(ButtonBase), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(Button_SizeChanged));
        }

        private static void Button_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // WPF names a button by string content itself (but "..." says nothing; the tooltip does)
            if (sender is not ButtonBase button || button.Content is string content && IsMeaningful(content))
                return;

            // a name given in XAML or by a style wins (and a button seen before already has its binding)
            if (DependencyPropertyHelper.GetValueSource(button, AutomationProperties.NameProperty).BaseValueSource != BaseValueSource.Default)
                return;

            // bound, so the name follows content and tooltips that change (Start / Stop, another language)
            var binding = new MultiBinding { Converter = NameConverter.Instance };
            binding.Bindings.Add(new Binding(nameof(ContentControl.Content)) { Source = button });
            binding.Bindings.Add(new Binding(nameof(FrameworkElement.ToolTip)) { Source = button });
            BindingOperations.SetBinding(button, AutomationProperties.NameProperty, binding);
        }

        // text with a letter or digit in it, not just punctuation such as the "..." of a browse button
        private static bool IsMeaningful(string text) => text != null && text.Any(char.IsLetterOrDigit);

        private sealed class NameConverter : IMultiValueConverter
        {
            public static readonly NameConverter Instance = new NameConverter();

            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                // the text shown, else the tooltip, else whatever the button shows even if it's only "..."
                var content = values.ElementAtOrDefault(0);
                return GetText(content, meaningfulOnly: true) ?? GetText(values.ElementAtOrDefault(1), meaningfulOnly: true) ??
                    GetText(content, meaningfulOnly: false) ?? string.Empty;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();

            private static string GetText(object value, bool meaningfulOnly)
            {
                switch (value)
                {
                    case string text:
                        return (meaningfulOnly ? IsMeaningful(text) : !string.IsNullOrWhiteSpace(text)) ? text.Trim() : null;
                    case TextBlock textBlock:
                        return GetText(textBlock.Text, meaningfulOnly);
                    case AccessText accessText:
                        return GetText(accessText.Text, meaningfulOnly);
                    case ToolTip toolTip:
                        return GetText(toolTip.Content, meaningfulOnly);
                    case DependencyObject element:
                        // the first text in a panel (e.g. an icon and a label)
                        foreach (var child in LogicalTreeHelper.GetChildren(element))
                        {
                            var text = GetText(child, meaningfulOnly);
                            if (text != null)
                                return text;
                        }
                        return null;
                    default:
                        return null;
                }
            }
        }
    }
}
