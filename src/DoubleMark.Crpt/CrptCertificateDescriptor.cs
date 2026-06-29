namespace DoubleMark.Crpt;

/// <summary>
/// UKEP certificate summary for UI selection (spec §11.1).
/// </summary>
public sealed record CrptCertificateDescriptor(
    string Subject,
    string Thumbprint,
    DateTime NotAfter);
