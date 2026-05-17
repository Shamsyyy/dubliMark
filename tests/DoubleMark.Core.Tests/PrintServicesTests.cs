using System.Text;
using System.Text.Json;
using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;
using FluentAssertions;

public sealed class PrintServicesTests
{
    private const char GS = (char)0x1D;
    private readonly Gs1Parser _parser = new();
    private readonly PrintTemplate _template = PrintTemplateService.CreateDefaultTemplates()[0];

    [Fact]
    public void Render_FullCode_PreservesRealGsAndFullAi92()
    {
        var ai92 = "abc+def/ghi=jkl-mno_pqr.stuvwxyz0123456789==EXTRA";
        var raw = $"010460000000000221SERIAL01{GS}91KEY1{GS}92{ai92}";
        var render = Render(raw, "HID");

        render.NormalizedPayload.Should().Contain(GS.ToString());
        render.NormalizedPayloadEscaped.Should().Contain("[GS]");
        render.RawHex.Should().Contain("1D");
        render.GsCount.Should().Be(2);
        render.HasAi91.Should().BeTrue();
        render.HasAi92.Should().BeTrue();
        render.Ai92.Should().Be(ai92);
        render.CodeType.Should().Be(MarkingCodeType.Full.ToString());
    }

    [Fact]
    public void Render_ShortCodeWithAi93_IsShort()
    {
        var raw = $"0104620219556479215BZqLW{GS}93pSfJ";
        var render = Render(raw, "Image");

        render.CodeType.Should().Be(MarkingCodeType.Short.ToString());
        render.HasAi93.Should().BeTrue();
        render.Ai93.Should().Be("pSfJ");
        render.HasAi91.Should().BeFalse();
        render.HasAi92.Should().BeFalse();
    }

    [Fact]
    public void Render_PdfPageSize_MatchesThirtyByTwentyMillimeters()
    {
        var render = Render($"010460000000000221SERIAL01{GS}91KEY1{GS}92CRYPTO", "COM");
        var pdf = Encoding.ASCII.GetString(render.PdfBytes);

        pdf.Should().Contain("/MediaBox [0 0 85.039 56.693]");
        pdf.Should().NotContain("595 842");
    }

    [Fact]
    public void Render_PngSize_MatchesTemplateAtDpi()
    {
        var render = Render($"010460000000000221SERIAL01{GS}91KEY1{GS}92CRYPTO", "COM");

        ReadPngSize(render.PngBytes).Should().Be((354, 236));
        render.PngWidthPx.Should().Be(354);
        render.PngHeightPx.Should().Be(236);
    }

