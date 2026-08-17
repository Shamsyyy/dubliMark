using DoubleMark.Core.Crpt;

namespace DoubleMark.Crpt;

public static class CrptUtilisationBuilder
{
    public static UtilisationReportRequest BuildRequest(
        CrptProductCatalogItem catalogItem,
        string productGroup,
        IReadOnlyList<string> rawPayloads,
        CrptConnectionSettings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productGroup);
        ArgumentNullException.ThrowIfNull(rawPayloads);

        if (rawPayloads.Count == 0)
            throw new ArgumentException("At least one marking code payload is required.", nameof(rawPayloads));

        var productionDate = settings?.UtilisationProductionDate
            ?? DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        var expirationDate = settings?.UtilisationExpirationDate
            ?? DateOnly.FromDateTime(DateTime.Now.AddYears(3)).ToString("yyyy-MM-dd");

        return new UtilisationReportRequest
        {
            ProductGroup = productGroup,
            RawPayloads = rawPayloads,
            ProductionDate = productionDate,
            ExpirationDate = expirationDate,
            CertificateDocType = catalogItem.CertificateDocType,
            CertificateDocNumber = catalogItem.CertificateDocNumber,
            CertificateDocDate = catalogItem.CertificateDocDate,
        };
    }

    public static Dictionary<string, object> BuildBody(UtilisationReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProductGroup);

        if (request.RawPayloads.Count == 0)
            throw new ArgumentException("At least one marking code payload is required.", nameof(request));

        var attributes = new Dictionary<string, object>
        {
            ["productionDate"] = request.ProductionDate
                ?? DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
            ["expirationDate"] = request.ExpirationDate
                ?? DateOnly.FromDateTime(DateTime.Now.AddYears(3)).ToString("yyyy-MM-dd"),
        };

        if (!string.IsNullOrWhiteSpace(request.CertificateDocNumber))
        {
            attributes["certificateDocument"] = request.CertificateDocType ?? "CONFORMITY_DECLARATION";
            attributes["certificateDocumentNumber"] = request.CertificateDocNumber;
            if (!string.IsNullOrWhiteSpace(request.CertificateDocDate))
                attributes["certificateDocumentDate"] = request.CertificateDocDate;
        }

        return new Dictionary<string, object>
        {
            ["productGroup"] = request.ProductGroup,
            ["sntins"] = request.RawPayloads,
            ["attributes"] = attributes,
        };
    }
}
