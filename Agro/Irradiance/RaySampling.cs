using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Agro;

/// <summary>
/// Stateless sampling helpers for the internal light model.
/// </summary>
/// <remarks>
/// Every sample is derived by hashing (seed, timestep, sensor, sample) instead of drawing from a
/// shared random stream. That keeps the tracer bit-reproducible no matter how the work is split
/// across threads, which the deterministic simulation probes rely on.
/// </remarks>
public static class RaySampling
{
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;

	/// <summary>Thomas Wang's 32 bit integer hash.</summary>
	[M(AI)]
	public static uint Hash(uint x)
	{
		x = (x ^ 61u) ^ (x >> 16);
		x *= 9u;
		x ^= x >> 4;
		x *= 0x27d4eb2du;
		x ^= x >> 15;
		return x;
	}

	[M(AI)] public static uint Hash(uint a, uint b) => Hash(a ^ (b * 0x9e3779b9u));
	[M(AI)] public static uint Hash(uint a, uint b, uint c) => Hash(Hash(a, b) ^ (c * 0x85ebca6bu));

	/// <summary>Maps a hash to [0,1).</summary>
	[M(AI)] public static float ToFloat01(uint h) => (h >> 8) * (1f / 16777216f);

	/// <summary>Van der Corput radical inverse in base 2.</summary>
	[M(AI)]
	public static float RadicalInverse2(int i)
	{
		var bits = (uint)i;
		bits = (bits << 16) | (bits >> 16);
		bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
		bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
		bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
		bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
		return bits * 2.3283064365386963e-10f;
	}

	/// <summary>
	/// The <paramref name="index"/>-th of <paramref name="count"/> stratified 2D samples, rotated
	/// by a per-sensor offset (Cranley-Patterson). One dimension is stratified and the other is a
	/// radical inverse, which gives noticeably lower variance than two independent hashes.
	/// </summary>
	[M(AI)]
	public static void Sample2D(int index, int count, uint key, out float u1, out float u2)
	{
		var offset1 = ToFloat01(Hash(key, 0x1u));
		var offset2 = ToFloat01(Hash(key, 0x2u));
		u1 = (index + 0.5f) / count + offset1;
		u2 = RadicalInverse2(index) + offset2;
		if (u1 >= 1f) u1 -= 1f;
		if (u2 >= 1f) u2 -= 1f;
	}

	/// <summary>Duff et al. branchless orthonormal basis around a unit vector.</summary>
	[M(AI)]
	public static void OrthonormalBasis(Vector3 n, out Vector3 t, out Vector3 b)
	{
		var sign = n.Z >= 0f ? 1f : -1f;
		var a = -1f / (sign + n.Z);
		var c = n.X * n.Y * a;
		t = new Vector3(1f + sign * n.X * n.X * a, sign * c, -sign * n.X);
		b = new Vector3(c, sign + n.Y * n.Y * a, -n.Y);
	}

	/// <summary>
	/// A direction drawn from the cosine-weighted hemisphere around <paramref name="n"/>. The
	/// cosine factor of the irradiance integral is carried exactly by the density, so a gather of
	/// K samples is simply <c>pi / K * sum(radiance)</c>.
	/// </summary>
	[M(AI)]
	public static Vector3 CosineHemisphere(Vector3 n, Vector3 tangent, Vector3 bitangent, float u1, float u2)
	{
		var r = MathF.Sqrt(u1);
		var phi = 2f * MathF.PI * u2;
		var z = MathF.Sqrt(MathF.Max(0f, 1f - u1));
		return tangent * (r * MathF.Cos(phi)) + bitangent * (r * MathF.Sin(phi)) + n * z;
	}

	/// <summary>
	/// A uniformly distributed point on the triangle <c>a, a + ab, a + ac</c>, from the standard
	/// square-root warp of the barycentric coordinates.
	/// </summary>
	[M(AI)]
	public static Vector3 TrianglePoint(Vector3 a, Vector3 ab, Vector3 ac, float u1, float u2)
	{
		var s = MathF.Sqrt(u1);
		return a + ab * (s * (1f - u2)) + ac * (s * u2);
	}
}
