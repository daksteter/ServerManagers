﻿using ServerManagerTool.Common.Enums;
using System;
using System.Diagnostics;

namespace ServerManagerTool.Lib
{
    public struct ServerStatusUpdate
    {
        public Process Process;
        public WatcherServerStatus Status;
        public int OnlinePlayerCount;
        /// <summary>Server version reported in the server log ("ARK Version: x.y"), or null when not known yet.</summary>
        public Version Version;
    }
}
