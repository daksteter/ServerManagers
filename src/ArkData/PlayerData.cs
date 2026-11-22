using System;
using System.Collections.Generic;

namespace ArkData
{
    /// <summary>
    /// A player profile: a &lt;player id&gt;.arkprofile file, or a profile stored in the world save (servers started with -usestore).
    /// </summary>
    public class PlayerData
    {
        /// <summary>
        /// The player's EOS ID (32 hex digits): the ID listplayers shows and the admin, whitelist and ban files use.
        /// Empty when the profile could not be read.
        /// </summary>
        public string PlayerId { get; set; }

        /// <summary>
        /// The player's platform name (Epic, Steam, Xbox, PlayStation), as the server saved it at their last login.
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// The in-game player ID (PlayerDataID), which tribes use for their owner and members.
        /// </summary>
        public long CharacterId { get; set; }

        public string CharacterName { get; set; }

        public int Level { get; set; }

        /// <summary>
        /// The tribe the player belongs to, or null.
        /// </summary>
        public int? TribeId { get; set; }

        /// <summary>
        /// The profile file, or null for a profile stored in the world save.
        /// </summary>
        public string File { get; set; }

        public string Filename { get; set; }

        public DateTime? FileCreated { get; set; }

        /// <summary>
        /// When the profile was last written (for a profile stored in the world save, when the world was last saved).
        /// </summary>
        public DateTime? FileUpdated { get; set; }

        public TribeData Tribe { get; set; }

        public List<TribeData> OwnedTribes { get; set; } = new List<TribeData>();

        internal PlayerData Copy()
        {
            var copy = (PlayerData)MemberwiseClone();
            copy.Tribe = null;
            copy.OwnedTribes = new List<TribeData>();
            return copy;
        }
    }
}
