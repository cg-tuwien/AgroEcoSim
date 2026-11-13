namespace GodotFrontend.Core.Offline;

/// <summary>
/// Integer-factor box average over RGBA8 rows in ENCODED sRGB, the web's PixelReadback contract for supersampled
/// stills (box-averaged in encoded sRGB, alpha forced opaque).
/// </summary>
public static class BoxDownsample
{
    public static byte[] Rgba8(ReadOnlySpan<byte> src, int srcW, int srcH, int factor, out int dstW, out int dstH)
    {
        if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor));
        dstW = srcW / factor;
        dstH = srcH / factor;
        var dst = new byte[dstW * dstH * 4];
        var n = factor * factor;
        for (var y = 0; y < dstH; ++y)
            for (var x = 0; x < dstW; ++x)
            {
                int r = 0, g = 0, b = 0;
                for (var sy = 0; sy < factor; ++sy)
                {
                    var row = ((y * factor + sy) * srcW + x * factor) * 4;
                    for (var sx = 0; sx < factor; ++sx)
                    {
                        r += src[row]; g += src[row + 1]; b += src[row + 2];
                        row += 4;
                    }
                }
                var o = (y * dstW + x) * 4;
                dst[o] = (byte)((r + n / 2) / n);
                dst[o + 1] = (byte)((g + n / 2) / n);
                dst[o + 2] = (byte)((b + n / 2) / n);
                dst[o + 3] = 255;
            }
        return dst;
    }
}
