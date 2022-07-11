using System;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using AgentsSystem;

namespace Agro;

//TODO IMPORTANT All resource transport should be request-confirm messages, i.e. pull-policy.
//  There should never be forced resource push since it may not be able to fit into the available storage.
//TODO Create properties computing the respective storages for water and energy. Just for clarity.

public readonly struct WaterFromUnderGroundMsg : IMessage<AboveGroundAgent>
{
	public readonly float Amount;
	public WaterFromUnderGroundMsg (float amount) => Amount = amount;
	public void Receive(ref AboveGroundAgent agent) => agent.IncWater(Amount);
}

public readonly struct AboveGroundWaterRequestMsg : IMessage<AboveGroundAgent>
{
	public readonly float Amount;
	public readonly PlantFormation Formation;
	public readonly int Index;
	public AboveGroundWaterRequestMsg(PlantFormation formation, int index, float amount)
	{
		Amount = amount;
		Formation = formation;
		Index = index;
	}

	public void Receive(ref AboveGroundAgent agent)
	{
		var water = agent.TryDecWater(Amount);
		if (water > 0)
			Formation.Send(Index, new WaterFromUnderGroundMsg(water));
	}
}

public readonly struct EnergyBetweenAboveGroundsMsg : IMessage<AboveGroundAgent>
{
	public readonly float Amount;
	public EnergyBetweenAboveGroundsMsg(float amount) => Amount = amount;
	public void Receive(ref AboveGroundAgent agent) => agent.IncEnergy(Amount);
}

public readonly struct AboveGroundEnergyRequestToAboveGround: IMessage<AboveGroundAgent>
{
	public readonly float Amount;
	public readonly PlantFormation Formation;
	public readonly int Index;
	public AboveGroundEnergyRequestToAboveGround(PlantFormation formation, float amount, int index)
	{
		Amount = amount;
		Formation = formation;
		Index = index;
	}

	public void Receive(ref AboveGroundAgent agent)
	{
		var energy = agent.TryDecEnergy(Amount);
		if (energy > 0)
			Formation.Send(Index, new EnergyBetweenAboveGroundsMsg(energy));
	}
}

public readonly struct AboveGroundEnergyRequestToUnderGround: IMessage<AboveGroundAgent>
{
	public readonly float Amount;
	public readonly PlantFormation Formation;
	public readonly int Index;
	public AboveGroundEnergyRequestToUnderGround(PlantFormation formation, float amount, int index)
	{
		Amount = amount;
		Formation = formation;
		Index = index;
	}

	public void Receive(ref AboveGroundAgent agent)
	{
		var energy = agent.TryDecEnergy(Amount);
		if (energy > 0)
			Formation.Send(Index, new EnergyBetweenAboveGroundsMsg(energy));
	}
}


//[Flags]  //flags are not needed anymore
public enum OrganTypes { Unspecified = 0, Seed = 1, Root = 2, Stem = 4, Sooth = 8, Leaf = 16, Fruit = 32 };

public record struct AboveGroundAgent : IAgent
{
	public Quaternion Direction  { get; private set; }
	public float Radius { get; private set; }
	public float Length { get; private set; }

	public float Energy { get; private set; }
	//float mEnergyPassingDown;
	//float mEnergyPassignUp;

	public float Water { get; private set; }
	//float mWaterPassingUp;

	float mPhotoFactor; //Capability of photosynthesis

	public OrganTypes Organ { get; private set; }

	public int Parent { get; private set; }
	public int[] Children { get; private set; }

	public AboveGroundAgent(int parent, OrganTypes organ, Quaternion parentDir, Vector3 unitDirection, float radius, float length, float initialEnergy)
	{
		Parent = parent;
		Radius = radius;
		Length = length;

		Debug.Assert(Math.Abs(unitDirection.LengthSquared() - 1f) < 1e-6f);
		var parentVec = Vector3.Transform(Vector3.UnitX, parentDir);        
		var zVec = Vector3.Cross(Vector3.Dot(unitDirection, parentVec) > 0.999f ? unitDirection + new Vector3(0.5f, 0.5f, 0.5f) : unitDirection, parentVec);
		var mat = new Matrix4x4(
			unitDirection.X, unitDirection.Y, unitDirection.Z, 0f,
			parentVec.X, parentVec.Y, parentVec.Z, 0f,
			zVec.X, zVec.Y, zVec.Z, 0f,
			0f, 0f, 0f, 1f);
		Direction = Quaternion.CreateFromRotationMatrix(mat);

		Organ = organ;

		Energy = initialEnergy;
		mPhotoFactor = 1f;
		// EnergyPassingDown = 0f;
		// EnergyPassignUp = 0f;

		Water = 0f;

		Children = null;
	}

