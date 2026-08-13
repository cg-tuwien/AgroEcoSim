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
        public partial struct GeraniumSanguineum
        {

            public static void TickGeraniumSanguineum(ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID, uint timestep)
            {

                var plant = formation.Plant;
                var species = plant.Parameters;
                var world = plant.World;
                var ageHours = (timestep - agent.BirthTime) * world.HoursPerTick; //age in hours
                bool sympodialPhase = ageHours >= species.SympodialStartAgeHours;
                bool stopGrowth = ageHours >= species.FloweringEndAgeHours;

                var phase = formation.GetFloweringPhase(species,timestep);

                //TODO growth should reflect temperature
                var lifeSupportPerHour = agent.LifeSupportPerHour();
                var lifeSupportPerTick = agent.LifeSupportPerTick(world);

                if (!agent.isRizome)
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
                            if (ageHours > 36 && formation.GetOrgan(agent.Parent) != OrganTypes.Meristem)
                            {
                                var p = ageHours / 4032; //6 months in hours
                                if (plant.RNG.NextFloatAccum(p * p, world.HoursPerTick))
                                    agent.MakeBud(formation, children);
                            }
                        }
                        break;

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

                    break;
                }

                //new Branches in spring
                if (!agent.isRizome && formation.GetIsRizome(agent.Parent) && agent.Organ.Equals(OrganTypes.Bud))
                {
                    if (agent.Organ == OrganTypes.Bud && phase.Equals(SeasonalPhase.PreFlower) && agent.trySpawn)
                    {
                        if (plant.RNG.NextFloat(0, 1) < 0.6f ) {
                            var initialYawAngle = plant.RNG.NextFloat(-MathF.PI, MathF.PI);
                            var initialYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, initialYawAngle);
                            var baseStemOrientation = initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f * MathF.PI);
                            agent.Organ = OrganTypes.Meristem;
                            agent.Orientation = baseStemOrientation;
                            agent.DominanceLevel = 1;
                            agent.Radius = 0.005f;
                            agent.LengthVar = species.NodeDistance + plant.RNG.NextFloatVar(species.NodeDistanceVar);

                            agent.Energy = agent.EnergyStorageCapacity();
                            if (species.LateralsPerNode > 0)
                                agent.CreateLeaves(agent, plant, agent.LateralAngle + species.LateralRoll, agentID);
                        } else agent.trySpawn = false;
                    }
                    if (phase.Equals(SeasonalPhase.ResetPending)) {
                        agent.trySpawn = true;
                    }
                }
                if (agent.Energy > enoughEnergyState && !agent.isRizome) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
                {
                    //Growth and branching
                    if (agent.Organ != OrganTypes.Bud && !agent.isRizome) //for simplicity dormant buds do not grow
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
                                    var sizeLimit = new Vector2(species.LeafLength + agent.LengthVar, species.LeafRadius + agent.RadiusVar);
                                    if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y && phase != SeasonalPhase.ResetPending)
                                    {
                                        //HoursPerTick are included in GrowthTimeVar
                                        var growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * (agent.PreviousDayProductionInvariant / formation.DailyProductionMax);
                                        var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                                        growth = resultingSize - currentSize;
                                        agent.Length += growth.X;
                                        agent.Radius += growth.Y;
                                    }
                                    else if (phase == SeasonalPhase.ResetPending)
                                    {
                                        const float LeafDrainPerHour = 0.20f;  // fraction of maintenance to drain
                                        const float LeafShrinkWindow = 72f;    // hours over which shrink eases in
                                        const float PetioleLignifyRate = 0.01f;

                                        float tLeaf = Math.Clamp((ageHours - species.FloweringEndAgeHours) / Math.Max(1f, LeafShrinkWindow), 0f, 1f);

                                        // energy drain, scaled like your flower senescence
                                        float drainLeaf = LeafDrainPerHour * agent.LifeSupportPerHour();
                                        float easedLeaf = 0.25f + 0.75f * tLeaf;
                                        agent.Energy -= drainLeaf * easedLeaf * world.HoursPerTick;
                                        agent.WoodFactor = Math.Min(
                                            agent.WoodFactor + PetioleLignifyRate,
                                            1f
                                        );


                                    }
                                }
                                break;
                            case OrganTypes.Petiole:
                                {
                                    var sizeLimit = new Vector2(species.PetioleLength + agent.LengthVar, species.PetioleRadius + agent.RadiusVar);
                                    if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y && phase != SeasonalPhase.ResetPending)
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
                                    else if (phase == SeasonalPhase.ResetPending)
                                    {
                                        const float PetioleDrainPerHour = 0.15f;
                                        const float PetioleLignifyRate = 0.01f; // per tick increment, clamped ≤ 1

                                        // increase wood factor but never exceed parent’s (keeps hierarchy consistent)
                                        float parentWood = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetWoodRatio(agent.Parent) : agent.WoodFactor;
                                        agent.WoodFactor = Math.Min(
                                            (agent.WoodFactor <= parentWood ? agent.WoodFactor : parentWood) + PetioleLignifyRate,
                                            1f
                                        );

                                        // energy drain
                                        agent.Energy -= PetioleDrainPerHour * agent.LifeSupportPerHour() * world.HoursPerTick;
                                    }
                                }
                                break;
                            case OrganTypes.Meristem:
                                {
                                    var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                    var waterReserve = Math.Min(1f, plant.WaterBalance);
                                    var growth = new Vector2(1e-3f, 2e-5f) * (dominanceFactor * energyReserve * waterReserve * world.HoursPerTick * (agent.PreviousDayProductionInvariant / formation.DailyProductionMax) * 0.20f);

                                    //assure not to outgrow the parent unless parent is rizome
                                    var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                    if (currentSize.Y + growth.Y > parentRadius)
                                        growth.Y = parentRadius - currentSize.Y;
                                    agent.Length += growth.X;
                                    agent.Radius += growth.Y;
                                }
                                break;
                            case OrganTypes.Stem:
                                {
                                    if (phase != SeasonalPhase.ResetPending)
                                    {
                                        var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                        var growth = 2e-5f * dominanceFactor * energyReserve * Math.Min(plant.WaterBalance, energyReserve) * world.HoursPerTick * 0.20f;

                                        //assure not to outgrow the parent
                                        var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                        if (currentSize.Y + growth > parentRadius)
                                            growth = parentRadius - currentSize.Y;

                                        if (agent.Radius < 0.005)
                                            agent.Radius += growth;

                                    } else
                                    {
                                        const float DrainPerHour = 0.15f;
                                        const float LignifyRate = 0.01f; // per tick increment, clamped ≤ 1
                                        // energy drain
                                        agent.Energy -= DrainPerHour * agent.LifeSupportPerHour() * world.HoursPerTick * 1000;


                                    }
                                }
                                break;

                            case OrganTypes.FlowerStem:
                                {
                                    /* var sizeLimit = new Vector2(species.PetioleLength + agent.LengthVar, species.PetioleRadius + agent.RadiusVar);
                                     if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y)
                                     {
                                         //HoursPerTick are included in GrowthTimeVar
                                         var growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * (agent.PreviousDayProductionInv / formation.DailyProductionMax);
                                         var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                                         growth = resultingSize - currentSize;

                                         //assure not to outgrow the parent
                                         var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                         if (currentSize.Y + growth.Y > parentRadius)
                                             growth.Y = parentRadius - currentSize.Y;
                                         agent.Length += growth.X;
                                         agent.Radius += growth.Y;
                                     }
                                    */

                                }
                                break;
                        }
                        ;

                        //TDMI maybe do it even if no growth
                        if (agent.Organ == OrganTypes.Stem || agent.Organ == OrganTypes.Meristem)
                        {
                            if (agent.Organ == OrganTypes.Stem && agent.WoodFactor < 1f)
                            {
                                var pw = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetWoodRatio(agent.Parent) : agent.WoodFactor; //assure that no child has a higher factor than its parent
                                agent.WoodFactor = Math.Min((agent.WoodFactor <= pw ? agent.WoodFactor : pw) + agent.GrowthTimeVar, 1f);
                            }

                            //chaining (meristem then continues in a new segment)
                            if (agent.Organ == OrganTypes.Meristem && agent.Length > agent.LengthVar)
                            {

                                agent.Organ = OrganTypes.Stem;
                                agent.GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
                                wasMeristem = true;
                                float prevResources, prevProduction;
                                if (timestep - agent.BirthTime > world.HoursPerTick)
                                {
                                    prevResources = agent.PreviousDayEnvResourcesInvariant;
                                    prevProduction = agent.PreviousDayProductionInvariant;
                                }
                                else
                                {
                                    prevResources = formation.DailyResourceMax;
                                    prevProduction = formation.DailyProductionMax;
                                    //Debug.WriteLine($"PREV res {prevResources} prod {prevProduction}");
                                }

                                bool doDichotomous = species.MonopodialFactor < 1f;
                                switch (phase)
                                {

                                    case SeasonalPhase.PreFlower:
                                        {
                                            if (doDichotomous && plant.RNG.NextFloat(0, 1) < 0.125f) //Dichotomous
                                            {
                                                if (agent.DominanceLevel < 255)
                                                {
                                                    var lateralPitch = 0.3f * MathF.PI * species.MonopodialFactor;

                                                    var ou = TurnUpwards(agent.Orientation);
                                                    var orientation1 = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f * MathF.PI);
                                                    var meristem1 = formation.Birth(new(plant, agentID, OrganTypes.Meristem, agent.RandomOrientation(plant, species, orientation1), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel });
                                                    var orientation2 = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.25f * MathF.PI);
                                                    var meristem2 = formation.Birth(new(plant, agentID, OrganTypes.Meristem, agent.RandomOrientation(plant, species, orientation2), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel });

                                                    agent.Energy *= 0.8f;
                                                    agent.Water_g *= 0.8f;
                                                    if (species.LateralsPerNode > 0)
                                                    {
                                                        agent.CreateLeaves(agent, plant, lateralPitch, meristem1);
                                                        agent.CreateLeaves(agent, plant, lateralPitch, meristem2);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                var lateralPitch = agent.LateralAngle + species.LateralRoll;
                                                var meristem = formation.Birth(new(plant, agentID, OrganTypes.Meristem, TurnUpwards(agent.RandomOrientation(plant, species, agent.Orientation)), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel });
                                                agent.Energy *= 0.9f;
                                                agent.Water_g *= 0.9f;

                                                if (species.LateralsPerNode > 0)
                                                    agent.CreateLeaves(agent, plant, lateralPitch, meristem);
                                            }
                                        }
                                        break;

                                    case SeasonalPhase.Flowering:
                                        {
                                            if (true)
                                            {
                                                bool leftIsVeg = plant.RNG.NextFloat(0, 1) < 0.5f;
                                                var lateralPitch = 0.3f * MathF.PI * species.MonopodialFactor;
                                                float tilt = 0.20f * MathF.PI;

                                                var ou = TurnUpwards(agent.Orientation);

                                                var orientationv = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, lateralPitch - 0.25f * MathF.PI);
                                                var orientationf = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -lateralPitch - 0.25f * MathF.PI);

                                                var meristemVeg = formation.Birth(new(plant, agentID, OrganTypes.Meristem, agent.RandomOrientation(plant, species, orientationv), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel });
                                                var florwerstem = formation.Birth(new(plant, agentID, OrganTypes.FlowerStem, agent.RandomOrientation(plant, species, orientationf), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, ParentRadiusAtBirth = agent.Radius });
                                                //formation.Birth(new(plant, florwerstem, OrganTypes.Flower, agent.RandomOrientation(plant, species, orientationv), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel,  ParentRadiusAtBirth = float.MaxValue });
                                                agent.Energy *= 0.8f;
                                                agent.Water_g *= 0.8f;

                                                if (species.LateralsPerNode > 0)
                                                {
                                                    agent.CreateLeaves(agent, plant, lateralPitch, meristemVeg);
                                                }
                                            }
                                            else
                                            {
                                                var lateralPitch = agent.LateralAngle + species.LateralRoll;
                                                var meristem = formation.Birth(new(plant, agentID, OrganTypes.Meristem, TurnUpwards(agent.RandomOrientation(plant, species, agent.Orientation)), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel });
                                                agent.Energy *= 0.9f;
                                                agent.Water_g *= 0.9f;

                                                if (species.LateralsPerNode > 0)
                                                    agent.CreateLeaves(agent, plant, lateralPitch, meristem);
                                            }
                                        }
                                        break;
                                    case SeasonalPhase.PostFlower:
                                        {
                                            if (plant.RNG.NextFloat(0, 1) < 0.25f)
                                            {
                                                var lateralPitch = agent.LateralAngle + species.LateralRoll;
                                                var meristem = formation.Birth(new(plant, agentID, OrganTypes.Meristem, TurnUpwards(agent.RandomOrientation(plant, species, agent.Orientation)), 0.08f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.08f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel });
                                                agent.Energy *= 0.92f;
                                                agent.Water_g *= 0.92f;

                                                if (species.LateralsPerNode > 0)
                                                    agent.CreateLeaves(agent, plant, lateralPitch, meristem);
                                            }
                                            break;
                                        }
                                    case SeasonalPhase.ResetPending:
                                        {
                                        }
                                        break;


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
                else if (agent.Energy <= 0f && !agent.isRizome) //remove organs that drained all their energy
                {

                    switch (agent.Organ)
                    {
                        case OrganTypes.Petiole: agent.MakeBud(formation, children); break; //keep an option for a new leaf
                        case OrganTypes.Leaf:
                            {
                                formation.Death(agentID);
                                formation.Death(agent.Parent); //remove the petiole as well
                            }
                            break;

                        case OrganTypes.Stem:
                            {
                                if (formation.GetIsRizome(agent.Parent))
                                {
                                    agent.MakeBud(formation, children);
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
                    if (phase.Equals(SeasonalPhase.ResetPending) && agent.rizomeInfo.test3 && agent.rizomeInfo.rizomeDepth < 3)
                    {
                        if (plant.RNG.NextFloat(0, 1) < 0.0025f && agent.rizomeInfo.test)
                        {
                            var rizomOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f * MathF.PI);
                            if (agentID > 0)
                                rizomOrientation = agent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);
                            CreateRizome(plant, agentID, rizomOrientation, agent, formation);
                            agent.rizomeInfo.test = false;
                        }
                        if (plant.RNG.NextFloat(0, 1) < 0.0025f && agent.rizomeInfo.test2)
                        {
                            var rizomOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.666f * MathF.PI);
                            if (agentID > 0)
                                rizomOrientation = agent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 4f);
                            CreateRizome(plant, agentID, rizomOrientation, agent, formation);
                            agent.rizomeInfo.test2 = false;
                        }
                        if (plant.RNG.NextFloat(0, 1) < 0.0025f && agent.rizomeInfo.test4 && agentID == 0)
                        {
                            var rizomOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.666f * MathF.PI);
                            CreateRizome(plant, agentID, rizomOrientation, agent, formation);
                            agent.rizomeInfo.test4 = false;

                        }
                    }
                    else if (phase.Equals(SeasonalPhase.PreFlower) && !agent.rizomeInfo.test3)
                        agent.rizomeInfo.test3 = true;
                }


                //update auxins for meristem and stems that were meristem in the previous step
                agent.Auxins = wasMeristem || agent.Organ == OrganTypes.Meristem ? species.AuxinsProduction : 0;
            }

            static void CreateRizome(PlantFormation2 plant, int agentID, Quaternion rizomOrientation, AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation)
            {
                var rizome = new AboveGroundAgent(plant, agentID, OrganTypes.Stem, rizomOrientation, 0, length: 0.03f, radius: 0.01f);
                rizome.isRizome = true;
                rizome.rizomeInfo.rizomeDepth = agent.rizomeInfo.rizomeDepth + 1;
                var rizomeIndex = formation.Birth(rizome);
                var bud = new AboveGroundAgent(plant, rizomeIndex, OrganTypes.Bud, rizomOrientation, 0, initialResources: 1f, initialProduction: 1f);
                formation.Birth(bud);
            }
        }
    }
}