    [Fact]
    public async Task Pipeline_AutoprintFalse_DoesNotCallPrinter()
    {
        using var temp = new TempFolder();
        var printer = new FakePrintService();
        var pipeline = CreatePipeline(printer);
        var raw = $"010460000000000221SERIAL01{GS}91KEY1{GS}92CRYPTO";

        var result = await pipeline.ProcessAsync(BuildPipelineRequest(raw, temp, autoPrint: false));

        result.Rendered.Should().BeTrue();
        result.Printed.Should().BeFalse();
        printer.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Pipeline_AutoprintTrue_PrintsSuccessfulScan()
    {
        using var temp = new TempFolder();
        var printer = new FakePrintService();
        var pipeline = CreatePipeline(printer);
        var raw = $"010460000000000221SERIAL01{GS}91KEY1{GS}92CRYPTO";

        var result = await pipeline.ProcessAsync(BuildPipelineRequest(raw, temp, autoPrint: true));

        result.Printed.Should().BeTrue();
        printer.Calls.Should().Be(1);
        printer.LastPayload.Should().Contain(GS.ToString());
        result.Export!.Success.Should().BeTrue();
        var json = JsonDocument.Parse(File.ReadAllText(result.Export.Files!.JsonPath));
        json.RootElement.GetProperty("printed").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("normalizedPayloadEscaped").GetString().Should().Contain("[GS]");
    }

    [Fact]
    public async Task Pipeline_InvalidScan_DoesNotPrint()
    {
        using var temp = new TempFolder();
        var printer = new FakePrintService();
        var pipeline = CreatePipeline(printer);
        var raw = "010460000000000221SERIAL0191KEY92CRYPTO";

        var result = await pipeline.ProcessAsync(BuildPipelineRequest(raw, temp, autoPrint: true));

        result.Rendered.Should().BeFalse();
        result.Printed.Should().BeFalse();
        printer.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Pipeline_DuplicateProtection_BlocksRecentRepeat()
    {
        using var temp = new TempFolder();
        var now = new DateTimeOffset(2026, 5, 16, 15, 0, 0, TimeSpan.Zero);
        var printer = new FakePrintService();
        var pipeline = new PrintPipelineService(
            new MarkRenderService(),
            new PrintExportService(),
            printer,
            () => now);
        var raw = $"010460000000000221SERIAL01{GS}91KEY1{GS}92CRYPTO";

        var first = await pipeline.ProcessAsync(BuildPipelineRequest(raw, temp, autoPrint: true));
        var second = await pipeline.ProcessAsync(BuildPipelineRequest(raw, temp, autoPrint: true));

        first.Printed.Should().BeTrue();
        second.BlockedDuplicate.Should().BeTrue();
        second.Printed.Should().BeFalse();
        printer.Calls.Should().Be(1);
    }

    [Fact]
    public void Templates_SaveLoad_AndSubstituteVariables()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Root, "templates.json");
        var service = new PrintTemplateService(path);
        var templates = PrintTemplateService.CreateDefaultTemplates();

        service.SaveTemplates(templates);
        var loaded = service.LoadTemplates();

        loaded.Should().HaveCount(2);
        loaded[0].Name.Should().Be("ЧЗ 30x20 мм");

        var code = new MarkingCode
        {
            Gtin = "04600000000002",
            Serial = "SERIAL01",
            VerificationKey = "KEY1",
            VerificationCode = "1234567890ABCDEFGHIJ",
            CodeType = MarkingCodeType.Full,
            RawData = "",
            RawDataHex = ""
        };

        var text = MarkRenderService.SubstituteText(
            "GTIN {gtin} SN {serial} {ai91} {ai92_short} {codeType} {source}",
            code,
            new DateTimeOffset(2026, 5, 16, 15, 0, 0, TimeSpan.Zero),
            "HID");

        text.Should().Contain("04600000000002");
        text.Should().Contain("SERIAL01");
        text.Should().Contain("KEY1");
        text.Should().Contain("123456789...");
        text.Should().Contain("Full");
        text.Should().Contain("HID");
    }

    private MarkRenderResult Render(string raw, string source)
    {
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue(result.ErrorMessage);
        return new MarkRenderService().Render(new MarkRenderRequest
        {
            RawPayload = raw,
            ParseResult = result,
            Template = _template,
            Source = source,
            Timestamp = new DateTimeOffset(2026, 5, 16, 15, 0, 0, TimeSpan.Zero)
        });
    }

    private PrintPipelineService CreatePipeline(FakePrintService printer) =>
        new(new MarkRenderService(), new PrintExportService(), printer,
            () => new DateTimeOffset(2026, 5, 16, 15, 0, 0, TimeSpan.Zero));

    private PrintPipelineRequest BuildPipelineRequest(string raw, TempFolder temp, bool autoPrint)
    {
        var parsed = _parser.Parse(raw);
        return new PrintPipelineRequest
        {
            RawPayload = raw,
            ParseResult = parsed,
            Source = "HID",
            Template = _template,
            Settings = new PrintPipelineSettings
            {
                AutoPrintEnabled = autoPrint,
                PrintRoot = temp.PrintRoot,
                SaveFileBeforePrint = true,
                DuplicateProtectionSeconds = 5,
                Copies = 1
            }
        };
    }

    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        var width = ReadBigEndianInt(png, 16);
        var height = ReadBigEndianInt(png, 20);
        return (width, height);
    }

    private static int ReadBigEndianInt(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private sealed class FakePrintService : IMarkPrintService
    {
        public int Calls { get; private set; }
        public string? LastPayload { get; private set; }

        public Task<PrintJobResult> PrintAsync(PrintJobRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPayload = request.Render.NormalizedPayload;
            return Task.FromResult(new PrintJobResult { Success = true });
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "DoubleMark.PrintTests", Guid.NewGuid().ToString("N"));
        public string PrintRoot => Path.Combine(Root, "prints");

        public TempFolder()
        {
            Directory.CreateDirectory(PrintRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
