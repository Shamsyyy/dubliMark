using System.Globalization;
using System.Windows.Data;

namespace DoubleMark.Desktop.ViewModels.Crpt;

public sealed class CrptProductGroupFilterLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        CrptCatalogDisplayLabels.FormatProductGroupFilter(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
