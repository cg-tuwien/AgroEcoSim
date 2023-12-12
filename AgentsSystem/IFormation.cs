namespace AgentsSystem;

public interface IFormation
{
    ///<summary>
    ///Handles births and deaths of agents
    ///</summary>
    void Census();
    ///<summary>
    ///Each agent performs its simulation step
    ///</summary>
    void Tick(uint timestep);
    ///<summary>
    ///Delivers messages to all its agents
    ///</summary>
    void DeliverPost(uint timestep);
    bool HasUndeliveredPost { get; }
    ///<summary>
    ///Number of agents in this formation
    ///</summary>
    int Count { get; }
}
