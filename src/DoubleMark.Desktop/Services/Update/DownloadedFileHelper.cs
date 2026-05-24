using System.Runtime.InteropServices;

namespace DoubleMark.Desktop.Services.Update;

internal static class DownloadedFileHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFile(string lpFileName);

    /// <summary>
    /// Removes Mark-of-the-Web (Zone.Identifier) so Windows treats the file as local, not downloaded.
    /// Does not replace Authenticode; unsigned installers may still trigger SmartScreen.
    /// </summary>
    public static void TryClearInternetZoneMark(string filePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            DeleteFile(filePath + ":Zone.Identifier");
        }
        catch
        {
            // best effort
        }
    }
}
