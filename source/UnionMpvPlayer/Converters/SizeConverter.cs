using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UnionMpvPlayer.Converters
{
    public class SizeConverter : IValueConverter
    {
        private const double ScaleFactor = 0.5; // Adjust this to set the scale

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double originalSize)
            {
                return originalSize * ScaleFactor;
            }
            return 0;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
