namespace AgentsSystem;

public interface IAgent
{
	void Tick(IFormation formation, int formationID, uint timestep);
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