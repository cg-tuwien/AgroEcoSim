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

	byte DominanceLevel { get; }


	float Energy { get; }

	/// <summary>
	/// Water volume in m³
	/// </summary>
	float Water { get; }

	float Auxins { get; }
	//float Cytokinins { get; }

	/// <summary>
	/// Plant organ, e.g. stem, leaft, fruit
	/// </summary>

	OrganTypes Organ { get; }

	/// <summary>
	/// Production during the previous day, per m² i.e. invariant of size
	/// </summary>
	float PreviousDayProductionInv { get; }

	/// <summary>
	/// Resources allocated during the previous day, per m² i.e. invariant of size
	/// </summary>
	float PreviousDayEnvResourcesInv { get; }

	public float PreviousDayEnvResources { get; }

	float EnergyStorageCapacity();
	float WaterStorageCapacity();
	float WaterTotalCapacityPerTick(AgroWorld world);
	float EnergyFlowToParentPerTick(AgroWorld world);

	float LifeSupportPerHour();
	float LifeSupportPerTick(AgroWorld world);
	float PhotosynthPerTick(AgroWorld world);

	float WoodRatio();

	Vector3 Scale();
	float Volume();

	bool NewDay(uint timestep, byte ticksPerDay);

	void Distribute(float water, float energy);
	void IncAuxins(float amount);
	void DailyMax(float resources, float production);
	void DailyAdd(float resources, float production);
	void DailySet(float resources, float production, float efficiency);
	void DailyDiv(uint count);
}