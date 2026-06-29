using System.Globalization;
using System.Windows.Data;

namespace DoubleMark.Desktop.ViewModels.Crpt;

public sealed class CrptCatalogEnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            CrptCatalogProductStateFilter productState =>
                CrptCatalogDisplayLabels.FormatProductStateFilter(productState),
            CrptCatalogCardStatusFilter cardStatus =>
                CrptCatalogDisplayLabels.FormatCardStatusFilter(cardStatus),
            CrptCatalogCardTypeFilter cardType =>
                CrptCatalogDisplayLabels.FormatCardTypeFilter(cardType),
            CrptCatalogFilter legacyFilter =>
                CrptCatalogDisplayLabels.FormatLegacyFilter(legacyFilter),
            _ => value?.ToString() ?? string.Empty,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
