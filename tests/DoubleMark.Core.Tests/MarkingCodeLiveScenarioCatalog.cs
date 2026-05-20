using DoubleMark.Core.Parsing;

namespace DoubleMark.Core.Tests;

public enum BrokenCodeMutation
{
    None,
    RemoveAllGs,
    GsToSpace,
    TruncatedAi92,
    WrongGtinDigit,
    SerialEmbedded91,
    UnknownAi77,
    OnlyAi91,
    SwappedAiBlocks,
    ShortAi93TwoChars,
    DoubleGs,
    Empty,
    GarbagePrefix
}

/// <summary>
/// Synthetic ЧЗ-like payloads (not production marking codes) for integrity live tests.
/// </summary>
public static class MarkingCodeLiveScenarioCatalog
{
    public const char GS = (char)0x1D;

    public const string GtinA = "04620219556479";
    public const string GtinB = "04600439931256";
    public const string Serial13 = "0123456789ABC";
    public const string Key91 = "EE06";
    public const string Crypto92 = "dGVzdGNyeXB0b2hhc2hleGFtcGxlMTIzNDU2Nzg5MA==";
    public const string Ai93 = "hpUR";

    public static string FullOfficial =>
        $"01{GtinA}21{Serial13}{GS}91{Key91}{GS}92{Crypto92}";

    public static string ShortWith93 =>
        $"01{GtinA}21{Serial13}{GS}93{Ai93}";

    public static string FullTobacco =>
        $"01{GtinB}21SN12345{GS}91EE06{GS}92{Crypto92}";

    public static string ShortNoGs =>
        "0104620219556479215BZqLW93pSfJ";

    public static string FullShoesLongSerial =>
        $"010460000000000221ABCDEFGHIJKLMNOPQRST{GS}91A1B2{GS}92xY3kJ8mN2pQ7rT9uV1wZ4aB6cD0eF==";

    public static string Corrupt(string baseline, BrokenCodeMutation mutation) =>
        mutation switch
        {
            BrokenCodeMutation.None => baseline,
            BrokenCodeMutation.RemoveAllGs => baseline.Replace(GS.ToString(), ""),
            BrokenCodeMutation.GsToSpace => baseline.Replace(GS, ' '),
            BrokenCodeMutation.TruncatedAi92 => TruncateAfterAi92(baseline, 10),
            BrokenCodeMutation.WrongGtinDigit => ReplaceGtinCheckDigit(baseline),
            BrokenCodeMutation.SerialEmbedded91 => Embed91InsideSerial(baseline),
            BrokenCodeMutation.UnknownAi77 => InsertUnknownAi(baseline),
            BrokenCodeMutation.OnlyAi91 => CutAfterAi91(baseline),
            BrokenCodeMutation.SwappedAiBlocks => SwapAi91And92(baseline),
            BrokenCodeMutation.ShortAi93TwoChars => baseline.Replace($"93{Ai93}", "93ab"),
            BrokenCodeMutation.DoubleGs => baseline.Replace($"{GS}91", $"{GS}{GS}91"),
            BrokenCodeMutation.Empty => "",
            BrokenCodeMutation.GarbagePrefix => "???" + baseline,
            _ => baseline
        };

    private static string TruncateAfterAi92(string raw, int keepTail)
    {
        var marker = $"{GS}92";
        var markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
        if (markerIdx < 0)
            return raw[..Math.Max(0, raw.Length - 5)];

        var tailStart = markerIdx + marker.Length;
        var tail = raw[tailStart..];
        return raw[..tailStart] + (tail.Length <= keepTail ? tail : tail[..keepTail]);
    }

    private static string ReplaceGtinCheckDigit(string raw)
    {
        if (raw.Length < 16)
            return raw;
        var chars = raw.ToCharArray();
        var idx = 15;
        chars[idx] = chars[idx] == '9' ? '0' : '9';
        return new string(chars);
    }

    private static string InsertUnknownAi(string raw) => raw + $"{GS}77JUNK";

    private static string CutAfterAi91(string raw)
    {
        var i = raw.IndexOf($"{GS}92", StringComparison.Ordinal);
        return i < 0 ? raw : raw[..i];
    }

    private static string SwapAi91And92(string raw) =>
        raw.Replace($"{GS}91{Key91}{GS}92", $"{GS}92SHORT{GS}91{Key91}");

    private static string Embed91InsideSerial(string raw)
    {
        var gsIdx = raw.IndexOf(GS, 18);
        if (gsIdx <= 18)
            return raw;

        var serial = raw.Substring(18, gsIdx - 18);
        var corrupted = serial.Length >= 4
            ? serial.Insert(serial.Length / 2, "91")
            : serial + "91";
        return raw.Replace("21" + serial, "21" + corrupted);
    }
}
