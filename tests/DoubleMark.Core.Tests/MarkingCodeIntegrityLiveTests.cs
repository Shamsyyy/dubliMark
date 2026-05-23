using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DoubleMark.Core.Tests;

/// <summary>
/// Live-style matrix: 5 synthetic baselines × distinct damage modes (10+ scenarios).
/// </summary>
public class MarkingCodeIntegrityLiveTests
{
    private readonly Gs1Parser _parser = new();
    private readonly ITestOutputHelper _output;

    public MarkingCodeIntegrityLiveTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> ScenarioMatrix()
    {
        var baselines = new (string Name, Func<string> Build)[]
        {
            ("full_official", () => MarkingCodeLiveScenarioCatalog.FullOfficial),
            ("short_93", () => MarkingCodeLiveScenarioCatalog.ShortWith93),
            ("full_tobacco", () => MarkingCodeLiveScenarioCatalog.FullTobacco),
            ("short_no_gs", () => MarkingCodeLiveScenarioCatalog.ShortNoGs),
            ("full_shoes", () => MarkingCodeLiveScenarioCatalog.FullShoesLongSerial)
        };

        var cases = new (string Baseline, BrokenCodeMutation Mutation, bool ExpectValidParse, ParseErrorCode? Error,
            bool ExpectBrokenIntegrity, IntegrityIssueCode? Issue)[]
        {
            ("full_official", BrokenCodeMutation.None, true, null, false, null),
            // HID scanner strips GS → parser now recovers the full code positionally;
            // integrity still flags MissingGsInRaw as an Error → ShouldTreatAsBroken stays true.
            ("full_official", BrokenCodeMutation.RemoveAllGs, true, null, true,
                IntegrityIssueCode.MissingGsInRaw),
            // GS replaced with visible space → NOT the same as stripped GS; parser rejects it.
            ("full_official", BrokenCodeMutation.GsToSpace, false, ParseErrorCode.NoGsSeparator, true, null),
            ("full_official", BrokenCodeMutation.TruncatedAi92, true, null, true,
                IntegrityIssueCode.TruncatedCryptoTail),
            ("full_official", BrokenCodeMutation.WrongGtinDigit, true, null, false,
                IntegrityIssueCode.InvalidGtinCheckDigit),
            ("full_official", BrokenCodeMutation.OnlyAi91, false, ParseErrorCode.TruncatedPayload, true, null),
            ("full_official", BrokenCodeMutation.UnknownAi77, false, ParseErrorCode.UnknownAi, true, null),

            ("short_93", BrokenCodeMutation.None, true, null, false, null),
            ("short_93", BrokenCodeMutation.RemoveAllGs, true, null, false, null),
            ("short_93", BrokenCodeMutation.ShortAi93TwoChars, true, null, false, null),

            ("short_no_gs", BrokenCodeMutation.None, true, null, false, null),
            ("short_no_gs", BrokenCodeMutation.GarbagePrefix, true, null, false, null),

            ("full_tobacco", BrokenCodeMutation.SerialEmbedded91, true, null, false,
                IntegrityIssueCode.SerialContainsAiMarker),
            ("full_tobacco", BrokenCodeMutation.DoubleGs, false, ParseErrorCode.TruncatedPayload, true, null),

            ("full_shoes", BrokenCodeMutation.SwappedAiBlocks, true, null, true,
                IntegrityIssueCode.TruncatedCryptoTail),

            ("full_official", BrokenCodeMutation.Empty, false, ParseErrorCode.Empty, true,
                IntegrityIssueCode.EmptyPayload),
        };

        foreach (var c in cases)
        {
            var baseline = baselines.First(b => b.Name == c.Baseline);
            yield return new object[]
            {
                $"{c.Baseline}+{c.Mutation}",
                baseline.Build(),
                c.Mutation,
                c.ExpectValidParse,
                c.Error,
                c.ExpectBrokenIntegrity,
                c.Issue
            };
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioMatrix))]
    public void LiveScenario_parse_and_integrity(
        string scenarioId,
        string baseline,
        BrokenCodeMutation mutation,
        bool expectValidParse,
        ParseErrorCode? expectedError,
        bool expectBrokenIntegrity,
        IntegrityIssueCode? expectedIssue)
    {
        var raw = MarkingCodeLiveScenarioCatalog.Corrupt(baseline, mutation);
        var parse = _parser.Parse(raw);
        var issues = MarkingCodeIntegrity.Assess(raw, parse);
        var enriched = MarkingCodeIntegrity.Enrich(parse, raw);
        var broken = MarkingCodeIntegrity.ShouldTreatAsBroken(parse, issues);

        _output.WriteLine($"=== {scenarioId} ===");
        _output.WriteLine($"raw escaped: {MarkExportServiceEscape(raw)}");
        _output.WriteLine($"parse valid: {parse.IsValid} code={parse.ErrorCode} msg={parse.ErrorMessage}");
        foreach (var issue in issues)
            _output.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
        _output.WriteLine($"enriched warnings: {string.Join(" | ", enriched.InfoMessages)}");
        _output.WriteLine($"treatAsBroken: {broken}");

        parse.IsValid.Should().Be(expectValidParse, scenarioId);
        if (expectedError != null)
            parse.ErrorCode.Should().Be(expectedError, scenarioId);

        if (expectedIssue != null)
            issues.Should().Contain(i => i.Code == expectedIssue, scenarioId);

        if (expectBrokenIntegrity)
            broken.Should().BeTrue(scenarioId);
        else if (expectValidParse && mutation == BrokenCodeMutation.None && !expectBrokenIntegrity)
            broken.Should().BeFalse(scenarioId);
    }

    [Fact]
    public void OfficialGtin_passes_check_digit()
    {
        MarkingCodeIntegrity.IsValidGtinCheckDigit(MarkingCodeLiveScenarioCatalog.GtinA).Should().BeTrue();
    }

    [Fact]
    public void CorruptedGtin_fails_check_digit_when_parse_succeeds()
    {
        var raw = MarkingCodeLiveScenarioCatalog.Corrupt(
            MarkingCodeLiveScenarioCatalog.FullOfficial,
            BrokenCodeMutation.WrongGtinDigit);
        var parse = _parser.Parse(raw);
        parse.IsValid.Should().BeTrue();
        MarkingCodeIntegrity.IsValidGtinCheckDigit(parse.Code!.Gtin).Should().BeFalse();
    }

    [Fact]
    public void LiveScenario_count_is_at_least_ten()
    {
        ScenarioMatrix().Count().Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void ShortNoGs_hid_wedge_has_no_integrity_warnings()
    {
        var raw = MarkingCodeLiveScenarioCatalog.ShortNoGs;
        var parse = _parser.Parse(raw);
        parse.IsValid.Should().BeTrue();
        var enriched = MarkingCodeIntegrity.Enrich(parse, raw);
        enriched.InfoMessages.Should().BeEmpty();
    }

    private static string MarkExportServiceEscape(string raw) =>
        raw.Replace(((char)0x1D).ToString(), "[GS]", StringComparison.Ordinal);
}
