using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace ServerManagerTool.Common.Utils
{
    public static class ProcessUtils
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Delegate type to be used as the Handler Routine for SCCH
        delegate bool ConsoleCtrlDelegate(CtrlTypes CtrlType);

        // Enumerated type for the control messages sent to the handler routine
        enum CtrlTypes : uint
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GenerateConsoleCtrlEvent(CtrlTypes dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

        private static readonly int[] StdHandles = { -10, -11, -12 }; // input, output, error

        // a process can be attached to one console at a time
        private static readonly object ConsoleLock = new object();

        // how long Ctrl+C stays ignored here after sending it, so that it can't reach this process
        private const int CtrlCGraceMilliseconds = 5000;

        [DllImport("user32.dll")]
        private static extern int ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern int SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int IsIconic(IntPtr hWnd);

        public static string FIELD_COMMANDLINE = "CommandLine";
        public static string FIELD_EXECUTABLEPATH = "ExecutablePath";
        public static string FIELD_PROCESSID = "ProcessId";

        private const int SW_RESTORE = 9;

        private static Mutex _mutex;

        public static string GetCommandLineForProcess(int processId)
        {
            var wmiQueryString = $"SELECT {FIELD_COMMANDLINE} FROM Win32_Process WHERE {FIELD_PROCESSID} = {processId}";

            using (var searcher = new ManagementObjectSearcher(wmiQueryString))
            {
                using (var results = searcher.Get())
                {
                    ManagementObject mo = results.Cast<ManagementObject>().FirstOrDefault();
                    if (mo != null)
                        return (string)mo[FIELD_COMMANDLINE];
                }
            }

            return null;
        }

        public static string GetMainModuleFilepath(int processId)
        {
            var wmiQueryString = $"SELECT {FIELD_EXECUTABLEPATH} FROM Win32_Process WHERE {FIELD_PROCESSID} = {processId}";

            using (var searcher = new ManagementObjectSearcher(wmiQueryString))
            {
                using (var results = searcher.Get())
                {
                    ManagementObject mo = results.Cast<ManagementObject>().FirstOrDefault();
                    if (mo != null)
                        return (string)mo[FIELD_EXECUTABLEPATH];
                }
            }

            return null;
        }

        /// <summary>
        /// Sends Ctrl+C to a process's console, as pressing Ctrl+C in its window does, and waits up to 10 seconds for
        /// it to exit. Kills the process if Ctrl+C can't be sent.
        /// </summary>
        public static Task SendStopAsync(Process process)
        {
            if (process != null && !SendCtrlC(process, 10000, out _) && !process.HasExited)
                process.Kill();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends Ctrl+C to a process's console, as pressing Ctrl+C in its window does (the window needn't be visible),
        /// and waits up to <paramref name="waitMilliseconds"/> for the process to exit.
        /// </summary>
        /// <param name="error">The Windows error when Ctrl+C couldn't be sent, such as when the process has no console.</param>
        /// <returns>Whether Ctrl+C was sent; check <see cref="Process.HasExited"/> for whether the process exited.</returns>
        public static bool SendCtrlC(Process process, int waitMilliseconds, out int error)
        {
            ArgumentNullException.ThrowIfNull(process);
            error = 0;

            var started = Stopwatch.StartNew();
            lock (ConsoleLock)
            {
                // AttachConsole points the standard handles at the console, and FreeConsole leaves them pointing at
                // closed handles, which would make the next Process.Start fail ("The handle is invalid")
                var stdHandles = StdHandles.Select(GetStdHandle).ToArray();

                if (!AttachConsole((uint)process.Id))
                {
                    error = Marshal.GetLastWin32Error();
                    return false;
                }

                var attached = true;
                try
                {
                    // ignore the Ctrl+C here: this process shares the console for a moment
                    if (!SetConsoleCtrlHandler(null, true))
                    {
                        error = Marshal.GetLastWin32Error();
                        return false;
                    }

                    try
                    {
                        if (!GenerateConsoleCtrlEvent(CtrlTypes.CTRL_C_EVENT, 0))
                        {
                            error = Marshal.GetLastWin32Error();
                            return false;
                        }

                        FreeConsole();
                        attached = false;

                        // handling Ctrl+C again too soon could let it end this process
                        process.WaitForExit(Math.Min(waitMilliseconds, CtrlCGraceMilliseconds));
                    }
                    finally
                    {
                        // or programs started from here would inherit the ignored Ctrl+C
                        SetConsoleCtrlHandler(null, false);
                    }
                }
                finally
                {
                    if (attached)
                        FreeConsole();

                    for (var i = 0; i < StdHandles.Length; i++)
                        SetStdHandle(StdHandles[i], stdHandles[i]);
                }
            }

            var remaining = waitMilliseconds - (int)started.ElapsedMilliseconds;
            if (remaining > 0)
                process.WaitForExit(remaining);
            return true;
        }

        public static Task<bool> RunProcessAsync(string file, string arguments, string verb, string workingDirectory, string username, SecureString password, List<int> SteamCmdIgnoreExitStatusCodes, DataReceivedEventHandler outputHandler, CancellationToken cancellationToken, ProcessWindowStyle windowStyle = ProcessWindowStyle.Normal)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                    throw new FileNotFoundException("The specified file does not exist or could not be found.", file);

                var fileName = Path.GetFileName(file);

                var startInfo = new ProcessStartInfo()
                {
                    FileName = file,
                    Arguments = arguments,
                    Verb = verb,
                    UseShellExecute = outputHandler == null && windowStyle == ProcessWindowStyle.Minimized && string.IsNullOrWhiteSpace(username),
                    RedirectStandardOutput = outputHandler != null,
                    CreateNoWindow = outputHandler != null || windowStyle == ProcessWindowStyle.Hidden,
                    WindowStyle = windowStyle,
                    UserName = string.IsNullOrWhiteSpace(username) ? null : username,
                    Password = string.IsNullOrWhiteSpace(username) ? null : password,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory) ? Path.GetDirectoryName(file) : workingDirectory,
                };

                var process = Process.Start(startInfo);
                process.EnableRaisingEvents = true;
                if (startInfo.RedirectStandardOutput && outputHandler != null)
                {
                    process.OutputDataReceived += outputHandler;
                    process.BeginOutputReadLine();
                }

                var tcs = new TaskCompletionSource<bool>();
                using (var cancelRegistration = cancellationToken.Register(() =>
                    {
                        try
                        {
                            process.Kill();
                        }
                        finally
                        {
                            tcs.TrySetCanceled();
                        }
                    }))
                {
                    process.Exited += (s, e) =>
                    {
                        var exitCode = process.ExitCode;

                        _logger.Debug($"{nameof(RunProcessAsync)}: filename {fileName}; exitcode = {exitCode}");

                        if (exitCode != 0)
                        {
                            _logger.Error($"{nameof(RunProcessAsync)}: filename {fileName}; exitcode = {exitCode}");

                            if (SteamCmdIgnoreExitStatusCodes.Contains(exitCode))
                            {
                                exitCode = 0;
                            }
                        }

                        tcs.TrySetResult(exitCode == 0);
                        process.Close();
                    };
                    return tcs.Task;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"{nameof(RunProcessAsync)}. {ex.Message}\r\n{ex.StackTrace}");
                throw;
            }
        }

        private static IntPtr GetCurrentInstanceWindowHandle()
        {
            var hWnd = IntPtr.Zero;
            var currentProcess = Process.GetCurrentProcess();

            var processes = Process.GetProcessesByName(currentProcess.ProcessName);
            foreach (var process in processes)
            {
                // Get the first instance that is not this instance, has the same process name and was started from the same file name
                // and location. Also check that the process has a valid window handle in this session to filter out other user's processes.
                if (process.Id != currentProcess.Id && process.MainModule.FileName == currentProcess.MainModule.FileName && process.MainWindowHandle != IntPtr.Zero)
                {
                    hWnd = process.MainWindowHandle;
                    break;
                }
            }

            return hWnd;
        }

        /// <summary>
        /// Opens a URL, folder or document with its default Windows handler.
        /// On .NET (unlike .NET Framework) Process.Start does not use the shell by default,
        /// so passing a URL or folder straight to Process.Start throws.
        /// </summary>
        public static void OpenWithShell(string target)
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }

        public static bool IsAlreadyRunning()
        {
            var assemblyLocation = Assembly.GetEntryAssembly().Location;
            var name = $"Global::{Path.GetFileName(assemblyLocation)}";

            _mutex = new Mutex(true, name, out bool createdNew);
            if (createdNew)
                _mutex.ReleaseMutex();

            return !createdNew;
        }

        public static bool SwitchToCurrentInstance()
        {
            var hWnd = GetCurrentInstanceWindowHandle();
            if (hWnd == IntPtr.Zero)
                return false;

            // Restore window if minimised. Do not restore if already in normal or maximised window state, since we don't want to
            // change the current state of the window.
            if (IsIconic(hWnd) != 0)
                ShowWindow(hWnd, SW_RESTORE);

            // Set foreground window.
            SetForegroundWindow(hWnd);

            return true;
        }

        public static int ProcessorCount
        {
            get
            {
                var processorCount = Environment.ProcessorCount;
//#if DEBUG
//                processorCount = 40;
//#endif
                return processorCount;
            }
        }

        public static IEnumerable<BigInteger> GetProcessorAffinityList()
        {
            var processorCount = ProcessorCount;
            var results = new List<BigInteger>(processorCount + 1);
            results.Add(BigInteger.Zero); // default - all processors

            for (int index = 0; index < processorCount; index++)
            {
                results.Add((BigInteger)Math.Pow(2, index));
            }
            return results;
        }

        public static string[] GetProcessPriorityList()
        {
            return new string[] { "low", "belownormal", "normal", "abovenormal", "high" };
        }

        public static bool IsProcessorAffinityValid(BigInteger affinityValue)
        {
            if (affinityValue == BigInteger.Zero)
                return true;

            var maxaffinity = (BigInteger)Math.Pow(2, ProcessorCount);
            if (affinityValue < BigInteger.Zero || affinityValue > maxaffinity)
                return false;

            return true;
        }

        public static bool IsProcessPriorityValid(string priorityValue)
        {
            if (string.IsNullOrWhiteSpace(priorityValue))
                return false;

            var priorityList = GetProcessPriorityList();
            if (!priorityList.Contains(priorityValue))
                return false;

            return true;
        }
    }
}
