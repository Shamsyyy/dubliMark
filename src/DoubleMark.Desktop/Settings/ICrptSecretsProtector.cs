namespace DoubleMark.Desktop.Settings;

public interface ICrptSecretsProtector
{
    byte[] Protect(byte[] plainBytes);

    byte[] Unprotect(byte[] protectedBytes);
}
