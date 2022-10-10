using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.AccessControl;
using System.Threading.Tasks;
using Utils;

namespace AgentsSystem;

public abstract class Formation3i<T> : Formation<T> where T : struct, IAgent
{
	public readonly int SizeX, SizeY, SizeZ;
	protected readonly int SizeXY;

	public Formation3i(int sizeX, int sizeY, int sizeZ) : base()
	{
		SizeX = sizeX;
		SizeY = sizeY;
		SizeZ = sizeZ;
		SizeXY = sizeX * sizeY;
		Agents = new T[SizeXY * sizeZ];
		AgentsTMP = new T[SizeXY * sizeZ];
	}

	public int Index(Vector3i coords) => coords.X + coords.Y * SizeX + coords.Z * SizeXY;
	public int Index(int x, int y, int z) => x + y * SizeX + z * SizeXY;

	public Vector3i Coords(int index)
	{
		var z = index / SizeXY;
		index -= z * SizeXY;
		var y = index / SizeX;
		return new(index - y * SizeX, y, z);
	}

	public bool CheckCoords(Vector3i coords) => coords.X >= 0 && coords.Y >= 0 && coords.Z >= 0 && coords.X < SizeX && coords.Y < SizeY && coords.Z < SizeZ;
	public bool CheckCoords(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ;

	public bool TryGet(Vector3i coords, out T result)
	{
		if (CheckCoords(coords))
		{
			result = ReadTMP ? AgentsTMP[Index(coords)] : Agents[Index(coords)];
			return true;
		}
		else
		{
			result = default;
			return false;
		}
	}

	//Private
	public bool Send(Vector3i dst, IMessage<T> msg)
	{
		if (CheckCoords(dst))
		{
			Postbox.Add(new (msg, Index(dst)));
			return true;
		}
		else
			return false;
	}

	public bool Send(int x, int y, int z, IMessage<T> msg)
	{
		if (CheckCoords(x, y, z))
		{
			Postbox.Add(new (msg, Index(x, y, z)));
			return true;
		}
		else
			return false;
	}

#if HISTORY_LOG || TICK_LOG
	public ulong GetID(Vector3i index) => GetID(Index(index));
	public ulong GetID(int x, int y, int z) => GetID(Index(x, y, z));
#endif
}