using System.Globalization;

namespace Nodus.Admin.Presentation.Converters;

public class BooleanToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return parameter as Color ?? Colors.Transparent;
        }

        return Colors.Gray; // Default inactive color
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
