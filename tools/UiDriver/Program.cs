using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Application = FlaUI.Core.Application;

namespace UiDriver;

/// <summary>
/// Drives a running WPF app through UI Automation, one command per run, so it can be scripted from a shell.
/// Acts through automation patterns (invoke, toggle, value, selection) rather than the real mouse and keyboard,
/// so it doesn't take over the desktop. See README.md.
/// </summary>
internal static class Program
{
    private static readonly string PidFile = Path.Combine(Path.GetTempPath(), "uidriver.pid");

    private static int Main(string[] args)
    {
        // physical pixels for window sizes and screenshots on scaled displays
        SetProcessDpiAwarenessContext(new IntPtr(-4));

        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            var options = Options.Parse(args.Skip(1));
            return args[0] switch
            {
                "launch" => Launch(options),
                "attach" => Attach(options),
                "windows" => WithApp(options, Windows),
                "tree" => WithWindow(options, Tree),
                "find" => WithWindow(options, Find),
                "click" => WithWindow(options, Click),
                "set" => WithWindow(options, Set),
                "get" => WithWindow(options, Get),
                "select" => WithWindow(options, Select),
                "expand" => WithWindow(options, (o, w) => ExpandCollapse(o, w, expand: true)),
                "collapse" => WithWindow(options, (o, w) => ExpandCollapse(o, w, expand: false)),
                "wait" => Wait(options),
                "screenshot" => WithWindow(options, Screenshot),
                "tap" => WithWindow(options, Tap),
                "close" => WithWindow(options, Close),
                "kill" => Kill(options),
                _ => throw new UsageException($"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Run 'UiDriver help' for the commands.");
            return 1;
        }
        catch (NotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private const string Usage = """
        UiDriver <command> [arguments] [options]

        Process
          launch <exe> [-- app arguments]   start the app (as this user, not elevated) and remember it
          attach <pid>                      remember an already running process
          kill                              end the remembered process
          windows                           list its top-level windows (dialogs included)

        Elements (in the window chosen by -w, default: an open modal dialog, else the main window)
          tree [-d depth] [--all]           print the element tree (offscreen elements only with --all)
          find <query> [--all]              list matching elements
          click <query>                     invoke / toggle / select it (--mouse: real mouse click instead)
          set <query> <text>                set a text box's value
          get <query>                       print its name, value, toggle and selection state
          select <query> <item>             choose an item in a combo box or list (item: name or #index)
          expand <query> | collapse <query> open or close an expander, tree item or combo box
          wait [query] [-t seconds]         wait for the window (-w) and, if given, a matching element
          screenshot <file.png>             save an image of the window (even if other windows cover it)
          tap <x> <y> [--mouse]             invoke what is at a point of the window (pixels, as in its
                                            screenshot). WPF only reports elements in the tree there; for
                                            others (e.g. buttons in a tab control's template) --mouse makes a
                                            real click: the cursor moves briefly and is put back
          close                             close the window

        Options
          -w <title text>|#n   pick the window by title (contains, any case) or by its number in 'windows'
          -p <pid>             use this process instead of the remembered one
          -t <seconds>         timeout for launch and wait (default 30)

        Queries: name text alone matches the name or automation id exactly. Otherwise terms joined by ';':
          id=StartButton   name=Start   name~=art (contains)   type=Button   class=TextBox
          help~=settings (tooltip, for icon buttons)   #2 (the third match)
        """;

    // ---------------------------------------------------------------------------------------------------------
    // process commands

    private static int Launch(Options options)
    {
        var exe = options.Positional.ElementAtOrDefault(0) ?? throw new UsageException("launch needs the path of the exe.");
        exe = Path.GetFullPath(exe);
        if (!File.Exists(exe))
            throw new NotFoundException($"No such file: {exe}");

        var startInfo = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            // a shell start passes on no handles: the app (and the console programs it runs, such as SteamCMD) must
            // not write into this driver's output, which the calling shell may still be reading
            UseShellExecute = true,
        };
        foreach (var argument in options.AppArguments)
            startInfo.ArgumentList.Add(argument);

        // a started app takes the keyboard focus, so keys typed elsewhere meanwhile would land in it
        var previousForeground = GetForegroundWindow();
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The process didn't start.");
        File.WriteAllText(PidFile, process.Id.ToString());
        Console.WriteLine($"Started {Path.GetFileName(exe)}, PID {process.Id}.");

        using var automation = new UIA3Automation();
        using var app = Application.Attach(process.Id);
        var windows = WaitFor(options.Timeout, () =>
        {
            KeepFocus(process.Id, previousForeground);
            return process.HasExited ? null : GetWindows(app, automation) is { Length: > 0 } w ? w : null;
        });
        if (windows == null)
        {
            Console.WriteLine(process.HasExited ? $"The process exited (code {process.ExitCode})." : "No window yet.");
            return process.HasExited ? 1 : 0;
        }

        // startup can show several windows in turn (a splash, then the main window); keep the focus where it was
        // until the app has stopped taking it for a while
        var stopwatch = Stopwatch.StartNew();
        var lastTaken = stopwatch.Elapsed;
        while (!process.HasExited && stopwatch.Elapsed - lastTaken < TimeSpan.FromSeconds(8) && stopwatch.Elapsed < options.Timeout)
        {
            if (KeepFocus(process.Id, previousForeground))
                lastTaken = stopwatch.Elapsed;
            Thread.Sleep(200);
        }

        PrintWindows(GetWindows(app, automation));
        return 0;
    }

    private static int Attach(Options options)
    {
        var pidText = options.Positional.ElementAtOrDefault(0) ?? throw new UsageException("attach needs a process ID.");
        if (!int.TryParse(pidText, out var pid))
            throw new UsageException($"Not a process ID: {pidText}");

        using var process = Process.GetProcessById(pid);
        File.WriteAllText(PidFile, pid.ToString());
        Console.WriteLine($"Attached to {process.ProcessName}, PID {pid}.");
        return 0;
    }

    private static int Kill(Options options)
    {
        var pid = GetPid(options);
        using var process = Process.GetProcessById(pid);
        process.Kill();
        process.WaitForExit(10000);
        Console.WriteLine($"Ended PID {pid}.");
        return 0;
    }

    private static int Windows(Options options, Application app, UIA3Automation automation)
    {
        var windows = GetWindows(app, automation);
        if (windows.Length == 0)
            throw new NotFoundException("The process has no windows.");

        PrintWindows(windows);
        return 0;
    }

    private static int Wait(Options options)
    {
        var pid = GetPid(options);
        if (!IsRunning(pid))
            throw new NotFoundException($"Process {pid} isn't running; use 'launch' (or 'attach') first.");

        using var automation = new UIA3Automation();
        using var app = Application.Attach(pid);
        var query = options.Positional.Count > 0 ? Query.Parse(options.Positional[0]) : null;

        var found = WaitFor(options.Timeout, () =>
        {
            if (app.HasExited)
                return null;

            var window = TryChooseWindow(options, app, automation);
            if (window == null || query == null)
                return window;

            return query.Match(window, includeOffscreen: true).FirstOrDefault();
        });

        if (found == null)
            throw new NotFoundException(app.HasExited ? "The process has exited." : "Timed out.");

        Console.WriteLine(Describe(found));
        return 0;
    }

    // ---------------------------------------------------------------------------------------------------------
    // element commands

    private static int Tree(Options options, Window window)
    {
        var builder = new StringBuilder();
        AppendTree(builder, window, 0, options.Depth ?? 12, options.All);
        Console.Write(builder);
        return 0;
    }

    private static void AppendTree(StringBuilder builder, AutomationElement element, int level, int maxDepth, bool all)
    {
        builder.Append(' ', level * 2).AppendLine(Describe(element));
        if (level >= maxDepth)
            return;

        foreach (var child in element.FindAllChildren())
        {
            if (!all && IsOffscreen(child))
                continue;
            AppendTree(builder, child, level + 1, maxDepth, all);
        }
    }

    private static int Find(Options options, Window window)
    {
        var query = Query.Parse(options.Positional.ElementAtOrDefault(0) ?? throw new UsageException("find needs a query."));
        var matches = query.Match(window, includeOffscreen: true).Where(e => options.All || !IsOffscreen(e)).ToList();
        if (matches.Count == 0)
            throw new NotFoundException($"Nothing matches {query}.");

        for (var i = 0; i < matches.Count; i++)
            Console.WriteLine($"#{i} {Describe(matches[i])}");
        return 0;
    }

    private static int Click(Options options, Window window)
    {
        var element = FindOne(options, window);

        if (options.Mouse)
        {
            var bounds = element.Properties.BoundingRectangle.ValueOrDefault;
            if (!element.TryGetClickablePoint(out var point))
                point = new System.Drawing.Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            MouseClick(window.Properties.NativeWindowHandle.ValueOrDefault, point);
            Console.WriteLine($"Mouse-clicked {Describe(element)}");
            return 0;
        }

        var patterns = element.Patterns;
        if (patterns.Invoke.TryGetPattern(out var invoke))
            invoke.Invoke();
        else if (patterns.Toggle.TryGetPattern(out var toggle))
            toggle.Toggle();
        else if (patterns.SelectionItem.TryGetPattern(out var selectionItem))
            selectionItem.Select();
        else if (patterns.ExpandCollapse.TryGetPattern(out var expandCollapse))
        {
            if (expandCollapse.ExpandCollapseState.Value == ExpandCollapseState.Collapsed)
                expandCollapse.Expand();
            else
                expandCollapse.Collapse();
        }
        else
            throw new InvalidOperationException($"{Describe(element)} can't be clicked through automation; add --mouse for a real click.");

        Console.WriteLine($"Clicked {Describe(element)}");
        return 0;
    }

    private static int Set(Options options, Window window)
    {
        var element = FindOne(options, window);
        var text = options.Positional.ElementAtOrDefault(1) ?? throw new UsageException("set needs the text.");

        if (!element.Patterns.Value.TryGetPattern(out var value))
            throw new InvalidOperationException($"{Describe(element)} has no value to set.");
        if (value.IsReadOnly.ValueOrDefault)
            throw new InvalidOperationException($"{Describe(element)} is read-only.");

        value.SetValue(text);
        Console.WriteLine($"Set {Describe(element)}");
        return 0;
    }

    private static int Get(Options options, Window window)
    {
        var element = FindOne(options, window);
        var patterns = element.Patterns;

        Console.WriteLine(Describe(element));
        if (patterns.Value.TryGetPattern(out var value))
            Console.WriteLine($"  value: {value.Value.ValueOrDefault}");
        if (patterns.Toggle.TryGetPattern(out var toggle))
            Console.WriteLine($"  toggle: {toggle.ToggleState.ValueOrDefault}");
        if (patterns.SelectionItem.TryGetPattern(out var selectionItem))
            Console.WriteLine($"  selected: {selectionItem.IsSelected.ValueOrDefault}");
        if (patterns.Selection.TryGetPattern(out var selection))
        {
            var labels = selection.Selection.ValueOrDefault?.Select(ItemLabel).ToList() ?? [];

            // a closed combo box exposes no text for its item (WPF creates it when the list opens), so open it briefly
            if (labels.Count == 1 && TypeNamePattern.IsMatch(labels[0]) && patterns.ExpandCollapse.TryGetPattern(out var combo) &&
                combo.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Collapsed)
            {
                combo.Expand();
                try
                {
                    var selected = WaitFor(TimeSpan.FromSeconds(3), () => element.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem))
                        .FirstOrDefault(i => i.Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault == true));
                    if (selected != null)
                        labels[0] = ItemLabel(selected);
                }
                finally
                {
                    combo.Collapse();
                }
            }

            Console.WriteLine($"  selection: {string.Join(", ", labels.Select(Quote))}");
        }
        if (patterns.ExpandCollapse.TryGetPattern(out var expandCollapse))
            Console.WriteLine($"  expand: {expandCollapse.ExpandCollapseState.ValueOrDefault}");
        if (patterns.RangeValue.TryGetPattern(out var range))
            Console.WriteLine($"  range: {range.Value.ValueOrDefault} ({range.Minimum.ValueOrDefault}..{range.Maximum.ValueOrDefault})");
        var helpText = element.Properties.HelpText.ValueOrDefault;
        if (!string.IsNullOrEmpty(helpText))
            Console.WriteLine($"  help: {helpText}");
        return 0;
    }

    private static int Select(Options options, Window window)
    {
        var element = FindOne(options, window);
        var itemText = options.Positional.ElementAtOrDefault(1) ?? throw new UsageException("select needs the item.");

        // a combo box only creates its items while open
        var expandCollapse = element.Patterns.ExpandCollapse.PatternOrDefault;
        expandCollapse?.Expand();
        try
        {
            var items = WaitFor(TimeSpan.FromSeconds(5), () => element.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)) is { Length: > 0 } i ? i : null)
                ?? element.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));

            AutomationElement? item;
            if (itemText.StartsWith('#') && int.TryParse(itemText.AsSpan(1), out var index))
                item = items.ElementAtOrDefault(index);
            else
                item = items.FirstOrDefault(i => string.Equals(ItemLabel(i), itemText, StringComparison.OrdinalIgnoreCase))
                    ?? items.FirstOrDefault(i => ItemLabel(i).Contains(itemText, StringComparison.OrdinalIgnoreCase));

            if (item == null)
                throw new NotFoundException($"No item '{itemText}'. Items: {string.Join(", ", items.Select((i, n) => $"#{n} {Quote(ItemLabel(i))}"))}");

            item.Patterns.SelectionItem.Pattern.Select();
            Console.WriteLine($"Selected {Quote(ItemLabel(item))} in {Describe(element)}");
        }
        finally
        {
            if (expandCollapse != null && expandCollapse.ExpandCollapseState.ValueOrDefault != ExpandCollapseState.Collapsed)
                expandCollapse.Collapse();
        }
        return 0;
    }

    private static int ExpandCollapse(Options options, Window window, bool expand)
    {
        var element = FindOne(options, window);
        if (!element.Patterns.ExpandCollapse.TryGetPattern(out var expandCollapse))
            throw new InvalidOperationException($"{Describe(element)} can't be expanded or collapsed.");

        if (expand)
            expandCollapse.Expand();
        else
            expandCollapse.Collapse();
        Console.WriteLine($"{(expand ? "Expanded" : "Collapsed")} {Describe(element)}");
        return 0;
    }

    private static int Screenshot(Options options, Window window)
    {
        var file = options.Positional.ElementAtOrDefault(0) ?? throw new UsageException("screenshot needs a file name.");
        file = Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
            throw new InvalidOperationException("The window has no handle to capture.");

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            try
            {
                // PW_RENDERFULLCONTENT: includes DirectX content such as WPF; works while the window is covered
                if (!PrintWindow(handle, hdc, 2))
                    throw new InvalidOperationException("PrintWindow failed.");
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        bitmap.Save(file, ImageFormat.Png);
        Console.WriteLine($"Saved {width}x{height} image of {Quote(window.Title)} to {file}");
        return 0;
    }

    private static int Tap(Options options, Window window)
    {
        if (options.Positional.Count < 2 || !int.TryParse(options.Positional[0], out var x) || !int.TryParse(options.Positional[1], out var y))
            throw new UsageException("tap needs x and y (pixels from the window's top-left corner, as in its screenshot).");

        var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
            throw new InvalidOperationException("The window has no handle.");

        // First ask automation what is at the point: WPF hit-tests the real controls, so this finds elements that
        // aren't in the automation tree (a tab control shows only its tabs), and they can be invoked without a click.
        // The lookup goes by screen position, so the window is lifted above the others for it (without taking the
        // keyboard focus) and then put back where it was in the window stack.
        var screenPoint = new System.Drawing.Point(rect.Left + x, rect.Top + y);
        var seen = new List<string>();
        var windowAbove = GetWindow(handle, GW_HWNDPREV);
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        AutomationElement? target = null;
        try
        {
            for (var element = window.Automation.FromPoint(screenPoint); element != null; element = element.Parent)
            {
                if (element.Properties.ProcessId.ValueOrDefault != window.Properties.ProcessId.ValueOrDefault)
                    break;
                seen.Add(Describe(element));

                var patterns = element.Patterns;
                if (patterns.Invoke.IsSupported || patterns.Toggle.IsSupported || patterns.SelectionItem.IsSupported)
                {
                    target = element;
                    break;
                }
            }
        }
        finally
        {
            SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (windowAbove != IntPtr.Zero)
                SetWindowPos(handle, windowAbove, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        if (target != null)
        {
            var patterns = target.Patterns;
            if (patterns.Invoke.TryGetPattern(out var invoke))
                invoke.Invoke();
            else if (patterns.Toggle.TryGetPattern(out var toggle))
                toggle.Toggle();
            else
                patterns.SelectionItem.Pattern.Select();

            Console.WriteLine($"Tapped ({x}, {y}): invoked {Describe(target)}");
            return 0;
        }

        var found = seen.Count == 0 ? "nothing of this window (is it on screen?)" : string.Join(" < ", seen);
        if (!options.Mouse)
        {
            // WPF ignores posted mouse messages, so the only other way is a real click
            throw new NotFoundException($"Nothing to invoke at ({x}, {y}): {found}. Add --mouse for a real click (moves the cursor briefly).");
        }

        MouseClick(handle, screenPoint);
        Console.WriteLine($"Mouse-clicked ({x}, {y}) in {Quote(window.Title)} ({found})");
        return 0;
    }

    /// <summary>
    /// A real click: brings the window to the front, clicks, then puts the cursor and the previous foreground
    /// window back.
    /// </summary>
    private static void MouseClick(IntPtr handle, System.Drawing.Point screenPoint)
    {
        var previousForeground = GetForegroundWindow();
        var previousPosition = FlaUI.Core.Input.Mouse.Position;
        try
        {
            SetForegroundWindow(handle);
            Thread.Sleep(100);
            FlaUI.Core.Input.Mouse.Click(screenPoint);
            Thread.Sleep(100);
        }
        finally
        {
            FlaUI.Core.Input.Mouse.Position = previousPosition;
            if (previousForeground != IntPtr.Zero && previousForeground != handle)
                SetForegroundWindow(previousForeground);
        }
    }

    private static int Close(Options options, Window window)
    {
        var title = window.Title;
        window.Close();
        Console.WriteLine($"Closed {Quote(title)}");
        return 0;
    }

    // ---------------------------------------------------------------------------------------------------------
    // helpers

    private static int WithApp(Options options, Func<Options, Application, UIA3Automation, int> action)
    {
        var pid = GetPid(options);
        if (!IsRunning(pid))
            throw new NotFoundException($"Process {pid} isn't running; use 'launch' (or 'attach') first.");

        using var automation = new UIA3Automation();
        using var app = Application.Attach(pid);
        if (app.HasExited)
            throw new NotFoundException("The process has exited.");
        return action(options, app, automation);
    }

    private static int WithWindow(Options options, Func<Options, Window, int> action)
    {
        return WithApp(options, (o, app, automation) =>
        {
            var window = TryChooseWindow(o, app, automation) ?? throw new NotFoundException(o.Window == null
                ? "The process has no windows."
                : $"No window matches '{o.Window}'. Run 'UiDriver windows' to list them.");
            return action(o, window);
        });
    }

    /// <summary>
    /// The process's visible top-level windows, front to back. Found through Windows itself: the automation tree
    /// under the desktop can miss a dialog the app shows (seen with a WPF ShowDialog window).
    /// </summary>
    private static Window[] GetWindows(Application app, UIA3Automation automation)
    {
        var handles = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == app.ProcessId && IsWindowVisible(handle))
                handles.Add(handle);
            return true;
        }, IntPtr.Zero);

        var windows = new List<Window>();
        foreach (var handle in handles)
        {
            try
            {
                windows.Add(automation.FromHandle(handle).AsWindow());
            }
            catch (COMException)
            {
                // closed meanwhile
            }
        }
        return windows.ToArray();
    }

    private static Window? TryChooseWindow(Options options, Application app, UIA3Automation automation)
    {
        var windows = GetWindows(app, automation);
        if (windows.Length == 0)
            return null;

        if (options.Window == null)
            return windows.FirstOrDefault(IsModal) ?? app.GetMainWindow(automation, TimeSpan.Zero) ?? windows[0];

        if (options.Window.StartsWith('#') && int.TryParse(options.Window.AsSpan(1), out var index))
            return windows.ElementAtOrDefault(index);

        return windows.FirstOrDefault(w => (w.Properties.Name.ValueOrDefault ?? "").Contains(options.Window, StringComparison.OrdinalIgnoreCase));
    }

    private static AutomationElement FindOne(Options options, Window window)
    {
        var query = Query.Parse(options.Positional.ElementAtOrDefault(0) ?? throw new UsageException("This command needs a query."));
        var matches = query.Match(window, includeOffscreen: true).ToList();

        // when the query alone doesn't decide, prefer what can be seen, then what can be acted on (a check box
        // and the text inside it share a name)
        if (query.Index == null && matches.Count > 1)
        {
            var visible = matches.Where(m => !IsOffscreen(m)).ToList();
            if (visible.Count > 0)
                matches = visible;
        }
        if (query.Index == null && matches.Count > 1)
        {
            var interactive = matches.Where(IsInteractive).ToList();
            if (interactive.Count > 0)
                matches = interactive;
        }

        if (query.Index is int index)
            return matches.ElementAtOrDefault(index) ?? throw new NotFoundException($"{query} has {matches.Count} match(es).");

        return matches.Count switch
        {
            0 => throw new NotFoundException($"Nothing matches {query}."),
            1 => matches[0],
            _ => throw new UsageException($"{query} matches {matches.Count} elements; add #n to pick one:{Environment.NewLine}" +
                string.Join(Environment.NewLine, matches.Take(20).Select((m, i) => $"  #{i} {Describe(m)}"))),
        };
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // exists, but elevated: no access to more
            return true;
        }
    }

    private static int GetPid(Options options)
    {
        if (options.Pid is int pid)
            return pid;
        if (File.Exists(PidFile) && int.TryParse(File.ReadAllText(PidFile).Trim(), out pid))
            return pid;
        throw new UsageException("No process: run 'launch' or 'attach' first, or pass -p <pid>.");
    }

    private static T? WaitFor<T>(TimeSpan timeout, Func<T?> probe) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var result = probe();
                if (result != null)
                    return result;
            }
            catch (Exception ex) when (ex is COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
            {
                // the UI is changing under us (an element went away mid-search); try again
            }

            if (stopwatch.Elapsed >= timeout)
                return null;
            Thread.Sleep(250);
        }
    }

    /// <summary>
    /// Gives the keyboard focus back to <paramref name="previousForeground"/> if the process has taken it.
    /// </summary>
    /// <returns>true if it had.</returns>
    private static bool KeepFocus(int processId, IntPtr previousForeground)
    {
        var foreground = GetForegroundWindow();
        if (previousForeground == IntPtr.Zero || foreground == IntPtr.Zero || foreground == previousForeground)
            return false;

        GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        if (foregroundProcessId != processId)
            return false;

        SetForegroundWindow(previousForeground);
        return true;
    }

    private static void PrintWindows(Window[] windows)
    {
        for (var i = 0; i < windows.Length; i++)
        {
            var w = windows[i];
            var rect = w.Properties.BoundingRectangle.ValueOrDefault;
            Console.WriteLine($"#{i} {Quote(w.Properties.Name.ValueOrDefault ?? "")} class={w.Properties.ClassName.ValueOrDefault} {rect.Width}x{rect.Height}{(IsModal(w) ? " modal" : "")}");
        }
    }

    private static bool IsModal(Window window) => window.Patterns.Window.PatternOrDefault?.IsModal.ValueOrDefault ?? false;

    private static bool IsOffscreen(AutomationElement element) => element.Properties.IsOffscreen.ValueOrDefault;

    private static bool IsInteractive(AutomationElement element)
    {
        var patterns = element.Patterns;
        return element.Properties.ControlType.ValueOrDefault != ControlType.Text &&
            (patterns.Invoke.IsSupported || patterns.Toggle.IsSupported || patterns.Value.IsSupported ||
             patterns.SelectionItem.IsSupported || patterns.ExpandCollapse.IsSupported || patterns.RangeValue.IsSupported);
    }

    internal static string Name(AutomationElement element) => element.Properties.Name.ValueOrDefault ?? "";

    /// <summary>
    /// A list or combo box item's label: its name, or the text shown in it when the name is just the item's .NET
    /// type (WPF names a data item by its ToString()).
    /// </summary>
    private static string ItemLabel(AutomationElement item)
    {
        var name = Name(item);
        if (name.Length > 0 && !TypeNamePattern.IsMatch(name))
            return name;

        var text = item.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text));
        return text != null && Name(text).Length > 0 ? Name(text) : name;
    }

    private static readonly System.Text.RegularExpressions.Regex TypeNamePattern = new(@"^[A-Za-z_][\w`]*(\.[A-Za-z_][\w`]*)+$");

    private static string Quote(string text)
    {
        text = text.Replace("\r", "").Replace("\n", "\\n");
        return "\"" + (text.Length > 80 ? text[..77] + "..." : text) + "\"";
    }

    internal static string Describe(AutomationElement element)
    {
        var p = element.Properties;
        var builder = new StringBuilder();
        builder.Append('[').Append(p.ControlType.ValueOrDefault).Append(']');

        var name = p.Name.ValueOrDefault;
        if (!string.IsNullOrEmpty(name))
            builder.Append(' ').Append(Quote(name));

        var id = p.AutomationId.ValueOrDefault;
        if (!string.IsNullOrEmpty(id))
            builder.Append(" id=").Append(id);

        var className = p.ClassName.ValueOrDefault;
        if (!string.IsNullOrEmpty(className))
            builder.Append(" class=").Append(className);

        // the tooltip: the only label an icon button has
        var helpText = p.HelpText.ValueOrDefault;
        if (!string.IsNullOrEmpty(helpText) && string.IsNullOrEmpty(name))
            builder.Append(" help=").Append(Quote(helpText));

        if (!p.IsEnabled.ValueOrDefault)
            builder.Append(" disabled");
        if (p.IsOffscreen.ValueOrDefault)
            builder.Append(" offscreen");

        var patterns = element.Patterns;
        if (patterns.Toggle.TryGetPattern(out var toggle))
            builder.Append(" toggle=").Append(toggle.ToggleState.ValueOrDefault);
        if (patterns.Value.TryGetPattern(out var value))
            builder.Append(" value=").Append(Quote(value.Value.ValueOrDefault ?? ""));
        if (patterns.SelectionItem.TryGetPattern(out var selectionItem) && selectionItem.IsSelected.ValueOrDefault)
            builder.Append(" selected");

        return builder.ToString();
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    private const uint GW_HWNDPREV = 3;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
}

