using DoubleMark.Core.Crpt;
using DoubleMark.Desktop.Services.Crpt;
using DoubleMark.Desktop.ViewModels.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptPrintQueueViewModelTests
{
    [Fact]
    public void CanMarkCodePrinted_AllowsReceivedAndQueuedForPrint()
    {
        var received = CreateCode(CrptCodeLifecycleStatus.Received);
        var queued = CreateCode(CrptCodeLifecycleStatus.QueuedForPrint);
        var printed = CreateCode(CrptCodeLifecycleStatus.Printed);

        CrptPrintQueueViewModel.CanMarkCodePrinted(received).Should().BeTrue();
        CrptPrintQueueViewModel.CanMarkCodePrinted(queued).Should().BeTrue();
        CrptPrintQueueViewModel.CanMarkCodePrinted(printed).Should().BeFalse();
    }

    [Fact]
    public void MarkCodePrinted_SetsStatusAndTimestamp()
    {
        var code = CreateCode(CrptCodeLifecycleStatus.Received);
        var printedAt = new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

        var updated = CrptPrintQueueViewModel.MarkCodePrinted(code, printedAt);

        updated.Status.Should().Be(CrptCodeLifecycleStatus.Printed);
        updated.PrintedAt.Should().Be(printedAt);
        updated.RawPayload.Should().Be(code.RawPayload);
    }

    [Fact]
    public void CanSubmitUtilisation_RequiresPrintedCodes()
    {
        var codes = new[]
        {
            CreateCode(CrptCodeLifecycleStatus.Received),
            CreateCode(CrptCodeLifecycleStatus.Printed),
        };

        CrptPrintQueueViewModel.CanSubmitUtilisation(codes).Should().BeTrue();
        CrptPrintQueueViewModel.CanSubmitUtilisation([CreateCode(CrptCodeLifecycleStatus.Received)])
            .Should().BeFalse();
    }

    private static CrptMarkingCodeItem CreateCode(CrptCodeLifecycleStatus status) =>
        new(1, "local-order", "010000000000000021SYNTH", status, null, null);
}
