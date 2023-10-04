using System;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace Utils;

public static class PcgSeed
{
	/// <summary>
	/// Provides a time-dependent seed value, matching the default behavior of System.Random.
	/// </summary>
	public static ulong TimeBasedSeed() => (ulong)Environment.TickCount;

	/// <summary>
	/// Provides a seed based on time and unique GUIDs.
	/// </summary>
	public static ulong GuidBasedSeed()
	{
		ulong upper = (ulong)(Environment.TickCount ^ Guid.NewGuid().GetHashCode()) << 32;
		ulong lower = (ulong)(Environment.TickCount ^ Guid.NewGuid().GetHashCode());
		return upper | lower;
	}
}

/// <summary>
/// PCG (Permuted Congruential Generator) is a C# port from C the base PCG generator
/// presented in "PCG: A Family of Simple Fast Space-Efficient Statistically Good
/// Algorithms for Random Number Generation" by Melissa E. O'Neill. The code follows closely the one
/// made available by O'Neill at her site: http://www.pcg-random.org/download.html
/// To understand how exactly this generator works read this:
/// http://www.pcg-random.org/pdf/toms-oneill-pcg-family-v1.02.pdf
/// </summary>
[Serializable]
[DataContract]
public class Pcg
{
	[DataMember(Order = 0)]
	ulong _state;
	// This shifted to the left and or'ed with 1ul results in the default increment.
	[DataMember(Order = 1)]
	ulong _increment = 1442695040888963407ul;
	[IgnoreDataMember]
	const ulong ShiftedIncrement = 721347520444481703ul;
	[IgnoreDataMember]
	const ulong Multiplier = 6364136223846793005ul;
	[IgnoreDataMember]
	const double ToDouble01 = 1.0 / uint.MaxValue;

	// This attribute ensures that every thread will get its own instance of PCG.
	// An alternative, since PCG supports streams, is to use a different stream per
	// thread.
	[ThreadStatic]
	static Pcg? _defaultInstance;
	/// <summary>
	/// Default instance.
	/// </summary>
	public static Pcg Default => _defaultInstance ??= new Pcg(PcgSeed.GuidBasedSeed());

	public int Next() => unchecked((int)(NextUInt() >> 1));

	public int Next(int max, bool maxExclusive = true)
	{
		Debug.Assert((maxExclusive && max >= 0) || max > 0);
		if ((maxExclusive && max < 0) || max <= 0)
			throw new ArgumentException("Max Exclusive must be positive");

		uint uMaxExclusive = (uint)(maxExclusive ? max : max + 1);
		uint threshold = (uint)(-uMaxExclusive) % uMaxExclusive;

		while (true)
		{
			uint result = NextUInt();
			if (result >= threshold)
				return (int)(result % uMaxExclusive);
		}
	}

	public int Next(int min, int max, bool maxExclusive = true)
	{
		Debug.Assert((maxExclusive && min < max) || min <= max);
		if ((maxExclusive && max < min) || max <= min)
			throw new ArgumentException("MaxExclusive must be larger than MinInclusive");

		uint uMaxExclusive = unchecked((uint)(max - min + (maxExclusive ? 0 : 1)));
		uint threshold = (uint)(-uMaxExclusive) % uMaxExclusive;

		while (true)
		{
			uint result = NextUInt();
			if (result >= threshold)
				return (int)unchecked((result % uMaxExclusive) + min);
		}
	}

	public int[] NextInts(int count)
	{
		if (count <= 0)
			return Array.Empty<int>();
		else
		{
			var resultA = new int[count];
			for (var i = 0; i < count; i++)
				resultA[i] = Next();

			return resultA;
		}
	}

	public int[] NextInts(int count, int max, bool maxExclusive = true)
	{
		if (count <= 0)
			return Array.Empty<int>();
		else
		{
			var resultA = new int[count];
			for (int i = 0; i < count; i++)
				resultA[i] = Next(max, maxExclusive);

			return resultA;
		}
	}

