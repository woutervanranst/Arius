using Humanizer;
using System.Globalization;
using System.Windows.Data;

namespace Arius.Explorer.Shared.Converters;

public class BytesToReadableSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            return bytes.Bytes().Humanize("#.#");
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}