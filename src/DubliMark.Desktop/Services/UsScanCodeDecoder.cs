namespace DubliMark.Desktop.Services;

/// <summary>
/// Maps PS/2 make codes to US QWERTY characters (layout-independent fallback).
/// Does not handle shifted symbols beyond A-Z / 0-9 basics.
/// </summary>
internal static class UsScanCodeDecoder
{
  private static readonly Dictionary<ushort, char> Base = new()
  {
    [0x02] = '1', [0x03] = '2', [0x04] = '3', [0x05] = '4', [0x06] = '5',
    [0x07] = '6', [0x08] = '7', [0x09] = '8', [0x0A] = '9', [0x0B] = '0',
    [0x10] = 'q', [0x11] = 'w', [0x12] = 'e', [0x13] = 'r', [0x14] = 't',
    [0x15] = 'y', [0x16] = 'u', [0x17] = 'i', [0x18] = 'o', [0x19] = 'p',
    [0x1E] = 'a', [0x1F] = 's', [0x20] = 'd', [0x21] = 'f', [0x22] = 'g',
    [0x23] = 'h', [0x24] = 'j', [0x25] = 'k', [0x26] = 'l',
    [0x2C] = 'z', [0x2D] = 'x', [0x2E] = 'c', [0x2F] = 'v', [0x30] = 'b',
    [0x31] = 'n', [0x32] = 'm',
    [0x35] = ']', // US right bracket — often sent with Ctrl as GS substitute
    [0x37] = '*', [0x4A] = '-', [0x4B] = '+', [0x4C] = '\r',
    [0x4D] = '/', [0x4E] = '-', [0x4F] = '7', [0x50] = '8', [0x51] = '9',
    [0x52] = '0', [0x53] = '1', [0x54] = '2', [0x55] = '3', [0x56] = '4',
    [0x57] = '5', [0x58] = '6', [0x59] = '1', [0x5A] = '2', [0x5B] = '3',
    [0x5C] = '4', [0x5D] = '5', [0x5E] = '6', [0x5F] = '7', [0x60] = '8',
    [0x61] = '9', [0x62] = '0',
  };

  private static readonly Dictionary<ushort, char> Shifted = new()
  {
    [0x02] = '!', [0x03] = '@', [0x04] = '#', [0x05] = '$', [0x06] = '%',
    [0x07] = '^', [0x08] = '&', [0x09] = '*', [0x0A] = '(', [0x0B] = ')',
    [0x10] = 'Q', [0x11] = 'W', [0x12] = 'E', [0x13] = 'R', [0x14] = 'T',
    [0x15] = 'Y', [0x16] = 'U', [0x17] = 'I', [0x18] = 'O', [0x19] = 'P',
    [0x1E] = 'A', [0x1F] = 'S', [0x20] = 'D', [0x21] = 'F', [0x22] = 'G',
    [0x23] = 'H', [0x24] = 'J', [0x25] = 'K', [0x26] = 'L',
    [0x2C] = 'Z', [0x2D] = 'X', [0x2E] = 'C', [0x2F] = 'V', [0x30] = 'B',
    [0x31] = 'N', [0x32] = 'M',
    [0x35] = '}',
  };

  public static char? TryDecode(ushort makeCode, bool shift)
  {
    var table = shift ? Shifted : Base;
    return table.TryGetValue(makeCode, out var ch) ? ch : null;
  }
}
