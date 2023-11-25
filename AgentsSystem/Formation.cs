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

	public virtual void Tick(uint timestep)
	{
		var (src, dst) = SrcDst();

		Array.Copy(src, dst, src.Length);
		for(int i = 0; i < dst.Length; ++i)
			dst[i].Tick(this, i, timestep);

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

	public virtual void DeliverPost(uint timestep)
	{
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, dst.Length);
		Postbox.Process(timestep, dst);
		ReadTMP = !ReadTMP;
	}

	public virtual void ProcessTransactions(uint timestep) { }

	public virtual bool HasUndeliveredPost => Postbox.AnyMessages;
	public virtual bool HasUnprocessedTransactions => false;
	public virtual int Count => Agents.Length;
}
