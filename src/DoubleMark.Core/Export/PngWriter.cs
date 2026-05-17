using System.IO.Compression;
using System.Text;
using ZXing.Common;

namespace DoubleMark.Core.Export;

internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(BitMatrix matrix, string path)
    {
        using var stream = File.Create(path);
        stream.Write(Signature);
        WriteChunk(stream, "IHDR", BuildIhdr(matrix.Width, matrix.Height));
        WriteChunk(stream, "IDAT", BuildImageData(matrix));
        WriteChunk(stream, "IEND", []);
    }

    private static byte[] BuildIhdr(int width, int height)
    {
        using var ms = new MemoryStream();
        WriteUInt32(ms, (uint)width);
        WriteUInt32(ms, (uint)height);
        ms.WriteByte(8);
        ms.WriteByte(2);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        return ms.ToArray();
    }

    private static byte[] BuildImageData(BitMatrix matrix)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < matrix.Height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < matrix.Width; x++)
            {
                var value = matrix[x, y] ? (byte)0 : (byte)255;
                raw.WriteByte(value);
                raw.WriteByte(value);
                raw.WriteByte(value);
            }
        }

        using var compressed = new MemoryStream();
        raw.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            raw.CopyTo(zlib);

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        WriteUInt32(stream, (uint)data.Length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        WriteUInt32(stream, Crc32.Compute(crcInput));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
