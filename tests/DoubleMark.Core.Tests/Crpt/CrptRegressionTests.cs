using System.Reflection;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

/// <summary>
/// §14.3 — Regression guardrails after each CRPT implementation phase.
/// </summary>
public class CrptRegressionTests
{
    private const int MinimumTestCount = 132;

    private static readonly string[] ExpectedCrptTestFiles =
    [
        "CrptArchitectureTests.cs",
        "CrptAuthResponseParserTests.cs",
        "CrptAuthServiceTests.cs",
        "CrptCatalogViewModelTests.cs",
        "CrptCodeLifecycleTests.cs",
        "CrptDataModelsTests.cs",
        "CrptDocumentationLinksTests.cs",
        "CrptEnumTests.cs",
        "CrptGisMtClientTests.cs",
        "CrptGisMtServiceTests.cs",
        "CrptImplementationPhaseTests.cs",
        "CrptIntegrationReadinessTests.cs",
        "CrptManufacturerWorkflowTests.cs",
        "CrptMvpScopeTests.cs",
        "CrptNkProductMapperTests.cs",
        "CrptPrintQueueViewModelTests.cs",
        "CrptPrintServiceTests.cs",
        "CrptProductCatalogItemTests.cs",
        "CrptProductCatalogStoreTests.cs",
        "CrptRegressionTests.cs",
        "CrptRiskMitigationTests.cs",
        "CrptSecurityTests.cs",
        "CrptSettingsStoreTests.cs",
        "CrptSettingsViewModelTests.cs",
        "CrptSuzClientTests.cs",
        "CrptSuzOrderStatusMappingTests.cs",
        "CrptSuzRequestBuilderTests.cs",
        "CrptSuzServiceTests.cs",
        "CrptUtilisationBuilderTests.cs",
        "SuzOrderStatusTests.cs",
    ];

    [Fact]
    public void Section14_3_TestCount_IsAtLeast132()
    {
        var assembly = typeof(CrptRegressionTests).Assembly;
        var testMethods = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method =>
                method.GetCustomAttributes<FactAttribute>(inherit: true).Any()
                || method.GetCustomAttributes<TheoryAttribute>(inherit: true).Any())
            .ToList();

        testMethods.Count.Should().BeGreaterOrEqualTo(
            MinimumTestCount,
            because: "spec §14.3 requires 132+ automated tests after each CRPT phase");
    }

    [Fact]
    public void Section14_3_AllCrptTestFilesExist()
    {
        var crptTestDirectory = ResolveCrptTestDirectory();
        Directory.Exists(crptTestDirectory).Should().BeTrue();

        var missing = ExpectedCrptTestFiles
            .Where(file => !File.Exists(Path.Combine(crptTestDirectory, file)))
            .ToList();

        missing.Should().BeEmpty(
            because: "all §14 CRPT test files should remain present for regression coverage");
    }

    [Fact]
    public void Section14_3_TestProjectBuilds()
    {
        var assembly = typeof(CrptRegressionTests).Assembly;
        assembly.GetName().Name.Should().Be("DoubleMark.Core.Tests");
        assembly.GetReferencedAssemblies()
            .Select(r => r.Name)
            .Should()
            .Contain(new[] { "DoubleMark.Core", "DoubleMark.Desktop" });
    }

    [Fact]
    public void Section14_1_UnitTestAreas_AreCoveredByCrptTests()
    {
        var crptTestDirectory = ResolveCrptTestDirectory();
        var present = ExpectedCrptTestFiles
            .Where(file => File.Exists(Path.Combine(crptTestDirectory, file)))
            .ToHashSet(StringComparer.Ordinal);

        present.Should().Contain("CrptAuthResponseParserTests.cs", because: "§14.1 auth JSON parsing");
        present.Should().Contain("CrptNkProductMapperTests.cs", because: "§14.1 feed-product mapping");
        present.Should().Contain("CrptSuzRequestBuilderTests.cs", because: "§14.1 SUZ order body assembly");
        present.Should().Contain("CrptSuzOrderStatusMappingTests.cs", because: "§14.1 SUZ status → CrptSuzOrder");
    }

    private static string ResolveCrptTestDirectory()
    {
        var fromOutput = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..",
            "Crpt"));

        if (Directory.Exists(fromOutput))
            return fromOutput;

        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "tests", "DoubleMark.Core.Tests", "Crpt");
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
