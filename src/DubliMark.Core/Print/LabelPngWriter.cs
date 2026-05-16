using System.IO.Compression;
using System.Text;
using DubliMark.Core.Export;

namespace DubliMark.Core.Print;

internal static class LabelPngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] EncodeRgb(int width, int height, byte[] rgb, int dpi)
    {
        using var stream = new MemoryStream();
        stream.Write(Signature);
        WriteChunk(stream, "IHDR", BuildIhdr(width, height));
        WriteChunk(stream, "pHYs", BuildPhys(dpi));
        WriteChunk(stream, "IDAT", BuildImageData(width, height, rgb));
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
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

    private static byte[] BuildPhys(int dpi)
    {
        var pixelsPerMeter = (uint)Math.Round(dpi / 0.0254);
        using var ms = new MemoryStream();
        WriteUInt32(ms, pixelsPerMeter);
        WriteUInt32(ms, pixelsPerMeter);
        ms.WriteByte(1);
        return ms.ToArray();
    }

    private static byte[] BuildImageData(int width, int height, byte[] rgb)
    {
        using var raw = new MemoryStream();
        var stride = width * 3;
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgb, y * stride, stride);
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
