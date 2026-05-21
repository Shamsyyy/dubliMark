using DoubleMark.Core.Models;
using DoubleMark.Core.Parsing;
using Xunit;

namespace DoubleMark.Core.Tests;

public sealed class CloudScanHistoryHelpersTests
{
    [Fact]
    public void ComputeCodeHash_IsStableLowerHex()
    {
        var hash1 = ComputeCodeHash("test-payload");
        var hash2 = ComputeCodeHash("test-payload");
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public void IsValidForCloudHistory_RequiresFullMarkingCode()
    {
        var shortCode = new MarkingCode
        {
            CodeType = MarkingCodeType.Short,
            Gtin = "04601234567890",
            Serial = "ABC",
            RawData = "",
            RawDataHex = ""
        };
        var shortResult = new ParseResult { IsValid = true, Code = shortCode };
        Assert.False(IsValidForCloudHistory(shortResult));

        var fullCode = new MarkingCode
        {
            CodeType = MarkingCodeType.Full,
            Gtin = "04601234567890",
            Serial = "ABC",
            VerificationKey = "key",
            VerificationCode = "code",
            RawData = "",
            RawDataHex = ""
        };
        var fullResult = new ParseResult { IsValid = true, Code = fullCode };
        Assert.True(IsValidForCloudHistory(fullResult));
    }

    [Fact]
    public void BuildMaskedPreview_DoesNotExposeFullPayload()
    {
        var payload = new string('A', 40);
        var masked = BuildMaskedPreview(payload);
        Assert.Contains("***", masked);
        Assert.True(masked.Length < payload.Length);
    }

    private static string ComputeCodeHash(string rawPayload)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawPayload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsValidForCloudHistory(ParseResult result) =>
        result.IsValid
        && result.Code != null
        && result.Code.CodeType == MarkingCodeType.Full
        && !string.IsNullOrWhiteSpace(result.Code.Gtin)
        && !string.IsNullOrWhiteSpace(result.Code.Serial)
        && result.Code.VerificationKey != null
        && result.Code.VerificationCode != null;

    private static string BuildMaskedPreview(string rawPayload, int maxVisiblePrefix = 16)
    {
        if (string.IsNullOrEmpty(rawPayload))
            return "—";

        var normalized = Gs1BarcodeEncoding.NormalizeForParse(rawPayload).Payload;
        if (normalized.Length == 0)
            return "—";

        var visible = normalized.Length <= maxVisiblePrefix
            ? normalized
            : normalized[..maxVisiblePrefix];

        return visible + " ***";
    }
}
