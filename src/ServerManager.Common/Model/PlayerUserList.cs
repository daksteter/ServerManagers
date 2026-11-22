using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;

namespace ServerManagerTool.Common.Model
{
    [DataContract]
    public class PlayerUserList : ObservableCollection<PlayerUserItem>
    {
        public void AddRange(PlayerUserList list)
        {
            if (list == null)
                return;

            foreach (var item in list)
            {
                if (string.IsNullOrWhiteSpace(item?.PlayerId))
                    continue;

                if (!this.Any(i => i.PlayerId.Equals(item.PlayerId, System.StringComparison.OrdinalIgnoreCase)))
                    this.Add(item);
            }
        }

        /// <param name="playerNames">Player names by player ID; IDs without a name are shown as not available.</param>
        /// <param name="ids">The player IDs (blank entries are skipped).</param>
        public static PlayerUserList GetList(IReadOnlyDictionary<string, string> playerNames, IEnumerable<string> ids)
        {
            var result = new PlayerUserList();
            if (ids is null)
                return result;

            foreach (var id in ids)
            {
                var playerId = id?.Trim();
                if (string.IsNullOrWhiteSpace(playerId))
                    continue;

                string playerName = null;
                playerNames?.TryGetValue(playerId, out playerName);

                result.Add(new PlayerUserItem()
                {
                    PlayerId = playerId,
                    PlayerName = string.IsNullOrWhiteSpace(playerName) ? "<not available>" : playerName,
                });
            }

            return result;
        }

        public void Remove(string steamId)
        {
            var ids = this.Where(i => i.PlayerId.Equals(steamId, System.StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var item in ids)
            {
                this.Remove(item);
            }
        }

        public IEnumerable<string> ToEnumerable()
        {
            return this.Select(i => i.PlayerId);
        }

        public string ToDelimitedString(string delimiter)
        {
            return string.Join(delimiter, this.Select(i => i.PlayerId));
        }

        public override string ToString()
        {
            return $"{nameof(PlayerUserList)} - {Count}";
        }
    }
}
