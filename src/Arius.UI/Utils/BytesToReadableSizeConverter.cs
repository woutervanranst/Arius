using System.Globalization;
using System.Windows.Data;
using WouterVanRanst.Utils.Extensions;

namespace Arius.UI.Utils;

public class BytesToReadableSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            return bytes.GetBytesReadable(precision: 0);
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}