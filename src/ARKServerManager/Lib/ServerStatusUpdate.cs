﻿using ServerManagerTool.Common.Enums;
using System;
using System.Diagnostics;

namespace ServerManagerTool.Lib
{
    public struct ServerStatusUpdate
    {
        public Process Process;
        public WatcherServerStatus Status;
        /// <summary>The number of players online; 0 when <see cref="PlayerCountStatus"/> says the count isn't known.</summary>
        public int OnlinePlayerCount;
        public PlayerCountStatus PlayerCountStatus;
        /// <summary>Server version reported in the server log ("ARK Version: x.y"), or null when not known yet.</summary>
        public Version Version;
    }
}
