using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Common.Lib;

namespace ServerManager.Common.Tests
{
    /// <summary>
    /// <see cref="AutomationNames"/>: buttons get the accessible name a screen reader reads.
    /// </summary>
    [TestClass]
    public class AutomationNamesUnitTest
    {
        [TestMethod]
        public void Register_GivesButtonsWithoutANameTheirTextOrToolTip()
        {
            RunOnStaThread(() =>
            {
                AutomationNames.Register();

                var iconOnly = new Button { Content = new Image(), ToolTip = "Open Help" };
                var iconAndText = new Button { Content = Panel(new Image(), new TextBlock { Text = "Install" }), ToolTip = "Install the latest version." };
                var ellipsisWithToolTip = new Button { Content = "...", ToolTip = "Choose the backup folder." };
                var ellipsisOnly = new Button { Content = "..." };
                var ellipsisInPanel = new Button { Content = Panel(new Label { Content = "..." }) };
                var text = new Button { Content = "Save", ToolTip = "Save the profile." };
                var named = new Button { Content = new Image(), ToolTip = "Close" };
                AutomationProperties.SetName(named, "Close window");
                var nothing = new Button { Content = new Image() };
                var checkBox = new CheckBox { Content = new TextBlock { Text = "Minimize to tray" } };

                ShowAndCheck(Panel(iconOnly, iconAndText, ellipsisWithToolTip, ellipsisOnly, ellipsisInPanel, text, named, nothing, checkBox), () =>
                {
                    Assert.AreEqual("Open Help", NameOf(iconOnly));
                    Assert.AreEqual("Install", NameOf(iconAndText));
                    Assert.AreEqual("Choose the backup folder.", NameOf(ellipsisWithToolTip));
                    Assert.AreEqual("...", NameOf(ellipsisOnly));
                    Assert.AreEqual("...", NameOf(ellipsisInPanel));
                    Assert.AreEqual("Save", NameOf(text));
                    Assert.AreEqual("Close window", NameOf(named));
                    Assert.AreEqual(string.Empty, NameOf(nothing));
                    Assert.AreEqual("Minimize to tray", NameOf(checkBox));
                });
            });
        }

        [TestMethod]
        public void Register_NameFollowsAToolTipThatChanges()
        {
            RunOnStaThread(() =>
            {
                AutomationNames.Register();

                var button = new Button { Content = new Image(), ToolTip = "Start the server." };
                ShowAndCheck(Panel(button), () =>
                {
                    Assert.AreEqual("Start the server.", NameOf(button));
                    button.ToolTip = "Stop the server.";
                    Assert.AreEqual("Stop the server.", NameOf(button));
                });
            });
        }

        private static StackPanel Panel(params UIElement[] children)
        {
            var panel = new StackPanel();
            foreach (var child in children)
                panel.Children.Add(child);
            return panel;
        }

        private static string NameOf(UIElement element) => UIElementAutomationPeer.CreatePeerForElement(element).GetName();

        private static void ShowAndCheck(UIElement content, Action check)
        {
            // off screen and not activated: nothing to see, no focus taken
            var window = new Window
            {
                Content = content,
                Width = 300,
                Height = 400,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };

            window.Show();
            try
            {
                window.UpdateLayout();
                DoEvents();
                check();
            }
            finally
            {
                window.Close();
            }
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static void RunOnStaThread(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
