using ServerManagerTool.Utils;
using System;
using System.Globalization;
using System.Windows.Data;

namespace ServerManagerTool.Lib.ViewModel
{
    public class MapNameValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var valueString = value as string;
                if (valueString == null)
                    return string.Empty;

                // a translated name, else the map's name in the game data ("TheIsland_WP" -> "The Island"), as in the
                // profile's map box; for a mod map path, the same for its last part
                var name = GameData.FriendlyMapNameForClass(valueString, true);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;

                var mapName = ServerProfile.GetMapName(valueString);
                name = GameData.FriendlyMapNameForClass(mapName, true);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;

                return string.IsNullOrWhiteSpace(mapName) ? valueString : mapName;
            }
            catch
            {
                return value ?? string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