internal sealed class UsageException(string message) : Exception(message);

internal sealed class NotFoundException(string message) : Exception(message);

internal sealed class Options
{
    public List<string> Positional { get; } = [];
    public List<string> AppArguments { get; } = [];
    public string? Window { get; private set; }
    public int? Pid { get; private set; }
    public int? Depth { get; private set; }
    public TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(30);
    public bool All { get; private set; }
    public bool Mouse { get; private set; }

    public static Options Parse(IEnumerable<string> args)
    {
        var options = new Options();
        using var e = args.GetEnumerator();
        string Next(string option) => e.MoveNext() ? e.Current : throw new UsageException($"{option} needs a value.");

        while (e.MoveNext())
        {
            switch (e.Current)
            {
                case "--":
                    while (e.MoveNext())
                        options.AppArguments.Add(e.Current);
                    break;
                case "-w":
                    options.Window = Next("-w");
                    break;
                case "-p":
                    options.Pid = int.Parse(Next("-p"));
                    break;
                case "-d":
                    options.Depth = int.Parse(Next("-d"));
                    break;
                case "-t":
                    options.Timeout = TimeSpan.FromSeconds(double.Parse(Next("-t"), System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--all":
                    options.All = true;
                    break;
                case "--mouse":
                    options.Mouse = true;
                    break;
                default:
                    options.Positional.Add(e.Current);
                    break;
            }
        }
        return options;
    }
}

/// <summary>
/// "Start" (name or automation id), or terms joined by ';': id=, name=, name~= (contains), type=, class=, #n.
/// </summary>
internal sealed class Query
{
    private readonly List<(string Key, bool Contains, string Value)> _terms = [];
    private readonly string _text;

    private Query(string text) => _text = text;

    public int? Index { get; private set; }

    public static Query Parse(string text)
    {
        var query = new Query(text);
        foreach (var raw in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith('#') && int.TryParse(raw.AsSpan(1), out var index))
            {
                query.Index = index;
                continue;
            }

            var contains = raw.IndexOf("~=", StringComparison.Ordinal);
            var equals = raw.IndexOf('=');
            if (contains > 0 && contains <= equals - 1)
                query._terms.Add((raw[..contains].Trim().ToLowerInvariant(), true, raw[(contains + 2)..]));
            else if (equals > 0)
                query._terms.Add((raw[..equals].Trim().ToLowerInvariant(), false, raw[(equals + 1)..]));
            else
                query._terms.Add(("nameorid", false, raw));
        }

        foreach (var (key, _, _) in query._terms)
        {
            if (key is not ("id" or "name" or "type" or "class" or "help" or "nameorid"))
                throw new UsageException($"Unknown query term '{key}' (use id, name, type, class, help or #n).");
        }
        return query;
    }

    public IEnumerable<AutomationElement> Match(AutomationElement root, bool includeOffscreen)
    {
        foreach (var element in root.FindAllDescendants())
        {
            if (!includeOffscreen && element.Properties.IsOffscreen.ValueOrDefault)
                continue;
            if (_terms.All(t => Matches(element, t)))
                yield return element;
        }
    }

    private static bool Matches(AutomationElement element, (string Key, bool Contains, string Value) term)
    {
        var p = element.Properties;
        bool Test(string? actual) => actual != null && (term.Contains
            ? actual.Contains(term.Value, StringComparison.OrdinalIgnoreCase)
            : string.Equals(actual, term.Value, StringComparison.OrdinalIgnoreCase));

        return term.Key switch
        {
            "id" => Test(p.AutomationId.ValueOrDefault),
            "name" => Test(p.Name.ValueOrDefault),
            "type" => Test(p.ControlType.ValueOrDefault.ToString()),
            "class" => Test(p.ClassName.ValueOrDefault),
            "help" => Test(p.HelpText.ValueOrDefault),
            _ => Test(p.Name.ValueOrDefault) || Test(p.AutomationId.ValueOrDefault),
        };
    }

    public override string ToString() => $"'{_text}'";
}
