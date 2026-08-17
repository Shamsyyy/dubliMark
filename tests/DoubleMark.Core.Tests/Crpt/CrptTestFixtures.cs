namespace DoubleMark.Core.Tests.Crpt;

/// <summary>
/// Synthetic CRPT test data only (spec §12.2). Never use real marking codes.
/// </summary>
public static class CrptTestFixtures
{
    public const string SyntheticGtin = "04600000000000";
    public const string SyntheticPayload = "010460000000000021SYNTH";

    private const char Gs = (char)0x1D;

    public static string SyntheticMarkingCode(int index) =>
        $"010460000000000021SYN{index:D3}{Gs}91EE12{Gs}92SYNTHETICPAYLOAD{index:D3}=";

    public static string SyntheticJwt(int index = 1) =>
        $"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJzeW50aGV0aWMteyJ9.signature-part-{index:D3}";
}
