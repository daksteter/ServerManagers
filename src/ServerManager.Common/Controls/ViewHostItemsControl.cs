using System.Collections.Generic;
using System.Linq;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ServerManagerTool.Common.Controls
{
    /// <summary>
    /// An ItemsControl that hosts views (such as each profile's settings) rather than list items. UI Automation sees
    /// the views that are showing: WPF's own ItemsControl peer shows each item as an empty data item, which hides
    /// everything in it from screen readers and UI test tools.
    /// </summary>
    public class ViewHostItemsControl : ItemsControl
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new ViewHostAutomationPeer(this);

        private sealed class ViewHostAutomationPeer : FrameworkElementAutomationPeer
        {
            public ViewHostAutomationPeer(ViewHostItemsControl owner)
                : base(owner)
            {
            }

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

            protected override string GetClassNameCore() => nameof(ViewHostItemsControl);

            // the views found in the visual tree, without the hidden ones (they would repeat the same controls)
            protected override List<AutomationPeer> GetChildrenCore() =>
                base.GetChildrenCore()?.Where(peer => peer is not UIElementAutomationPeer { Owner.IsVisible: false }).ToList();
        }
    }
}
