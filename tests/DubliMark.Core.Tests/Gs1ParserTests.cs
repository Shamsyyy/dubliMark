using FluentAssertions;
using Xunit;
using DubliMark.Core.Parsing;
using DubliMark.Core.Models;

public class Gs1ParserTests
{
    private const char GS = (char)0x1D;
    private readonly Gs1Parser _parser = new();

    // ───── Positive cases ─────

    [Fact]
    public void Parse_TobaccoCode_ShouldExtractAllFields()
    {
        var raw = $"010460043993125621SN12345{GS}91EE06{GS}92dGVzdGNyeXB0b2hhc2hleGFtcGxlMTIzNDU2Nzg5MA==";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.Gtin.Should().Be("04600439931256");
        result.Code.Serial.Should().Be("SN12345");
        result.Code.VerificationKey.Should().Be("EE06");
        result.Code.VerificationCode.Should().Be("dGVzdGNyeXB0b2hhc2hleGFtcGxlMTIzNDU2Nzg5MA==");
    }

    [Fact]
    public void Parse_ShoesCode_LongSerial_ShouldWork()
    {
        var raw = $"010460000000000221ABCDEFGHIJKLMNOPQRST{GS}91A1B2{GS}92xY3kJ8mN2pQ7rT9uV1wZ4aB6cD0eF==";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.Serial.Should().Be("ABCDEFGHIJKLMNOPQRST");
    }

    [Fact]
    public void Parse_CryptoWithBase64Chars_ShouldPreserveAll()
    {
        var crypto = "abc+def/ghi=jkl-mno_pqr.stuvwxyz0123456789==";
        var raw = $"010460000000000221ABC123{GS}91TEST{GS}92{crypto}";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.VerificationCode.Should().Be(crypto);
    }

    [Fact]
    public void Parse_WithoutCrypto_OnlyGtinAndSerial_ShouldWork()
    {
        var raw = $"010460000000000221SERIAL01{GS}";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.VerificationKey.Should().BeNull();
        result.Code.VerificationCode.Should().BeNull();
    }

    [Fact]
    public void Parse_WithAimIdentifier_ShouldStripPrefix()
    {
        var raw = $"]d2010460000000000221ABC{GS}91KEY1{GS}92CRYPTO";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.Gtin.Should().Be("04600000000002");
    }

    [Fact]
    public void Parse_ShouldStoreHexRepresentation()
    {
        var raw = $"010460000000000221A{GS}91K{GS}92C";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.RawDataHex.Should().Contain("1D");
    }

    [Theory]
    [InlineData("SN0001")]
    [InlineData("X-Y_Z.123")]
    [InlineData("!\"%&'()*+,-./")]
    public void Parse_SerialWithVariousChars_ShouldWork(string serial)
    {
        var raw = $"010460000000000221{serial}{GS}91A{GS}92B";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.Serial.Should().Be(serial);
    }

    // ───── Negative cases ─────

    [Fact]
    public void Parse_EmptyString_ShouldReturnEmptyError()
    {
        var result = _parser.Parse("");
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.Empty);
    }

    [Fact]
    public void Parse_NoGsSeparator_ShouldReportScannerIssue()
    {
        var raw = "010460000000000221ABC12391KEY92CRYPTO";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.NoGsSeparator);
        result.ErrorMessage.Should().Contain("сканер");
    }

    [Fact]
    public void Parse_NoGtinPrefix_ShouldFail()
    {
        var raw = $"990460000000000221ABC{GS}";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.NoGtin);
    }

    [Fact]
    public void Parse_ShortGtin_ShouldFail()
    {
        var raw = $"0112345{GS}";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.InvalidGtinLength);
    }

    [Fact]
    public void Parse_GtinWithLetters_ShouldFail()
    {
        var raw = $"01ABCD0000000002 21ABC{GS}".Replace(" ", "");
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.InvalidGtinLength);
    }

    [Fact]
    public void Parse_NoSerialAi_ShouldFail()
    {
        var raw = $"010460000000000299WRONG{GS}";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.NoSerial);
    }

    [Fact]
    public void Parse_UnknownAi_ShouldFail()
    {
        var raw = $"010460000000000221ABC{GS}77JUNK";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.UnknownAi);
    }

    [Fact]
    public void Parse_DoesNotThrow_OnGarbage()
    {
        var act = () => _parser.Parse("random garbage ");
        act.Should().NotThrow();
    }
}
