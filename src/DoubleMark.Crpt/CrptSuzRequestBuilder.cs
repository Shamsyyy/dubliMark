namespace DoubleMark.Crpt;

/// <summary>
/// Builds SUZ 3.0 create-order JSON bodies without HTTP (spec §14.1).
/// </summary>
public static class CrptSuzRequestBuilder
{
    public static Dictionary<string, object> BuildOrderBody(
        string productGroup,
        string gtin,
        int quantity,
        string contactPerson,
        int? templateId = null,
        string releaseMethodType = "PRODUCTION",
        string createMethodType = "SELF_MADE",
        string serialNumberType = "OPERATOR",
        string cisType = "UNIT")
    {
        var attributes = new Dictionary<string, object>
        {
            ["releaseMethodType"] = releaseMethodType,
            ["createMethodType"] = createMethodType,
            ["contactPerson"] = contactPerson,
        };

        var product = new Dictionary<string, object>
        {
            ["gtin"] = gtin,
            ["quantity"] = quantity,
            ["serialNumberType"] = serialNumberType,
            ["cisType"] = cisType,
        };

        if (templateId.HasValue)
            product["templateId"] = templateId.Value;

        return new Dictionary<string, object>
        {
            ["productGroup"] = productGroup,
            ["attributes"] = attributes,
            ["products"] = new[] { product },
        };
    }
}
