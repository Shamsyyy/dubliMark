using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DoubleMark.Crpt;

/// <summary>Signing via CryptoPro CAdESCOM (GOST UKEP).</summary>
public static class CryptoProCadesSigner
{
    private const int CadesBes = 1;
    private const int EncodeBase64 = 0;
    private const int ContentEncodingBase64 = 1;

    public static string SignAttachedBase64(string data, X509Certificate2 certificate) =>
        SignCadesBase64(data, certificate, detached: false);

    /// <summary>Detached CMS signature for True API auth/key challenge (spec §8.1).</summary>
    public static string SignDetachedBase64(string data, X509Certificate2 certificate) =>
        SignCadesBase64(data, certificate, detached: true);

    private static string SignCadesBase64(string data, X509Certificate2 certificate, bool detached)
    {
        CrptRiskMitigations.EnsureWindowsForCryptoPro();

        dynamic signedData = CreateSignedDataObject();
        signedData.Content = data;

        dynamic signer = CreateSigner(certificate);
        var signature = (string)signedData.SignCades(signer, CadesBes, detached, EncodeBase64);
        return NormalizeBase64(signature);
    }

    /// <summary>Detached signature for SUZ order body (JSON pre-encoded as base64 content).</summary>
    public static string SignDetachedOrderBodyBase64(string compactJson, X509Certificate2 certificate)
    {
        CrptRiskMitigations.EnsureWindowsForCryptoPro();

        var base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(compactJson));

        dynamic signedData = CreateSignedDataObject();
        signedData.ContentEncoding = ContentEncodingBase64;
        signedData.Content = base64Payload;

        dynamic signer = CreateSigner(certificate);
        var signature = (string)signedData.SignCades(signer, CadesBes, true, EncodeBase64);
        return NormalizeBase64(signature);
    }

    private static dynamic CreateSignedDataObject() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("CAdESCOM.CadesSignedData")!)
        ?? throw new InvalidOperationException("CAdESCOM.CadesSignedData is not available");

    private static dynamic CreateSigner(X509Certificate2 certificate)
    {
        dynamic signer = Activator.CreateInstance(Type.GetTypeFromProgID("CAdESCOM.CPSigner")!)
            ?? throw new InvalidOperationException("CAdESCOM.CPSigner is not available");

        dynamic comCert = FindComCertificate(certificate)
            ?? throw new InvalidOperationException($"Certificate not found in CryptoPro store: {certificate.Thumbprint}");

        signer.Certificate = comCert;
        return signer;
    }

    private static dynamic? FindComCertificate(X509Certificate2 certificate)
    {
        const int currentUserStore = 2;
        const string myStore = "My";
        const int openReadOnly = 0;

        dynamic? store = null;
        try
        {
            store = Activator.CreateInstance(Type.GetTypeFromProgID("CAdESCOM.Store")!);
            store!.Open(currentUserStore, myStore, openReadOnly);

            foreach (dynamic cert in store.Certificates)
            {
                var serial = ((string)cert.SerialNumber).Replace(" ", "", StringComparison.Ordinal);
                var target = certificate.SerialNumber.Replace(" ", "", StringComparison.Ordinal);
                if (serial.Equals(target, StringComparison.OrdinalIgnoreCase))
                    return cert;
            }

            foreach (dynamic cert in store.Certificates)
            {
                var thumb = ((string)cert.Thumbprint).Replace(" ", "", StringComparison.Ordinal);
                if (thumb.Equals(certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                    return cert;
            }
        }
        finally
        {
            if (store is not null)
            {
                try { store.Close(); } catch { /* ignore */ }
                Marshal.ReleaseComObject(store);
            }
        }

        return null;
    }

    private static string NormalizeBase64(string value) =>
        value.Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal);
}
