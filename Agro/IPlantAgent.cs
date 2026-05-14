using System;
using System.Numerics;
using AgentsSystem;

namespace Agro;

public interface IPlantAgent : ITreeAgent
{
	uint BirthTime { get; }
	float Length { get; }

	/// <summary>
	/// Radius of the bottom face in m.
	/// </summary>
	float Radius { get; }

	/// <summary>
	/// Orientation with respect to the parent. If there is no parent, this is the initial orientation.
	/// </summary>
	Quaternion Orientation { get; }
    Quaternion baseOrientation { get; set; }
    Quaternion restOrientation { get; set; }
    Quaternion targetOrientation { get; set; }

    byte DominanceLevel { get; }


    bool isRizome { get; }
	Vector3 BaseOffset { get; }

    float Energy { get; }

	/// <summary>
	/// Water amount in gramms
	/// </summary>
	float Water_g { get; }

	float Auxins { get; }
	//float Cytokinins { get; }

	/// <summary>
	/// Plant organ, e.g. stem, leaft, fruit
	/// </summary>

	OrganTypes Organ { get; }

	/// <summary>
	/// Production during the previous day, per m² i.e. invariant of size
	/// </summary>
	float PreviousDayProductionInvariant { get; }

	/// <summary>
	/// Resources allocated during the previous day, per m² i.e. invariant of size
	/// </summary>
	float PreviousDayEnvResourcesInvariant { get; }

	public float PreviousDayEnvResources { get; }

	float EnergyStorageCapacity();
	float WaterStorageCapacity_g();
	float WaterTotalCapacityPerTick_g(AgroWorld world);
	float EnergyFlowToParentPerTick(AgroWorld world);

	float LifeSupportPerHour();
	float LifeSupportPerTick(AgroWorld world);
	float PhotosynthPerTick(AgroWorld world);

	float WoodRatio();
	float Adulcy(uint timestep);
	float Stress { get; }
	float Senescence(uint timestep);

	Vector3 Scale();
	float Volume();

	bool CompleteDay(uint timestep, byte ticksPerDay);

	void Distribute(float water, float energy);
	void IncAuxins(float amount);
	void DailyMax(float resources, float production);
	void DailyAdd(float resources, float production);
	void DailySet(float resources, float production, float efficiency);
	void DailyDiv(uint count);
    void SetOrientation(Quaternion quaternion);
}