using DoubleMark.Core.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.ViewModels.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptPrintServiceTests
{
    private const char Gs = (char)0x1D;

    [Fact]
    public void RenderLabel_PreservesGsCount()
    {
        var service = new CrptPrintService();
        var code = CreateCode($"010000000000000021SYNTH{Gs}91EE12{Gs}92SYNTHETICPAYLOAD=");

        var render = service.RenderLabel(code, CrptPrintDefaults.MinimalTemplate);

        render.GsCount.Should().Be(2);
        render.RawPayload.Should().Be(code.RawPayload);
    }

    [Fact]
    public void RenderBatch_ReturnsOneResultPerCode()
    {
        var service = new CrptPrintService();
        var codes = new[]
        {
            CreateCode($"010000000000000021SYN001{Gs}91EE12{Gs}92SYNTHETICPAYLOAD001="),
            CreateCode($"010000000000000021SYN002{Gs}91EE12{Gs}92SYNTHETICPAYLOAD002="),
        };

        var renders = service.RenderBatch(codes, CrptPrintDefaults.MinimalTemplate);

        renders.Should().HaveCount(2);
        renders.Should().OnlyContain(r => r.PngBytes.Length > 0);
    }

    [Fact]
    public void MarkCodePrinted_DoesNotMutateRawPayload()
    {
        var code = CreateCode("010000000000000021SYNTH", CrptCodeLifecycleStatus.Received);
        var printed = CrptPrintQueueViewModel.MarkCodePrinted(code, DateTimeOffset.UtcNow);
        printed.RawPayload.Should().Be(code.RawPayload);
    }

    [Fact]
    public void SelectUtilisationCandidates_IncludesOnlyPrinted()
    {
        var codes = new[]
        {
            CreateCode("010000000000000021A", CrptCodeLifecycleStatus.Received),
            CreateCode("010000000000000021B", CrptCodeLifecycleStatus.Printed),
            CreateCode("010000000000000021C", CrptCodeLifecycleStatus.UtilisationSent),
        };

        var candidates = CrptPrintQueueViewModel.SelectUtilisationCandidates(codes);

        candidates.Should().ContainSingle();
        candidates[0].RawPayload.Should().EndWith("B");
    }

    private static CrptMarkingCodeItem CreateCode(
        string payload,
        CrptCodeLifecycleStatus status = CrptCodeLifecycleStatus.Received) =>
        new(1, "local-order", payload, status, null, null);
}

internal static class CrptPrintDefaults
{
    public static DoubleMark.Core.Print.PrintTemplate MinimalTemplate { get; } = new()
    {
        Name = "CRPT test minimal",
        LabelWidthMm = 58,
        LabelHeightMm = 40,
        DataMatrixWidthMm = 24,
        DataMatrixHeightMm = 24,
        DataMatrixXmm = 2,
        DataMatrixYmm = 8,
        MarginMm = 1,
        RotationDegrees = 0,
        DefaultCopies = 1,
    };
}
