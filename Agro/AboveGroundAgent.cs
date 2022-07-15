using System;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using AgentsSystem;
using System.Runtime.InteropServices;

namespace Agro;

//TODO IMPORTANT All resource transport should be request-confirm messages, i.e. pull-policy.
//  There should never be forced resource push since it may not be able to fit into the available storage.
//TODO Create properties computing the respective storages for water and energy. Just for clarity.
//[Flags]  //flags are not needed anymore

public enum OrganTypes { Unspecified = 0, Seed = 1, Shoot = 2, Root = 4, Stem = 8, Leaf = 16, Fruit = 32 };

[StructLayout(LayoutKind.Auto)]
public partial struct AboveGroundAgent : IAgent
{
	/// <summary>
	/// Orientation with respect to the parent. If there is no parent, this is the initial orientation.
	/// </summary>
	public Quaternion Orientation { get; private set; }

	/// <summary>
	/// Length of the agent in m.
	/// </summary>
	public float Length { get; private set; }

	/// <summary>
	/// Radius of the bottom face in m.
	/// </summary>
	public float Radius { get; private set; }


	public float Energy { get; private set; }

	/// <summary>
	/// Water volume in m³
	/// </summary>
	public float Water { get; private set; }

	/// <summary>
	/// Inverse woodyness ∈ [0, 1]. The more woody (towards 0) the less photosynthesis can be achieved. 
	/// </summary>
	float mPhotoFactor;

	/// <summary>
	/// Plant organ, e.g. stem, leaft, fruit
	/// </summary>
	public OrganTypes Organ { get; private set; }

	/// <summary>
	/// Index of the parent agent. -1 represents the root of the hierarchy.
	/// </summary>
	public int Parent { get; private set; }
	//public int[] Children { get; private set; }


	/// <summary>
	/// Recommended initial length of the agent at birth in m.
	/// </summary>
	public const float InitialLength = 1e-5f;

	/// <summary>
	/// Recommended initial bottom face radius of the agent at birth in m.
	/// </summary>
	public const float InitialRadius = 0.2e-5f;

	public const float EnergyTransportRatio = 4f;
	//So far same as for undergrounds
	public const float WaterTransportRatio = 1.8f;

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per hour
	/// </summary>
	public float WaterFlowToParentPerHour => 4f * Radius * Radius * WaterTransportRatio;

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per timestep
	/// </summary>
	public float WaterFlowToParentPerTick => WaterFlowToParentPerHour / AgroWorld.TicksPerHour;

	public float EnergyFlowToParentPerHour => 4f * Radius * Radius * WaterTransportRatio;

	public float EnergyFlowToParentPerTick => EnergyFlowToParentPerHour / AgroWorld.TicksPerHour;

	/// <summary>
	/// Volume ratio ∈ [0, 1] of the agent that can used for storing water
	/// </summary>
	const float WaterCapacityRatio = 0.75f;

	/// <summary>
	/// Water volume in m³ which can be stored in this agent
	/// </summary>
	public float WaterStorageCapacity => 4f * Radius * Radius * Length * WaterCapacityRatio;

	/// <summary>
	/// Water volume in m³ which can flow through per hour, or can be stored in this agent
	/// </summary>
	public float WaterCapacityPerHour => 4f * Radius * Radius * (Length * WaterCapacityRatio + WaterTransportRatio);

	/// <summary>
	/// Water volume in m³ which can flow through per tick, or can be stored in this agent
	/// </summary>
	public float WaterCapacityPerTick => WaterCapacityPerHour / AgroWorld.TicksPerHour;

	/// <summary>
	/// Timespan for which 1 unit of energy can feed 1m³ of plant tissue
	/// </summary>
	//Assuming the energy units are chosen s.t. a plant need an amount of energy per hour
	//equal to its volume, then this coefficient expresses how long it can survive
	//without any energy gains if its storage is initially full
	const float EnergyStorageCoef = 24 * 31 * 3; //3 months

	static float EnergyCapacityFunc(float radius, float length) => 4f * radius * radius * length * (1f - WaterCapacityRatio) * EnergyStorageCoef;

	public float EnergyCapacity => EnergyCapacityFunc(Radius, Length);


	public AboveGroundAgent(int parent, OrganTypes organ, Quaternion orientation, float initialEnergy, float radius = InitialRadius, float length = InitialLength)
	{
		Parent = parent;
		Radius = radius;
		Length = length;
		Orientation = orientation;

		Organ = organ;

		Energy = initialEnergy;
		mPhotoFactor = 1f;

		Water = 0f;

		//Children = null;
	}

