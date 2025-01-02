using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace UnionMpvPlayer.Converters
{
    public class IconColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string title)
            {
                return title switch
                {
                    "Success" => Brushes.LightGreen,
                    "Warning" => Brushes.Gold,
                    _ => Brushes.Gray
                };
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
