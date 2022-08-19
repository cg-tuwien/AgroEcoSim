using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;

namespace AgentsSystem;
public partial class SimulationWorld
{
	internal readonly List<IFormation> Formations = new();
	internal readonly List<Action<uint, IList<IFormation>>> Callbacks = new();
	public uint Timestep { get; private set; }

	#if TICK_LOG
	List<MethodInfo> MessageLogClears = new();
	#endif

	public SimulationWorld()
	{
		Formations = new List<IFormation>();
		Timestep = 0;
		#if TICK_LOG
		foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies().ToArray()) //ToArray is required for NET6
			foreach(var type in assembly.GetTypes())
				if (type.IsDefined(typeof(MessageAttribute), false))
				{
					var method = type.GetMethod("ClearHistory", BindingFlags.Public | BindingFlags.Static);
					if (method != null)
						MessageLogClears.Add(method);
					else
						throw new Exception($"{type.FullName} is marked with [Message] attribute but it does not proivde a public static ClearHistory method.");
				}
		#endif
	}

    public void ForEach(Action<IFormation> action) => Formations.ForEach(formation => action(formation));

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
			TickSequential(Timestep);
			ProcessTransactionsSequential(Timestep);
			DeliverPostSequential(Timestep);
			CensusSequential();
			ExecCallbacks();
#if GODOT
			foreach(var item in Formations)
				item.GodotProcess(Timestep);
#endif
#if HISTORY_LOG || HISTORY_TICK
			// if (i >= 477)
			// {
			// 	var exported = HistoryToJSON((int)i);
			// 	File.WriteAllText($"export-{i}.json", exported.Replace("},", "},\n").Replace("],", "],\n"));
			// }
#endif
		}
	}

	public void RunParallel(uint simulationLength)
	{
		for(uint i = 0U; i < simulationLength; ++i, ++Timestep)
		{
			TickParallel(Timestep);
			ProcessTransactionsParallel(Timestep);
			DeliverPostParallel(Timestep);
			CensusParallel();
			ExecCallbacks();
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
		Debug.WriteLine($"TIMESTEP: {timestep}");
		for(int i = 0; i < Formations.Count; ++i)
			Formations[i].Tick(this, timestep);
	}

	void TickParallel(uint timestep) => Parallel.For(0, Formations.Count, i => Formations[i].Tick(this, timestep));

	public void ProcessTransactionsSequential(uint timestep)
	{
		var anyDelivered = true;
		while(anyDelivered)
		{
			anyDelivered = false;
			for(int i = 0; i < Formations.Count; ++i)
				if (Formations[i].HasUnprocessedTransactions)
				{
					Formations[i].ProcessTransactions(timestep);
					anyDelivered = true;
				}
		}
	}

	public void ProcessTransactionsParallel(uint timestep)
	{
		#if TICK_LOG
		foreach(var clear in MessageLogClears)
			clear.Invoke(null, null);
		#endif
		var anyDelivered = true;
		while(anyDelivered)
		{
			anyDelivered = false;
			Parallel.For(0, Formations.Count, i => {
				if (Formations[i].HasUnprocessedTransactions)
				{
					Formations[i].ProcessTransactions(timestep);
					anyDelivered = true;
				}
			});
		}
	}

	public void DeliverPostSequential(uint timestep)
	{
		#if TICK_LOG
		foreach(var clear in MessageLogClears)
			clear.Invoke(null, null);
		#endif
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
		#if TICK_LOG
		foreach(var clear in MessageLogClears)
			clear.Invoke(null, null);
		#endif
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

    public void AddCallback(Action<uint, IList<IFormation>> callback) => Callbacks.Add(callback);

    public void ExecCallbacks()
	{
		foreach(var callback in Callbacks)
			callback(Timestep, Formations);
	}

	#if HISTORY_LOG || TICK_LOG
	public string HistoryToJSON(int timestep = -1)
	{
		var sb = new System.Text.StringBuilder();
		//assuming all formations are present all the time (no additions or removals)
		sb.Append("{ \"Formations\": [ ");
		for(int i = 0; i < Formations.Count; ++i)
		{
			sb.Append(Formations[i].HistoryToJSON(timestep));
			if (i < Formations.Count - 1)
				sb.Append(", ");
		}
		sb.Append("], \"Message\": {");

		var anyMessagesType = false;
		foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies().ToArray()) //ToArray is required for NET6
			foreach(var type in assembly.GetTypes())
				if (type.IsDefined(typeof(MessageAttribute), false))
				{
					anyMessagesType = true;
					sb.Append($"\"{type.FullName}\": ");
					var data = type.GetField("MessagesHistory", BindingFlags.Public | BindingFlags.Static).GetValue(null);
#if HISTORY_LOG
					if (timestep < 0)
#endif
						sb.Append(Utils.Export.Json(data));
#if HISTORY_LOG
					else
					{
						var timestepData = new List<object>();
						var t = data.GetType();
						var count = (int)t.GetProperty("Count").GetValue(data);
						var get = t.GetMethod("get_Item");
						for(int i = 0; i < count; ++i)
						{
							var item = get.Invoke(data, new object[]{i});
							var typedItem = (IMsgLogData)item;
							if (typedItem.TimeStep == timestep)
								timestepData.Add(item);
						}
						sb.Append(Utils.Export.Json(timestepData));
					}
#endif
					sb.Append(", ");
				}

		sb.Append($"{(anyMessagesType ? "\"_\": []" : "")} }} }}");
		return sb.ToString();
	}
	#endif
}
