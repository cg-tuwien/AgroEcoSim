namespace AgentsSystem;

public interface IAgent
{
	void Tick(IFormation formation, int formationID, uint timestep);
	#if HISTORY_LOG || TICK_LOG
	/// <summary>
	/// Unique ID of the agent
	/// </summary>
	ulong ID { get; }
	#endif
}

public interface ITreeAgent : IAgent
{
	/// <summary>
	/// Index of the parent agent. -1 represents the root of the hierarchy.
	/// </summary>
	int Parent { get; }

	///<summary>
	/// Use with caution, call only from census! Updates the Parent value after splitting an agent.
	///</summary>
	void CensusUpdateParent(int newParent);
}

// public abstract class Agent : IAgent
// {
//     public readonly string ClassName;
//     public readonly Dictionary<string, AttrValue> Attributes;

//     public Agent(string className) => ClassName = className;

//     public abstract IAgent Tick(World world, IAgent parent, int formationID, uint timestep);
// }
