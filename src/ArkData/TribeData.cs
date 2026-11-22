using System;
using System.Collections.Generic;

namespace ArkData
{
    /// <summary>
    /// A tribe: a &lt;tribe id&gt;.arktribe file, or a tribe stored in the world save (servers started with -usestore).
    /// </summary>
    public class TribeData
    {
        public int Id { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The owner's in-game player ID (<see cref="PlayerData.CharacterId"/>).
        /// </summary>
        public long? OwnerId { get; set; }

        /// <summary>
        /// The members' in-game player IDs (<see cref="PlayerData.CharacterId"/>), as listed in the tribe.
        /// </summary>
        public List<long> MemberIds { get; set; } = new List<long>();

        public int TamedDinoCount { get; set; }

        /// <summary>
        /// The tribe file, or null for a tribe stored in the world save.
        /// </summary>
        public string File { get; set; }

        public string Filename { get; set; }

        public DateTime? FileCreated { get; set; }

        public DateTime? FileUpdated { get; set; }

        public ICollection<PlayerData> Players { get; set; } = new List<PlayerData>();

        public PlayerData Owner { get; set; }

        internal TribeData Copy()
        {
            var copy = (TribeData)MemberwiseClone();
            copy.Owner = null;
            copy.Players = new List<PlayerData>();
            return copy;
        }
    }
}