	public static void Reindex(AboveGroundAgent[] src, int[] map)
	{
		for(int i = 0; i < src.Length; ++i)
		{
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
			if (src[i].Children != null)
				for(int j = 0; j < src[i].Children.Length; ++j)
					src[i].Children[j] = map[src[i].Children[j]];
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

		//Growth
		if (Energy > lifeSupportPerHour * 36) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage        
		{
			var widthFactor = Organ switch
			{
				OrganTypes.Leaf => 1e-4f,
				_ => 1e-5f,
			};
			var childrenCount = (Children?.Length ?? 0) + 1;
			var lengthGrowth = (1e-4f / AgroWorld.TicksPerHour) / MathF.Pow(lr * Length * childrenCount, 0.1f);
			var widthGrowth = (widthFactor / AgroWorld.TicksPerHour) / MathF.Pow(lifeSupportPerHour * childrenCount, 0.1f); //just optimized the number of multiplications

			Length += lengthGrowth;
			Radius += widthGrowth;

			if (Organ == OrganTypes.Stem)
			{
				mPhotoFactor -= widthGrowth * childrenCount; //become wood faste with children
				if (mPhotoFactor < 0f)
					mPhotoFactor = 0f;
			}
		}
		else if (Energy > 0) //if running out of energy, balance it by thaking it away from children
		{            
			if (Children != null && Children.Length > 0)
				foreach(var child in Children)
				{
					var childEnergy = formation.GetAboveGroundEnergy(child);
					if (childEnergy > Energy)
						formation.Send(child, new AboveGroundEnergyRequestToAboveGround(formation, 2f * Radius * Radius / AgroWorld.TicksPerHour, formationID));
				}
		}
		else //Without energy the part dies
		{
			formation.AboveGroundDeath(formationID);
			return;
		}
		
		//Photosynthesis
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

				var energyGain = Math.Min(possibleAmountByLight, Math.Min(possibleAmountByWater, possibleAmountByCO2));

				Water -= energyGain;
				Energy += energyGain;
			}
		}

		//Transport energy        
		if (Parent >= 0)
		{
			var parentEnergy = formation.GetAboveGroundEnergy(Parent);
			if (parentEnergy > Energy)
				formation.Send(Parent, new AboveGroundEnergyRequestToAboveGround(formation, lifeSupportPerTick, formationID)); //TODO make requests based on own need and the need of children
		}
		else
		{
			var parentEnergy = formation.GetUnderGroundEnergy(0);
			if (parentEnergy > Energy)
				formation.Send(0, new UnderGroundEnergyRequestToAboveGround(formation, lifeSupportPerTick, formationID)); //TODO make requests based on own need and the need of children
		}

		var waterCapacity = lifeSupportPerHour * 0.7f;
		if (Water < waterCapacity) //TODO different coefficients for different organs and different state (amount of wood)
		{
			if (Parent >= 0)
				formation.Send(Parent, new AboveGroundWaterRequestMsg(formation, formationID, Math.Min(Radius * Radius / AgroWorld.TicksPerHour, waterCapacity - Water)));
		}
	}

	public void IncWater(float amount) => Water += amount;
	public void IncEnergy(float amount) => Energy += amount * 0.99f;

	internal float TryDecWater(float amount)
	{
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

	internal AboveGroundAgent AddChild(int index)
	{
		var result = default(AboveGroundAgent);
		result.Direction = Direction;
		result.Radius = Radius;
		result.Length = Length;
		result.Energy = Energy;
		result.Water = Water;
		result.mPhotoFactor = mPhotoFactor;
		result.Organ = Organ;
		result.Parent = Parent;

		if (Children == null)
			result.Children = new int[]{index};
		else
		{
			var children = new int[Children.Length];
			Array.Copy(Children, children, Children.Length);
			children[^1] = index;
			result.Children = children;
		}
		
		return result;
	}

	internal void RemoveChild(int index)
	{
		Debug.Assert(Children.Contains(index));
		if (Children.Length == 1)
			Children = null;
		else
		{
			var children = new int[Children.Length - 1];
			for(int s = 0, t = 0; s < Children.Length; ++s)
				if (Children[s] != index)
					children[t++] = Children[s];
			Children = children;
		}
	}
}
