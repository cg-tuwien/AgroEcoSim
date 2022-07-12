using System;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentsSystem;

namespace Agro;

public interface IUnderGround : IAgent
{
    /// <summary>
    /// Water volume in m³
    /// </summary>
    void IncWater(float amount);
}

[StructLayout(LayoutKind.Auto)]
public readonly struct Water_UG_PullFrom_Soil : IMessage<SoilAgent>
{
    /// <summary>
    /// Water volume in m³
    /// </summary>
    public readonly float Amount;
    public readonly PlantFormation DstFormation;
    public readonly int DstIndex;
    public Water_UG_PullFrom_Soil(PlantFormation dstFormation, float amount, int dstIndex)
    {
        Amount = amount;
        DstFormation = dstFormation;
        DstIndex = dstIndex;
    }
    public void Receive(ref SoilAgent srcAgent)
    {
        var water = srcAgent.TryDecWater(Math.Min(Amount, DstFormation.GetWaterCapacity_UG(DstIndex)));
        DstFormation.IncWater_UG(DstIndex, water);
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct Energy_UG_PullFrom_UG: IMessage<UnderGroundAgent>
{
    public readonly float Amount;
    public readonly PlantFormation DstFormation;
    public readonly int DstIndex;
    public Energy_UG_PullFrom_UG(PlantFormation dstFormation, float amount, int dstIndex)
    {
        Amount = amount;
        DstFormation = dstFormation;
        DstIndex = dstIndex;
    }

    public void Receive(ref UnderGroundAgent srcAgent)
    {
        var capacity = DstFormation.GetEnergyCapacity_UG(DstIndex);
        var energy = srcAgent.TryDecEnergy(Math.Min(capacity, Amount));

        if (energy > 0)
            DstFormation.IncEnergy_UG(DstIndex, energy);
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct Energy_UG_PullFrom_AG: IMessage<AboveGroundAgent>
{
    public readonly float Amount;
    public readonly PlantFormation DstFormation;
    public readonly int DstIndex;
    public Energy_UG_PullFrom_AG(PlantFormation dstFormation, float amount, int dstIndex)
    {
        Amount = amount;
        DstFormation = dstFormation;
        DstIndex = dstIndex;
    }

    public void Receive(ref AboveGroundAgent srcAgent)
    {
        var energy = srcAgent.TryDecEnergy(Math.Min(Amount, DstFormation.GetEnergyCapacity_UG(DstIndex)));
        DstFormation.IncEnergy_UG(DstIndex, energy);
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct Water_UG_PushTo_UG : IMessage<UnderGroundAgent>
{
    /// <summary>
    /// Water volume in m³
    /// </summary>    
    public readonly float Amount;
    public readonly PlantFormation SrcFormation;
    public readonly int SrcIndex;
    public Water_UG_PushTo_UG(PlantFormation srcFormation, float amount, int srcIndex)
    {
        Amount = amount;
        SrcFormation = srcFormation;
        SrcIndex = srcIndex;
    }
    public void Receive(ref UnderGroundAgent dstAgent)
    {
        var water = SrcFormation.TryDecWater_UG(SrcIndex, Math.Min(Amount, dstAgent.WaterCapacity));
        dstAgent.IncWater(water);
    }
}

[StructLayout(LayoutKind.Auto)]
public struct UnderGroundAgent : IUnderGround
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
    /// Inverse woodyness \in [0, 1]. The more woody (towards 0) the less water the root can absorb. 
    /// </summary>
    float WaterAbsorbtionFactor; //factor 0 .. 1

    /// <summary>
    /// Index of the parent agent. -1 represents the root of the hierarchy.
    /// </summary>
    public int Parent { get; private set; }

    /// <summary>
    /// Recommended initial length of the agent at birth in m.
    /// </summary>
    public const float InitialLength = 1e-5f;

    /// <summary>
    /// Recommended initial bottom face radius of the agent at birth in m.
    /// </summary>
    public const float InitialRadius = 0.2e-5f;

    /// <summary>
    /// Water volume in m³ that can be absorbed per m² of root surface per hour
    /// </summary>
    public const float WaterAbsortionRatio = 1f;

    /// <summary>
    /// Water volume in m³ which can be absorbed from soil per hour
    /// </summary>
    public float WaterAbsorbtionPerHour => Radius * 8f * Length * WaterAbsortionRatio;

    /// <summary>
    /// Water volume in m³ which can be absorbed from soil per timestep
    /// </summary>
    public float WaterAbsorbtionPerTick => WaterAbsorbtionPerHour / AgroWorld.TicksPerHour;

    public const float EnergyTransportRatio = 2f;
    public const float WaterTransportRatio = 2f;

    /// <summary>
    /// Water volume in m³ which can be passed to the parent per hour
    /// </summary>
    public float WaterFlowToParentPerHour
    {
        get
        {
            var d = Radius * 2f;
            return d * d * WaterTransportRatio;
        }
    }

    /// <summary>
    /// Water volume in m³ which can be passed to the parent per timestep
    /// </summary>
    public float WaterFlowToParentPerTick => WaterFlowToParentPerHour / AgroWorld.TicksPerHour;

    public float EnergyFlowToParentPerHour
    {
        get
        {
            var d = Radius * 2f;
            return d * d * WaterTransportRatio;
        }
    }
    public float EnergyFlowToParentPerTick => EnergyFlowToParentPerHour / AgroWorld.TicksPerHour;

    public float EnergyCapacity
    {
        get
        {
            var d = Radius * 2f;
            return d * d * Length * (1f - WaterCapacityRatio);
        }
    }

    /// <summary>
    /// Volume ratio \in [0, 1] of the agent that can used for storing water
    /// </summary>
    const float WaterCapacityRatio = 0.75f;

    /// <summary>
    /// Water volume in m³ which can be stored in this agent
    /// </summary>
    public float WaterCapacity
    {
        get
        {
            var d = Radius * 2f;
            return d * d * Length * WaterCapacityRatio;
        }
    }

    public UnderGroundAgent(int parent, Quaternion parentDir, Vector3 unitDirection, float initialEnergy, float initialWater = 0f, float initialWaterIntake = 1f, float radius = InitialRadius, float length = InitialLength)
    {
        Parent = parent;
        Radius = radius;
        Length = length;

        Debug.Assert(Math.Abs(unitDirection.LengthSquared() - 1f) < 1e-6f);
        var parentVec = parentDir.LengthSquared() == 0f ? -Vector3.UnitY : Vector3.Transform(Vector3.UnitX, parentDir);
        if (Vector3.Dot(unitDirection, parentVec) > 0.999f)
            parentVec = Vector3.Normalize(parentVec + new Vector3(0.5f, 0.5f, 0.5f));

        var zVec = Vector3.Normalize(Vector3.Cross(unitDirection, parentVec));
        parentVec = Vector3.Normalize(Vector3.Cross(zVec, unitDirection));

        var mat = new Matrix4x4(
            unitDirection.X, unitDirection.Y, unitDirection.Z, 0f,
            parentVec.X, parentVec.Y, parentVec.Z, 0f,
            zVec.X, zVec.Y, zVec.Z, 0f,
            0f, 0f, 0f, 1f);
        Orientation = Quaternion.CreateFromRotationMatrix(mat);

        Energy = initialEnergy;
        Water = initialWater;
        WaterAbsorbtionFactor = initialWaterIntake;
    }

    public UnderGroundAgent(int parent, Quaternion dir, float initialEnergy)
    {
        Parent = parent;
        Radius = InitialRadius;
        Length = InitialLength;
        Orientation = dir;

        Energy = initialEnergy;
        Water = 0f;
        WaterAbsorbtionFactor = 1f;
    }

	public static void Reindex(UnderGroundAgent[] src, int[] map)
	{
		for(int i = 0; i < src.Length; ++i)
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
	}

    public void Tick(SimulationWorld world, IFormation _formation, int formationID, uint timestep)
    {
        var formation = (PlantFormation)_formation;

        //TODO perhaps it should somehow reflect temperature
        var lr = Length * Radius;
        var lifeSupportPerHour = lr * Radius;
        var lifeSupportPerTick = lifeSupportPerHour / AgroWorld.TicksPerHour;

        //life support
        Energy -= lifeSupportPerTick;

        var children = formation.GetUnderGroundChildren(formationID);
        //Growth
        if (Energy > lifeSupportPerHour * 36) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage        
        {
            var childrenCount = children.Count + 1;
            childrenCount *= childrenCount;
            var lengthGrowth = 1e-4f / (AgroWorld.TicksPerHour * childrenCount * MathF.Pow(lr * Length, 0.1f));
            var widthGrowth = 1e-5f / (AgroWorld.TicksPerHour * childrenCount * MathF.Pow(lifeSupportPerHour, 0.1f)); //just optimized the number of multiplications
            
            Length += lengthGrowth;
            Radius += widthGrowth;

            //Console.WriteLine($"{timestep} x {formationID}: dl={lengthGrowth} dr={widthGrowth} L={Length} R={Radius}");

            WaterAbsorbtionFactor -= widthGrowth * childrenCount;  //become wood faste with children
            if (WaterAbsorbtionFactor < 0f)
                WaterAbsorbtionFactor = 0f;

            if (children.Count == 0 && Length > PlantFormation.RootSegmentLength * 0.5f + formation.RNG.NextFloat(PlantFormation.RootSegmentLength * 0.5f))
            {
                var q1 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, formation.RNG.NextFloat(MathF.PI * 0.04f));
                var q2 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, formation.RNG.NextFloat(MathF.PI * 0.04f));
                var q = Quaternion.Concatenate(q1, q2);
                formation.UnderGroundBirth(new UnderGroundAgent(formationID, Quaternion.Concatenate(Orientation, q), Energy * 0.01f));
                Energy *= 0.98f;
                Console.WriteLine($"New root child of {formationID} at time {timestep}");
            }

            //Branching work in progress
            //     //TODO this is box based, should be pyramid or cone based
            //     const float length = 0.49f;
            //     const float complLength = 1f - 2f * length;

            //     const float ratio = 0.49f;
            //     const float complRatio = (1f - 3f * ratio) / 3f;
            //     var dir = Vector3.Transform(Vector3.UnitX, Direction);
            //     var childDir = Vector3.Normalize((dir + 0.1f * new Vector3(0.1f, 0.2f, 0.1f)));

            //     var nodeID = formation.UnderGroundBirth(new UnderGroundAgent(formationID, Direction, dir, 0.0001f, Length * complLength, Energy * complRatio));
            //     formation.UnderGroundBirth(new UnderGroundAgent(nodeID, Direction, childDir, 0.0001f, 0.0001f, Energy * complRatio));

            //     formation.UnderGroundBirth(new UnderGroundAgent(nodeID, Direction, dir, 0.0001f, Length * ratio, Energy * ratio));

            //     Length *= 0.49f;
            // }

        }
        else if (Energy > 0) //if running out of energy, balance it by taking it away from children
        {
            //Console.WriteLine($"Root {formationID} starving at time {timestep}");
            foreach(var child in children)
            {
                var childEnergy = formation.GetEnergy_UG(child);
                if (childEnergy > Energy)
                    formation.Send(child, new Energy_UG_PullFrom_UG(formation, Math.Min(formation.GetEnergyFlow_PerTick_UG(child), (Energy - childEnergy) * 0.5f), formationID));
            }
        }
        else //Without energy the part dies
        {
            Console.WriteLine($"Root {formationID} depleeted at time {timestep}");
            formation.UnderGroundDeath(formationID);
            return;
        }

        ///////////////////////////
        #region Absorb WATER from soil
        ///////////////////////////
        if (WaterAbsorbtionFactor > 0f)
        {
            if (Water < WaterCapacity)
            {
                var soil = formation.Soil;
                var baseCenter = formation.GetBaseCenter_UG(formationID);
                //find all soild cells that the shpere intersects
                var sources = soil.IntersectSphere(baseCenter + Vector3.Transform(Vector3.UnitX, Orientation) * Length * 0.75f, Length * 0.25f); //TODO make a tube intersection
        
                var vegetativeTemp = formation.VegetativeLowTemperature;
                
                if (sources.Count > 0) //TODO this is a rough approximation taking only the first intersected soil cell
                {
                    var amount = WaterAbsorbtionPerTick;
                    var soilTemperature = soil.GetTemperature(sources[0]);
                    if (soilTemperature > vegetativeTemp.X)
                    {
                        if (soilTemperature < vegetativeTemp.Y)
                            amount *= (soilTemperature - vegetativeTemp.X) / (vegetativeTemp.Y - vegetativeTemp.X);
                        soil.Send(sources[0], new Water_UG_PullFrom_Soil(formation, amount, formationID)); //TODO change to tube surface!
                    }
                }
            }
        }
        else
            WaterAbsorbtionFactor = 0f;
        #endregion

        ///////////////////////////
        #region Transport ENERGY
        ///////////////////////////
        if (Parent >= 0)
        {
            var parentEnergy = formation.GetEnergy_UG(Parent);
            if (parentEnergy > Energy)
                formation.Send(Parent, new Energy_UG_PullFrom_UG(formation, Math.Min(EnergyFlowToParentPerTick, (parentEnergy - Energy) * 0.5f), formationID)); //TODO make requests based on own need and the need of children
        }
        else
        {
            var parentEnergy = formation.GetEnergy_AG(0);
            if (parentEnergy > Energy)
                formation.Send(0, new Energy_UG_PullFrom_AG(formation, Math.Min(formation.GetEnergyFlow_PerTick_AG(0), (parentEnergy - Energy) * 0.5f), formationID)); //TODO make requests based on own need and the need of children
        }
        #endregion

        ///////////////////////////
        #region Transport WATER
        ///////////////////////////
        if (Water > 0f)
        {
            if (Parent >= 0)
                formation.Send(Parent, new Water_UG_PushTo_UG(formation, WaterFlowToParentPerTick, formationID));
            else
                formation.Send(0, new Water_UG_PushTo_AG(formation, WaterFlowToParentPerTick, formationID));
        }
        #endregion
    }

    public void IncWater(float amount) => Water += amount;
    public void IncEnergy(float amount) => Energy += amount;

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

	// internal UnderGroundAgent AddChild(int index)
	// {
	// 	var result = default(UnderGroundAgent);
	// 	result.Direction = Direction;
	// 	result.Radius = Radius;
	// 	result.Length = Length;
	// 	result.Energy = Energy;
	// 	result.Water = Water;
    //     result.mWaterIntake = mWaterIntake;
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
