namespace AgentsSystem;
//This must not be part of a namespace, otherwise Godot will throw
//  non-sense errors about the interface not being implemented where it is implemented.
//  Moreover it must have the following using declared:
//using AgentsSystem;

public interface IFormation
{
    void Census();
    void Tick(SimulationWorld world, uint timestep, byte stage);
    void ProcessTransactions(uint timestep, byte stage);
    void DeliverPost(uint timestep, byte stage);
    bool HasUndeliveredPost { get; }
    bool HasUnprocessedTransactions { get; }
    byte Stages { get; }
#if HISTORY_LOG || TICK_LOG
    string HistoryToJSON(int timestep = -1, byte stage = 0);
#endif
#if GODOT
    void GodotReady();
	void GodotProcess();
#endif
}
