using FluentAssertions;
using Xunit;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Models;

public class Gs1ParserTests
{
    private const char GS = (char)0x1D;
    private readonly Gs1Parser _parser = new();

    // ───── Official ЧЗ structure (infographic) ─────

    private const string OfficialGtin = "04620219556479";
    private const string OfficialSerial13 = "0123456789ABC"; // 13 chars
    private const string OfficialKey91 = "EE06";             // 4 chars
    private const string OfficialCode92 =
        "dGVzdGNyeXB0b2hhc2hleGFtcGxlMTIzNDU2Nzg5MA==";     // 44 chars
    private const string OfficialCode93 = "hpUR";            // 4 chars (short)

    [Fact]
    public void Parse_OfficialFullStructure_AllFourBlocks()
    {
        var raw = $"01{OfficialGtin}21{OfficialSerial13}{GS}91{OfficialKey91}{GS}92{OfficialCode92}";
        var result = _parser.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.InfoMessages.Should().BeEmpty();
        result.Code!.CodeType.Should().Be(MarkingCodeType.Full);
        result.Code.Gtin.Should().Be(OfficialGtin);
        result.Code.Serial.Should().HaveLength(13);
        result.Code.Serial.Should().Be(OfficialSerial13);
        result.Code.VerificationKey.Should().Be(OfficialKey91);
        result.Code.VerificationKey.Should().HaveLength(4);
        result.Code.VerificationCode.Should().Be(OfficialCode92);
        result.Code.VerificationCode.Should().HaveLength(44);
        result.Code.AdditionalField93.Should().BeNull();
    }

    [Fact]
    public void Parse_OfficialShortStructure_GtinSerial93()
    {
        var raw = $"01{OfficialGtin}21{OfficialSerial13}{GS}93{OfficialCode93}";
        var result = _parser.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.InfoMessages.Should().BeEmpty();
        result.Code!.CodeType.Should().Be(MarkingCodeType.Short);
        result.Code.Gtin.Should().Be(OfficialGtin);
        result.Code.Serial.Should().Be(OfficialSerial13);
        result.Code.AdditionalField93.Should().Be(OfficialCode93);
        result.Code.AdditionalField93.Should().HaveLength(4);
        result.Code.VerificationKey.Should().BeNull();
        result.Code.VerificationCode.Should().BeNull();
    }

    [Fact]
    public void Parse_SerialNot13Chars_ShouldSucceedWithoutWarning()
    {
        var shortSerial = "SN12345";
        var raw = $"010460043993125621{shortSerial}{GS}91EE06{GS}92dGVzdGNyeXB0b2hhc2hleGFtcGxlMTIzNDU2Nzg5MA==";
        var result = _parser.Parse(raw);

        result.IsValid.Should().BeTrue();
        result.Code!.Serial.Should().Be(shortSerial);
        result.InfoMessages.Should().BeEmpty();
    }

    // ───── Full codes (AI 91/92) ─────

