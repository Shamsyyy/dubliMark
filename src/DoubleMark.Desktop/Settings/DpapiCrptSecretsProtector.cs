using System.Security.Cryptography;

namespace DoubleMark.Desktop.Settings;

public sealed class DpapiCrptSecretsProtector : ICrptSecretsProtector
{
    public byte[] Protect(byte[] plainBytes) =>
        ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedBytes) =>
        ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
}
