using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoubleMark.Desktop.Services;

public enum PdfBatchJobKind
{
    Print,
    Verify
}

public sealed class PdfBatchPageRecordDto
{
    public int PageNumber { get; init; }
    public PdfBatchPageStatus Status { get; init; }
    public string? Reason { get; init; }
    public string? Gtin { get; init; }
    public string? Serial { get; init; }
}

public sealed class PdfBatchJobSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.Now;
    public PdfBatchJobKind Kind { get; set; }
    public string PdfPath { get; set; } = "";
    public string PdfFileName { get; set; } = "";
    public string? PageRange { get; set; }
    public int TotalPagesInFile { get; set; }
    public int SelectedPages { get; set; }
    public int PrintedCount { get; set; }
    public int ProblemCount { get; set; }
    public string? TemplateName { get; set; }
    public string? PrinterName { get; set; }
    public List<PdfBatchPageRecordDto> Pages { get; set; } = new();
}

public sealed class PdfBatchJobSummary
{
    public string Id { get; init; } = "";
    public DateTimeOffset CompletedAt { get; init; }
    public PdfBatchJobKind Kind { get; init; }
    public string PdfFileName { get; init; } = "";
    public string PdfPath { get; init; } = "";
    public int SelectedPages { get; init; }
    public int PrintedCount { get; init; }
    public int ProblemCount { get; init; }
}

public sealed class PdfBatchHistoryService
{
    private const int MaxJobs = 80;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string HistoryDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DoubleMark",
            "pdf-jobs");

    private static string IndexPath => Path.Combine(HistoryDirectory, "index.json");

    public IReadOnlyList<PdfBatchJobSummary> ListRecent(int max = 30)
    {
        try
        {
            if (!File.Exists(IndexPath))
                return Array.Empty<PdfBatchJobSummary>();

            var json = File.ReadAllText(IndexPath);
            var items = JsonSerializer.Deserialize<List<PdfBatchJobSummary>>(json, JsonOptions)
                        ?? new List<PdfBatchJobSummary>();
            return items
                .OrderByDescending(j => j.CompletedAt)
                .Take(Math.Max(1, max))
                .ToList();
        }
        catch (Exception ex)
        {
            LoggingService.Warn("PdfHistory", "List failed: " + ex.Message);
            return Array.Empty<PdfBatchJobSummary>();
        }
    }

    public PdfBatchJobSnapshot? Load(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return null;

        try
        {
            var path = JobFilePath(jobId);
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PdfBatchJobSnapshot>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            LoggingService.Warn("PdfHistory", $"Load {jobId} failed: " + ex.Message);
            return null;
        }
    }

    public void Save(PdfBatchJobSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(HistoryDirectory);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(JobFilePath(snapshot.Id), json);

            var index = ListRecent(MaxJobs + 1).ToList();
            index.RemoveAll(j => string.Equals(j.Id, snapshot.Id, StringComparison.Ordinal));
            index.Insert(0, ToSummary(snapshot));
            if (index.Count > MaxJobs)
            {
                foreach (var removed in index.Skip(MaxJobs))
                    TryDeleteJobFile(removed.Id);
                index = index.Take(MaxJobs).ToList();
            }

            File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, JsonOptions));
        }
        catch (Exception ex)
        {
            LoggingService.Error("PdfHistory", "Save failed", ex);
        }
    }

    public void UpdatePages(string jobId, IReadOnlyList<PdfBatchPageRecord> records)
    {
        var snapshot = Load(jobId);
        if (snapshot == null)
            return;

        snapshot.Pages = records.Select(ToDto).ToList();
        snapshot.PrintedCount = records.Count(r => r.Status == PdfBatchPageStatus.Printed);
        snapshot.ProblemCount = records.Count(r => r.IsProblem);
        Save(snapshot);
    }

    public static PdfBatchPageRecord FromDto(PdfBatchPageRecordDto dto) =>
        new()
        {
            PageNumber = dto.PageNumber,
            Status = dto.Status,
            Reason = dto.Reason,
            Gtin = dto.Gtin,
            Serial = dto.Serial
        };

    public static PdfBatchPageRecordDto ToDto(PdfBatchPageRecord record) =>
        new()
        {
            PageNumber = record.PageNumber,
            Status = record.Status,
            Reason = record.Reason,
            Gtin = record.Gtin,
            Serial = record.Serial
        };

    private static PdfBatchJobSummary ToSummary(PdfBatchJobSnapshot snapshot) =>
        new()
        {
            Id = snapshot.Id,
            CompletedAt = snapshot.CompletedAt,
            Kind = snapshot.Kind,
            PdfFileName = snapshot.PdfFileName,
            PdfPath = snapshot.PdfPath,
            SelectedPages = snapshot.SelectedPages,
            PrintedCount = snapshot.PrintedCount,
            ProblemCount = snapshot.ProblemCount
        };

    private static string JobFilePath(string jobId) =>
        Path.Combine(HistoryDirectory, jobId + ".json");

    private static void TryDeleteJobFile(string jobId)
    {
        try
        {
            var path = JobFilePath(jobId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore cleanup errors
        }
    }
}
