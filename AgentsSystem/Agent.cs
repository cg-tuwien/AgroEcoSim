namespace AgentsSystem;

public interface IAgent
{
	void Tick(SimulationWorld world, IFormation formation, int formationID, uint timestep);
	#if HISTORY_LOG
    /// <summary>
    /// Unique ID of the agent
    /// </summary>
	ulong ID { get; }
	#endif
}

// public abstract class Agent : IAgent
// {
//     public readonly string ClassName;
//     public readonly Dictionary<string, AttrValue> Attributes;

//     public Agent(string className) => ClassName = className;

//     public abstract IAgent Tick(World world, IAgent parent, int formationID, uint timestep);
// }
