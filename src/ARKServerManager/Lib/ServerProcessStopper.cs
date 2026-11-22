using ServerManagerTool.Common.Lib;
using ServerManagerTool.Common.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;

namespace ServerManagerTool.Lib
{
    /// <summary>
    /// Stops a running server without a countdown, saving the world first: over RCON (saveworld, then doexit) when
    /// RCON is on, else with Ctrl+C in the server's console, which ASA answers by saving and exiting ("Closing by
    /// request" in its log). The process is killed, without a save, only when neither works. Blocks until done.
    /// </summary>
    public static class ServerProcessStopper
    {
        // ASA answers saveworld once the save is done
        private const int WorldSaveTimeout = 120000;
        // how long the server gets to exit after doexit or Ctrl+C
        private const int ExitTimeout = 60000;
        private const int KillTimeout = 10000;

        /// <param name="log">Receives each step, for the log.</param>
        /// <returns>Whether the server process has exited.</returns>
        public static bool Stop(Process process, ServerProfileSnapshot profile, Action<string> log)
        {
            ArgumentNullException.ThrowIfNull(process);
            ArgumentNullException.ThrowIfNull(profile);
            log ??= _ => { };

            if (process.HasExited)
                return true;

            if (profile.RCONEnabled && SaveAndExitOverRcon(profile, log))
            {
                if (process.WaitForExit(ExitTimeout))
                {
                    log("The server saved the world and exited.");
                    return true;
                }
                log($"The server didn't exit within {ExitTimeout / 1000} seconds of {Config.Default.ServerShutdownCommand}.");
            }

            log("Sending Ctrl+C to the server's console.");
            if (ProcessUtils.SendCtrlC(process, ExitTimeout, out var error))
            {
                if (process.HasExited)
                {
                    log("The server exited after Ctrl+C.");
                    return true;
                }
                log($"The server didn't exit within {ExitTimeout / 1000} seconds of Ctrl+C.");
            }
            else
            {
                log($"Ctrl+C couldn't be sent: {new Win32Exception(error).Message} ({error})");
            }

            log("Killing the server process. The world isn't saved.");
            try
            {
                process.Kill();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                log($"The server process couldn't be killed: {ex.Message}");
            }

            return process.WaitForExit(KillTimeout);
        }

        /// <returns>Whether the world was saved and doexit sent.</returns>
        private static bool SaveAndExitOverRcon(ServerProfileSnapshot profile, Action<string> log)
        {
            try
            {
                var endPoint = new IPEndPoint(profile.ServerIPAddress, profile.RCONPort);
                using var rcon = RconClient.Connect(endPoint, profile.RCONPassword);
                rcon.ReplyTimeout = WorldSaveTimeout;

                log($"RCON> {Config.Default.ServerSaveCommand}");
                var watch = Stopwatch.StartNew();
                var reply = rcon.SendCommand(Config.Default.ServerSaveCommand);
                log($"The server answered after {watch.Elapsed.TotalSeconds:0.0} seconds: {reply.Trim()}");

                log($"RCON> {Config.Default.ServerShutdownCommand}");
                try
                {
                    rcon.SendCommand(Config.Default.ServerShutdownCommand);
                }
                catch (Exception)
                {
                    // the server may close the connection as it exits
                }

                return true;
            }
            catch (Exception ex)
            {
                log($"The world couldn't be saved over RCON: {ex.Message}");
                return false;
            }
        }
    }
}
