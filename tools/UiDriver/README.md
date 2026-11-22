# UiDriver

A command-line UI Automation driver ([FlaUI](https://github.com/FlaUI/FlaUI) 5.0.0, UIA3) for checking the
app's UI from a shell: start it, list its windows, find controls, click, type, read values and take screenshots,
one command per run. It acts through automation patterns (invoke, toggle, value, selection), not the real mouse
and keyboard, so it doesn't take over the desktop. Not part of the solution build.

## Isolated test copy

Windows doesn't let a normal process drive an elevated window, and the app normally runs as administrator with
the real data folder. So tests use a separate copy that runs as the current user with its own settings and data:

```powershell
dotnet build src\ARKServerManager -c Release
tools\UiDriver\Deploy-TestApp.ps1 -Reset    # -Reset: start like a new installation
```

This copies the Release build to `src\ARKServerManager\bin\UiTest\app` and points it at
`src\ARKServerManager\bin\UiTest\data` (both under `bin\`, so git ignores them). The app reads a settings backup
(`userconfig.json`) from its install folder on first start; the script leaves out the real one and writes a seed
that sets the data folder and turns off the "run as administrator" and "already running" prompts.

**Never copy the Release folder by hand:** the copy would read the real `userconfig.json`, data folder included,
and show the administrator prompt; answering Yes gives an elevated copy working on the real profiles.

Without admin rights the copy can't change firewall rules or scheduled tasks; everything else works. Its windows
appear on the desktop while it runs; leave its dialogs to the driver. A newly started app takes the keyboard
focus, so `launch` gives it back to the window that had it (keys typed into a game or editor meanwhile would
otherwise land in the test copy); windows the app opens later don't take it.

## Use

```powershell
dotnet build tools\UiDriver -c Release
$d = 'tools\UiDriver\bin\Release\net11.0-windows\UiDriver.exe'

& $d launch 'src\ARKServerManager\bin\UiTest\app\JVH Ark Ascended Server Manager.exe'
& $d windows                                         # dialogs included; -w picks one by title
& $d click 'help=Open Global Settings'               # icon buttons go by their tooltip
& $d get 'name=Minimize To Tray' -w Settings         # a check box: toggle state
& $d select id=WindowStateMainWindowComboBox Minimized -w Settings
& $d screenshot shot.png -w Settings                 # works while other windows cover it
& $d close -w Settings
& $d close                                           # or: kill
```

`UiDriver help` lists the commands and the query syntax. The process ID from `launch` / `attach` is kept in
`%TEMP%\uidriver.pid` for the later commands. Exit codes: 0 done, 1 error, 2 not found.

## Limits

- Controls inside a tab control's template, such as the main window's "+" (new profile) button, aren't in the
  automation tree, and WPF ignores posted mouse messages. `tap <x> <y> --mouse` clicks them for real: the window
  comes to the front, the cursor moves there for a moment, and both are put back.
- The app's combo box items have no readable name (WPF uses the item's `ToString()`), so the driver uses the text
  shown in them; reading a closed combo box's selection opens its list for a moment.
- A query that matches several elements is refused with the list (add `#n`), except that visible and actionable
  elements win over hidden ones and plain text.
