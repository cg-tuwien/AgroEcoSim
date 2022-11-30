using System;
using System.Numerics;
using AgentsSystem;

namespace Agro;

public interface IPlantAgent : ITreeAgent
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

	float EnergyStorageCapacity();
	float WaterStorageCapacity { get; }
	float WaterTotalCapacityPerTick { get; }
	float EnergyFlowToParentPerTick { get; }

	float LifeSupportPerTick { get; }
	float PhotosynthPerTick { get; }

	float WoodRatio { get; }

	Vector3 Scale { get; }

	static void Reindex(IPlantAgent[] data, int[] map) => throw new NotImplementedException();

	bool ChangeAmount(PlantFormation1 plant, int index, int substanceIndex, float amount, bool increase);
	bool ChangeAmount(PlantFormation2 plant, int index, int substanceIndex, float amount, bool increase);

	void Distribute(float water, float energy);
}