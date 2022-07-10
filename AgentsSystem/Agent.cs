namespace AgentsSystem;
//This must not be part of a namespace, otherwise Godot will throw 
//  non-sense errors about the interface not being implemented where it is implemented.
//  Moreover it must have the following using declared:
//using AgentsSystem;

public interface IAgent
{
	void Tick(SimulationWorld world, IFormation formation, int formationID, uint timestep);
	//void DeliverPost();
	//void UpdatePopulation();
}

// public abstract class Agent : IAgent
// {
//     public readonly string ClassName;
//     public readonly Dictionary<string, AttrValue> Attributes;

//     public Agent(string className) => ClassName = className;

//     public abstract IAgent Tick(World world, IAgent parent, int formationID, uint timestep);
// }
