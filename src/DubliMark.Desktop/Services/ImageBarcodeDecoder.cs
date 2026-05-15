using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DubliMark.Core.Parsing;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace DubliMark.Desktop.Services;

public static class ImageBarcodeDecoder
{
    private const string Gs1DecodeFailedMessage =
        "Декодер не смог прочитать GS1 DataMatrix. Попробуйте: крупный кадр только квадрат кода, без бликов.";

    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    public static bool TryDecodeFromFile(string path, out ImageDecodeResult? result, out string? error)
    {
        result = null;
        error = null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return TryDecodeFromBitmap(bitmap, out result, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryDecodeFromBitmap(BitmapSource bitmap, out ImageDecodeResult? result, out string? error)
    {
        result = null;
        error = null;

        try
        {
            var candidates = new List<DecodeCandidate>();
            var seenRaw = new HashSet<string>(StringComparer.Ordinal);
            var hadAnyBarcode = false;
            var hadAi01InBytes = false;

            foreach (var variant in BuildBitmapVariants(bitmap))
            {
                foreach (var zxingResult in DecodeAllResults(variant))
                {
                    hadAnyBarcode = true;
                    foreach (var (rawBytes, rawString) in CollectPayloadsFromResult(zxingResult))
                    {
                        if (rawBytes is { Length: > 0 } && Gs1BarcodeEncoding.ContainsAi01Pattern(rawBytes))
                            hadAi01InBytes = true;

                        var norm = rawBytes is { Length: > 0 }
                            ? Gs1BarcodeEncoding.NormalizeForParse(rawBytes)
                            : Gs1BarcodeEncoding.NormalizeForParse(rawString ?? string.Empty);

                        var raw = norm.Payload;
                        if (string.IsNullOrEmpty(raw) || !seenRaw.Add(raw))
                            continue;

                        var byteLen = norm.Bytes.Length > 0 ? norm.Bytes.Length : Latin1.GetByteCount(raw);
                        var gs = Gs1BarcodeEncoding.CountGs(raw);
                        var hex = Gs1BarcodeEncoding.ToHex(raw);
                        var score = Gs1BarcodeEncoding.ScoreGs1Payload(raw);
                        candidates.Add(new DecodeCandidate(raw, hex, gs, score, byteLen, norm));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                error = hadAnyBarcode && !hadAi01InBytes
                    ? Gs1DecodeFailedMessage
                    : "Код на изображении не найден.";
                return false;
            }

            var best = candidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.ByteLength)
                .ThenByDescending(c => c.Gs)
                .ThenByDescending(c => c.Raw.Length)
                .First();

            if (string.IsNullOrEmpty(best.Raw) ||
                (!best.Norm.FoundAi01 && !best.Raw.StartsWith("01", StringComparison.Ordinal)))
            {
                error = Gs1DecodeFailedMessage;
                return false;
            }

            result = new ImageDecodeResult
            {
                Raw = best.Raw,
                RawHex = best.Hex,
                GsCount = best.Gs,
                PayloadByteLength = best.ByteLength,
                PreambleStrippedBytes = best.Norm.StrippedPrefixBytes,
                Ai01Offset = best.Norm.Ai01Offset,
                NormalizeNote = BuildNormalizeNote(best.Norm)
            };
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed record DecodeCandidate(
        string Raw,
        string Hex,
        int Gs,
        int Score,
        int ByteLength,
        Gs1NormalizeResult Norm);

    private static string? BuildNormalizeNote(Gs1NormalizeResult norm)
    {
        if (norm.StrippedPrefixBytes <= 0 && norm.Ai01Offset <= 0)
            return null;

        var parts = new List<string>();
        if (norm.StrippedPrefixBytes > 0)
            parts.Add($"Обрезан префикс {norm.StrippedPrefixBytes} байт");
        if (norm.Ai01Offset > 0)
            parts.Add($"AI 01 найден со смещения {norm.Ai01Offset}");
        return string.Join("; ", parts);
    }

    private static IEnumerable<BitmapSource> BuildBitmapVariants(BitmapSource source)
    {
        var bases = new List<BitmapSource> { source };
        var gray = ToGrayscaleHighContrast(source);
        if (gray != null)
        {
            bases.Add(gray);
            var inverted = InvertGrayscale(gray);
            if (inverted != null)
                bases.Add(inverted);
        }

        foreach (var bmp in bases)
        {
            foreach (var scale in new[] { 1.0, 1.5, 2.0, 3.0, 4.0, 0.75 })
            {
                var scaled = Math.Abs(scale - 1.0) < 0.01 ? bmp : ScaleBitmap(bmp, scale);
                if (scaled == null)
                    continue;

                foreach (var angle in new[] { 0, 90, 180, 270 })
                    yield return angle == 0 ? scaled : RotateBitmap(scaled, angle);
            }
        }
    }

    private static IEnumerable<Result> DecodeAllResults(BitmapSource bitmap)
    {
        var formatPasses = new[]
        {
            new[] { BarcodeFormat.DATA_MATRIX },
            new[] { BarcodeFormat.DATA_MATRIX, BarcodeFormat.QR_CODE },
            (BarcodeFormat[])Enum.GetValues(typeof(BarcodeFormat))
        };

        var seen = new HashSet<string>();

        foreach (var formatList in formatPasses)
        {
            foreach (var pureBarcode in new[] { false, true })
            {
                var reader = CreateReader(formatList, pureBarcode);

                foreach (var result in InvokeDecodeMethods(reader, bitmap))
                {
                    var key = result.Text ?? Convert.ToHexString(result.RawBytes ?? Array.Empty<byte>());
                    if (seen.Add(key))
                        yield return result;
                }
            }
        }
    }

    private static IEnumerable<Result> InvokeDecodeMethods(BarcodeReader reader, BitmapSource bitmap)
    {
        var single = reader.Decode(bitmap);
        if (single != null)
            yield return single;

        foreach (var method in reader.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "DecodeMultiple", StringComparison.Ordinal))
                continue;

            if (method.GetParameters().Length != 1)
                continue;

            object? multi;
            try
            {
                multi = method.Invoke(reader, new object[] { bitmap });
            }
            catch
            {
                continue;
            }

            if (multi is not Result[] results)
                continue;

            foreach (var r in results)
            {
                if (r != null)
                    yield return r;
            }
        }
    }

    private static IEnumerable<(byte[]? Bytes, string? Text)> CollectPayloadsFromResult(Result result)
    {
        foreach (var payload in CollectBytePayloads(result))
            yield return (payload, null);

        if (!string.IsNullOrEmpty(result.Text))
            yield return (null, result.Text);
    }

    private static BarcodeReader CreateReader(IList<BarcodeFormat> formats, bool pureBarcode)
    {
        var options = new DecodingOptions
        {
            PossibleFormats = formats,
            TryHarder = true,
            PureBarcode = pureBarcode,
            CharacterSet = "ISO-8859-1"
        };

        TrySetGs1Option(options);

        return new BarcodeReader
        {
            Options = options,
            AutoRotate = true
        };
    }

    private static void TrySetGs1Option(DecodingOptions options)
    {
        foreach (var name in new[] { "AssumeGS1", "Gs1Formatting", "GS1Format" })
        {
            var prop = typeof(DecodingOptions).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop is { CanWrite: true } && prop.PropertyType == typeof(bool))
            {
                prop.SetValue(options, true);
                return;
            }
        }
    }

    private static IEnumerable<byte[]> CollectBytePayloads(Result result)
    {
        var payloads = new List<byte[]>();

        if (result.RawBytes is { Length: > 0 } bytes)
            payloads.Add(bytes);

        var barcodeBytesProp = result.GetType().GetProperty("BarcodeBytes");
        if (barcodeBytesProp?.GetValue(result) is byte[] barcodeBytes && barcodeBytes.Length > 0)
            payloads.Add(barcodeBytes);

        if (result.ResultMetadata != null)
        {
            foreach (var value in result.ResultMetadata.Values)
            {
                if (value is byte[] metaBytes && metaBytes.Length > 0)
                    payloads.Add(metaBytes);
            }
        }

        if (!string.IsNullOrEmpty(result.Text))
            payloads.Add(Latin1.GetBytes(result.Text));

        return payloads
            .OrderByDescending(p => p.Length)
            .DistinctBy(p => Convert.ToHexString(p));
    }

    private static BitmapSource? ScaleBitmap(BitmapSource source, double scale)
    {
        try
        {
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? ToGrayscaleHighContrast(BitmapSource source)
    {
        try
        {
            var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            gray.Freeze();

            return StretchGrayscaleBuffer(gray, invert: false);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? InvertGrayscale(BitmapSource gray)
    {
        try
        {
            return StretchGrayscaleBuffer(gray, invert: true);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? StretchGrayscaleBuffer(BitmapSource gray, bool invert)
    {
        var writable = new WriteableBitmap(gray);
        writable.Lock();
        try
        {
            var stride = writable.BackBufferStride;
            var height = writable.PixelHeight;
            var width = writable.PixelWidth;
            var buffer = new byte[stride * height];
            System.Runtime.InteropServices.Marshal.Copy(
                writable.BackBuffer, buffer, 0, buffer.Length);

            byte min = 255, max = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                var v = buffer[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }

            var range = Math.Max(1, max - min);
            for (var i = 0; i < buffer.Length; i++)
            {
                var stretched = (byte)(((buffer[i] - min) * 255) / range);
                buffer[i] = invert ? (byte)(255 - stretched) : stretched;
            }

            System.Runtime.InteropServices.Marshal.Copy(
                buffer, 0, writable.BackBuffer, buffer.Length);
            writable.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            writable.Unlock();
        }

        writable.Freeze();
        return writable;
    }

    private static BitmapSource RotateBitmap(BitmapSource source, double angle)
    {
        var rotated = new TransformedBitmap(
            source,
            new RotateTransform(angle));
        rotated.Freeze();
        return rotated;
    }
}
