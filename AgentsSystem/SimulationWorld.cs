namespace AgentsSystem;
public class SimulationWorld
{
	internal readonly List<IFormation> Formations = [];
	internal readonly List<Action<SimulationWorld, uint, IList<IFormation>, IList<IObstacle>>> Callbacks = [];
	public virtual uint Timestep { get; protected set; } = 0U;

	/// <summary>
	/// Puts the clock back where a checkpoint left it.
	///
	/// Deliberately narrow and deliberately not a public setter: the timestep is what the sun, the
	/// weather tables and every age in the simulation are read against, so the only legitimate
	/// reason to move it is restoring a world that has already lived those steps.
	/// </summary>
	protected void RestoreTimestep(uint timestep) => Timestep = timestep;

	public int Count => Formations.Count;

	/// <summary>
	/// Forces the sequential tick even when there is more than one formation.
	///
	/// A diagnostic now, not a trade. The parallel path used to be irreproducible: formations tick
	/// concurrently and their soil water requests reached the soil in whatever order the thread
	/// pool produced, so the same request with the same seed diverged at the first timestep once
	/// there were several plants (59 of 60 stored frames differed), and a scenario branched from a
	/// checkpoint could never be compared against its parent. The requests are now buffered on each
	/// plant and handed to the soil in formation order (<c>SoilRequestRouter</c> in Agro) - the only
	/// thing in a tick that crosses formations - and the parallel tick is byte-identical to the
	/// sequential one: measured frame for frame at 100 and 400 plants over 650 steps. Keep this off
	/// unless a divergence between the two paths is itself what is being investigated.
	/// </summary>
	public bool Deterministic { get; set; } = false;

	internal readonly List<IObstacle> Obstacles = [];

	/// <summary>The obstacles, for a caller that has to report on them - the leaf area index
	/// measurement names the ones it matched, and a few of the ones it did not.</summary>
	public IReadOnlyList<IObstacle> ObstacleList => Obstacles;

	/// <summary>The formations, for a caller that measures the scene without driving it.</summary>
	public IReadOnlyList<IFormation> FormationList => Formations;

	public void ForEach(Action<IFormation> action) => Formations.ForEach(formation => action(formation));

    public void Add(IFormation formation) => Formations.Add(formation);

    public void AddRange(IEnumerable<IFormation> formations) => Formations.AddRange(formations);

    public void Add(IObstacle obstacle) => Obstacles.Add(obstacle);

    public void Run(uint simulationLength)
	{
		#if !DEBUG
		if (Formations.Count > 1 && !Deterministic)
			RunParallel(simulationLength);
		else
		#endif
			RunSequential(simulationLength);
	}

	public void RunSequential(uint simulationLength)
	{
		for(int i = 0; i < simulationLength; ++i, ++Timestep)
		{
			TickSequential();
			DeliverPostSequential();

			CensusSequential();
			ExecCallbacks();
		}
	}

	public void RunParallel(uint simulationLength)
	{
		for(int i = 0; i < simulationLength; ++i, ++Timestep)
		{
			TickParallel();
			DeliverPostParallel();

			CensusParallel();
			ExecCallbacks();
		}
	}

	void CensusSequential()
	{
		for(int i = 0; i < Formations.Count; ++i)
			Formations[i].Census();
	}

	void CensusParallel() => Parallel.For(0, Formations.Count, i => Formations[i].Census());

	void TickSequential()
	{
		//Debug.WriteLine($"TIMESTEP: {Timestep}");
		for(int i = 0; i < Formations.Count; ++i)
			Formations[i].Tick(Timestep);
	}

	protected virtual void TickParallel() => Parallel.For(0, Formations.Count, i => Formations[i].Tick(Timestep));

	public void DeliverPostSequential()
	{
		var anyDelivered = true;
		while(anyDelivered)
		{
			anyDelivered = false;
			for(int i = 0; i < Formations.Count; ++i)
				if (Formations[i].HasUndeliveredPost)
				{
					Formations[i].DeliverPost(Timestep);
					anyDelivered = true;
				}
		}
	}

	public virtual void DeliverPostParallel()
	{
		var anyDelivered = true;
		while(anyDelivered)
		{
			anyDelivered = false;
			Parallel.For(0, Formations.Count, i => {
				if (Formations[i].HasUndeliveredPost)
				{
					Formations[i].DeliverPost(Timestep);
					anyDelivered = true;
				}
			});
		}
	}

	public void AddCallback(Action<SimulationWorld, uint, IList<IFormation>, IList<IObstacle>> callback) => Callbacks.Add(callback);

	public void ExecCallbacks()
	{
		foreach(var callback in Callbacks)
			callback(this, Timestep, Formations, Obstacles);
	}

	public string ToJson()
	{
		var sb = new System.Text.StringBuilder();
		//assuming all formations are present all the time (no additions or removals)
		sb.Append("{ \"Formations\": [ ");
		for(int i = 0; i < Formations.Count; ++i)
		{
			sb.Append(Utils.Export.Json(Formations[i]));
			if (i < Formations.Count - 1)
				sb.Append(", ");
		}
		sb.Append("]}");

		return sb.ToString();
	}

	public Func<byte, List<IFormation>, List<IObstacle>, byte[]> StreamExporterFunc = null;
	public byte[]? ExportToStream(byte flags) => StreamExporterFunc == null ? null : StreamExporterFunc(flags, Formations, Obstacles);
	public string RendererName;
}
