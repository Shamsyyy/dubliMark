using System.Text.Json;
using DoubleMark.Core.Crpt;

namespace DoubleMark.Crpt;

public static class CrptSuzResponseParser
{
    public static string ParseCreateOrderId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var orderId = ReadString(root, "orderId");
        if (string.IsNullOrWhiteSpace(orderId))
            throw new InvalidOperationException($"Create order response missing orderId: {json}");

        return orderId;
    }

    public static CrptSuzOrderStatus ParseOrderStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var status = SuzOrderStatus.ParseFromElement(root);
        var errorMessage = ReadString(root, "errorMessage")
            ?? ReadString(root, "message")
            ?? ReadString(root, "error");
        return new CrptSuzOrderStatus(status, errorMessage, json);
    }

    public static CrptSuzCodesBlock ParseCodesBlock(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("codes", out var codesEl) || codesEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("SUZ codes response missing codes array");

        var codes = codesEl.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();

        var blockId = ReadString(root, "blockId");
        var isLast = ReadBool(root, "isLast") ?? true;
        return new CrptSuzCodesBlock(codes, blockId, isLast);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
