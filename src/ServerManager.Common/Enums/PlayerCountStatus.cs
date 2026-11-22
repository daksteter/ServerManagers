namespace ServerManagerTool.Common.Enums
{
    /// <summary>
    /// Whether a server's player count is known. ASA answers no Steam queries, so the count comes from RCON.
    /// </summary>
    public enum PlayerCountStatus
    {
        /// <summary>
        /// The count came from RCON, or the server isn't running (no players).
        /// </summary>
        Known,

        /// <summary>
        /// RCON is off in the profile, so the count can't be read.
        /// </summary>
        RconDisabled,

        /// <summary>
        /// RCON is on, but the server hasn't answered: still starting, or the RCON port isn't reachable.
        /// </summary>
        NoRconReply,

        /// <summary>
        /// The server didn't accept the profile's admin password over RCON.
        /// </summary>
        RconPasswordRejected,
    }
}
