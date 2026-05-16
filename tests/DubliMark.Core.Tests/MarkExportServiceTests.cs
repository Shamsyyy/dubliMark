using System.Text.Json;
using DubliMark.Core.Export;
using DubliMark.Core.Parsing;
using FluentAssertions;

public sealed class MarkExportServiceTests
{
    private const char GS = (char)0x1D;
    private readonly Gs1Parser _parser = new();

    [Fact]
    public void Save_FullCodeWithAi91Ai92_PreservesGsAndWritesRequiredFiles()
    {
        using var temp = new TempFolder();
        var raw = $"010460000000000221SERIAL01{GS}91KEY1{GS}92abc+def/ghi=jkl-mno_pqr.stuvwxyz0123456789==";
        var parsed = _parser.Parse(raw);
        var service = new MarkExportService(new CapturingArtifactWriter());

        var result = service.Save(new MarkExportRequest
        {
            RawPayload = raw,
            ParseResult = parsed,
            Source = "HID",
            ExportRoot = temp.ExportRoot,
            DiagnosticsRoot = temp.DiagnosticsRoot,
            Timestamp = new DateTimeOffset(2026, 5, 16, 14, 15, 16, TimeSpan.Zero)
        });

        result.Success.Should().BeTrue();
        result.Files.Should().NotBeNull();
        foreach (var path in result.Files!.All)
            File.Exists(path).Should().BeTrue(path);

        var json = ReadJson(result.Files.JsonPath);
        json.RootElement.GetProperty("source").GetString().Should().Be("HID");
        json.RootElement.GetProperty("gsCount").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("hasAi01").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("hasAi21").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("hasAi91").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("hasAi92").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("hasAi93").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("codeType").GetString().Should().Be("Full");
        json.RootElement.GetProperty("normalizedPayload").GetString().Should().Contain(GS.ToString());
        json.RootElement.GetProperty("normalizedTextEscaped").GetString().Should().Contain("[GS]");
        json.RootElement.GetProperty("rawHex").GetString().Should().Contain("1D");

        var text = File.ReadAllText(result.Files.TextPath);
        text.Should().Contain("[GS]");
        text.Should().Contain("AI 91: KEY1");
    }

