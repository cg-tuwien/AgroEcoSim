namespace AgentsSystem;
//This must not be part of a namespace, otherwise Godot will throw
//  non-sense errors about the interface not being implemented where it is implemented.
//  Moreover it must have the following using declared:
//using AgentsSystem;

public interface IFormation
{
    void Census();
    void Tick(uint timestep);
    void ProcessTransactions(uint timestep);
    void DeliverPost(uint timestep);
    bool HasUndeliveredPost { get; }
    bool HasUnprocessedTransactions { get; }
    #if HISTORY_LOG || TICK_LOG
    string HistoryToJSON(int timestep = -1);
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
