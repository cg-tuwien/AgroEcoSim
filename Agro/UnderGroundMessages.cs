using System;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentsSystem;

namespace Agro;

public partial struct UnderGroundAgent : IAgent
{
    [StructLayout(LayoutKind.Auto)]
    public readonly struct WaterInc : IMessage<UnderGroundAgent>
    {
        public readonly float Amount;
        public WaterInc(float amount) => Amount = amount;
        public void Receive(ref UnderGroundAgent dstAgent) => dstAgent.IncWater(Amount);
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly struct EnergyInc : IMessage<UnderGroundAgent>
    {
        public readonly float Amount;
        public EnergyInc(float amount) => Amount = amount;
        public void Receive(ref UnderGroundAgent dstAgent) => dstAgent.IncEnergy(Amount);
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
            var freeCapacity = DstFormation.GetWaterCapacityPerTick_UG(DstIndex) - DstFormation.GetWater_UG(DstIndex);
            var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
            //Writing actions from other formations must not be implemented directly, but over messages
            if (water > 0) DstFormation.Send(DstIndex, new WaterInc(water));
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
            var freeCapacity = DstFormation.GetEnergyCapacity_UG(DstIndex) - DstFormation.GetEnergy_UG(DstIndex);
            var energy = srcAgent.TryDecEnergy(Math.Min(freeCapacity, Amount));
            if (energy > 0) DstFormation.Send(DstIndex, new EnergyInc(energy));
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly struct Energy_AG_PullFrom_UG: IMessage<AboveGroundAgent>
    {
        public readonly float Amount;
        public readonly PlantFormation DstFormation;
        public readonly int DstIndex;
        public Energy_AG_PullFrom_UG(PlantFormation dstFormation, float amount, int dstIndex)
        {
            Amount = amount;
            DstFormation = dstFormation;
            DstIndex = dstIndex;
        }

        public void Receive(ref AboveGroundAgent srcAgent)
        {
            var freeCapacity = DstFormation.GetEnergyCapacity_UG(DstIndex) - DstFormation.GetEnergy_UG(DstIndex);
            var energy = srcAgent.TryDecEnergy(Math.Min(Amount, freeCapacity));
            if (energy > 0) DstFormation.Send(DstIndex, new EnergyInc(energy));
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly struct Water_UG_PullFrom_UG : IMessage<UnderGroundAgent>
    {
        /// <summary>
        /// Water volume in m³
        /// </summary>
        public readonly float Amount;
        public readonly PlantFormation DstFormation;
        public readonly int DstIndex;
        public Water_UG_PullFrom_UG(PlantFormation dstFormation, float amount, int dstIndex)
        {
            Amount = amount;
            DstFormation = dstFormation;
            DstIndex = dstIndex;
        }
        public void Receive(ref UnderGroundAgent srcAgent)
        {
            var freeCapacity = DstFormation.GetWaterCapacityPerTick_UG(DstIndex) - DstFormation.GetWater_UG(DstIndex);
            var energy = srcAgent.TryDecWater(Math.Min(freeCapacity, Amount));
            if (energy > 0) DstFormation.Send(DstIndex, new WaterInc(energy));
        }
    }

    public readonly struct Water_AG_PullFrom_UG : IMessage<UnderGroundAgent>
    {
        public readonly float Amount;
        public readonly PlantFormation DstFormation;
        public readonly int DstIndex;
        public Water_AG_PullFrom_UG (PlantFormation dstFormation, float amount, int dstIndex)
        {
            Amount = amount;
            DstFormation = dstFormation;
            DstIndex = dstIndex;
        }
        public void Receive(ref UnderGroundAgent srcAgent)
        {
            var capacity = DstFormation.GetWaterCapacityPerTick_AG(DstIndex) - DstFormation.GetWater_AG(DstIndex);
            var water = srcAgent.TryDecWater(Math.Min(Amount, capacity));
            if (water > 0) DstFormation.Send(DstIndex, new AboveGroundAgent.WaterInc(water));
        }
    }
}
