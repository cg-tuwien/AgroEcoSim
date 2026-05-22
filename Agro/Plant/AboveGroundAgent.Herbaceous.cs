using AgentsSystem;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Agro
{
    public partial struct AboveGroundAgent
    {
        public partial struct Herbaceous
        {
            
            public static void Tick(ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID, uint timestep)
            {
                var plant = formation.Plant;
                var species = plant.Parameters;
                var world = plant.World;
                var ageHours = (timestep - agent.BirthTime) * world.HoursPerTick; //age in hours
                bool sympodialPhase = ageHours >= species.SympodialStartAgeHours;
                bool stopGrowth = ageHours >= species.FloweringEndAgeHours;

                var phase = formation.GetPhase(species, timestep);

                //TODO growth should reflect temperature
                var lifeSupportPerHour = agent.LifeSupportPerHour();
                var lifeSupportPerTick = agent.LifeSupportPerTick(world);

                
                    agent.Energy -= lifeSupportPerTick; //life support

                var children = formation.GetChildren(agentID);

                var enoughEnergyState = agent.EnoughEnergy(lifeSupportPerHour);
                var wasMeristem = false;

                //Photosynthesis
                if (agent.Organ == OrganTypes.Leaf && agent.Water_g > 0f)
                {
                    var approxLight = world.Irradiance.GetIrradiance(formation, agentID); //in Watt per Hour per m²
                    if (approxLight > 0.01f)
                    {
                        //var airTemp = world.GetTemperature(timestep);
                        //var surface = Length * Radius * (Organ == OrganTypes.Leaf ? 2f : TwoPi);
                        var surface = agent.Length * agent.Radius * 2f;
                        //var possibleAmountByLight = surface * approxLight * mPhotoFactor * (Organ != OrganTypes.Leaf ? 0.1f : 1f);
                        var possibleAmountByLight = surface * approxLight;
                        var possibleAmountByWater = agent.Water_g;
                        // var possibleAmountByCO2 = airTemp >= plant.VegetativeHighTemperature.Y
                        // 	? 0f
                        // 	: (airTemp <= plant.VegetativeHighTemperature.X
                        // 		? float.MaxValue
                        // 		: surface * (airTemp - plant.VegetativeHighTemperature.X) / (plant.VegetativeHighTemperature.Y - plant.VegetativeHighTemperature.X)); //TODO respiratory cycle

                        //simplified photosynthesis equation:
                        //CO_2 + H2O + photons → [CH_2 O] + O_2
                        //var photosynthesizedEnergy = Math.Min(possibleAmountByLight * mPhotoEfficiency, Math.Min(possibleAmountByWater, possibleAmountByCO2));
                        var photosynthesizedEnergy = Math.Min(possibleAmountByLight * mPhotoEfficiency, possibleAmountByWater);

                        agent.Water_g -= photosynthesizedEnergy;
                        agent.Energy += photosynthesizedEnergy;
                        agent.CurrentDayEnvResources += approxLight * surface;
                        agent.CurrentDayEnvResourcesInv += approxLight;
                        agent.CurrentDayProductionInv += photosynthesizedEnergy / surface;
                    }
                }

                switch (agent.Organ)
                {
                    //leafs fall off with increasing age
                    case OrganTypes.Petiole:
                        {
                            if (ageHours > species.MaxLeaveAge && formation.GetOrgan(agent.Parent) != OrganTypes.Meristem)
                            {

                                var p = ageHours / (7000 ); //6 months in hours
                                if (plant.RNG.NextFloatAccum(p * p, world.HoursPerTick))
                                    agent.MakeBud(formation, children);
                            }
                        }
                        break;
                    case OrganTypes.Leaf:
                        {
                            
                                var col = species.OldLeafColor;
                                agent.Color = Vector3.Lerp(species.BaseLeafColor, col, ageHours/species.MaxLeaveAge);
                            
                        }break;
                    //height-based termination
                    case OrganTypes.Stem:
                        if (agent.DominanceLevel > 1 && formation.GetDominance(agent.Parent) < agent.DominanceLevel && !agent.isRizome)
                        {
                            var h = 5f * formation.GetBaseCenter(agentID).Y / formation.Height;
                            var e = 4f * agent.PreviousDayEnvResources / formation.DailyEfficiencyMax;
                            var q = 1f + h * h + 20f * agent.Radius + e * e + agent.WoodFactor;
                            var p = 0.004f / (q * q);
                            //Debug.WriteLine($"{formationID}: h {formation.GetBaseCenter(formationID).Y / formation.Height}  r {Radius}  e {PreviousDayEnvResources / formation.DailyEfficiencyMax}  =  {q}  % {p}");
                            if (plant.RNG.NextFloatAccum(p, world.HoursPerTick))
                            {
                                agent.Energy = 0f;
                                Debug.WriteLine($"DEL STEM {agentID} % {p} @ {timestep}");
                            }
                        }
                        break;

                    //a marker to later indicate transformation
                    case OrganTypes.Meristem: wasMeristem = true; break;

                    
                }

                //new Branches in spring
                if (!agent.isRizome && formation.GetIsRizome(agent.Parent) && agent.Organ.Equals(OrganTypes.Bud))
                {
                    if (agent.Organ == OrganTypes.Bud && phase.Equals(SeasonalPhase.PreFlower) && agent.trySpawn)
                    {
                        if (plant.RNG.NextFloat(0, 1) < species.pNewCrown)
                        {
                            var initialYawAngle = plant.RNG.NextFloat(-MathF.PI, MathF.PI);
                            var initialYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, initialYawAngle);
                            var baseStemOrientation = agent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, species.crownPitch * MathF.PI);

                            var budOrientation = initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f * MathF.PI);

                            agent.Organ = OrganTypes.Meristem;
                            var tiltAngle = plant.RNG.NextFloat(0f, 5f * MathF.PI / 180f);
                            var tiltAxis = Vector3.Normalize(new Vector3(plant.RNG.NextFloatVar(1f), 0f, plant.RNG.NextFloatVar(1f)));
                            agent.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f * MathF.PI);
                            agent.DominanceLevel = 1;
                            agent.Radius = AboveGroundAgent.InitialRadius;
                            agent.LengthVar = species.NodeDistance + plant.RNG.NextFloatVar(species.NodeDistanceVar);
                            //agent.SetOrientation(budOrientation);
                            agent.Energy = 0.1f * formation.GetEnergy(agent.Parent);
                            if (species.LateralsPerNode > 0)
                                agent.CreateLeaves(agent, plant, agent.LateralAngle + species.LateralRoll, agentID);
                           
                        }
                        else agent.trySpawn = false;
                    }
                    if (phase.Equals(SeasonalPhase.ResetPending))
                    {
                        agent.trySpawn = true;

                    }
                    
                }
                var flowerOrgans = new List<OrganTypes>() { OrganTypes.FlowerStem, OrganTypes.FlowerPadel, OrganTypes.FlowerPetiol, OrganTypes.FlowerMeristem, OrganTypes.FlowerBud };
                if (flowerOrgans.Contains(agent.Organ))
                {
                    var flowerHelper = new FlowerHelper(species.FlowerSettings);
                    flowerHelper.handleAgent(ref agent, agentID, formation, timestep);
                    return;
                }
                if (agent.Energy > enoughEnergyState && !agent.isRizome) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
                {
                    //Growth and branching
                    if (agent.Organ != OrganTypes.Bud) //for simplicity dormant buds do not grow
                    {
                        var currentSize = new Vector2(agent.Length, agent.Radius);
                        //dominance factor decreases the growth of structures further away from the dominant branch (based on the tree structure)
                        var dominanceFactor = agent.DominanceLevel < species.DominanceFactors.Length ? species.DominanceFactors[agent.DominanceLevel] : species.DominanceFactors[species.DominanceFactors.Length - 1];
                        switch (agent.Organ)
                        {
                            case OrganTypes.Leaf:
                                {
                                    //TDMI take env res efficiency into account
                                    //TDMI thickness of the parent and parent-parent decides the max. leaf size,
                                    //  also the energy consumption of the siblings beyond the node should have effect
                                    var sizeLimit = !agent.FlowerAgent.flowerBase ? new Vector2(species.LeafLength + agent.LengthVar, species.LeafRadius + agent.RadiusVar): new Vector2(species.FlowerSettings.LeafLength , species.FlowerSettings.LeafRadius );
                                    if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y)
                                    {
                                        //HoursPerTick are included in GrowthTimeVar
                                        var growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * (agent.PreviousDayProductionInvariant / formation.DailyProductionMax);
                                        var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                                        growth = resultingSize - currentSize;
                                        agent.Length += growth.X;
                                        agent.Radius += growth.Y;
                                    }
                                }
                                break;
                            case OrganTypes.Petiole:
                                {


                                    var sizeLimit = !agent.FlowerAgent.flowerBase ? new Vector2(species.PetioleLength + agent.LengthVar, species.PetioleRadius + agent.RadiusVar) : new Vector2(species.FlowerSettings.LeavePetioleLength , species.FlowerSettings.LeavePetioleRadius );
                                    if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y)
                                    {
                                        //HoursPerTick are included in GrowthTimeVar
                                        var growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * (agent.PreviousDayProductionInvariant / formation.DailyProductionMax);
                                        var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                                        growth = resultingSize - currentSize;

                                        //assure not to outgrow the parent
                                        var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                        if (currentSize.Y + growth.Y > parentRadius)
                                            growth.Y = parentRadius - currentSize.Y;
                                        agent.Length += growth.X;
                                        agent.Radius += growth.Y;
                                    }
                                }
                                break;
                            case OrganTypes.Meristem:
                                {
                                    if (!agent.isRizome)
                                    {
                                        var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                        var waterReserve = Math.Min(1f, plant.WaterBalance);
                                        var growth = new Vector2(1e-3f, 2e-5f) * (dominanceFactor * energyReserve * waterReserve * world.HoursPerTick * (agent.PreviousDayProductionInvariant / formation.DailyProductionMax) * species.growthFactor);

                                        //assure not to outgrow the parent unless parent is rizome
                                        var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                        if (currentSize.Y + growth.Y > parentRadius)
                                            growth.Y = parentRadius - currentSize.Y;
                                        agent.Length += agent.Length <= agent.LengthVar ? growth.X : 0f;
                                        if (agent.Radius < species.MaxRadius)
                                            agent.Radius += growth.Y;
                                    } else
                                    {

                                    }
                                }
                                break;
                            case OrganTypes.Stem:
                                {
                                    if (phase != SeasonalPhase.ResetPending)
                                    {
                                        var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                        var growth = 2e-5f * dominanceFactor * energyReserve * Math.Min(plant.WaterBalance, energyReserve) * world.HoursPerTick * species.growthFactor;

                                        //assure not to outgrow the parent
                                        var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                        if (currentSize.Y + growth > parentRadius)
                                            growth = parentRadius - currentSize.Y;

                                        if (agent.Radius < species.MaxRadius)
                                            agent.Radius += growth;

                                    }
                                    else
                                    {
                                        const float DrainPerHour = 0.15f;
                                        const float LignifyRate = 0.01f; // per tick increment, clamped ≤ 1
                                        // energy drain
                                        //agent.Energy -= DrainPerHour * agent.LifeSupportPerHour() * world.HoursPerTick * 1000;


                                    }
                                }
                                break;
                            case OrganTypes.FlowerStem: case OrganTypes.FlowerPadel:
                            case OrganTypes.FlowerPetiol:
                            case OrganTypes.FlowerMeristem:
                            {} return;
                         
                        }
                        ;

                        //TDMI maybe do it even if no growth
                        if (agent.Organ == OrganTypes.Stem || agent.Organ == OrganTypes.Meristem)
                        {
                            if (agent.Organ == OrganTypes.Stem && agent.WoodFactor < 1f)
                            {
                                var pw = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetWoodRatio(agent.Parent) : agent.WoodFactor; //assure that no child has a higher factor than its parent
                                //agent.WoodFactor = Math.Min((agent.WoodFactor <= pw ? agent.WoodFactor : pw) + agent.GrowthTimeVar, 1f);
                            }
                            var pchaning = 0f;
                            var pflower = 0f;
                            //chaining (meristem then continues in a new segment)
                            switch (phase) {
                            case SeasonalPhase.PreFlower:
                                    pflower = species.pFloweringSeaonns[0];
                                    pchaning = species.pChaningSeaonns[0];
                                    break;
                            case SeasonalPhase.Flowering:
                                    pflower = species.pFloweringSeaonns[1];
                                    pchaning = species.pChaningSeaonns[1]; break;
                            case SeasonalPhase.PostFlower:
                                    pflower = species.pFloweringSeaonns[2];
                                    pchaning = species.pChaningSeaonns[2]; break;
                            case SeasonalPhase.ResetPending:
                                    pflower = species.pFloweringSeaonns[3];
                                    pchaning = species.pChaningSeaonns[3]; break;
                            }

                            

                            if (agent.Organ == OrganTypes.Meristem && agent.Length > agent.LengthVar )
                            {

                                
                                if (plant.RNG.NextFloat(0, 1) < pflower)
                                {
                                    agent.Organ = OrganTypes.Stem;
                                    agent.GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
                                    wasMeristem = true;
                                    float prevResources, prevProduction;
                                    if (timestep - agent.BirthTime > world.HoursPerTick)
                                    {
                                        prevResources = agent.PreviousDayProductionInvariant;
                                        prevProduction = agent.PreviousDayProductionInvariant;
                                    }
                                    else
                                    {
                                        prevResources = formation.DailyResourceMax;
                                        prevProduction = formation.DailyProductionMax;
                                        //Debug.WriteLine($"PREV res {prevResources} prod {prevProduction}");
                                    }

                                    commitToFlower(agent, formation, agentID, prevResources, prevProduction);
                                    doChaning(agent, formation, agentID, prevResources, prevProduction);
                                }
                                else if (plant.RNG.NextFloat(0, 1) < pchaning)
                                {
                                    agent.Organ = OrganTypes.Stem;
                                    agent.GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
                                    wasMeristem = true;
                                    float prevResources, prevProduction;
                                    if (timestep - agent.BirthTime > world.HoursPerTick)
                                    {
                                        prevResources = agent.PreviousDayProductionInvariant;
                                        prevProduction = agent.PreviousDayProductionInvariant;
                                    }
                                    else
                                    {
                                        prevResources = formation.DailyResourceMax;
                                        prevProduction = formation.DailyProductionMax;
                                        //Debug.WriteLine($"PREV res {prevResources} prod {prevProduction}");
                                    }

                                    doChaning(agent, formation, agentID, prevResources, prevProduction);



                                    var parent = agent.Parent;
                                    while (!formation.GetIsRizome(parent))
                                    {
                                        foreach (var child in formation.GetChildren(parent))
                                        {
                                            if (formation.GetOrgan(child).Equals(OrganTypes.Petiole))
                                            {
                                                bendPetiol(formation, child);
                                            }
                                        }
                                        parent = formation.GetParent(parent);
                                    }
                                }
                            }
                            else
                            {
                                //if the stem grows too thick so that it already covers a large portion of the petiole, it gets removed and a new bud emerges.
                                if (agent.Organ == OrganTypes.Petiole && agent.ParentRadiusAtBirth + species.PetioleCoverThreshold < formation.GetBaseRadius(agent.Parent))
                                    agent.MakeBud(formation, children);

                                //termination of unproductive branches
                                if (agent.Organ == OrganTypes.Petiole && ageHours > 48 && formation.GetOrgan(agent.Parent) != OrganTypes.Meristem && children != null)
                                {
                                    var production = 0f;
                                    for (int c = 0; c < children.Count; ++c)
                                        production += formation.GetDailyProductionInv(children[c]);
                                    //Debug.WriteLine($"T{world.Timestep} Prod: {production}");

                                    production /= plant.EnergyProductionMax;
                                    if (production < 0.5) //the lower half of production efficiency
                                    {
                                        production = 1f - 2f * production;
                                        production *= production;
                                        if (plant.RNG.NextFloatAccum(production, world.HoursPerTick))
                                        {
                                            Debug.WriteLine($"DEL LEAF {agentID} % {production} @ {timestep}");
                                            formation.Death(agentID);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (agent.Energy <= 0f && !agent.isRizome ) //remove organs that drained all their energy
                {

                    switch (agent.Organ)
                    {
                        case OrganTypes.Petiole: //agent.MakeBud(formation, children); break; //keep an option for a new leaf
                        case OrganTypes.Leaf:
                            {
                                if (!agent.FlowerAgent.flowerBase)
                                {
                                    formation.Death(agentID);
                                    formation.Death(agent.Parent); //remove the petiole as well
                                }
                            }
                            break;

                        case OrganTypes.Stem:
                            {
                                if (formation.GetIsRizome(agent.Parent))
                                {
                                    agent.MakeBud(formation, children);
                                    agent.SetOrientation(formation.GetDirection(agent.Parent));
                                }
                                else
                                {

                                    formation.Death(agentID);
                                }
                            }
                            break;
                        default:


                            if (formation.GetIsRizome(agent.Parent) && !agent.Organ.Equals(OrganTypes.Bud))
                            {
                                agent.MakeBud(formation, children);
                                agent.SetOrientation(formation.GetDirection(agent.Parent));
                            }
                            else
                            {
                                //formation.Death(agentID);
                            }
                            //formation.Death(agentID);

                            break; //remove the item
                    }
                    return;
                }
                else if (agent.isRizome)
                {
                    if(agent.Energy<=0) {
                        formation.Death(agentID);
                        return; }
                    if (agent.Organ.Equals(OrganTypes.Meristem) && agent.Length >=plant.Parameters.RizomeLength)
                    {
                        agent.Organ = OrganTypes.Stem;
                        agent.GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
                        wasMeristem = true;
                        var initialYawAngle = plant.RNG.NextFloat(-MathF.PI, MathF.PI);
                        var initialYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, initialYawAngle);

                        var lateralPitch = agent.LateralAngle + formation.Plant.Parameters.LateralRoll;
                        var tiltAngle = plant.RNG.NextFloat(0f, 5f * MathF.PI / 180f);
                        var tiltAxis = Vector3.Normalize(new Vector3(plant.RNG.NextFloatVar(1f), 0f, plant.RNG.NextFloatVar(1f)));
                        var budOrientation = Quaternion.CreateFromAxisAngle(tiltAxis, tiltAngle) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f * MathF.PI);
                        var bud = new AboveGroundAgent(plant, agentID, OrganTypes.Bud, budOrientation, 0, initialResources: 1f, initialProduction: 1f) { LateralAngle = lateralPitch, Radius = 0f, RadiusVar = 0f };

                        formation.Birth(bud);
                    }
                    float prevResources, prevProduction;
                    if (timestep - agent.BirthTime > world.HoursPerTick)
                    {
                        prevResources = agent.PreviousDayProductionInvariant;
                        prevProduction = agent.PreviousDayProductionInvariant;
                    }
                    else
                    {
                        prevResources = formation.DailyResourceMax;
                        prevProduction = formation.DailyProductionMax;
                        //Debug.WriteLine($"PREV res {prevResources} prod {prevProduction}");
                    }


                    var currentSize = new Vector2(agent.Length, agent.Radius);
                    //dominance factor decreases the growth of structures further away from the dominant branch (based on the tree structure)
                    var dominanceFactor = agent.DominanceLevel < species.DominanceFactors.Length ? species.DominanceFactors[agent.DominanceLevel] : species.DominanceFactors[species.DominanceFactors.Length - 1];
                    switch (agent.Organ)
                    {
                        case OrganTypes.Meristem:
                            {
                               
                                var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                var waterReserve = Math.Min(1f, plant.WaterBalance);
                                var growth = new Vector2(1e-3f, 2e-5f) * (dominanceFactor * energyReserve * waterReserve * world.HoursPerTick);
                                // Console.WriteLine($"growm {growth.X} {energyReserve} {waterReserve} {agent.PreviousDayProductionInvariant / formation.DailyProductionMax} {agent.PreviousDayProductionInvariant} {formation.DailyProductionMax}");
                                //assure not to outgrow the parent unless parent is rizome
                                var test = formation.CollisionBvh.QueryOverlaps(formation.ComputeBoundsFromParameters(formation.GetTipPosition(agent.Parent), agent.Orientation, agent.Length + growth.X, agent.Radius+ growth.Y));
                                var intersetc = false;
                                if (test != null)
                                {
                                    foreach (var colission in test)
                                    {
                                        

                                        if ((agent.Parent != colission && !formation.GetChildren(agent.Parent).Contains(colission)) && formation.GetIsRizome(colission))
                                        {
                                            agent.Energy = 0;
                                            formation.Death(agentID);
                                            return;
                                        }
                                    }
                                }
                                //if (plant.Soil.IntersectPoint(formation.GetBaseCenterWorld(agentID) + Vector3.Transform(Vector3.UnitX, formation.GetDirection(agentID)) * agent.Length + Vector3.Transform(Vector3.UnitX, rizomeAgent.Orientation) * plant.Parameters.RizomeLength, plant.SoilIndex) < 0) { return; }
                                if (!intersetc)
                                {
                                    var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                    if (currentSize.Y + growth.Y > parentRadius)
                                        growth.Y = parentRadius - currentSize.Y;
                                    agent.Length += agent.Length <= plant.Parameters.RizomeLength ? growth.X : 0f;
                                    if (agent.Radius < species.RizomeRadius)
                                        agent.Radius += growth.Y;
                                }
                            }
                            break;
                        case OrganTypes.Stem:
                            {
                               //Console.WriteLine("grows");
                                if (phase != SeasonalPhase.ResetPending)
                                {
                                    var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                    var growth = 2e-5f * dominanceFactor * energyReserve * Math.Min(plant.WaterBalance, energyReserve) * world.HoursPerTick * species.growthFactor;

                                    //assure not to outgrow the parent
                                    var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                    if (currentSize.Y + growth > parentRadius)
                                        growth = parentRadius - currentSize.Y;

                                    if (agent.Radius < species.RizomeRadius)
                                        agent.Radius += growth;
                                }

                            }
                            break;
                    }

                    if (agent.Organ.Equals(OrganTypes.Stem))
                    {

                        //Console.WriteLine("chan");
                        if ((!phase.Equals(SeasonalPhase.ResetPending)) && agent.rizomeInfo.test3 && agent.rizomeInfo.rizomeDepth < species.RizomeMaxDepth)
                        {
                           // Console.WriteLine($"chan1 {species.pExpandRizome} {agent.rizomeInfo.test} {agent.rizomeInfo.test2} {agent.rizomeInfo.test4} {agent.rizomeInfo.test3} ");
                            if (plant.RNG.NextFloat(0, 1) < species.pExpandRizome && agent.rizomeInfo.test)
                            {
                                var rizomOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f * MathF.PI);
                                if (agentID > 0)
                                    rizomOrientation = agent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);

                                CreateRizome(plant, agentID, rizomOrientation, agent, formation,prevResources,prevProduction);
                                agent.rizomeInfo.test = false;
                            }
                            if (plant.RNG.NextFloat(0, 1) < species.pExpandRizome && agent.rizomeInfo.test2)
                            {
                                var rizomOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.666f * MathF.PI);
                                if (agentID > 0)
                                    rizomOrientation = agent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 4f);
                                CreateRizome(plant, agentID, rizomOrientation, agent, formation, prevResources, prevProduction);
                                agent.rizomeInfo.test2 = false;
                            }
                            if (plant.RNG.NextFloat(0, 1) < species.pExpandRizome && agent.rizomeInfo.test4 && agentID == 0)
                            {
                                var rizomOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.666f * MathF.PI);
                                CreateRizome(plant, agentID, rizomOrientation, agent, formation, prevResources, prevProduction);
                                agent.rizomeInfo.test4 = false;

                            }
                        }
                        else if (phase.Equals(SeasonalPhase.PreFlower) && !agent.rizomeInfo.test3)
                            agent.rizomeInfo.test3 = true;
                    }
                }


                //update auxins for meristem and stems that were meristem in the previous step
                agent.Auxins = wasMeristem || agent.Organ == OrganTypes.Meristem ? species.AuxinsProduction : 0;
            }
            
            private static void commitToFlower(AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID, float prevResources, float prevProduction)
            {
                var flowerOrgans = new List<OrganTypes>() { OrganTypes.FlowerStem, OrganTypes.FlowerPadel, OrganTypes.FlowerPetiol, OrganTypes.FlowerMeristem, OrganTypes.FlowerBud };
                var i = agent.Parent;
                var flowerPerCrown = 1;
                var currentflowerPerCrown = 0;
                while (!formation.GetIsRizome(i) && currentflowerPerCrown < flowerPerCrown) {
                    foreach (var child in formation.GetChildren(i))
                    {
                        if (flowerOrgans.Contains(formation.GetOrgan(child))) currentflowerPerCrown++;
                    }
                    i = formation.GetParent(i);
                }
                if (currentflowerPerCrown < flowerPerCrown)
                {
                    //Console.WriteLine("commit");
                    var lateralPitch = agent.LateralAngle + formation.Plant.Parameters.LateralRoll;
                    var meristem = formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerMeristem, TurnUpwards(agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, agent.Orientation)), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = new Flower() { debth = 0, flowerBase = true } });
                    agent.Energy *= 0.9f;
                    agent.Water_g *= 0.9f;
                }

            }

            static void CreateRizome(PlantFormation2 plant, int agentID, Quaternion rizomOrientation, AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, float prevResources, float prevProduction)
            {
               // Console.WriteLine("new");

                var initialYawAngle = plant.RNG.NextFloat(-MathF.PI, MathF.PI);
                var initialYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, initialYawAngle);
                var budOrientation = initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f * MathF.PI);

                var rizomeAgent = new AboveGroundAgent(plant, agentID, OrganTypes.Meristem, rizomOrientation, 0.25f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction, rizome: true, length:0.0005f) { Water_g = 0.1f * agent.Water_g, WoodFactor = 1f };
                rizomeAgent.rizomeInfo.rizomeDepth = agent.rizomeInfo.rizomeDepth + 1;
                var lateralPitch = agent.LateralAngle + formation.Plant.Parameters.LateralRoll;
             
                var rizomeIndex = formation.Birth(rizomeAgent);
               
            }
            static void doChaning(AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID, float prevResources, float prevProduction) {
                var lateralPitch = agent.LateralAngle + formation.Plant.Parameters.LateralRoll;
                var meristem = formation.Birth(new(formation.Plant, agentID, OrganTypes.Meristem, TurnUpwards(agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, agent.Orientation)), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel });
                agent.Energy *= 0.9f;
                agent.Water_g *= 0.9f;

                if (formation.Plant.Parameters.LateralsPerNode > 0)
                    agent.CreateLeaves(agent, formation.Plant, lateralPitch, meristem);

            }

            static void bendPetiol(PlantSubFormation<AboveGroundAgent> formation, int agentID) {
                float maxAngle = formation.Plant.Parameters.PetiolMoveDownMax * MathF.PI;   // max. Winkel zwischen Parent- und Child-Stängel
                // Parent-Orientierung → Parent-Richtung (im Worldspace)
                var parentRot = formation.GetDirection(formation.GetParent(agentID));
                var parentDir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, parentRot));

                // Child-Orientierung → Child-Richtung (im Worldspace)
                var dir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, formation.GetDirection(agentID)));

                // Winkel zwischen Parent und Child
                float cos = Vector3.Dot(dir, parentDir);
                cos = Math.Clamp(cos, -1f, 1f);      // numerische Sicherheit
                float angleFromParent = MathF.Acos(cos);

                // nur weiter „runterbiegen“, solange der Winkel noch kleiner als maxAngle ist
                if (angleFromParent < maxAngle)
                {
                    // Achse senkrecht zu Parent- und Child-Richtung
                    var axis = Vector3.Cross(parentDir, dir);
                    if (axis.LengthSquared() > 1e-8f)
                    {
                        axis = Vector3.Normalize(axis);

                        float delta = formation.Plant.Parameters.PetiolMoveDown * MathF.PI;
                        //Console.WriteLine($"{agentID}");
                        // Worldspace-Rotation um diese Achse
                        var worldRot = Quaternion.CreateFromAxisAngle(axis, delta);
                        var or = formation.GetDirection(agentID);
                        formation.SendProtected(agentID,new AboveGroundAgent.OrientationSet(Quaternion.Normalize(worldRot * or)));

                        formation.SendProtected(formation.GetChildren(agentID).First(), new AboveGroundAgent.OrientationSet(Quaternion.Normalize(worldRot * formation.GetDirection(formation.GetChildren(agentID).First()))));
                    }
                }
            }
        }
    }
}
