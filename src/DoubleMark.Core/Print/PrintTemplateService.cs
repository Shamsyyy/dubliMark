using System.Text.Json;

namespace DoubleMark.Core.Print;

public sealed class PrintTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _templatesPath;

    public PrintTemplateService(string? templatesPath = null)
    {
        _templatesPath = string.IsNullOrWhiteSpace(templatesPath)
            ? DefaultTemplatesPath
            : templatesPath!;
    }

    public static string DefaultTemplatesPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DoubleMark", "templates.json");

    public IReadOnlyList<PrintTemplate> LoadOrCreateDefaults()
    {
        var templates = LoadTemplates();
        if (templates.Count > 0)
            return templates;

        templates = CreateDefaultTemplates();
        SaveTemplates(templates);
        return templates;
    }

    public IReadOnlyList<PrintTemplate> LoadTemplates()
    {
        try
        {
            if (!File.Exists(_templatesPath))
                return Array.Empty<PrintTemplate>();

            var json = File.ReadAllText(_templatesPath);
            var set = JsonSerializer.Deserialize<PrintTemplateSet>(json, JsonOptions);
            return set?.Templates?
                .Where(IsUsable)
                .Select(TemplateLayoutHelper.ClampDataMatrixInLabel)
                .ToList() ?? new List<PrintTemplate>();
        }
        catch
        {
            return Array.Empty<PrintTemplate>();
        }
    }

    public void SaveTemplates(IEnumerable<PrintTemplate> templates)
    {
        var directory = Path.GetDirectoryName(_templatesPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var set = new PrintTemplateSet
        {
            Templates = templates.Where(IsUsable).ToList()
        };
        File.WriteAllText(_templatesPath, JsonSerializer.Serialize(set, JsonOptions));
    }

    public PrintTemplate ResolveTemplate(string? templateName)
    {
        var templates = LoadOrCreateDefaults();
        var selected = templates.FirstOrDefault(t =>
            string.Equals(t.Name, templateName, StringComparison.OrdinalIgnoreCase));
        return selected ?? templates[0];
    }

    public static List<PrintTemplate> CreateDefaultTemplates() =>
        new()
        {
            new PrintTemplate
            {
                Name = "ЧЗ 30x20 мм",
                LabelWidthMm = 30,
                LabelHeightMm = 20,
                DataMatrixWidthMm = 14,
                DataMatrixHeightMm = 14,
                DataMatrixXmm = 2,
                DataMatrixYmm = 3,
                MarginMm = 1,
                RotationDegrees = 0,
                DefaultCopies = 1,
                TextBlocks =
                {
                    new PrintTextBlock { Text = "GTIN {gtin}", Xmm = 17, Ymm = 4, FontSizePt = 4.5 },
                    new PrintTextBlock { Text = "SN {serial}", Xmm = 17, Ymm = 8, FontSizePt = 4.5 },
                    new PrintTextBlock { Text = "{codeType}", Xmm = 17, Ymm = 12, FontSizePt = 4.5, Bold = true },
                    new PrintTextBlock { Text = "{date} {time}", Xmm = 2, Ymm = 18, FontSizePt = 4 }
                }
            },
            new PrintTemplate
            {
                Name = "ЧЗ 40x30 мм",
                LabelWidthMm = 40,
                LabelHeightMm = 30,
                DataMatrixWidthMm = 18,
                DataMatrixHeightMm = 18,
                DataMatrixXmm = 2,
                DataMatrixYmm = 3,
                MarginMm = 1,
                RotationDegrees = 0,
                DefaultCopies = 1,
                TextBlocks =
                {
                    new PrintTextBlock { Text = "GTIN {gtin}", Xmm = 22, Ymm = 5, FontSizePt = 5 },
                    new PrintTextBlock { Text = "SN {serial}", Xmm = 22, Ymm = 10, FontSizePt = 5 },
                    new PrintTextBlock { Text = "{codeType}", Xmm = 22, Ymm = 15, FontSizePt = 5, Bold = true },
                    new PrintTextBlock { Text = "{source}", Xmm = 22, Ymm = 20, FontSizePt = 5 },
                    new PrintTextBlock { Text = "{date} {time}", Xmm = 2, Ymm = 27, FontSizePt = 4.5 }
                }
            }
        };

    public static bool IsUsable(PrintTemplate template) =>
        !string.IsNullOrWhiteSpace(template.Name)
        && template.LabelWidthMm > 0
        && template.LabelHeightMm > 0
        && template.DataMatrixWidthMm > 0
        && template.DataMatrixHeightMm > 0
        && template.DefaultCopies > 0
        && template.RotationDegrees is 0 or 90 or 180 or 270;

    public static string CreateUniqueName(IEnumerable<PrintTemplate> templates, string baseName) =>
        TemplateLayoutHelper.CreateUniqueName(templates, baseName);
}
