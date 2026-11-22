using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ServerManagerTool.Common.Controls
{
    /// <summary>
    /// A TabControl whose UI Automation peer also lists the buttons in its template, such as a "new tab" button next
    /// to the tab strip. WPF's own peer shows only the tabs, so screen readers and UI test tools can't reach them.
    /// </summary>
    public class AccessibleTabControl : TabControl
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new AccessibleTabControlAutomationPeer(this);

        private sealed class AccessibleTabControlAutomationPeer : TabControlAutomationPeer
        {
            public AccessibleTabControlAutomationPeer(TabControl owner)
                : base(owner)
            {
            }

            protected override List<AutomationPeer> GetChildrenCore()
            {
                var children = base.GetChildrenCore() ?? new List<AutomationPeer>();

                foreach (var button in FindTemplateButtons(Owner))
                {
                    var peer = UIElementAutomationPeer.CreatePeerForElement(button);
                    if (peer != null)
                        children.Add(peer);
                }

                return children;
            }

            // the template's buttons, not those in the tabs or the selected tab's content (the tab peers cover those)
            private static IEnumerable<ButtonBase> FindTemplateButtons(DependencyObject parent)
            {
                var count = VisualTreeHelper.GetChildrenCount(parent);
                for (var i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is TabItem || child is FrameworkElement { Name: "PART_SelectedContentHost" })
                        continue;

                    if (child is ButtonBase button)
                    {
                        yield return button;
                        continue;
                    }

                    foreach (var descendant in FindTemplateButtons(child))
                        yield return descendant;
                }
            }
        }
    }
}
