namespace AgentsSystem;
//This must not be part of a namespace, otherwise Godot will throw 
//  non-sense errors about the interface not being implemented where it is implemented.
//  Moreover it must have the following using declared:
//using AgentsSystem;

public interface IFormation
{
    void Tick(SimulationWorld world, uint timestep);
    void DeliverPost();
#if GODOT
    void GodotReady();
	void GodotProcess(uint timestep);
#endif
}
