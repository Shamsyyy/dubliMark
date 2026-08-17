namespace DoubleMark.Desktop.Settings;

/// <summary>
/// CRPT secrets persisted via DPAPI in crpt-secrets.dat (§7.1).
/// </summary>
public sealed class CrptSecrets
{
    public string? OmsId { get; set; }
    public string? ConnectionId { get; set; }
    public string? CertificateThumbprint { get; set; }

    /// <summary>Used when <see cref="CrptSettings.NkUseJwtFromTrueApi"/> is false.</summary>
    public string? NkApiKey { get; set; }
}
