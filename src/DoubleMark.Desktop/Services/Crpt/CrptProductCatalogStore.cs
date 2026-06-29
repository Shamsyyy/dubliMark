using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoubleMark.Core.Crpt;
using DoubleMark.Desktop.Settings;

namespace DoubleMark.Desktop.Services.Crpt;

/// <summary>
/// Persists the local NK product catalog as JSON (spec §6.2, B0.4).
/// </summary>
public sealed class CrptProductCatalogStore : ICrptProductCatalogStore
{
    private readonly string _catalogPath;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public CrptProductCatalogStore(string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        _catalogPath = catalogPath;
    }

    public CrptProductCatalogStore(CrptSettings settings)
        : this(settings.EffectiveProductCatalogPath)
    {
    }

    public string CatalogPath => _catalogPath;

    public IReadOnlyList<CrptProductCatalogItem> Load()
    {
        if (!File.Exists(_catalogPath))
            return [];

        try
        {
            var json = File.ReadAllText(_catalogPath);
            var document = JsonSerializer.Deserialize<CatalogDocument>(json, JsonOptions);
            return document?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<CrptProductCatalogItem> items)
    {
        var directory = Path.GetDirectoryName(_catalogPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var document = new CatalogDocument { Items = items.ToList() };
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(_catalogPath, json);
    }

    public IReadOnlyList<CrptProductCatalogItem> List() => Load();

    public IReadOnlyList<CrptProductCatalogItem> Filter(Func<CrptProductCatalogItem, bool> predicate) =>
        Load().Where(predicate).ToList();

    public IReadOnlyList<CrptProductCatalogItem> GetOrderableItems() =>
        Filter(item => item.CanOrderCodes);

    private sealed class CatalogDocument
    {
        public List<CrptProductCatalogItem> Items { get; set; } = [];
    }
}
