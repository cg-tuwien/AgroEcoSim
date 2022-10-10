using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.AccessControl;
using System.Threading.Tasks;
using Utils;

namespace AgentsSystem;

[Flags]
public enum TransformFlags : byte { None, Translated = 1, Rotated = 2, Scaled = 4};
public abstract class Formation3iTransformed<T> : Formation3i<T> where T : struct, IAgent
{
	public Formation3iTransformed(int sizeX, int sizeY, int sizeZ) : base(sizeX, sizeY, sizeZ) {}

	TransformFlags Flags = TransformFlags.None;
	Vector3 Translation;
	Vector3 Scale;
	Quaternion Rotation;

	public void SetScale(float factor)
	{
		Scale = new Vector3(factor);
		Flags |= TransformFlags.Scaled;
	}

	public void SetScale(Vector3 scale)
	{
		Scale = scale;
		Flags |= TransformFlags.Scaled;
	}

	public void SetScale(float x, float y, float z)
	{
		Scale = new Vector3(x, y, z);
		Flags |= TransformFlags.Scaled;
	}

	public void AddScale(float factor)
	{
		Scale += new Vector3(factor);
		Flags |= TransformFlags.Scaled;
	}

	public void AddScale(Vector3 scale)
	{
		Scale += scale;
		Flags |= TransformFlags.Scaled;
	}

	public void AddScale(float x, float y, float z)
	{
		Scale += new Vector3(x, y, z);
		Flags |= TransformFlags.Scaled;
	}

	public void MultiplyScale(float factor)
	{
		Scale *= new Vector3(factor);
		Flags |= TransformFlags.Scaled;
	}

	public void MultiplyScale(Vector3 scale)
	{
		Scale *= scale;
		Flags |= TransformFlags.Scaled;
	}

	public void MultiplyScale(float x, float y, float z)
	{
		Scale *= new Vector3(x, y, z);
		Flags |= TransformFlags.Scaled;
	}

	public void SetTranslation(Vector3 translation)
	{
		Translation = translation;
		Flags |= TransformFlags.Translated;
	}

	public void SetTranslation(float x, float y, float z)
	{
		Translation = new Vector3(x, y, z);
		Flags |= TransformFlags.Translated;
	}

	public void AddTranslation(Vector3 translation)
	{
		Translation += translation;
		Flags |= TransformFlags.Translated;
	}

	public void AddTranslation(float x, float y, float z)
	{
		Translation += new Vector3(x, y, z);
		Flags |= TransformFlags.Translated;
	}

	public virtual List<int> IntersectSphere(Vector3 center, float radius)
	{
		if (Flags.HasFlag(TransformFlags.Translated))
			center -= Translation;
		if (Flags.HasFlag(TransformFlags.Scaled))
		{
			center *= new Vector3(SizeX, SizeY, SizeZ) / Scale;
		}

		var iCenter = new Vector3i(center);

		if (iCenter.X >= 0 && iCenter.Y >= 0 && iCenter.Z >= 0 && iCenter.X < SizeX && iCenter.Y < SizeY && iCenter.Z < SizeZ)
			return new List<int>{Index(iCenter)};
		else
			return new List<int>();
	}
}
