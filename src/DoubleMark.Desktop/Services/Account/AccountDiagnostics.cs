using System.Diagnostics;

namespace DoubleMark.Desktop.Services.Account;

public static class AccountDiagnostics
{
    [Conditional("DEBUG")]
    public static void Log(string message) =>
        Debug.WriteLine("[DoubleMark.Account] " + message);

    [Conditional("DEBUG")]
    public static void LogError(string area, Exception ex) =>
        Debug.WriteLine("[DoubleMark.Account] " + area + " error: " + ex.GetType().Name + ": " + ex.Message);
}
