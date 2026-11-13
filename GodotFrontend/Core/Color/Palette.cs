namespace GodotFrontend.Core.Color;

/// <summary>render/core/Palette.ts: the 800 seeded near-greens used for laminae that carry no colour (legacy frames).</summary>
public static class Palette
{
    public const uint GreenPaletteSeed = 0x5eed6ee7;

    /// <summary>Mulberry32, bit-for-bit with the JavaScript version (Math.imul and >>> 0 semantics).</summary>
    public static Func<double> Mulberry32(uint seed)
    {
        var a = seed;
        return () =>
        {
            a = unchecked(a + 0x6D2B79F5u);
            var t = a;
            t = unchecked((uint)((int)(t ^ (t >> 15)) * (int)(t | 1)));
            t ^= unchecked(t + (uint)((int)(t ^ (t >> 7)) * (int)(t | 61)));
            return (t ^ (t >> 14)) / 4294967296.0;
        };
    }

    public static T[] Shuffled<T>(T[] src, Func<double> rnd)
    {
        var array = (T[])src.Clone();
        var currentIndex = array.Length;
        while (currentIndex > 0)
        {
            var randomIndex = (int)System.Math.Floor(rnd() * currentIndex);
            currentIndex--;
            (array[currentIndex], array[randomIndex]) = (array[randomIndex], array[currentIndex]);
        }
        return array;
    }

    private static RGB[] BuildGreenRamp()
    {
        var o = new List<RGB>(800);
        for (var g = 120; g < 160; ++g)
            for (var r = 0; r < 20; ++r)
                o.Add(ColorMath.FromSrgb8((byte)r, (byte)g, 0));
        return o.ToArray();
    }

    public static readonly RGB[] GreenColors = Shuffled(BuildGreenRamp(), Mulberry32(GreenPaletteSeed));
}
