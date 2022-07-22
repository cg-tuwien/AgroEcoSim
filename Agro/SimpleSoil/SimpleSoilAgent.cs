using System;
using System.Numerics;
using System.Collections.Generic;
using AgentsSystem;
using Utils;

namespace SimpleSoil;

//Rule idea
//Cell water content is over some threshold => cell will diffuse water into other cells
//  - Send message to neighbouring cells to give them water, they can either accept or decline 

public partial struct SimpleSoilAgent : IAgent{
    public float Ammount;

    public ulong ID { get; }

    public void Tick(SimulationWorld world, IFormation _formation, int formationID, uint timestep){
        float volume = MathF.Pow(1,-SimpleSoilSettings.SplitsPerMeter*3);
        float waterVolumeLimit = volume * SimpleSoilSettings.MaxWaterMultiplier;

        float waterContent = Ammount / waterVolumeLimit;

        if(waterContent > SimpleSoilSettings.WaterContentForDiffusion){
            var formation = (SimpleSoilFormation) _formation;

            
            Vector3i position = formation.GetPosition(formationID);
            
            float availableForDiffusion = waterContent * SimpleSoilSettings.DiffusionPerHour / SimpleSoilSettings.TicksPerHour;

            float[] diffusionCapacity = new float[6];
            float diffusionCapacitySum = 0f;

            for(int i = 0; i < 6; i++){
                if(!formation.ExistsNeighbor(formationID,SimpleSoilUtility.NeighborDirections[i])) continue;

                var neighborIndex = formation.GetNeighbor(formationID,SimpleSoilUtility.NeighborDirections[i]);
            
                diffusionCapacity[i] = Math.Max(0f,waterVolumeLimit - formation.Agents[neighborIndex].Ammount);
                diffusionCapacitySum += diffusionCapacity[i];
            }

            float diffusionAmmount = Math.Min(diffusionCapacitySum,availableForDiffusion);
            Ammount -= diffusionAmmount;

            for(int i = 0; i < 6; i++){
                if(diffusionCapacity[i] == 0) continue;

                float multiplier = diffusionCapacity[i]/diffusionCapacitySum;

                int targetIndex = formation.GetIndex(position + SimpleSoilUtility.NeighborDirections[i]);

                formation.Send(new SimpleSoilAgent.WaterIncrease(diffusionAmmount * multiplier, targetIndex));
            }
            //DO DIFFUSION!
        }
    }
}