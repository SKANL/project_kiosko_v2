using System.Globalization;

namespace Nodus.Admin.Presentation.Converters;

public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var result = !string.IsNullOrWhiteSpace(value as string);
        if (parameter is string s && s.Equals("inverse", StringComparison.OrdinalIgnoreCase))
            return !result;
        return result;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
