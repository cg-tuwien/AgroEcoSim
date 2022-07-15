using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.AccessControl;
using System.Threading.Tasks;
using Utils;

namespace AgentsSystem;
//This must not be part of a namespace, otherwise Godot will throw 
//  non-sense errors about the interface not being implemented where it is implemented.
//  Moreover it must have the following using declared:
//using AgentsSystem;

public abstract class Formation<T> : IFormation where T : struct, IAgent
{
	protected bool ReadTMP = false;
	protected T[] Agents;
	protected T[] AgentsTMP;

	protected readonly PostBox<T> Postbox = new();
	readonly List<T> Births = new();
	readonly List<int> Deaths = new();

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	(T[], T[]) SrcDst() => ReadTMP ? (AgentsTMP, Agents) : (Agents, AgentsTMP);

	public void Census()
	{
		if (Births.Count > 0 || Deaths.Count > 0)
		{
			var src =  ReadTMP ? AgentsTMP : Agents;
			if (Deaths.Count > 0)
			{
				Deaths.Sort();
				//remove duplicates
				for(int i = Deaths.Count - 2; i >= 0; --i)
					if (Deaths[i] == Deaths[i + 1])
						Deaths.RemoveAt(i + 1);
			}

			var diff = Births.Count - Deaths.Count;
			T[] underGround;
			if (diff != 0)
				underGround = new T[src.Length + diff];
			else
				underGround = src;

			int a = 0;
			if (Deaths.Count > 0)
			{
				var dc = Deaths.Count;
				for(int i = 0, d = 0; i < src.Length; ++i)
				{
					if (Deaths[d] == i)
					{
						if (++d == dc && i + 1 < src.Length)
						{
							Array.Copy(src, i + 1, underGround, a, src.Length - i - 1);
							break;
						}
					}
					else
						underGround[a++] = src[i];
				}
				Deaths.Clear();
			}
			else
			{
				Array.Copy(src, underGround, src.Length);
				a = src.Length;
			}

			for(int i = 0; i < Births.Count; ++i, ++a)
				underGround[a] = Births[i];

			Births.Clear();

			if (ReadTMP)
			{
				Agents = new T[underGround.Length];
				AgentsTMP = underGround;
			}
			else
			{
				Agents = underGround;
				AgentsTMP = new T[underGround.Length];
			}
		}
	}

	public virtual void Tick(SimulationWorld world, uint timestep)
	{
		var (src, dst) = SrcDst();

		Array.Copy(src, dst, src.Length);
		//for(int i = 0; i < AgentsTMP.Length; ++i)
#if !DEBUG
		if (dst.Length > Environment.ProcessorCount)
			Parallel.For(0, dst.Length, i =>
				dst[i].Tick(world, this, i, timestep));
		else
#endif
		for(int i = 0; i < dst.Length; ++i)
			dst[i].Tick(world, this, i, timestep);

		ReadTMP = !ReadTMP;
	}

	/// <summary>
	/// Broadcast messaging
	/// </summary>
	public void Send(IMessage<T> msg) => Postbox.Add(new (msg));

	/// <summary>
	/// Private targeted messaging
	/// </summary>
	public bool Send(int recipient, IMessage<T> msg)
	{
		if (recipient < Agents.Length)
		{
			Postbox.Add(new (msg, recipient));
			return true;
		}
		else
			return false;
	}

	public virtual void Birth(T agent)
	{
		Births.Add(agent);
	}

	public virtual void Death(int index)
	{
		Deaths.Add(index);
	}

	public virtual void DeliverPost()
	{
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, dst.Length);
		Postbox.Process(dst);
		ReadTMP = !ReadTMP;
	}

	public virtual bool HasUndeliveredPost => Postbox.AnyMessages;

#if GODOT
	public virtual void GodotReady() {}
	public virtual void GodotProcess(uint timestep) {}
#endif
}

public class FormationTree<T> : Formation<T> where T : struct, IAgent
{
	int Count = 0;
	readonly List<int> Parents = new();
	public FormationTree()
	{
		Agents = new T[1];
		AgentsTMP = new T[1];
	}

	public int Add(T node, int parent)
	{        
		if (Count >= Agents.Length)
		{
			var newSize = Math.Min(Agents.Length << 1, 512);
			Array.Resize(ref Agents, newSize);
			Array.Resize(ref AgentsTMP, newSize);
		}

		var newIndex = Count++;
		if (ReadTMP)
			AgentsTMP[newIndex] = node;
		else
			Agents[newIndex] = node;

		Parents.Add(parent);

		return newIndex;
	}

	public bool TryGetParent(int index, out int result) 
	{
		result = Parents[index];        
		return result >= 0 && result != index;
	}

	public T Get(int index) => Agents[index]; 
}

public class Formation3i<T> : Formation<T> where T : struct, IAgent
{
	protected readonly int SizeX, SizeY, SizeZ;
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
}

[Flags]
public enum TransformFlags { None, Translated = 1, Rotated = 2, Scaled = 4};
public class Formation3iTransformed<T> : Formation3i<T> where T : struct, IAgent
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
