namespace DoubleMark.Core.Parsing;

/// <summary>
/// Detects likely keyboard-distortion conflicts in HID scan results.
/// Moved out of the UI layer so it can be tested independently.
/// </summary>
public static class HidConflictDetector
{
    /// <summary>
    /// Returns true when <paramref name="current"/> and <paramref name="previous"/> are
    /// the same length and differ in exactly one character that looks like a keyboard
    /// variant (shifted digit, case difference). Used to detect HID wedge artefacts where
    /// the scanner presses Shift mid-scan.
    /// </summary>
    public static bool IsLikelySameCodeWithConflictingSerial(string current, string previous)
    {
        if (current.Length != previous.Length)
            return false;

        var differences = 0;
        for (var i = 0; i < current.Length; i++)
        {
            if (current[i] == previous[i])
                continue;

            differences++;
            if (differences > 1)
                return false;

            if (!LooksLikeKeyboardVariant(current[i], previous[i]))
                return false;
        }

        return differences == 1;
    }

    private static bool LooksLikeKeyboardVariant(char current, char previous)
    {
        if (ShiftedDigitToDigit(current) == previous || ShiftedDigitToDigit(previous) == current)
            return true;

        return char.ToUpperInvariant(current) == char.ToUpperInvariant(previous);
    }

    private static char? ShiftedDigitToDigit(char ch) => ch switch
    {
        ')' => '0',
        '!' => '1',
        '@' => '2',
        '#' => '3',
        '$' => '4',
        '%' => '5',
        '^' => '6',
        '&' => '7',
        '*' => '8',
        '(' => '9',
        _ => null
    };
}
