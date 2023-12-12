using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentsSystem;
using System.Buffers;

namespace Agro;

public partial struct UnderGroundAgent2 : IPlantAgent
{
    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct WaterInc : IMessage<UnderGroundAgent2>
    {
        public readonly float Amount;
        public readonly float Factor;
        public WaterInc(float amount)
        {
            Amount = amount;
            Factor = 1;
        }
        public WaterInc(float amount, float factor)
        {
            Amount = amount * factor;
            Factor = factor;
        }
        public bool Valid => Amount > 0f;
        public Transaction Type => Transaction.Increase;
        public void Receive(ref UnderGroundAgent2 dstAgent, uint timestep) => dstAgent.IncWater(Amount, Factor);
    }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct WaterDec : IMessage<UnderGroundAgent2>
    {
        public readonly float Amount;
        public WaterDec(float amount) => Amount = amount;
        public bool Valid => Amount > 0f;
        public Transaction Type => Transaction.Increase;
        public void Receive(ref UnderGroundAgent2 dstAgent, uint timestep) => dstAgent.TryDecWater(Amount);
    }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct EnergyInc : IMessage<UnderGroundAgent2>
    {
        public readonly float Amount;
        public EnergyInc(float amount) => Amount = amount;
        public bool Valid => Amount > 0f;
        public Transaction Type => Transaction.Increase;
        public void Receive(ref UnderGroundAgent2 dstAgent, uint timestep) => dstAgent.IncEnergy(Amount);
    }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct EnergyDec : IMessage<UnderGroundAgent2>
    {
        public readonly float Amount;
        public EnergyDec(float amount) => Amount = amount;
        public bool Valid => Amount > 0f;
        public Transaction Type => Transaction.Increase;
        public void Receive(ref UnderGroundAgent2 dstAgent, uint timestep) => dstAgent.TryDecEnergy(Amount);
    }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct Energy_PullFrom_AG: IMessage<AboveGroundAgent3>
    {
        public readonly float Amount;
        public readonly PlantSubFormation<UnderGroundAgent2> DstFormation;
        public readonly int DstIndex;
        public Energy_PullFrom_AG(PlantSubFormation<UnderGroundAgent2> dstFormation, float amount, int dstIndex)
        {
            Amount = amount;
            DstFormation = dstFormation;
            DstIndex = dstIndex;
        }
        public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
        public Transaction Type => Transaction.Decrease;

        public void Receive(ref AboveGroundAgent3 srcAgent, uint timestep)
        {
            var freeCapacity = Math.Max(0f, DstFormation.GetEnergyCapacity(DstIndex) - DstFormation.GetEnergy(DstIndex));
            var energy = srcAgent.TryDecEnergy(Math.Min(Amount, freeCapacity));
            if (energy > 0f)
                DstFormation.SendProtected(DstIndex, new EnergyInc(energy));
        }
    }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct Water_AG_PullFrom_UG : IMessage<UnderGroundAgent2>
    {
        public readonly float Amount;
        public readonly PlantSubFormation<AboveGroundAgent3> DstFormation;
        public readonly int DstIndex;
        public Water_AG_PullFrom_UG(PlantSubFormation<AboveGroundAgent3> dstFormation, float amount, int dstIndex)
        {
            Amount = amount;
            DstFormation = dstFormation;
            DstIndex = dstIndex;
        }
        public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
        public Transaction Type => Transaction.Decrease;

        public void Receive(ref UnderGroundAgent2 srcAgent, uint timestep)
        {
            //var freeCapacity = Math.Max(0f, DstFormation.GetWaterTotalCapacity(DstIndex) - DstFormation.GetWater(DstIndex));
            //var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
            var water = srcAgent.TryDecWater(Amount);
            if (water > 0f)
                DstFormation.SendProtected(DstIndex, new AboveGroundAgent3.WaterInc(water));
        }
    }
}