	/// <summary>
	/// If the plant structure changes (agents added or removed), parent entries need to be reindexed according to the map
	/// </summary>
	public static void Reindex(AboveGroundAgent[] src, int[] map)
	{
		for(int i = 0; i < src.Length; ++i)
		{
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
			// if (src[i].Children != null)
			// 	for(int j = 0; j < src[i].Children.Length; ++j)
			// 		src[i].Children[j] = map[src[i].Children[j]];
		}
	}

	const float TwoPi = MathF.PI * 2f;
	public const float LeafThickness = 0.0001f;

	public void Tick(SimulationWorld world, IFormation _formation, int formationID, uint timestep)
	{
		var formation = (PlantFormation)_formation;

		//TODO perhaps growth should somehow reflect temperature
		var lr = Length * Radius;
		var lifeSupportPerHour = lr * (Organ == OrganTypes.Leaf ? LeafThickness : Radius);
		var lifeSupportPerTick = lifeSupportPerHour / AgroWorld.TicksPerHour;
		Energy -= lifeSupportPerTick; //life support

		var children = formation.GetChildren_AG(formationID);

		//Growth
		if (Energy > lifeSupportPerHour * 36) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage        
		{
			if (Organ != OrganTypes.Shoot)
			{
				Vector2 factor;
				var isLeafStem = children.Count == 1 && formation.GetOrgan_AG(children[0]) == OrganTypes.Leaf;
				switch(Organ)
				{
					case OrganTypes.Leaf: factor.X = 4e-5f; factor.Y = 8e-6f; break;
					case OrganTypes.Stem:
					{
						if (isLeafStem)
							factor = new Vector2(1e-5f, 1e-6f);
						else
							factor = new Vector2(1e-4f, 4e-6f);
					}
					break;
					default: factor = Vector2.Zero; break;
				};
				var childrenCount = children.Count + 1;
				var lengthGrowth = factor.X / (MathF.Pow(lr * Length * childrenCount, 0.1f) * AgroWorld.TicksPerHour);
				var widthGrowth = factor.Y / (MathF.Pow(lifeSupportPerHour * childrenCount, 0.1f) * AgroWorld.TicksPerHour); //just optimized the number of multiplications

				Length += lengthGrowth;
				Radius += widthGrowth;

				if (Organ == OrganTypes.Stem)
				{
					mPhotoFactor -= widthGrowth * childrenCount; //become wood faste with children
					if (mPhotoFactor < 0f)
						mPhotoFactor = 0f;
				}

				//Chaining
				if (false && Organ == OrganTypes.Stem && !isLeafStem && Length > PlantFormation.RootSegmentLength * 0.5f + formation.RNG.NextFloat(PlantFormation.RootSegmentLength * 0.5f))
				{
					//split this stem into two parts
					var lowerIndex = formation.AboveGroundBirth(new AboveGroundAgent(Parent, OrganTypes.Stem, Orientation, Energy * 0.5f, Radius, Length * 0.5f){ Water = Water * 0.5f, mPhotoFactor = mPhotoFactor });
					Parent = lowerIndex;
					Energy *= 0.5f;
					Water *= 0.5f;
					//TODO reduce the radius according to the children
				}
				//Console.WriteLine($"{formationID}x{timestep}: l={Length} r={Radius} OK={Length > Radius}");
			}
		}
		else if (Energy > 0) //if running out of energy, balance it by thaking it away from children
		{            
			if (children != null && children.Count > 0)
				foreach(var child in children)
				{
					var childEnergy = formation.GetEnergy_AG(child);
					if (childEnergy > Energy)
						formation.Send(child, new Energy_AG_PullFrom_AG(formation, 2f * Radius * Radius / AgroWorld.TicksPerHour, formationID));
				}
		}
		else //Without energy the part dies
		{
			formation.AboveGroundDeath(formationID);
			return;
		}
		
		//Photosynthesis
		var photosynthesizedEnergy = 0f;
		if ((Organ == OrganTypes.Stem || Organ == OrganTypes.Leaf) && Water > 0f)
		{
			var approxLight = AgroWorld.GetAmbientLight(timestep);
			if (approxLight > 0.2f)
			{
				var airTemp = AgroWorld.GetTemperature(timestep);
				var surface = Length * Radius * (Organ == OrganTypes.Leaf ? 2f : TwoPi);
				var possibleAmountByLight = surface * approxLight * mPhotoFactor;
				var possibleAmountByWater = Water * (Organ == OrganTypes.Stem ? 0.7f : 1f);
				var possibleAmountByCO2 = airTemp >= formation.VegetativeHighTemperature.Y 
					? 0f 
					: (airTemp <= formation.VegetativeHighTemperature.X 
						? float.MaxValue
						: surface * (airTemp - formation.VegetativeHighTemperature.X) / (formation.VegetativeHighTemperature.Y - formation.VegetativeHighTemperature.X)); //TODO respiratory cycle

				photosynthesizedEnergy = Math.Min(possibleAmountByLight, Math.Min(possibleAmountByWater, possibleAmountByCO2));

				Water -= photosynthesizedEnergy;
				Energy += photosynthesizedEnergy;
			}
		}

		if (Organ != OrganTypes.Shoot)
		{
			///////////////////////////
			#region Transport ENERGY
			///////////////////////////
			if (Parent >= 0)
			{
				var parentEnergy = formation.GetEnergy_AG(Parent);
				if (parentEnergy < Energy)
				{
					var amount = EnergyForParent(photosynthesizedEnergy, lifeSupportPerTick, parentEnergy);
					if (amount > 0)
						formation.Send(Parent, new Energy_AG_PullFrom_AG(formation, Math.Min(amount, EnergyFlowToParentPerTick), formationID)); //TODO make requests based on own need and the need of children
				}
			}
			else
			{
				var parentEnergy = formation.GetEnergy_UG(0);
				if (parentEnergy < Energy)
				{
					var amount = EnergyForParent(photosynthesizedEnergy, lifeSupportPerTick, parentEnergy);
					if (amount > 0)
						formation.Send(formationID, new UnderGroundAgent.Energy_UG_PullFrom_AG(formation, Math.Min(amount, EnergyFlowToParentPerTick), 0)); //TODO make requests based on own need and the need of children
				}
			}
			#endregion

			///////////////////////////
			#region Transport WATER
			///////////////////////////
			var freeCapacity = WaterCapacityPerTick - Water;
			if (freeCapacity > 0) //TODO different coefficients for different organs and different state (amount of wood)
			{
				if (Parent >= 0)
					formation.Send(Parent, new Water_AG_PullFrom_AG(formation, Math.Min(WaterFlowToParentPerTick, freeCapacity), formationID));
			}
			#endregion
		}
	}

