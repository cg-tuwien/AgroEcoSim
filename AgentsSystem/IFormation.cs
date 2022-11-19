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
    [Newtonsoft.Json.JsonIgnore] bool HasUndeliveredPost { get; }
    [Newtonsoft.Json.JsonIgnore] bool HasUnprocessedTransactions { get; }
    [Newtonsoft.Json.JsonIgnore] byte Stages { get; }
#if HISTORY_LOG || TICK_LOG
    string HistoryToJSON(int timestep = -1, byte stage = 0);
#endif
#if GODOT
    void GodotReady();
	void GodotProcess();
#endif
    ///<summary>
    ///Number of agents in this formation
    ///</summary>
    int Count { get; }
}
