using System.Diagnostics;

namespace DoubleMark.Desktop.Services.Account;

public static class AccountDiagnostics
{
    public static void Log(string message) =>
        Debug.WriteLine("[DoubleMark.Account] " + message);

    public static void LogError(string area, Exception ex) =>
        Debug.WriteLine("[DoubleMark.Account] " + area + " error: " + ex.GetType().Name + ": " + ex.Message);
}
