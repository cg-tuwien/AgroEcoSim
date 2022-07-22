using System;
using System.Numerics;
using System.Collections.Generic;
using AgentsSystem;
using Utils;

namespace SimpleSoil;

public partial struct SimpleSoilAgent : IAgent{
    public struct WaterDecrease : IMessage<SimpleSoilAgent>{
        public float Ammount;
        public bool Valid => Ammount > 0f;
        public int DestinationIndex;
        public Transaction Type => Transaction.Decrease;

        public void Receive(ref SimpleSoilAgent dstAgent, uint timestep){
            dstAgent.Ammount -= Ammount;
        }

        public WaterDecrease(float ammount, int destinationIndex){
            DestinationIndex = destinationIndex;
            Ammount = ammount;
        }
    }

    public struct WaterIncrease : IMessage<SimpleSoilAgent>{
        public float Ammount;

        public int DestinationIndex;
        public bool Valid => Ammount > 0f;
        public Transaction Type => Transaction.Increase;

        public void Receive(ref SimpleSoilAgent dstAgent, uint timestep){
            dstAgent.Ammount += Ammount;
        }

        public WaterIncrease(float ammount, int destinationIndex){
            DestinationIndex = destinationIndex;
            Ammount = ammount;
        }
    }

}