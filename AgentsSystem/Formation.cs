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
	//Once GODOT supports C# 6.0: Make it a List and then for processing send System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Stems);
	protected T[] Agents;
	protected T[] AgentsTMP;

	protected readonly PostBox<T> Postbox = new();
	protected readonly List<T> Births = new();
	protected readonly HashSet<int> Deaths = new();
	protected readonly List<int> DeathsHelper = new();

	public abstract byte Stages { get; }

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	protected (T[], T[]) SrcDst() => ReadTMP ? (AgentsTMP, Agents) : (Agents, AgentsTMP);
	protected T[] Src() => ReadTMP ? AgentsTMP : Agents;

	public virtual void Census()
	{
		if (Births.Count > 0 || Deaths.Count > 0)
		{
			var src = Src();
			if (Deaths.Count > 0)
			{
				DeathsHelper.Clear();
				DeathsHelper.AddRange(Deaths);
				DeathsHelper.Sort();
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
					if (DeathsHelper[d] == i)
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

	public virtual void Tick(SimulationWorld world, uint timestep, byte stage)
	{
		var (src, dst) = SrcDst();

		Array.Copy(src, dst, src.Length);
		for(int i = 0; i < dst.Length; ++i)
			dst[i].Tick(world, this, i, timestep, stage);

		#if TICK_LOG
		StatesHistory.Clear();
		#endif
		#if HISTORY_LOG || TICK_LOG
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

	public virtual int Birth(T agent)
	{
		Births.Add(agent);
		return Agents.Length + Births.Count - 1;
	}

	public virtual void Death(int index)
	{
		Deaths.Add(index);
	}

	public virtual void DeliverPost(uint timestep, byte stage)
	{
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, dst.Length);
		Postbox.Process(timestep, stage, dst);
		ReadTMP = !ReadTMP;
	}

	public virtual void ProcessTransactions(SimulationWorld world, uint timestep, byte stage) { }

	public virtual bool HasUndeliveredPost => Postbox.AnyMessages;
	public virtual bool HasUnprocessedTransactions => false;
	public virtual int Count => Agents.Length;

	#if HISTORY_LOG || TICK_LOG
	readonly List<T[]> StatesHistory = new();
	public string HistoryToJSON(int timestep = -1, byte stage = 0) => timestep >= 0 ? Export.Json(StatesHistory[timestep]) : Export.Json(StatesHistory);

	public ulong GetID(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].ID : ulong.MaxValue)
		: (Agents.Length > index ? Agents[index].ID : ulong.MaxValue);
	#endif

	#if GODOT
	public virtual void GodotReady() {}
	public virtual void GodotProcess() {}
	#endif
}
