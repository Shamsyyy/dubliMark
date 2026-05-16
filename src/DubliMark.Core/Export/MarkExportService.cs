using System.Globalization;
using System.Text;
using System.Text.Json;
using DubliMark.Core.Models;
using DubliMark.Core.Parsing;

namespace DubliMark.Core.Export;

public sealed class MarkExportService
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IDataMatrixArtifactWriter _artifactWriter;

    public MarkExportService()
        : this(new DataMatrixArtifactWriter())
    {
    }

    public MarkExportService(IDataMatrixArtifactWriter artifactWriter)
    {
        _artifactWriter = artifactWriter;
    }

    public static string DefaultExportRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DubliMark", "exports");

    public static string DefaultDiagnosticsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DubliMark", "diagnostics");

    public MarkExportResult Save(MarkExportRequest request)
    {
        var timestamp = request.Timestamp ?? DateTimeOffset.Now;
        var normalized = NormalizePayload(request.RawPayload, request.ParseResult);

        try
        {
            if (!request.ParseResult.IsValid || request.ParseResult.Code == null)
                return SaveInvalidDiagnostic(request, timestamp, normalized);

            var code = request.ParseResult.Code;
            var root = string.IsNullOrWhiteSpace(request.ExportRoot)
                ? DefaultExportRoot
                : request.ExportRoot!;
            var exportDirectory = Path.Combine(root, timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(exportDirectory);

            var baseName = MakeUniqueBaseName(exportDirectory, timestamp, code);
            var files = new MarkExportFileSet
            {
                TextPath = Path.Combine(exportDirectory, baseName + ".txt"),
                JsonPath = Path.Combine(exportDirectory, baseName + ".json"),
                PngPath = Path.Combine(exportDirectory, baseName + ".png"),
                PdfPath = Path.Combine(exportDirectory, baseName + ".pdf")
            };

            File.WriteAllText(files.TextPath, BuildTextExport(request, timestamp, normalized, code), Encoding.UTF8);
            File.WriteAllText(files.JsonPath, BuildJsonExport(request, timestamp, normalized, code), Encoding.UTF8);

            _artifactWriter.WritePng(normalized, files.PngPath);
            _artifactWriter.WritePdf(normalized, BuildPdfInfo(request, timestamp, normalized, code), files.PdfPath);

            return new MarkExportResult
            {
                Success = true,
                ExportDirectory = exportDirectory,
                Files = files,
                NormalizedPayload = normalized
            };
        }
        catch (Exception ex)
        {
            return new MarkExportResult
            {
                Success = false,
                Error = ex.Message,
                NormalizedPayload = normalized
            };
        }
    }

    public static string EscapePayload(string payload) =>
        payload.Replace(Gs1BarcodeEncoding.GsChar.ToString(), "[GS]", StringComparison.Ordinal);

    private MarkExportResult SaveInvalidDiagnostic(
        MarkExportRequest request,
        DateTimeOffset timestamp,
        string normalized)
    {
        if (!request.SaveInvalidDiagnostics)
        {
            return new MarkExportResult
            {
                Success = false,
                Error = request.ParseResult.ErrorMessage,
                NormalizedPayload = normalized
            };
        }

        try
        {
            var diagnosticsRoot = string.IsNullOrWhiteSpace(request.DiagnosticsRoot)
                ? DefaultDiagnosticsRoot
                : request.DiagnosticsRoot!;
            Directory.CreateDirectory(diagnosticsRoot);
            var fileName = timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_invalid.txt";
            var path = Path.Combine(diagnosticsRoot, fileName);
            path = MakeUniquePath(path);

            var text = new StringBuilder()
                .AppendLine("DubliMark invalid scan diagnostic")
                .AppendLine("Timestamp: " + timestamp.ToString("O", CultureInfo.InvariantCulture))
                .AppendLine("Source: " + request.Source)
                .AppendLine("Error: " + (request.ParseResult.ErrorMessage ?? request.ParseResult.ErrorCode?.ToString() ?? "Unknown"))
                .AppendLine("Raw escaped: " + EscapePayload(request.RawPayload))
                .AppendLine("Normalized escaped: " + EscapePayload(normalized))
                .AppendLine("Raw hex: " + Gs1BarcodeEncoding.ToHex(request.RawPayload))
                .AppendLine("GS count: " + Gs1BarcodeEncoding.CountGs(normalized).ToString(CultureInfo.InvariantCulture))
                .ToString();

            File.WriteAllText(path, text, Encoding.UTF8);

            return new MarkExportResult
            {
                Success = false,
                Error = request.ParseResult.ErrorMessage,
                DiagnosticsFilePath = path,
                NormalizedPayload = normalized
            };
        }
        catch (Exception ex)
        {
            return new MarkExportResult
            {
                Success = false,
                Error = ex.Message,
                NormalizedPayload = normalized
            };
        }
    }

    private static string NormalizePayload(string raw, ParseResult result)
    {
        if (result.IsValid && result.Code?.RawData is { Length: > 0 } parsedRaw)
            return parsedRaw;

        var normalized = Gs1BarcodeEncoding.NormalizeForParse(raw);
        return normalized.FoundAi01 ? normalized.Payload : raw;
    }

    private static string MakeUniqueBaseName(string exportDirectory, DateTimeOffset timestamp, MarkingCode code)
    {
        var prefix = timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var serial = SanitizeFileNamePart(code.Serial);
        var baseName = $"{prefix}_{SanitizeFileNamePart(code.Gtin)}_{serial}";
        var candidate = baseName;
        var index = 1;

        while (File.Exists(Path.Combine(exportDirectory, candidate + ".json"))
               || File.Exists(Path.Combine(exportDirectory, candidate + ".txt"))
               || File.Exists(Path.Combine(exportDirectory, candidate + ".png"))
               || File.Exists(Path.Combine(exportDirectory, candidate + ".pdf")))
        {
            candidate = $"{baseName}_{index:000}";
            index++;
        }

        return candidate;
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (char.IsControl(ch) || invalid.Contains(ch))
                sb.Append('_');
            else
                sb.Append(ch);
        }

        var safe = sb.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safe))
            safe = "EMPTY";
        return safe.Length <= 80 ? safe : safe[..80];
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name}_{i:000}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static string BuildTextExport(
        MarkExportRequest request,
        DateTimeOffset timestamp,
        string normalized,
        MarkingCode code)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DubliMark local export");
        sb.AppendLine("Timestamp: " + timestamp.ToString("O", CultureInfo.InvariantCulture));
        sb.AppendLine("Source: " + request.Source);
        sb.AppendLine("Code type: " + code.CodeType);
        sb.AppendLine("GS count: " + Gs1BarcodeEncoding.CountGs(normalized).ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("AI 01 GTIN: " + code.Gtin);
        sb.AppendLine("AI 21 Serial: " + code.Serial);
        sb.AppendLine("AI 91: " + (code.VerificationKey ?? ""));
        sb.AppendLine("AI 92: " + (code.VerificationCode ?? ""));
        sb.AppendLine("AI 93: " + (code.AdditionalField93 ?? ""));
        sb.AppendLine("Raw escaped: " + EscapePayload(request.RawPayload));
        sb.AppendLine("Normalized escaped: " + EscapePayload(normalized));
        sb.AppendLine("Raw hex: " + Gs1BarcodeEncoding.ToHex(request.RawPayload));
        return sb.ToString();
    }

    private static string BuildJsonExport(
        MarkExportRequest request,
        DateTimeOffset timestamp,
        string normalized,
        MarkingCode code)
    {
        var hasAi21 = normalized.Length >= 18 && normalized.AsSpan(16, 2).SequenceEqual("21");
        var dto = new
        {
            timestamp = timestamp.ToString("O", CultureInfo.InvariantCulture),
            source = request.Source,
            rawPayload = request.RawPayload,
            normalizedPayload = normalized,
            escapedPayload = EscapePayload(normalized),
            rawTextEscaped = EscapePayload(request.RawPayload),
            normalizedTextEscaped = EscapePayload(normalized),
            rawHex = Gs1BarcodeEncoding.ToHex(request.RawPayload),
            gsCount = Gs1BarcodeEncoding.CountGs(normalized),
            hasAi01 = normalized.StartsWith("01", StringComparison.Ordinal),
            hasAi21,
            hasAi91 = code.VerificationKey != null,
            hasAi92 = code.VerificationCode != null,
            hasAi93 = code.AdditionalField93 != null,
            codeType = code.CodeType.ToString(),
            gtin = code.Gtin,
            serial = code.Serial,
            ai91 = code.VerificationKey,
            ai92 = code.VerificationCode,
            ai93 = code.AdditionalField93
        };

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static MarkExportPdfInfo BuildPdfInfo(
        MarkExportRequest request,
        DateTimeOffset timestamp,
        string normalized,
        MarkingCode code) =>
        new()
        {
            Timestamp = timestamp,
            Source = request.Source,
            CodeType = code.CodeType.ToString(),
            Gtin = code.Gtin,
            Serial = code.Serial,
            GsCount = Gs1BarcodeEncoding.CountGs(normalized),
            HasAi91 = code.VerificationKey != null,
            HasAi92 = code.VerificationCode != null,
            HasAi93 = code.AdditionalField93 != null,
            Ai92Length = code.VerificationCode?.Length ?? 0
        };
}
