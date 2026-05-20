using DoubleMark.Core.History;
using FluentAssertions;

namespace DoubleMark.Core.Tests;

public class ExportScanHistoryImporterTests
{
    [Fact]
    public void TryParseExportJson_reads_export_fields()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dm-export-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, "20260520_120000_04600000000000_SN1.json");
        File.WriteAllText(jsonPath,
            """
            {
              "timestamp": "2026-05-20T12:00:00+03:00",
              "source": "HID",
              "rawPayload": "010460000000000021SN1",
              "normalizedPayload": "010460000000000021SN1",
              "gsCount": 0,
              "codeType": "Short",
              "gtin": "04600000000000",
              "serial": "SN1"
            }
            """);

        try
        {
            ExportScanHistoryImporter.TryParseExportJson(jsonPath, out var record).Should().BeTrue();
            record.Gtin.Should().Be("04600000000000");
            record.Serial.Should().Be("SN1");
            record.Source.Should().Be("HID");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Import_collects_json_from_export_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), "dm-export-tree-" + Guid.NewGuid().ToString("N"));
        var day = Path.Combine(root, "2026-05-20");
        Directory.CreateDirectory(day);
        File.WriteAllText(Path.Combine(day, "a.json"),
            """{"timestamp":"2026-05-20T10:00:00Z","source":"COM","rawPayload":"010460000000000021A","normalizedPayload":"010460000000000021A","gsCount":0,"codeType":"Short","gtin":"04600000000000","serial":"A"}""");
        File.WriteAllText(Path.Combine(day, "b.json"),
            """{"timestamp":"2026-05-20T11:00:00Z","source":"COM","rawPayload":"010460000000000021B","normalizedPayload":"010460000000000021B","gsCount":0,"codeType":"Short","gtin":"04600000000000","serial":"B"}""");

        try
        {
            var items = ExportScanHistoryImporter.Import(root, 10);
            items.Should().HaveCount(2);
            items[0].Serial.Should().Be("B");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