	public int[] NextInts(int count, int min, int max, bool maxExclusive = true)
	{
		if (count <= 0)
			return Array.Empty<int>();
		else
		{
			var resultA = new int[count];
			for (int i = 0; i < count; i++)
				resultA[i] = Next(min, max, maxExclusive);

			return resultA;
		}
	}

	public uint NextUInt()
	{
		ulong oldState = _state;
		_state = unchecked(oldState * Multiplier + _increment);
		uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
		int rot = (int)(oldState >> 59);
		uint result = (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
		return result;
	}

	public uint NextUInt(uint max, bool maxExclusive = true)
	{
		if (!maxExclusive) ++max;

		if (max == 0) return 0;
		else
		{
			uint threshold = (uint)(-max) % max; //What is this? I don't remember anymore :(

			while (true)
			{
				uint result = NextUInt();
				if (result >= threshold)
					return result % max;
			}
		}
	}

	public uint NextUInt(uint min, uint max, bool maxExclusive = true)
	{
		Debug.Assert((maxExclusive && min < max) || min <= max);
		if ((maxExclusive && max <= min) || max < min)
			throw new ArgumentException();

		uint diff = max - min + (maxExclusive ? 0U : 1U);
		if (diff == 0) return min;
		else
		{
			uint threshold = (uint)(-diff) % diff;

			while (true)
			{
				uint result = NextUInt();
				if (result >= threshold)
					return (result % diff) + min;
			}
		}
	}

	public uint[] NextUInts(int count)
	{
		if (count <= 0)
			throw new ArgumentException("Zero count");

		var resultA = new uint[count];
		for (int i = 0; i < count; i++)
			resultA[i] = NextUInt();

		return resultA;
	}

	public uint[] NextUInts(int count, uint max, bool maxExclusive = true)
	{
		if (count <= 0)
			throw new ArgumentException("Zero count");

		var resultA = new uint[count];
		for (int i = 0; i < count; i++)
			resultA[i] = NextUInt(max, maxExclusive);

		return resultA;
	}

	public uint[] NextUInts(int count, uint min, uint max, bool maxExclusive)
	{
		if (count <= 0)
			throw new ArgumentException("Zero count");

		var resultA = new uint[count];
		for (int i = 0; i < count; i++)
			resultA[i] = NextUInt(min, max, maxExclusive);

		return resultA;
	}

	public ulong NextULong() => (((ulong)NextUInt()) << 32) | NextUInt();

	public float NextFloat() => (float)(NextUInt() * ToDouble01);

	public float NextFloat(float maxInclusive)
	{
		if (maxInclusive <= 0)
			throw new ArgumentException("MaxInclusive must be larger than 0");

		return (float)(NextUInt() * ToDouble01) * maxInclusive;
	}

	public float NextFloat(float minInclusive, float maxInclusive)
	{
		if (maxInclusive < minInclusive)
			throw new ArgumentException("Max must be larger than min");

		return (float)(NextUInt() * ToDouble01) * (maxInclusive - minInclusive) + minInclusive;
	}

	public float[] NextFloats(int count)
	{
		if (count <= 0)
			return Array.Empty<float>();

		var resultA = new float[count];
		for (int i = 0; i < count; i++)
			resultA[i] = NextFloat();

		return resultA;
	}

	public float[] NextFloats(int count, float maxInclusive)
	{
		if (count <= 0)
			return Array.Empty<float>();

		var resultA = new float[count];
		for (int i = 0; i < count; i++)
		{
			resultA[i] = NextFloat(maxInclusive);
		}
		return resultA;
	}

	public float[] NextFloats(int count, float minInclusive, float maxInclusive)
	{
		if (count <= 0)
			return Array.Empty<float>();

		var resultA = new float[count];
		for (int i = 0; i < count; i++)
		{
			resultA[i] = NextFloat(minInclusive, maxInclusive);
		}
		return resultA;
	}

	public double NextDouble() => NextUInt() * ToDouble01;

	public double NextDouble(double maxInclusive)
	{
		if (maxInclusive <= 0)
			throw new ArgumentException("Max must be larger than 0");

		return NextUInt() * ToDouble01 * maxInclusive;
	}

	public double NextDouble(double minInclusive, double maxInclusive)
	{
		if (maxInclusive < minInclusive)
			throw new ArgumentException("Max must be larger than min");

		return NextUInt() * ToDouble01 * (maxInclusive - minInclusive) + minInclusive;
	}

	public double[] NextDoubles(int count)
	{
		if (count <= 0)
			return Array.Empty<double>();

		var resultA = new double[count];
		for (int i = 0; i < count; i++)
			resultA[i] = NextDouble();

		return resultA;
	}

	public double[] NextDoubles(int count, double maxInclusive)
	{
		if (count <= 0)
			return Array.Empty<double>();

		var resultA = new double[count];
		for (int i = 0; i < count; i++)
			resultA[i] = NextDouble(maxInclusive);

		return resultA;
	}

	public double[] NextDoubles(int count, double minInclusive, double maxInclusive)
	{
		if (count <= 0)
			return Array.Empty<double>();

		var resultA = new double[count];
		for (int i = 0; i < count; i++)
			resultA[i] = NextDouble(minInclusive, maxInclusive);

		return resultA;
	}

	public double[] NextDoublesScaled(int count, double targetSum = 1.0)
	{
		if (count <= 0)
			return Array.Empty<double>();

		var resultA = new double[count];
		var sum = 0.0;
		for (int i = 0; i < count; i++)
		{
			var r = NextDouble();
			resultA[i] = r;
			sum += r;
		}

		var factor = targetSum / sum;
		for (int i = 0; i < count; i++)
			resultA[i] *= factor;

		return resultA;
	}

	public byte NextByte() => unchecked((byte)(NextUInt() & 255));

	public byte[] NextBytes(int count)
	{
		if (count <= 0)
			return Array.Empty<byte>();

		var resultA = new byte[count];
		for (var i = 0; i < count; i++)
		{
			resultA[i] = NextByte();
		}
		return resultA;
	}

	public bool NextBool() => (NextUInt() & 1) == 1;

	public bool[] NextBools(int count)
	{
		if (count <= 0)
			throw new ArgumentException("Zero count");

		var resultA = new bool[count];
		for (var i = 0; i < count; i++)
			resultA[i] = NextBool();
		return resultA;
	}

	public double NextNormal(double mean, double variance)
	{
		double u, v, x, y, q;
		do
		{
			u = NextDouble();
			v = (NextDouble() - 0.5) * 1.7156;
			x = u - 0.449871;
			y = Math.Abs(v) + 0.386595;
			q = x * x + y * (0.19600 * y - 0.25472 * x);
		}
		while(q > 0.27597 && ( q >0.27846 || v*v > -4.0* Math.Log(u) *u * u));

		return mean + variance * v / u;
	}

	public void SetStream(int sequence) => SetStream((ulong)sequence);

	public void SetStream(ulong sequence) => _increment = (sequence << 1) | 1;

	public ulong CurrentStream() => _increment >> 1;

	public Pcg() : this(PcgSeed.GuidBasedSeed()) { }

	public Pcg(int seed) : this((ulong)seed) { }

	public Pcg(int seed, int sequence) : this((ulong)seed, (ulong)sequence) { }

	public Pcg(ulong seed, ulong sequence = ShiftedIncrement) => Initialize(seed, sequence);

	public Pcg(Pcg original)
	{
		_state = original._state;
		_increment = original._increment;
	}

	public Pcg NextRNG() => new(_state, (NextULong() << 1) | 1);

	void Initialize(ulong seed, ulong initseq)
	{
		_state = 0ul;
		SetStream(initseq);
		NextUInt();
		_state += seed;
		NextUInt();
	}

	internal ulong GetSeed() => _state;

	internal void SaveBinary(System.IO.BinaryWriter writer)
	{
		writer.Write(_state);
		writer.Write(_increment);
	}

	internal Pcg(System.IO.BinaryReader reader)
	{
		_state = reader.ReadUInt64();
		_increment = reader.ReadUInt64();
	}
}