    [Fact]
    public void Parse_TobaccoCode_ShouldExtractAllFields()
    {
        var raw = $"010460043993125621SN12345{GS}91EE06{GS}92dGVzdGNyeXB0b2hhc2hleGFtcGxlMTIzNDU2Nzg5MA==";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.CodeType.Should().Be(MarkingCodeType.Full);
        result.Code.Gtin.Should().Be("04600439931256");
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
        result.Code.CodeType.Should().Be(MarkingCodeType.Full);
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
    public void Parse_WithoutCrypto_OnlyGtinAndSerial_ShouldBeShort()
    {
        var raw = $"010460000000000221SERIAL01{GS}";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.CodeType.Should().Be(MarkingCodeType.Short);
        result.Code.VerificationKey.Should().BeNull();
        result.Code.VerificationCode.Should().BeNull();
    }

    [Fact]
    public void Parse_WithAimIdentifier_ShouldStripPrefix()
    {
        var raw = $"]d2010460000000000221ABC{GS}91KEY1{GS}92CRYPTO";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.Gtin.Should().Be("04600000000002");
        result.Code.CodeType.Should().Be(MarkingCodeType.Full);
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

    // ───── Short codes (~31 bytes, AI 93) ─────

    [Fact]
    public void Parse_ShortCzWithAi93_ShouldSucceedAsShort()
    {
        var raw = $"0104620219556479215??>PY{GS}93hpUR";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.CodeType.Should().Be(MarkingCodeType.Short);
        result.Code.Gtin.Should().Be("04620219556479");
        result.Code.Serial.Should().Be("5??>PY");
        result.Code.AdditionalField93.Should().Be("hpUR");
        result.Code.VerificationKey.Should().BeNull();
        result.Code.VerificationCode.Should().BeNull();
        result.Code.RawData.Length.Should().Be(31);
    }

    [Theory]
    [InlineData("0104620219556479215??>PY", "5??>PY", "04620219556479")]
    [InlineData("0104620219556479215DzI<r", "5DzI<r", "04620219556479")]
    [InlineData("0104620219555861215zCXG", "5zCXG", "04620219555861")]
    public void Parse_ShortCzExamples_WithAi93_ShouldSucceed(string prefix, string serial, string gtin)
    {
        var raw = $"{prefix}{GS}93hpUR";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.CodeType.Should().Be(MarkingCodeType.Short);
        result.Code.Gtin.Should().Be(gtin);
        result.Code.Serial.Should().Be(serial);
        result.Code.AdditionalField93.Should().Be("hpUR");
    }

    [Fact]
    public void Parse_ShortCzWithoutGs_Ai93Concatenated_ShouldSucceed()
    {
        var raw = "0104620219556479215BZqLW93pSfJ";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.CodeType.Should().Be(MarkingCodeType.Short);
        result.Code.Gtin.Should().Be("04620219556479");
        result.Code.Serial.Should().Be("5BZqLW");
        result.Code.AdditionalField93.Should().Be("pSfJ");
        result.InfoMessages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShortCzWithoutGs_WithShiftedDigitSymbolInSerial_ShouldNotWarnByItself()
    {
        var raw = "0104602547000886215qaei(93Uzm3";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.Gtin.Should().Be("04602547000886");
        result.Code.Serial.Should().Be("5qaei(");
        result.Code.AdditionalField93.Should().Be("Uzm3");
        result.InfoMessages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShortCzWithoutGs_WithDigitInSerial_ShouldStayGreen()
    {
        var raw = "0104602547000886215qaei993Uzm3";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.Serial.Should().Be("5qaei9");
        result.Code.AdditionalField93.Should().Be("Uzm3");
        result.InfoMessages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShortCzWithoutGs_WithThreeCharAi93_ShouldNotWarnByItself()
    {
        var raw = "0104607041131753215mhIhabfoM9xG93FTb";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeTrue();
        result.Code!.Serial.Should().Be("5mhIhabfoM9xG");
        result.Code.AdditionalField93.Should().Be("FTb");
        result.InfoMessages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_FullCzWith91And92_AfterNormalization_ShouldSucceed()
    {
        var raw = $"0104628219556479215BZqLW{GS}91EE06{GS}92dGVzdGNyeXB0b2hhc2hleGFtcGxlMTIzNDU2Nzg5MA==";
        var norm = Gs1BarcodeEncoding.NormalizeForParse(raw);
        norm.FoundAi01.Should().BeTrue();

        var result = _parser.Parse(norm.Payload);
        result.IsValid.Should().BeTrue();
        result.Code!.Gtin.Should().Be("04628219556479");
        result.Code.CodeType.Should().Be(MarkingCodeType.Full);
        result.Code.VerificationKey.Should().Be("EE06");
        result.Code.VerificationCode.Should().NotBeNullOrEmpty();
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
    public void Parse_NoGsSeparator_FullCodeWithoutGs_ShouldReportScannerIssue()
    {
        var raw = "010460000000000221ABC12391KEY92CRYPTO";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.NoGsSeparator);
        result.ErrorMessage.Should().Contain("сканер");
    }

    [Fact]
    public void Parse_TruncatedFullCzWithoutGs_ShouldSuggestScannerConfig()
    {
        var raw = "010460000000000221ABCDEFGHIJKLMNOPQRSTUVWXYZ91KEY";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.NoGsSeparator);
        result.ErrorMessage.Should().Contain("обрезан");
        result.ErrorMessage.Should().Contain("GS");
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
    public void Parse_UnknownAi_OnFullCodePath_ShouldFail()
    {
        var raw = $"010460000000000221ABC{GS}91KEY{GS}77JUNK";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.UnknownAi);
    }

    [Fact]
    public void Parse_PartialFull_Only91_ShouldReportTruncated()
    {
        var raw = $"010460000000000221ABC{GS}91KEYONLY";
        var result = _parser.Parse(raw);
        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be(ParseErrorCode.TruncatedPayload);
    }

    [Fact]
    public void Parse_DoesNotThrow_OnGarbage()
    {
        var act = () => _parser.Parse($"random garbage \u001Dmore junk");
        act.Should().NotThrow();
    }
}
