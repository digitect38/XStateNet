using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimelineWPF
{
    public class HeightAndMarginToOffsetConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
            {
                return 0.0;
            }

            if (values[0] is double actualHeight && values[1] is Thickness margin)
            {
                return actualHeight + margin.Bottom;
            }

            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}