    [Fact]
    public void Save_ShortCodeWithAi93_WritesShortFields()
    {
        using var temp = new TempFolder();
        var raw = $"0104620219556479215BZqLW{GS}93pSfJ";
        var parsed = _parser.Parse(raw);
        var service = new MarkExportService(new CapturingArtifactWriter());

        var result = service.Save(new MarkExportRequest
        {
            RawPayload = raw,
            ParseResult = parsed,
            Source = "Image",
            ExportRoot = temp.ExportRoot,
            DiagnosticsRoot = temp.DiagnosticsRoot,
            Timestamp = new DateTimeOffset(2026, 5, 16, 14, 20, 0, TimeSpan.Zero)
        });

        result.Success.Should().BeTrue();
        var json = ReadJson(result.Files!.JsonPath);
        json.RootElement.GetProperty("codeType").GetString().Should().Be("Short");
        json.RootElement.GetProperty("hasAi93").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("ai93").GetString().Should().Be("pSfJ");
        json.RootElement.GetProperty("ai91").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("ai92").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Save_DataMatrixWriterReceivesNormalizedPayload_NotEscapedText()
    {
        using var temp = new TempFolder();
        var raw = "010460000000000221SERIAL01" + (char)0xE8 + "91KEY1" + (char)0xE8 + "92CRYPTO";
        var parsed = _parser.Parse(raw);
        var writer = new CapturingArtifactWriter();
        var service = new MarkExportService(writer);

        var result = service.Save(new MarkExportRequest
        {
            RawPayload = raw,
            ParseResult = parsed,
            Source = "COM",
            ExportRoot = temp.ExportRoot,
            DiagnosticsRoot = temp.DiagnosticsRoot,
            Timestamp = new DateTimeOffset(2026, 5, 16, 14, 25, 0, TimeSpan.Zero)
        });

        result.Success.Should().BeTrue();
        writer.PngPayload.Should().Be(result.NormalizedPayload);
        writer.PdfPayload.Should().Be(result.NormalizedPayload);
        writer.PngPayload.Should().Contain(GS.ToString());
        writer.PngPayload.Should().NotContain("[GS]");
        writer.PngPayload.Should().NotContain(((char)0xE8).ToString());
    }

    [Fact]
    public void Save_Ai92LongTail_IsNotCutToFixedLength()
    {
        using var temp = new TempFolder();
        var crypto = "abc+def/ghi=jkl-mno_pqr.stuvwxyz0123456789==EXTRA-LONG-TAIL-1234567890";
        var raw = $"010460000000000221SERIAL01{GS}91KEY1{GS}92{crypto}";
        var parsed = _parser.Parse(raw);
        var service = new MarkExportService(new CapturingArtifactWriter());

        var result = service.Save(new MarkExportRequest
        {
            RawPayload = raw,
            ParseResult = parsed,
            Source = "HID",
            ExportRoot = temp.ExportRoot,
            DiagnosticsRoot = temp.DiagnosticsRoot,
            Timestamp = new DateTimeOffset(2026, 5, 16, 14, 30, 0, TimeSpan.Zero)
        });

        result.Success.Should().BeTrue();
        var json = ReadJson(result.Files!.JsonPath);
        json.RootElement.GetProperty("ai92").GetString().Should().Be(crypto);
    }

    [Fact]
    public void Save_InvalidCode_DoesNotCreateCompletedExport()
    {
        using var temp = new TempFolder();
        var raw = "010460000000000221SERIAL0191KEY192CRYPTO";
        var parsed = _parser.Parse(raw);
        var service = new MarkExportService(new CapturingArtifactWriter());

        var result = service.Save(new MarkExportRequest
        {
            RawPayload = raw,
            ParseResult = parsed,
            Source = "HID",
            ExportRoot = temp.ExportRoot,
            DiagnosticsRoot = temp.DiagnosticsRoot,
            Timestamp = new DateTimeOffset(2026, 5, 16, 14, 35, 0, TimeSpan.Zero)
        });

        result.Success.Should().BeFalse();
        Directory.Exists(Path.Combine(temp.ExportRoot, "2026-05-16")).Should().BeFalse();
        result.DiagnosticsFilePath.Should().NotBeNull();
        File.Exists(result.DiagnosticsFilePath!).Should().BeTrue();
        Directory.GetFiles(temp.ExportRoot, "*.json", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void Save_RealArtifacts_CreatesPngAndPdf()
    {
        using var temp = new TempFolder();
        var raw = $"010460000000000221SERIAL01{GS}91KEY1{GS}92CRYPTO";
        var parsed = _parser.Parse(raw);
        var service = new MarkExportService();

        var result = service.Save(new MarkExportRequest
        {
            RawPayload = raw,
            ParseResult = parsed,
            Source = "Manual",
            ExportRoot = temp.ExportRoot,
            DiagnosticsRoot = temp.DiagnosticsRoot,
            Timestamp = new DateTimeOffset(2026, 5, 16, 14, 40, 0, TimeSpan.Zero)
        });

        result.Success.Should().BeTrue(result.Error);
        File.ReadAllBytes(result.Files!.PngPath).Take(8)
            .Should().Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        File.ReadAllText(result.Files.PdfPath)[..4].Should().Be("%PDF");
    }

    private static JsonDocument ReadJson(string path) =>
        JsonDocument.Parse(File.ReadAllText(path));

    private sealed class CapturingArtifactWriter : IDataMatrixArtifactWriter
    {
        public string? PngPayload { get; private set; }
        public string? PdfPayload { get; private set; }

        public void WritePng(string payload, string path)
        {
            PngPayload = payload;
            File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10]);
        }

        public void WritePdf(string payload, MarkExportPdfInfo info, string path)
        {
            PdfPayload = payload;
            File.WriteAllText(path, "%PDF fake");
        }
    }

    private sealed class TempFolder : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "DubliMark.Tests", Guid.NewGuid().ToString("N"));

        public string ExportRoot => Path.Combine(_root, "exports");
        public string DiagnosticsRoot => Path.Combine(_root, "diagnostics");

        public TempFolder()
        {
            Directory.CreateDirectory(ExportRoot);
            Directory.CreateDirectory(DiagnosticsRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
