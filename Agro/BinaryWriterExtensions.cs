using System.Numerics;
using System.Runtime.CompilerServices;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Agro;

public static class BinaryWriterExtensions
{
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
	[M(AI)]public static void WriteU8(this BinaryWriter writer, int value) => writer.Write((byte)value);
	[M(AI)]public static void WriteU32(this BinaryWriter writer, int value) => writer.Write((uint)value);
	[M(AI)]public static void WriteU32(this BinaryWriter writer, uint value) => writer.Write(value);
	[M(AI)]public static void WriteV32(this BinaryWriter writer, Vector3 xyz)
	{
		writer.Write(xyz.X);
		writer.Write(xyz.Y);
		writer.Write(xyz.Z);
	}
	[M(AI)]public static void WriteV32(this BinaryWriter writer, float x, float y, float z)
	{
		writer.Write(x);
		writer.Write(y);
		writer.Write(z);
	}
	[M(AI)]public static void WriteV32(this BinaryWriter writer, Vector3 xyz, float w)
	{
		writer.Write(xyz.X);
		writer.Write(xyz.Y);
		writer.Write(xyz.Z);
		writer.Write(w);
	}
	[M(AI)]public static void WriteM32(this BinaryWriter writer, Vector3 ax, Vector3 ay, Vector3 az, float tx, float ty, float tz)
	{
		writer.Write(ax.X); writer.Write(ay.X); writer.Write(az.X); writer.Write(tx);
		writer.Write(ax.Y); writer.Write(ay.Y); writer.Write(az.Y); writer.Write(ty);
		writer.Write(ax.Z); writer.Write(ay.Z); writer.Write(az.Z); writer.Write(tz);
	}

	[M(AI)]public static void WriteM32(this BinaryWriter writer, Vector3 ax, Vector3 ay, Vector3 az, Vector3 t)
	{
		writer.Write(ax.X); writer.Write(ay.X); writer.Write(az.X); writer.Write(t.X);
		writer.Write(ax.Y); writer.Write(ay.Y); writer.Write(az.Y); writer.Write(t.Y);
		writer.Write(ax.Z); writer.Write(ay.Z); writer.Write(az.Z); writer.Write(t.Z);
	}
}
