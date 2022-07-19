using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgentsSystem;
public partial class SimulationWorld
{
	internal readonly List<IFormation> Formations = new();
	public uint Timestep { get; private set; }

	public SimulationWorld()
	{
		Formations = new List<IFormation>();
		Timestep = 0;
	}

	public void Add(IFormation formation)
	{
		Formations.Add(formation);
#if GODOT
		formation.GodotReady();
#endif
	}

	public void AddRange(IEnumerable<IFormation> formations)
	{
		Formations.AddRange(formations);
#if GODOT
		foreach(var item in formations)
			item.GodotReady();
#endif
	}

	public void Run(uint simulationLength)
	{
#if !DEBUG
		if (Formations.Count > Environment.ProcessorCount)
			RunParallel(simulationLength);
		else
#endif
			RunSequential(simulationLength);
	}

	public void RunSequential(uint simulationLength)
	{
		for(uint i = 0U; i < simulationLength; ++i, ++Timestep)
		{
			CensusSequential();
			TickSequential(Timestep);
			DeliverPostSequential(Timestep);
#if GODOT
			foreach(var item in Formations)
				item.GodotProcess(Timestep);
#endif
		}
	}

	public void RunParallel(uint simulationLength)
	{
		for(uint i = 0U; i < simulationLength; ++i, ++Timestep)
		{
			CensusParallel();
			TickParallel(Timestep);
			DeliverPostParallel(Timestep);
#if GODOT
			foreach(var item in Formations)
				item.GodotProcess(Timestep);
#endif
		}
	}

	void CensusSequential()
	{
		for(int i = 0; i < Formations.Count; ++i)
			Formations[i].Census();
	}

	void CensusParallel() => Parallel.For(0, Formations.Count, i => Formations[i].Census());

	void TickSequential(uint timestep)
	{
		//Console.WriteLine($"TIMESTEP: {timestep}");
		for(int i = 0; i < Formations.Count; ++i)
			Formations[i].Tick(this, timestep);
	}

	void TickParallel(uint timestep) => Parallel.For(0, Formations.Count, i => Formations[i].Tick(this, timestep));

	public void DeliverPostSequential(uint timestep)
	{
		var anyDelivered = true;
		while(anyDelivered)
		{
			anyDelivered = false;
			for(int i = 0; i < Formations.Count; ++i)
				if (Formations[i].HasUndeliveredPost)
				{
					Formations[i].DeliverPost(timestep);
					anyDelivered = true;
				}
		}
	}

	public void DeliverPostParallel(uint timestep)
	{
		var anyDelivered = true;
		while(anyDelivered)
		{
			anyDelivered = false;
			Parallel.For(0, Formations.Count, i => {
				if (Formations[i].HasUndeliveredPost)
				{
					Formations[i].DeliverPost(timestep);
					anyDelivered = true;
				}
			});
		}
	}

	#if HISTORY_LOG || TICK_LOG
	public string HistoryToJSON()
	{
		var sb = new System.Text.StringBuilder();
		//assuming all formations are present all the time (no additions or removals)
		sb.Append("{ \"Formations\": [ ");
		for(int i = 0; i < Formations.Count; ++i)
		{
			sb.Append(Formations[i].HistoryToJSON());
			if (i < Formations.Count - 1)
				sb.Append(", ");
		}
		sb.Append("], \"Transactions\": {");


		foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies().ToArray()) //ToArray is required for NET6
			foreach(var type in assembly.GetTypes())
				if (type.IsDefined(typeof(MessageAttribute), false))
				{
					sb.Append($"\"{type.FullName}\": ");
					sb.Append(Utils.Export.Json(type.GetField("TransactionsHistory", BindingFlags.Public | BindingFlags.Static).GetValue(null)));
					sb.Append(", ");
				}

		sb.Append("} }");
		return sb.ToString();
	}
	#endif
}
