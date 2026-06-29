using System.Text.Json;
using System.Text.Json.Serialization;
using DoubleMark.Crpt;

namespace DoubleMark.CrptProbe;

public sealed class CrptProbeConfig
{
    public string Inn { get; set; } = "7810928720";
    public string TrueApiBaseUrl { get; set; } = "https://markirovka.crpt.ru/";
    public string SuzBaseUrl { get; set; } = "https://suzgrid.crpt.ru/";
    public string OmsId { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public string? CertificateThumbprint { get; set; }
    public string ContactPerson { get; set; } = "Городилов Иван Александрович";
    public string ProductGroup { get; set; } = "chemistry";
    public int? TemplateId { get; set; } = 46;
    public string? NkBaseUrl { get; set; }
    public string Gtin { get; set; } = "04620490950423";
    public int Quantity { get; set; } = 10;
    public string? TnvedCode { get; set; }
    public string? CertificateDocType { get; set; } = "CONFORMITY_DECLARATION";
    public string? CertificateDocNumber { get; set; }
    public string? CertificateDocDate { get; set; }
    public string? CodesFile { get; set; }
    public string? UtilisationProductionDate { get; set; }
    public string? UtilisationExpirationDate { get; set; }

    public CrptConnectionSettings ToConnectionSettings() => new()
    {
        Inn = Inn,
        TrueApiBaseUrl = TrueApiBaseUrl,
        SuzBaseUrl = SuzBaseUrl,
        OmsId = OmsId,
        ConnectionId = ConnectionId,
        CertificateThumbprint = CertificateThumbprint,
        ContactPerson = ContactPerson,
        ProductGroup = ProductGroup,
        TemplateId = TemplateId,
        TnvedCode = TnvedCode,
        CertificateDocType = CertificateDocType,
        CertificateDocNumber = CertificateDocNumber,
        CertificateDocDate = CertificateDocDate,
        NkBaseUrl = NkBaseUrl ?? CrptUrl.ProductionNkBaseUrl,
        UtilisationProductionDate = UtilisationProductionDate,
        UtilisationExpirationDate = UtilisationExpirationDate
    };

    public static CrptProbeConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CrptProbeConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse config: {path}");
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}
