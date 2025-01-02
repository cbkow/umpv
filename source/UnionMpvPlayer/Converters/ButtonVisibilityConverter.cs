using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace UnionMpvPlayer.Converters
{
    public class ButtonVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string name && parameter is string buttonType)
            {
                switch (buttonType)
                {
                    case "Link":
                        // Show Link button for Directory and Project rows only
                        return name == "Directory" ||
                               name.Contains("Project", StringComparison.OrdinalIgnoreCase);

                    case "Project":
                        // Show Project button for Directory and Project rows
                        return name == "Directory" ||
                               name.Contains("Project", StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
