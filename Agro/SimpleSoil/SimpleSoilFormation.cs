using System;
using System.Numerics;
using System.Collections.Generic;
using AgentsSystem;
using Utils;

namespace SimpleSoil;

public class SimpleSoilFormation : IFormation{
    public SimpleSoilAgent[] Agents;
    public List<IMessage<SimpleSoilAgent>> PostBox = new List<IMessage<SimpleSoilAgent>>();
    public bool HasUndeliveredPost{ get;}

    public bool ExistsNeighbor(int formationIndex, Vector3i direction){
        return false;//Todo
    }
    public int GetNeighbor(int formationIndex, Vector3i direction){
        return 0;//Todo
    }

    public int GetIndex(Vector3i position){
        return 0;//Todo
    }

    public Vector3i GetPosition(int formationIndex){
        return new();//Todo
    }

    public void Send(IMessage<SimpleSoilAgent> message){
        PostBox.Add(message);
    }

    public void Census(){
        //Todo?
    }

    public void DeliverPost(uint timeStep){
        foreach(var message in PostBox){
            var index = ((SimpleSoilAgent.WaterDecrease)message).DestinationIndex;
            message.Receive(ref Agents[index],timeStep);
        }
        PostBox.Clear();
    }

    public void Tick(SimulationWorld world, uint timestep){
        for(int i = 0; i < Agents.Length; i++){
            Agents[i].Tick(world,this,i,timestep);
        }
        DeliverPost(timestep);
    }

    public void GodotReady(){

    }
    
    public string HistoryToJSON(){
        return string.Empty;
    }

    public void GodotProcess(uint timestep){

    }

    public SimpleSoilFormation(){
        int voxelsPerMeter = SimpleSoilSettings.SplitsPerMeter*SimpleSoilSettings.SplitsPerMeter*SimpleSoilSettings.SplitsPerMeter;
        Agents = new SimpleSoilAgent[SimpleSoilSettings.Dimensions[0]*SimpleSoilSettings.Dimensions[1]*SimpleSoilSettings.Dimensions[2]*voxelsPerMeter];
    }

}