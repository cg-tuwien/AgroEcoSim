using System;
using System.Numerics;
using AgentsSystem;

namespace Agro;

public interface IPlantAgent : IAgent
{
	float Length { get; }

	/// <summary>
	/// Radius of the bottom face in m.
	/// </summary>
	float Radius { get; }

	/// <summary>
	/// Orientation with respect to the parent. If there is no parent, this is the initial orientation.
	/// </summary>
	Quaternion Orientation { get; }


	float Energy { get; }

	/// <summary>
	/// Water volume in m³
	/// </summary>
	float Water { get; }

	/// <summary>
	/// Plant organ, e.g. stem, leaft, fruit
	/// </summary>
	OrganTypes Organ { get; }

	/// <summary>
	/// Index of the parent agent. -1 represents the root of the hierarchy.
	/// </summary>
	int Parent { get; }

	float EnergyCapacity { get; }
	float WaterStorageCapacity { get; }
	float WaterCapacityPerTick { get; }
	float EnergyFlowToParentPerTick { get; }

	static void Reindex(IPlantAgent[] data, int[] map) => throw new NotImplementedException();
}