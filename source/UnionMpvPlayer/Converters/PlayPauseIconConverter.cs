using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace UnionMpvPlayer.Converters
{
    public class PlayPauseIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isPaused && App.Current?.Resources != null)
            {
                var iconKey = isPaused ? "play_regular" : "pause_regular";
                if (App.Current.Resources.TryGetResource(iconKey, ThemeVariant.Default, out object? resource) &&
                    resource is StreamGeometry geometry)
                {
                    return geometry;
                }
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}