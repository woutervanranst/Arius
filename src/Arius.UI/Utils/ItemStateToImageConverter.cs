using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Arius.UI.Utils;

public class ItemStateToImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string itemName)
        {
            // Assuming that the image is a resource, adjust the path as needed.
            return new BitmapImage(new Uri($"/Resources/{itemName}.png", UriKind.Relative));
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}