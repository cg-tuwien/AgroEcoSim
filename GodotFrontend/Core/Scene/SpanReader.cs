using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace GodotFrontend.Core.Scene;

/// <summary>
/// Sequential little-endian reader over a byte span, the C# counterpart of helpers/BinaryReader.ts. No allocation
/// per read; vectors are block-copied. Strings are C#'s BinaryWriter format: a 7-bit encoded length then UTF-8.
/// </summary>
public ref struct SpanReader
{
    private readonly ReadOnlySpan<byte> s;
    public int Pos;

    public SpanReader(ReadOnlySpan<byte> source)
    {
        s = source;
        Pos = 0;
    }

    public int Length => s.Length;
    public bool IsEnd => Pos == s.Length;
    public int Remaining => s.Length - Pos;

    public byte U8()
    {
        Need(1);
        return s[Pos++];
    }

    public sbyte I8() => (sbyte)U8();

    public ushort U16()
    {
        Need(2);
        var v = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(Pos));
        Pos += 2;
        return v;
    }

    public int I32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(Pos));
        Pos += 4;
        return v;
    }

    public uint U32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(Pos));
        Pos += 4;
        return v;
    }

    public float F32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(Pos));
        Pos += 4;
        return v;
    }

    /// <summary>Reads n floats into dst (little-endian hosts only; asserted once at startup).</summary>
    public void F32Into(scoped Span<float> dst)
    {
        Need(dst.Length * 4);
        if (BitConverter.IsLittleEndian)
            MemoryMarshal.Cast<byte, float>(s.Slice(Pos, dst.Length * 4)).CopyTo(dst);
        else
            for (var i = 0; i < dst.Length; ++i) dst[i] = BinaryPrimitives.ReadSingleLittleEndian(s.Slice(Pos + i * 4));
        Pos += dst.Length * 4;
    }

    public float[] F32Array(int n)
    {
        var a = new float[n];
        F32Into(a);
        return a;
    }

    public void I32Into(scoped Span<int> dst)
    {
        Need(dst.Length * 4);
        if (BitConverter.IsLittleEndian)
            MemoryMarshal.Cast<byte, int>(s.Slice(Pos, dst.Length * 4)).CopyTo(dst);
        else
            for (var i = 0; i < dst.Length; ++i) dst[i] = BinaryPrimitives.ReadInt32LittleEndian(s.Slice(Pos + i * 4));
        Pos += dst.Length * 4;
    }

    public int[] I32Array(int n)
    {
        var a = new int[n];
        I32Into(a);
        return a;
    }

    public int Read7BitEncodedInt()
    {
        var result = 0;
        var shift = 0;
        byte b;
        do
        {
            b = U8();
            result |= (b & 0x7F) << shift;
            shift += 7;
            if (shift > 35) throw new InvalidDataException("bad 7-bit encoded length");
        } while ((b & 0x80) != 0);
        return result;
    }

    public string String()
    {
        var byteLength = Read7BitEncodedInt();
        Need(byteLength);
        var str = Encoding.UTF8.GetString(s.Slice(Pos, byteLength));
        Pos += byteLength;
        return str;
    }

    public void Skip(int bytes)
    {
        Need(bytes);
        Pos += bytes;
    }

    private void Need(int bytes)
    {
        if (bytes < 0 || Pos + bytes > s.Length)
            throw new InvalidDataException($"scene stream truncated at byte {Pos} (needed {bytes} more of {s.Length})");
    }
}
