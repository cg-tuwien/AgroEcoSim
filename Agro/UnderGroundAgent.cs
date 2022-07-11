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
    void IncWater(float amount);
}

[StructLayout(LayoutKind.Auto)]
public readonly struct SoilWaterToUnderGroundMsg : IMessage<UnderGroundAgent>
{
    /// <summary>
    /// Water volume in m³
    /// </summary>    
    public readonly float Amount;
    public SoilWaterToUnderGroundMsg(float amount) => Amount = amount;
    public void Receive(ref UnderGroundAgent agent) => agent.IncWater(Amount);
}

[StructLayout(LayoutKind.Auto)]
public readonly struct UnderGroundWaterRequestToSoilMsg : IMessage<SoilAgent>
{
    public readonly float Amount;
    public readonly PlantFormation Formation;
    public readonly int Index;
    public UnderGroundWaterRequestToSoilMsg(PlantFormation formation, float amount, int index)
    {
        Amount = amount;
        Formation = formation;
        Index = index;
    }
    public void Receive(ref SoilAgent agent)
    {
        var water = agent.TryDecWater(Amount);
        if (water > 0)
            Formation.Send(Index, new SoilWaterToUnderGroundMsg(water));
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct EnergyBetweenUnderGroundsMsg : IMessage<UnderGroundAgent>
{
    public readonly float Amount;
    public EnergyBetweenUnderGroundsMsg(float amount) => Amount = amount;

    public void Receive(ref UnderGroundAgent agent) => agent.IncEnergy(Amount);
}

[StructLayout(LayoutKind.Auto)]
public readonly struct UnderGroundEnergyRequestToUnderGround: IMessage<UnderGroundAgent>
{
    public readonly float Amount;
    public readonly PlantFormation Formation;
    public readonly int Index;
    public UnderGroundEnergyRequestToUnderGround(PlantFormation formation, float amount, int index)
    {
        Amount = amount;
        Formation = formation;
        Index = index;
    }

    public void Receive(ref UnderGroundAgent agent)
    {
        var energy = agent.TryDecEnergy(Amount);
        if (energy > 0)
            Formation.Send(Index, new EnergyBetweenUnderGroundsMsg(energy));
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct UnderGroundEnergyRequestToAboveGround: IMessage<AboveGroundAgent>
{
    public readonly float Amount;
    public readonly PlantFormation Formation;
    public readonly int Index;
    public UnderGroundEnergyRequestToAboveGround(PlantFormation formation, float amount, int index)
    {
        Amount = amount;
        Formation = formation;
        Index = index;
    }

    public void Receive(ref AboveGroundAgent agent)
    {
        var energy = agent.TryDecEnergy(Amount);
        if (energy > 0)
            Formation.Send(Index, new EnergyBetweenUnderGroundsMsg(energy));
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly struct WaterBetweenUnderGroundMsg : IMessage<UnderGroundAgent>
{
    public readonly float Amount;
    public WaterBetweenUnderGroundMsg(float amount) => Amount = amount;
    public void Receive(ref UnderGroundAgent agent) => agent.IncWater(Amount);
}

[StructLayout(LayoutKind.Auto)]
public struct UnderGroundAgent : IUnderGround
{   
    public Quaternion Direction { get; private set; }
    public float Length { get; private set; }
    public float Radius { get; private set; }

    public float Energy { get; private set; }
    //float mEnergyPassingDown;
    //float mEnergyPassignUp;

    public float Water { get; private set; }
    //float mWaterPassingUp;
    float mWaterIntake; //factor 0 .. 1

    public int Parent { get; private set; }
    public int[] Children { get; private set; }

    public UnderGroundAgent(int parent, Quaternion parentDir, Vector3 unitDirection, float radius, float length, float initialEnergy)
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
        Direction = Quaternion.CreateFromRotationMatrix(mat);

        Energy = initialEnergy * 0.9f;
        //mEnergyPassingDown = 0f;
        //mEnergyPassignUp = 0f;
        
        Water = 0f;
        mWaterIntake = 1f;
        //mWaterPassingUp = 0f;

        Parent = parent;
        Children = null;
    }

	public static void Reindex(UnderGroundAgent[] src, int[] map)
	{
		for(int i = 0; i < src.Length; ++i)
		{
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
			if (src[i].Children != null)
				for(int j = 0; j < src[i].Children.Length; ++j)
					src[i].Children[j] = map[src[i].Children[j]];
		}
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

        //Growth
        if (Energy > lifeSupportPerHour * 36) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage        
        {
            var childrenCount = (Children?.Length ?? 0) + 1;
            var lengthGrowth = (1e-4f / AgroWorld.TicksPerHour) / MathF.Pow(lr * Length * childrenCount, 0.1f);
            var widthGrowth = (1e-5f / AgroWorld.TicksPerHour) / MathF.Pow(lifeSupportPerHour * childrenCount, 0.1f); //just optimized the number of multiplications
            
            Length += lengthGrowth;
            Radius += widthGrowth;

            mWaterIntake -= widthGrowth * childrenCount;  //become wood faste with children
            if (mWaterIntake < 0f)
                mWaterIntake = 0f;
        }
        else if (Energy > 0) //if running out of energy, balance it by thaking it away from children
        {            
            if (Children != null && Children.Length > 0)
                foreach(var child in Children)
                {
                    var childEnergy = formation.GetUnderGroundEnergy(child);
                    if (childEnergy > Energy)
                        formation.Send(child, new UnderGroundEnergyRequestToUnderGround(formation, 2f * Radius * Radius / AgroWorld.TicksPerHour, formationID));
                }
        }
        else //Without energy the part dies
        {
            formation.UnderGroundDeath(formationID);
            return;
        }

        //Take water from soil (only if the intake factor is > 0)
        if (mWaterIntake > 0f)
        {
            if (Water < lifeSupportPerHour * 0.3f)
            {
                var soil = formation.Soil;
                var baseCenter = formation.GetUnderGroundBaseCenter(formationID);
                //find all soild cells that the shpere intersects
                var sources = soil.IntersectSphere(baseCenter + Vector3.Transform(Vector3.UnitX, Direction) * Length * 0.75f, Length * 0.25f); //TODO make a tube intersection
        
                var vegetativeTemp = formation.VegetativeLowTemperature;
                
                if (sources.Count > 0) //TODO this is a rough approximation taking only the first intersected soil cell
                {
                    var amount = lifeSupportPerHour * 0.3f * mWaterIntake;
                    var soilTemperature = soil.GetTemperature(sources[0]);
                    if (soilTemperature > vegetativeTemp.X)
                    {
                        if (soilTemperature < vegetativeTemp.Y)
                            amount *= (soilTemperature - vegetativeTemp.X) / (vegetativeTemp.Y - vegetativeTemp.X);
                        soil.Send(sources[0], new UnderGroundWaterRequestToSoilMsg(formation, amount / AgroWorld.TicksPerHour, formationID)); //TODO change to tube surface!
                    }
                }
            }
        }
        else
            mWaterIntake = 0f;

        //Transport energy
        if (Parent >= 0)
        {
            var parentEnergy = formation.GetUnderGroundEnergy(Parent);
            if (parentEnergy > Energy)
                formation.Send(Parent, new UnderGroundEnergyRequestToUnderGround(formation, lifeSupportPerTick, formationID)); //TODO make requests based on own need and the need of children
        }
        else
        {
            var parentEnergy = formation.GetAboveGroundEnergy(0);
            if (parentEnergy > Energy)
                formation.Send(0, new UnderGroundEnergyRequestToAboveGround(formation, lifeSupportPerTick, formationID)); //TODO make requests based on own need and the need of children
        }

        if (Water > 0f)
        {
            if (Parent >= 0)
                formation.Send(Parent, new WaterBetweenUnderGroundMsg(Math.Min(Radius * Radius / AgroWorld.TicksPerHour, Water)));
            else
                formation.Send(0, new WaterFromUnderGroundMsg(Math.Min(Radius * Radius / AgroWorld.TicksPerHour, Water)));
        }
    }

    public void IncWater(float amount) => Water += amount * 0.97f;
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

    internal void AddChild(int index)
    {
		if (Children == null)
			Children = new int[]{index};
		else
		{
			var children = new int[Children.Length];
			Array.Copy(Children, children, Children.Length);
			children[^1] = index;
			Children = children;
		}
    }

	internal void RemoveChild(int index)
	{
		Debug.Assert(Children.Contains(index));
		var children = new int[Children.Length - 1];
		for(int s = 0, t = 0; s < Children.Length; ++s)
			if (Children[s] != index)
				children[t++] = Children[s];
		Children = children;
	}
}