	float EnergyForParent(float photosynthesizedEnergy, float lifeSupportPerTick, float parentEnergy) => 
		Math.Max(0f, photosynthesizedEnergy > lifeSupportPerTick ? Energy - lifeSupportPerTick * 2f : Math.Min((Energy - parentEnergy) * 0.5f, Energy - lifeSupportPerTick * 2f));

	public void IncWater(float amount)
	{
		Debug.Assert(amount >= 0f);
		Water += amount;
	}
	public void IncEnergy(float amount)
	{
		Debug.Assert(amount >= 0f);
		Energy += amount;
	}

	internal float TryDecWater(float amount)
	{
		Debug.Assert(amount >= 0f);
		if (Water > amount)
		{
			Water -= amount;
			return amount;
		}
		else
		{
			var w = Water;
			Water = 0f;
			return w;
		}
	}

	internal float TryDecEnergy(float amount)
	{
		Debug.Assert(amount >= 0f);
		if (Energy > amount)
		{
			Energy -= amount;
			return amount;
		}
		else
		{
			var e = Energy;
			Energy = 0f;
			return e;
		}
	}

	// internal AboveGroundAgent AddChild(int index)
	// {
	// 	var result = default(AboveGroundAgent);
	// 	result.Orientation = Orientation;
	// 	result.Radius = Radius;
	// 	result.Length = Length;
	// 	result.Energy = Energy;
	// 	result.Water = Water;
	// 	result.mPhotoFactor = mPhotoFactor;
	// 	result.Organ = Organ;
	// 	result.Parent = Parent;

	// 	if (Children == null)
	// 		result.Children = new int[]{index};
	// 	else
	// 	{
	// 		var children = new int[Children.Length];
	// 		Array.Copy(Children, children, Children.Length);
	// 		children[^1] = index;
	// 		result.Children = children;
	// 	}
		
	// 	return result;
	// }

	// internal void RemoveChild(int index)
	// {
	// 	Debug.Assert(Children.Contains(index));
	// 	if (Children.Length == 1)
	// 		Children = null;
	// 	else
	// 	{
	// 		var children = new int[Children.Length - 1];
	// 		for(int s = 0, t = 0; s < Children.Length; ++s)
	// 			if (Children[s] != index)
	// 				children[t++] = Children[s];
	// 		Children = children;
	// 	}
	// }
}
