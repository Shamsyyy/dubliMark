using System.Reflection;
using DoubleMark.Core.Crpt;
using DoubleMark.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.Settings;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptArchitectureTests
{
    private static readonly string[] Section41ServiceComponents =
    [
        nameof(CrptSettingsStore),
        nameof(CrptAuthService),
        nameof(CrptNkService),
        nameof(CrptCatalogSyncService),
        nameof(CrptProductCatalogStore),
        nameof(CrptSuzService),
        nameof(CrptGisMtService),
        nameof(CrptTokenRefreshHostedService),
    ];

    private static readonly string[] Section41ServiceInterfaces =
    [
        nameof(ICrptSettingsStore),
        nameof(ICrptAuthService),
        nameof(ICrptNkService),
        nameof(ICrptCatalogSyncService),
        nameof(ICrptProductCatalogStore),
        nameof(ICrptSuzService),
        nameof(ICrptGisMtService),
        nameof(ICrptCertificateProvider),
    ];

    private static readonly string[] Section41LibClients =
    [
        nameof(CrptAuthClient),
        nameof(CrptSuzClient),
        nameof(CrptGisMtClient),
    ];

    private static readonly string[] ForbiddenViewCrptClientReferences =
    [
        nameof(CrptAuthClient),
        nameof(CrptSuzClient),
        nameof(CrptGisMtClient),
        "CrptHttp",
        "CrptNkClient",
    ];

    [Fact]
    public void Section41_AllServiceComponentsExist()
    {
        var serviceAssembly = typeof(CrptAuthService).Assembly;

        foreach (var component in Section41ServiceComponents)
        {
            var type = serviceAssembly.GetType($"DoubleMark.Desktop.Services.Crpt.{component}", throwOnError: false)
                ?? serviceAssembly.GetType($"DoubleMark.Desktop.Settings.{component}", throwOnError: false);

            type.Should().NotBeNull($"§4.1 service component {component} should exist");
        }
    }

    [Fact]
    public void Section41_AllServiceInterfacesExist()
    {
        var desktopAssembly = typeof(CrptAuthService).Assembly;
        var crptAssembly = typeof(CrptAuthClient).Assembly;

        foreach (var iface in Section41ServiceInterfaces)
        {
            if (iface == nameof(ICrptSettingsStore))
            {
                desktopAssembly.GetType($"DoubleMark.Desktop.Settings.{iface}", throwOnError: false)
                    .Should().NotBeNull($"interface {iface} should exist");
                continue;
            }

            if (iface == nameof(ICrptCertificateProvider))
            {
                crptAssembly.GetType($"DoubleMark.Crpt.{iface}", throwOnError: false)
                    .Should().NotBeNull($"interface {iface} should exist");
                continue;
            }

            desktopAssembly.GetType($"DoubleMark.Desktop.Services.Crpt.{iface}", throwOnError: false)
                .Should().NotBeNull($"interface {iface} should exist");
        }
    }

    [Fact]
    public void Section41_ExistingLibClientsArePresent()
    {
        var libAssembly = typeof(CrptAuthClient).Assembly;

        foreach (var client in Section41LibClients)
        {
            libAssembly.GetType($"DoubleMark.Crpt.{client}", throwOnError: false)
                .Should().NotBeNull($"§4.1 lib client {client} should exist");
        }
    }

    [Fact]
    public void Section5_NkClientAndMappersExist()
    {
        var libAssembly = typeof(CrptAuthClient).Assembly;

        foreach (var typeName in new[]
        {
            nameof(CrptNkClient),
            nameof(CrptNkProductMapper),
            nameof(CrptTrueApiProductClient),
            nameof(CrptAuthResponseParser),
            nameof(CrptSuzRequestBuilder),
        })
        {
            libAssembly.GetType($"DoubleMark.Crpt.{typeName}", throwOnError: false)
                .Should().NotBeNull($"§5 lib type {typeName} should exist");
        }
    }

    [Fact]
    public void Section5_CoreCrptModelsExist()
    {
        var coreAssembly = typeof(CrptProductCatalogItem).Assembly;

        typeof(CrptProductCatalogItem).Should().NotBeNull();
        typeof(CrptProductGroup).Should().NotBeNull();
        typeof(SuzOrderStatus).Should().NotBeNull();
        coreAssembly.GetType("DoubleMark.Core.Crpt.SuzOrderRemoteStatus", throwOnError: false)
            .Should().NotBeNull();
    }

    [Fact]
    public void Section5_DesktopCrptStructureExists()
    {
        var desktopAssembly = typeof(CrptAuthService).Assembly;
        var repoRoot = FindRepoRoot();
        var viewsDir = Path.Combine(repoRoot, "src", "DoubleMark.Desktop", "Views");

        desktopAssembly.GetType("DoubleMark.Desktop.Services.Crpt.CrptOrderRepository", throwOnError: false)
            .Should().NotBeNull();
        desktopAssembly.GetType("DoubleMark.Desktop.MainWindow", throwOnError: false)
            .Should().NotBeNull();

        File.Exists(Path.Combine(repoRoot, "src", "DoubleMark.Desktop", "MainWindow.Crpt.cs"))
            .Should().BeTrue();

        foreach (var view in new[] { "CrptSettingsView", "CrptCatalogView", "CrptOrdersView" })
        {
            File.Exists(Path.Combine(viewsDir, $"{view}.xaml")).Should().BeTrue();
            File.Exists(Path.Combine(viewsDir, $"{view}.xaml.cs")).Should().BeTrue();
        }
    }

    [Fact]
    public void Section5_NkServiceCreatesNkClient()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var provider = new ServiceCollection()
                .AddCrptServices(tempDirectory)
                .BuildServiceProvider();

            var nkService = provider.GetRequiredService<ICrptNkService>();
            nkService.Should().BeOfType<CrptNkService>();

            var client = ((CrptNkService)nkService).CreateNkClient();
            client.Should().NotBeNull();
            client.Dispose();
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void Section41_NkClientExistsInLib()
    {
        typeof(CrptNkClient).Should().NotBeNull();
        typeof(CrptNkService).Should().Implement<ICrptNkService>();
    }

    [Fact]
    public void DiRegistration_ResolvesKeyInterfaces()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "DoubleMark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var provider = new ServiceCollection()
                .AddCrptServices(tempDirectory)
                .BuildServiceProvider();

            provider.GetRequiredService<ICrptSettingsStore>().Should().BeOfType<CrptSettingsStore>();
            provider.GetRequiredService<ICrptAuthService>().Should().BeOfType<CrptAuthService>();
            provider.GetRequiredService<ICrptNkService>().Should().BeOfType<CrptNkService>();
            provider.GetRequiredService<ICrptCatalogSyncService>().Should().BeOfType<CrptCatalogSyncService>();
            provider.GetRequiredService<ICrptProductCatalogStore>().Should().BeOfType<CrptProductCatalogStore>();
            provider.GetRequiredService<ICrptSuzService>().Should().BeOfType<CrptSuzService>();
            provider.GetRequiredService<ICrptGisMtService>().Should().BeOfType<CrptGisMtService>();
            provider.GetRequiredService<ICrptCertificateProvider>().Should().BeOfType<StoreCrptCertificateProvider>();
            provider.GetRequiredService<IHostedService>().Should().BeOfType<CrptTokenRefreshHostedService>();
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test directory.
            }
        }
    }

    [Fact]
    public void ServiceLayer_DoesNotExposeCrptHttpClientsAsPublicApi()
    {
        var serviceTypes = typeof(CrptAuthService).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "DoubleMark.Desktop.Services.Crpt" && t.IsClass && t.IsPublic)
            .ToList();

        var libClientTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            typeof(CrptAuthClient).FullName!,
            typeof(CrptSuzClient).FullName!,
            typeof(CrptGisMtClient).FullName!,
        };

        foreach (var serviceType in serviceTypes)
        {
            var publicCrptClientMembers = serviceType
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m switch
                {
                    PropertyInfo p => p.PropertyType.FullName,
                    FieldInfo f => f.FieldType.FullName,
                    MethodInfo method => method.ReturnType.FullName,
                    _ => null,
                })
                .Where(name => name is not null && libClientTypes.Contains(name))
                .ToList();

            publicCrptClientMembers.Should().BeEmpty(
                because: $"{serviceType.Name} must not expose DoubleMark.Crpt HTTP clients in its public API");
        }
    }

    [Fact]
    public void Views_DoNotReferenceCrptHttpClientsDirectly()
    {
        var viewsDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DoubleMark.Desktop", "Views"));

        if (!Directory.Exists(viewsDirectory))
            viewsDirectory = Path.GetFullPath(Path.Combine(
                FindRepoRoot(),
                "src", "DoubleMark.Desktop", "Views"));

        Directory.Exists(viewsDirectory).Should().BeTrue("Views directory should exist for layer boundary check");

        var violations = Directory
            .EnumerateFiles(viewsDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, lineNumber: index + 1, line)))
            .Where(entry => ForbiddenViewCrptClientReferences.Any(token =>
                entry.line.Contains(token, StringComparison.Ordinal)))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.lineNumber}: {entry.line.Trim()}")
            .ToList();

        violations.Should().BeEmpty(
            because: "WPF views must consume CRPT through Desktop service interfaces, not HTTP clients");
    }

    [Fact]
    public void Section12_CrptLogRedactorExists()
    {
        typeof(CrptLogRedactor).Should().NotBeNull();
        typeof(CrptSecurityGuard).Should().NotBeNull();
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DoubleMark.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
