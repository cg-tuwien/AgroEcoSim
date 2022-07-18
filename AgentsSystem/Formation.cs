using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.AccessControl;
using System.Threading.Tasks;
using Utils;

namespace AgentsSystem;

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
		for(int i = 0; i < dst.Length; ++i)
			dst[i].Tick(world, this, i, timestep);

		#if HISTORY_LOG
		var states = new T[dst.Length];
		Array.Copy(dst, states, dst.Length);
		StatesHistory.Add(states);
		#endif
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

	public virtual void DeliverPost(uint timestep)
	{
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, dst.Length);
		Postbox.Process(timestep, dst);
		ReadTMP = !ReadTMP;
	}

	public virtual bool HasUndeliveredPost => Postbox.AnyMessages;

#if HISTORY_LOG
	List<T[]> StatesHistory = new();
	public string HistoryToJSON() => Utils.Export.Json(StatesHistory);

	public ulong GetID(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].ID : ulong.MaxValue)
		: (Agents.Length > index ? Agents[index].ID : ulong.MaxValue);
#endif

#if GODOT
	public virtual void GodotReady() {}
	public virtual void GodotProcess(uint timestep) {}
#endif
}
