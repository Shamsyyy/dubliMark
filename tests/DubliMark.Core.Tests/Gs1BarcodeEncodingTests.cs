using System.Text;
using DubliMark.Core.Models;
using FluentAssertions;
using DubliMark.Core.Parsing;

public class Gs1BarcodeEncodingTests
{
    private const char GS = (char)0x1D;
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
    private readonly Gs1Parser _parser = new();

    [Fact]
    public void BytesToRawString_WithGsSeparators_ParsesSuccessfully()
    {
        var payload = $"010460043993125621SN12345{GS}91EE06{GS}92dGVzdGNyeXB0b2hhc2g=";
        var bytes = Latin1.GetBytes(payload);

        var raw = Gs1BarcodeEncoding.BytesToRawString(bytes);
        raw.Should().Contain(GS.ToString());
        Gs1BarcodeEncoding.CountGs(raw).Should().Be(2);

        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.Gtin.Should().Be("04600439931256");
        result.Code.Serial.Should().Be("SN12345");
        result.Code.VerificationKey.Should().Be("EE06");
    }

    [Fact]
    public void BytesToRawString_NormalizesMisencodedE8ToGs()
    {
        var bytes = Latin1.GetBytes("010460000000000221ABC");
        bytes = bytes.Concat(new byte[] { 0xE8 }).Concat(Latin1.GetBytes("91KEY")).ToArray();
        bytes = bytes.Concat(new byte[] { 0xE8 }).Concat(Latin1.GetBytes("92CRYPTO")).ToArray();

        var raw = Gs1BarcodeEncoding.BytesToRawString(bytes);
        Gs1BarcodeEncoding.CountGs(raw).Should().Be(2);

        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.VerificationKey.Should().Be("KEY");
    }

    [Fact]
    public void ToHex_IncludesGsByte()
    {
        var raw = $"01AB{GS}CD";
        var hex = Gs1BarcodeEncoding.ToHex(raw);
        hex.Should().Contain("1D");
    }

    [Fact]
    public void NormalizeForParse_LeadingGsThenAi01_ParsesSuccessfully()
    {
        var payload = $"010460043993125621SN12345{GS}91EE06{GS}92dGVzdA==";
        var bytes = new byte[] { 0x1D }.Concat(Latin1.GetBytes(payload)).ToArray();

        var norm = Gs1BarcodeEncoding.NormalizeForParse(bytes);
        norm.FoundAi01.Should().BeTrue();
        norm.StrippedPrefixBytes.Should().Be(1);
        norm.Payload.Should().StartWith("01");

        var result = _parser.Parse(norm.Payload);
        result.IsValid.Should().BeTrue();
        result.Code!.Gtin.Should().Be("04600439931256");
    }

    [Fact]
    public void NormalizeForParse_ZxingGarbagePreamble_FindsAi01()
    {
        var payload = $"010460000000000221SERIAL01{GS}91KEY{GS}92CRYPTO";
        var garbage = new byte[] { 0x1D, 0x83, 0x86, 0xC0, 0x84 };
        var bytes = garbage.Concat(Latin1.GetBytes(payload)).ToArray();

        var norm = Gs1BarcodeEncoding.NormalizeForParse(bytes);
        norm.FoundAi01.Should().BeTrue();
        norm.StrippedPrefixBytes.Should().Be(garbage.Length);
        norm.Ai01Offset.Should().Be(garbage.Length);

        var result = _parser.Parse(norm.Payload);
        result.IsValid.Should().BeTrue();
        result.Code!.Serial.Should().Be("SERIAL01");
    }

    [Fact]
    public void NormalizeForParse_ZxingMisreadWithoutAi01_NotFound()
    {
        var bytes = new byte[] { 0x1D, 0x83, 0x86, 0xC0, 0x84, 0xC0, 0x84 };
        Gs1BarcodeEncoding.ContainsAi01Pattern(bytes).Should().BeFalse();

        var norm = Gs1BarcodeEncoding.NormalizeForParse(bytes);
        norm.FoundAi01.Should().BeFalse();
    }

    [Fact]
    public void ScoreGs1Payload_PrefersFullCzOverShortButShortStillScoresWell()
    {
        var full = $"010460000000000221SERIAL01{GS}91KEY{GS}92" + new string('A', 44);
        var shortCode = $"010460000000000221SERIAL01{GS}93hpUR";

        Gs1BarcodeEncoding.ScoreGs1Payload(full)
            .Should().BeGreaterThan(Gs1BarcodeEncoding.ScoreGs1Payload(shortCode));
        Gs1BarcodeEncoding.LooksLikeShortMarkingCode(shortCode).Should().BeTrue();
        Gs1BarcodeEncoding.LooksLikeShortMarkingCode(full).Should().BeFalse();
    }

    [Fact]
    public void NormalizeForParse_DoesNotReplaceE8InsideCrypto()
    {
        var cryptoWithE8 = "ab" + (char)0xE8 + "cd";
        var raw = $"010460000000000221ABC{GS}91KEY{GS}92{cryptoWithE8}";
        var bytes = Latin1.GetBytes(raw);

        var norm = Gs1BarcodeEncoding.NormalizeForParse(bytes);
        norm.Payload.Should().Contain(((char)0xE8).ToString());
    }

    [Fact]
    public void BuildBarcodePayload_WithoutGs_ReinsertsSeparators()
    {
        var raw = "0104620219556479215BZqLW93pSfJ";
        var parsed = _parser.Parse(raw);
        parsed.IsValid.Should().BeTrue();

        var payload = Gs1BarcodeEncoding.BuildBarcodePayload(parsed.Code!);
        payload.Should().Be($"0104620219556479215BZqLW{GS}93pSfJ");
        Gs1BarcodeEncoding.CountGs(payload).Should().Be(1);
    }

    [Fact]
    public void BuildBarcodePayload_WithGs_KeepsOriginalBytes()
    {
        var raw = $"0104620219556479215BZqLW{GS}93pSfJ";
        var parsed = _parser.Parse(raw);
        Gs1BarcodeEncoding.BuildBarcodePayload(parsed.Code!).Should().Be(raw);
    }
}
