using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DoubleMark.Core.Parsing;
using DoubleMark.Core.Print;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace DoubleMark.Desktop.Services;

public static class ImageBarcodeDecoder
{
    private const string Gs1DecodeFailedMessage =
        "Декодер не смог прочитать GS1 DataMatrix. Попробуйте: крупный кадр только квадрат кода, без бликов.";

    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    private const int MaxDecodeSidePx = 3600;

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

    /// <summary>
    /// Fast path for PDF batch: template crop first, then minimal scale passes on the page.
    /// </summary>
    public static bool TryDecodeFromBitmapFast(
        BitmapSource bitmap,
        PrintTemplate? template,
        out ImageDecodeResult? result,
        out string? error)
    {
        if (template != null)
        {
            var crop = BuildTemplateDataMatrixCrop(bitmap, template, paddingRatio: 0.12);
            if (crop != null && TryDecodeFastOnRegion(crop, out result, out error))
                return true;
        }

        return TryDecodeFastOnRegion(bitmap, out result, out error);
    }

    public static bool TryDecodeFromBitmapFast(BitmapSource bitmap, out ImageDecodeResult? result, out string? error) =>
        TryDecodeFromBitmapFast(bitmap, template: null, out result, out error);

    private static bool TryDecodeFastOnRegion(BitmapSource bitmap, out ImageDecodeResult? result, out string? error)
    {
        result = null;
        error = null;

        try
        {
            foreach (var variant in BuildFastBitmapVariants(bitmap))
            {
                foreach (var zxingResult in DecodeFastResults(variant))
                {
                    foreach (var (rawBytes, rawString) in CollectPayloadsFromResult(zxingResult))
                    {
                        if (TryBuildFastDecodeResult(rawBytes, rawString, out result))
                            return true;
                    }
                }
            }

            error = "Код на изображении не найден.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryBuildFastDecodeResult(
        byte[]? rawBytes,
        string? rawString,
        out ImageDecodeResult? result)
    {
        result = null;
        var norm = rawBytes is { Length: > 0 }
            ? Gs1BarcodeEncoding.NormalizeForParse(rawBytes)
            : Gs1BarcodeEncoding.NormalizeForParse(rawString ?? string.Empty);

        var raw = norm.Payload;
        if (string.IsNullOrEmpty(raw))
            return false;

        if (!norm.FoundAi01 && !raw.StartsWith("01", StringComparison.Ordinal))
            return false;

        var byteLen = norm.Bytes.Length > 0 ? norm.Bytes.Length : Latin1.GetByteCount(raw);
        result = new ImageDecodeResult
        {
            Raw = raw,
            RawHex = Gs1BarcodeEncoding.ToHex(raw),
            GsCount = Gs1BarcodeEncoding.CountGs(raw),
            PayloadByteLength = byteLen,
            PreambleStrippedBytes = norm.StrippedPrefixBytes,
            Ai01Offset = norm.Ai01Offset,
            NormalizeNote = BuildNormalizeNote(norm)
        };
        return true;
    }

    /// <summary>
    /// PDF page decode: template crop + label heuristics, capped upscale for small DataMatrix.
    /// </summary>
    public static bool TryDecodeFromBitmapPdfEnhanced(
        BitmapSource bitmap,
        PrintTemplate? template,
        out ImageDecodeResult? result,
        out string? error)
    {
        result = null;
        error = null;

        try
        {
            var candidates = new List<DecodeCandidate>();
            var seenRaw = new HashSet<string>(StringComparer.Ordinal);

            foreach (var variant in BuildPdfPageVariants(bitmap, template))
            {
                CollectFastCandidates(variant, candidates, seenRaw);

                if (TryPickBestCandidate(candidates, out result, out _))
                    return true;
            }

            return TryPickBestCandidate(candidates, out result, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryDecodeFromBitmapPdfEnhanced(BitmapSource bitmap, out ImageDecodeResult? result, out string? error) =>
        TryDecodeFromBitmapPdfEnhanced(bitmap, template: null, out result, out error);

    private static void CollectFastCandidates(
        BitmapSource variant,
        List<DecodeCandidate> candidates,
        HashSet<string> seenRaw)
    {
        foreach (var zxingResult in DecodeFastResults(variant))
        {
            foreach (var (rawBytes, rawString) in CollectPayloadsFromResult(zxingResult))
            {
                var norm = rawBytes is { Length: > 0 }
                    ? Gs1BarcodeEncoding.NormalizeForParse(rawBytes)
                    : Gs1BarcodeEncoding.NormalizeForParse(rawString ?? string.Empty);

                var raw = norm.Payload;
                if (string.IsNullOrEmpty(raw) || !seenRaw.Add(raw))
                    continue;

                var byteLen = norm.Bytes.Length > 0 ? norm.Bytes.Length : Latin1.GetByteCount(raw);
                candidates.Add(new DecodeCandidate(
                    raw,
                    Gs1BarcodeEncoding.ToHex(raw),
                    Gs1BarcodeEncoding.CountGs(raw),
                    Gs1BarcodeEncoding.ScoreGs1Payload(raw),
                    byteLen,
                    norm));
            }
        }
    }

    private static bool TryPickBestCandidate(
        List<DecodeCandidate> candidates,
        out ImageDecodeResult? result,
        out string? error)
    {
        result = null;
        error = null;

        if (candidates.Count == 0)
        {
            error = "Код на изображении не найден (мелкий DataMatrix — проверьте качество PDF).";
            return false;
        }

        var best = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.ByteLength)
            .ThenByDescending(c => c.Gs)
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

    private static IEnumerable<BitmapSource> BuildPdfPageVariants(BitmapSource source, PrintTemplate? template)
    {
        foreach (var region in BuildPdfPageRegions(source, template))
        {
            var isSmallCrop = region.PixelWidth < source.PixelWidth * 0.8 ||
                              region.PixelHeight < source.PixelHeight * 0.8;
            var scales = isSmallCrop ? new[] { 2.0, 3.0, 4.0, 5.0, 6.0 } : new[] { 2.0, 3.0 };

            yield return region;

            var gray = ToGrayscaleHighContrast(region);
            if (gray == null)
                continue;

            yield return gray;

            var inverted = InvertGrayscale(gray);
            if (inverted != null)
                yield return inverted;

            foreach (var scale in scales)
            {
                var scaled = ScaleBitmapCapped(gray, scale);
                if (scaled != null)
                    yield return scaled;
            }
        }
    }

    private static IEnumerable<BitmapSource> BuildPdfPageRegions(BitmapSource source, PrintTemplate? template)
    {
        if (template != null)
        {
            foreach (var region in BuildTemplateDecodeRegions(source, template))
                yield return region;
        }

        yield return source;

        var bottom = CropBitmap(source, 0.0, 0.45, 1.0, 0.55);
        if (bottom != null)
            yield return bottom;

        var center = CropBitmap(source, 0.15, 0.25, 0.7, 0.55);
        if (center != null)
            yield return center;

        var lowerLeft = CropBitmap(source, 0.0, 0.5, 0.55, 0.5);
        if (lowerLeft != null)
            yield return lowerLeft;

        var lowerRight = CropBitmap(source, 0.45, 0.5, 0.55, 0.5);
        if (lowerRight != null)
            yield return lowerRight;
    }

    private static IEnumerable<BitmapSource> BuildTemplateDecodeRegions(BitmapSource source, PrintTemplate template)
    {
        var tight = BuildTemplateDataMatrixCrop(source, template, paddingRatio: 0.12);
        if (tight != null)
            yield return tight;

        var loose = BuildTemplateDataMatrixCrop(source, template, paddingRatio: 0.35);
        if (loose != null)
            yield return loose;
    }

    private static BitmapSource? BuildTemplateDataMatrixCrop(BitmapSource source, PrintTemplate template, double paddingRatio)
    {
        var labelW = Math.Max(0.1, template.LabelWidthMm);
        var labelH = Math.Max(0.1, template.LabelHeightMm);
        var dmW = Math.Max(0.1, template.DataMatrixWidthMm);
        var dmH = Math.Max(0.1, template.DataMatrixHeightMm);

        var xRatio = template.DataMatrixXmm / labelW;
        var yRatio = template.DataMatrixYmm / labelH;
        var wRatio = dmW / labelW;
        var hRatio = dmH / labelH;

        xRatio = Math.Max(0, xRatio - wRatio * paddingRatio);
        yRatio = Math.Max(0, yRatio - hRatio * paddingRatio);
        wRatio = Math.Min(1.0 - xRatio, wRatio * (1 + paddingRatio * 2));
        hRatio = Math.Min(1.0 - yRatio, hRatio * (1 + paddingRatio * 2));

        return CropBitmap(source, xRatio, yRatio, wRatio, hRatio);
    }

    private static BitmapSource? CropBitmap(BitmapSource source, double xRatio, double yRatio, double wRatio, double hRatio)
    {
        try
        {
            var x = Math.Clamp((int)(source.PixelWidth * xRatio), 0, Math.Max(0, source.PixelWidth - 1));
            var y = Math.Clamp((int)(source.PixelHeight * yRatio), 0, Math.Max(0, source.PixelHeight - 1));
            var w = Math.Max(32, (int)(source.PixelWidth * wRatio));
            var h = Math.Max(32, (int)(source.PixelHeight * hRatio));
            if (x + w > source.PixelWidth)
                w = source.PixelWidth - x;
            if (y + h > source.PixelHeight)
                h = source.PixelHeight - y;
            if (w <= 0 || h <= 0)
                return null;

            var cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<BitmapSource> BuildFastBitmapVariants(BitmapSource source)
    {
        yield return source;

        var gray = ToGrayscaleHighContrast(source);
        if (gray == null)
            yield break;

        yield return gray;

        foreach (var scale in new[] { 2.0, 3.0 })
        {
            var scaled = ScaleBitmapCapped(gray, scale);
            if (scaled != null)
                yield return scaled;
        }
    }

    private static IEnumerable<Result> DecodeFastResults(BitmapSource bitmap)
    {
        foreach (var pureBarcode in new[] { true, false })
        {
            var reader = CreateReader(new[] { BarcodeFormat.DATA_MATRIX }, pureBarcode);
            reader.AutoRotate = false;
            var single = reader.Decode(bitmap);
            if (single != null)
                yield return single;
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

    private static BitmapSource? ScaleBitmapCapped(BitmapSource source, double scale)
    {
        var maxDim = Math.Max(source.PixelWidth, source.PixelHeight);
        if (maxDim <= 0)
            return null;

        var effectiveScale = maxDim * scale > MaxDecodeSidePx
            ? MaxDecodeSidePx / (double)maxDim
            : scale;

        if (effectiveScale <= 1.01)
            return null;

        return ScaleBitmap(source, effectiveScale);
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
