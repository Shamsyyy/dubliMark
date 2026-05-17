using System.IO;

namespace DoubleMark.Desktop.Services;

public static class AppDataMigrationService
{
    private const string CurrentAppName = "DoubleMark";
    private static readonly string LegacyAppName = "Dubli" + "Mark";

    public static void MigrateLegacyData()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var legacyDirectory = Path.Combine(appData, LegacyAppName);
        var currentDirectory = Path.Combine(appData, CurrentAppName);

        if (!Directory.Exists(legacyDirectory))
            return;

        Directory.CreateDirectory(currentDirectory);
        CopyDirectoryIfMissing(legacyDirectory, currentDirectory);
    }

    private static void CopyDirectoryIfMissing(string sourceDirectory, string targetDirectory)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));
            if (!File.Exists(targetFile))
                File.Copy(sourceFile, targetFile);
        }

        foreach (var sourceChild in Directory.EnumerateDirectories(sourceDirectory))
        {
            var targetChild = Path.Combine(targetDirectory, Path.GetFileName(sourceChild));
            Directory.CreateDirectory(targetChild);
            CopyDirectoryIfMissing(sourceChild, targetChild);
        }
    }
}
