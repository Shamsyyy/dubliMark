using DoubleMark.Core.Export;
using DoubleMark.Core.Parsing;
using FluentAssertions;

namespace DoubleMark.Core.Tests;

/// <summary>
/// Renders synthetic DataMatrix PNGs for manual QA (not production marking codes).
/// </summary>
public class MutationSampleImageGeneratorTests
{
    private static readonly (string Baseline, Func<string> Build)[] Baselines =
    [
        ("full_official", () => MarkingCodeLiveScenarioCatalog.FullOfficial),
        ("short_93", () => MarkingCodeLiveScenarioCatalog.ShortWith93),
        ("full_tobacco", () => MarkingCodeLiveScenarioCatalog.FullTobacco),
        ("short_no_gs", () => MarkingCodeLiveScenarioCatalog.ShortNoGs),
        ("full_shoes", () => MarkingCodeLiveScenarioCatalog.FullShoesLongSerial)
    ];

    private static readonly BrokenCodeMutation[] Mutations =
    [
        BrokenCodeMutation.None,
        BrokenCodeMutation.RemoveAllGs,
        BrokenCodeMutation.GsToSpace,
        BrokenCodeMutation.TruncatedAi92,
        BrokenCodeMutation.WrongGtinDigit,
        BrokenCodeMutation.SerialEmbedded91,
        BrokenCodeMutation.ShortAi93TwoChars,
        BrokenCodeMutation.GarbagePrefix
    ];

    [Fact]
    public void Generate_mutation_datamatrix_pngs()
    {
        var repoRoot = FindRepoRoot();
        var outDir = Path.Combine(repoRoot, "artifacts", "mutation-samples");
        Directory.CreateDirectory(outDir);

        var writer = new DataMatrixArtifactWriter();
        var parser = new Gs1Parser();
        var index = new List<string>();

        foreach (var (baselineName, build) in Baselines)
        {
            foreach (var mutation in Mutations)
            {
                var raw = MarkingCodeLiveScenarioCatalog.Corrupt(build(), mutation);
                if (string.IsNullOrEmpty(raw))
                    continue;

                var fileName = $"{baselineName}_{mutation}.png";
                var path = Path.Combine(outDir, fileName);
                writer.WritePng(raw, path);

                var parse = parser.Parse(raw);
                var issues = MarkingCodeIntegrity.Assess(raw, parse);
                var issueSummary = issues.Count == 0
                    ? "ok"
                    : string.Join("; ", issues.Select(i => i.Code));

                index.Add($"{fileName}\tvalid={parse.IsValid}\t{issueSummary}");
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "index.txt"), index);
        index.Count.Should().BeGreaterThan(0);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DoubleMark.sln"))
                || Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
