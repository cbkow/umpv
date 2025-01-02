using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace UnionMpvPlayer.Converters
{
    public class IconPathConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string title)
            {
                return title switch
                {
                    "Success" => App.Current.Resources["checkmark_regular"],
                    "Warning" => App.Current.Resources["warning_regular"],
                    _ => App.Current.Resources["chat_regular"]
                };
            }
            return App.Current.Resources["chat_regular"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
