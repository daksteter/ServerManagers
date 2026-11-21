﻿using System;
using System.Net;
using System.Threading.Tasks;

namespace ServerManagerTool.Lib
{
    public class ServerStatusUpdateRegistration : IAsyncDisposable
    {
        public string InstallDirectory;
        /// <summary>Game port; matched against -port= in the server process command line.</summary>
        public int GamePort;
        /// <summary>Server IP and game port; the IP is matched against ?MultiHome= in the server process command line.</summary>
        public IPEndPoint LocalEndpoint;
        /// <summary>RCON endpoint, or null when RCON is disabled for the profile.</summary>
        public IPEndPoint RconEndpoint;
        public string RconPassword;
        public Action<IAsyncDisposable, ServerStatusUpdate> UpdateCallback;
        public Func<Task> UnregisterAction;

        public string ProfileId;

        // State kept between status polls; only touched on the status watcher's queue.
        internal QueryMaster.Server RconServer;
        internal QueryMaster.Rcon Rcon;
        internal int LogProcessId;
        internal bool LogStartupComplete;
        internal Version LogVersion;

        public async ValueTask DisposeAsync()
        {
            await UnregisterAction();
        }
    }
}
