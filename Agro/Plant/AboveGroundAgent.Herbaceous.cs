using System.Diagnostics;
using System.Numerics;

namespace Agro
{
    public partial struct AboveGroundAgent
    {
        public partial struct Herbaceous
        {
            static bool IsFlowerOrgan(OrganTypes organ) => OrganTypes.FlowerBud <= organ && organ <= OrganTypes.FlowerBaseBud;

            /// <summary>
            /// Consecutive blocked ticks after which a deflected rhizome apex gives up
            /// and retires into a stem segment.
            /// </summary>
            const byte MaximumBlockedRhizomeAttempts = 3;
            public static void Tick(ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID, uint timestep)
            {
                var plant = formation.Plant;
                var species = plant.Parameters;
                var world = plant.World;
                var ageHours = (timestep - agent.BirthTime) * world.HoursPerTick; //age in hours
                //bool sympodialPhase = ageHours >= species.SympodialStartAgeHours;

                var phase = formation.GetFloweringPhase(species, timestep);

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
                        if (ageHours > species.LeafMaximumAgeHours && formation.GetOrgan(agent.Parent) != OrganTypes.Meristem)
                        {
                            var p = ageHours / (7000 ); //6 months in hours
                            if (plant.RNG.NextFloatAccum(p * p, world.HoursPerTick))
                            {
                                // A perennial rosette replaces a senescent leaf at its existing node.  Leaving the petiole as an inert Bud
                                // made every old node permanently bare, which turned Geranium crowns into palm-like stems over time.
                                RenewPetiole(ref agent, formation, agentID);
                                return;
                            }
                        }
                    }
                    break;
                    case OrganTypes.Leaf:
                    {
                        var col = species.SenescentLeafColor;
                        agent.Color = Vector3.Lerp(species.YoungLeafColor, col, ageHours / species.LeafMaximumAgeHours);
                    }
                    break;
                    //height-based termination
                    case OrganTypes.Stem:
                        if (agent.DominanceLevel > 1 && formation.GetDominance(agent.Parent) < agent.DominanceLevel && !agent.IsRizome)
                        {
                            var h = 5f * formation.GetRelativeHeight(agentID);
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
                    case OrganTypes.Meristem:
                        wasMeristem = true;
                        break;
                }

                //new crown branches -- bud activation at the rhizome
                if (!agent.IsRizome && formation.GetIsRizome(agent.Parent) && agent.Organ.Equals(OrganTypes.Bud))
                {
                    if (TryActivateCrownBud(ref agent, formation, agentID, phase, ageHours, timestep))
                        return;
                }

                if (IsFlowerOrgan(agent.Organ))
                {
                    var flowerHelper = new FlowerHelper(species.FlowerSettings);
                    flowerHelper.handleAgent(ref agent, agentID, formation, timestep);
                    return;
                }

                if (agent.Energy > enoughEnergyState && !agent.IsRizome) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
                {
                    //Growth and branching
                    if (agent.Organ != OrganTypes.Bud) //for simplicity dormant buds do not grow
                    {
                        var currentSize = new Vector2(agent.Length, agent.Radius);
                        //dominance factor decreases the growth of structures further away from the dominant branch (based on the tree structure)
                        var dominanceFactor = species.GetDominanceFactor(agent.DominanceLevel);
                        var productionFactor = formation.DailyProductionMax > 1e-6f ? agent.PreviousDayProductionInvariant / formation.DailyProductionMax : 0f;
                        switch (agent.Organ)
                        {
                            case OrganTypes.Leaf:
                            {
                                //TDMI take env res efficiency into account
                                //TDMI thickness of the parent and parent-parent decides the max. leaf size,
                                //  also the energy consumption of the siblings beyond the node should have effect
                                var sizeLimit = !agent.FlowerAgent.flowerBase ? new Vector2(species.LeafLength + agent.LengthVar, species.LeafRadius + agent.RadiusVar): new Vector2(species.FlowerSettings.FlowerBaseLeafLength , species.FlowerSettings.FlowerBaseLeafRadius );
                                if (currentSize.X < sizeLimit.X || currentSize.Y < sizeLimit.Y)
                                {
                                    //HoursPerTick are included in GrowthTimeVar
                                    var growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * productionFactor;
                                    var petioleSizeLimit = !agent.FlowerAgent.flowerBase
                                        ? new Vector2(species.PetioleLength, species.PetioleRadius)
                                        : new Vector2(species.FlowerSettings.FlowerBaseLeafPetioleLength, species.FlowerSettings.FlowerBaseLeafPetioleRadius);
                                    growth = LimitLeafGrowthToPetioleSupport(formation, agent.Parent, currentSize, sizeLimit, growth, petioleSizeLimit);
                                    var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                                    growth = resultingSize - currentSize;
                                    agent.Length += growth.X;
                                    agent.Radius += growth.Y;
                                }
                            }
                            break;
                            case OrganTypes.Petiole:
                            {
                                var sizeLimit = !agent.FlowerAgent.flowerBase ? new Vector2(species.PetioleLength + agent.LengthVar, species.PetioleRadius + agent.RadiusVar) : new Vector2(species.FlowerSettings.FlowerBaseLeafPetioleLength , species.FlowerSettings.FlowerBaseLeafPetioleRadius );
                                if (currentSize.X < sizeLimit.X || currentSize.Y < sizeLimit.Y)
                                {
                                    //HoursPerTick are included in GrowthTimeVar
                                    var growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * productionFactor;
                                    var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                                    growth = resultingSize - currentSize;

                                    //assure not to outgrow the parent
                                    var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                    growth = LimitPetioleGrowthToSupport(currentSize, sizeLimit, growth, parentRadius);
                                    agent.Length += growth.X;
                                    agent.Radius += growth.Y;
                                }
                            }
                            break;
                            case OrganTypes.Meristem:
                            {
                                if (!agent.IsRizome)
                                {
                                    var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                    var waterReserve = Math.Min(1f, plant.WaterBalance);
                                    var growth = new Vector2(1e-3f, 2e-5f) * (dominanceFactor * energyReserve * waterReserve * world.HoursPerTick * productionFactor * species.HerbaceousGrowthRateScale);

                                    //assure not to outgrow the parent unless parent is rizome
                                    var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                    if (currentSize.Y + growth.Y > parentRadius)
                                        growth.Y = parentRadius - currentSize.Y;
                                    agent.Length += agent.Length <= agent.LengthVar ? growth.X : 0f;
                                    if (agent.Radius < species.ShootMaximumRadius)
                                        agent.Radius = Math.Min(agent.Radius + growth.Y, species.ShootMaximumRadius);
                                }
                            }
                            break;
                            case OrganTypes.Stem:
                            {
                                if (phase != SeasonalPhase.ResetPending)
                                {
                                    var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                    var growth = 2e-5f * dominanceFactor * energyReserve * Math.Min(plant.WaterBalance, energyReserve) * world.HoursPerTick * species.HerbaceousGrowthRateScale;

                                    //assure not to outgrow the parent
                                    var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                    if (currentSize.Y + growth > parentRadius)
                                        growth = parentRadius - currentSize.Y;

                                    if (agent.Radius < species.ShootMaximumRadius)
                                        agent.Radius = Math.Min(agent.Radius + growth, species.ShootMaximumRadius);

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
                        }

                        //TDMI maybe do it even if no growth
                        if (agent.Organ == OrganTypes.Stem || agent.Organ == OrganTypes.Meristem)
                        {
                            var crownContinuationProbability = 0f;
                            var flowerCommitmentProbability = 0f;

                            //chaining (meristem then continues in a new segment)
                            switch (phase) {
                                case SeasonalPhase.PreFlower:
                                    flowerCommitmentProbability = species.PreFloweringCommitmentProbabilityPerHour;
                                    crownContinuationProbability = species.PreFloweringCrownContinuationProbabilityPerHour;
                                    break;
                                case SeasonalPhase.Flowering:
                                    flowerCommitmentProbability = species.FloweringCommitmentProbabilityPerHour;
                                    crownContinuationProbability = species.FloweringCrownContinuationProbabilityPerHour;
                                    break;
                                case SeasonalPhase.PostFlower:
                                    flowerCommitmentProbability = species.PostFloweringCommitmentProbabilityPerHour;
                                    crownContinuationProbability = species.PostFloweringCrownContinuationProbabilityPerHour;
                                    break;
                                case SeasonalPhase.ResetPending:
                                    flowerCommitmentProbability = species.DormantFloweringCommitmentProbabilityPerHour;
                                    crownContinuationProbability = species.DormantCrownContinuationProbabilityPerHour;
                                    break;
                            }

                            if (agent.Organ == OrganTypes.Meristem && agent.Length > agent.LengthVar)
                            {
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
                                }

                                var committedToFlower = plant.RNG.NextFloatAccum(flowerCommitmentProbability, world.HoursPerTick) &&
                                    CommitToFlower(ref agent, formation, agentID, prevResources, prevProduction);
                                if (committedToFlower)
                                {
                                    agent.Organ = OrganTypes.Stem;
                                    agent.GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
                                    wasMeristem = true;
                                    if (species.ContinueCrownAfterFlowerCommitment)
                                        ContinueCrown(ref agent, formation, agentID, prevResources, prevProduction);
                                }
                                else if (plant.RNG.NextFloatAccum(crownContinuationProbability, world.HoursPerTick))
                                {
                                    agent.Organ = OrganTypes.Stem;
                                    agent.GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
                                    wasMeristem = true;
                                    ContinueCrown(ref agent, formation, agentID, prevResources, prevProduction);

                                    var parent = agent.Parent;
                                    while (parent >= 0 && !formation.GetIsRizome(parent))
                                    {
                                        foreach (var child in formation.GetChildren(parent))
                                        {
                                            if (formation.GetOrgan(child).Equals(OrganTypes.Petiole))
                                            {
                                                ReorientPetiole(formation, child);
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
                else if (agent.Energy <= 0f && !agent.IsRizome) //remove organs that drained all their energy
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
                else if (agent.IsRizome)
                {
                    if (agent.Energy <= 0)
                    {
                        // A perennial rhizome may exhaust its immediately available reserve in dormancy, but it does not vanish
                        // organ by organ. Keep the living structure available for the next season's crown buds and resource flow.
                        if (species.RetainDormantRhizomes || phase == SeasonalPhase.ResetPending)
                            agent.Energy = 0f;
                        else
                            formation.Death(agentID);
                        return;
                    }

                    if (agent.Organ == OrganTypes.Bud)
                    {
                        TryActivateRhizomeBud(ref agent, plant, species, phase, ageHours);
                        return;
                    }

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
                    }

                    if (agent.Organ == OrganTypes.Meristem && agent.Length >= plant.Parameters.RhizomeSegmentLength)
                    {
                        agent.Organ = OrganTypes.Stem;
                        agent.GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
                        wasMeristem = true;

                        var lateralPitch = agent.LateralAngle + formation.Plant.Parameters.LateralRoll;
                        // Store the dormant crown bud in the rhizome's local frame. CrownPitch is
                        // applied exactly once at bud break, so pi/2 produces an upright shoot.
                        var budOrientation = agent.Orientation;
                        var crownBudEnergy = 0.1f * Math.Max(agent.Energy, 0f);
                        var crownBudWater = 0.1f * Math.Max(agent.Water_g, 0f);
                        var crownBudRadius = species.CrownBudRadius;
                        var bud = new AboveGroundAgent(plant, agentID, OrganTypes.Bud, budOrientation, crownBudEnergy,
                            radius: crownBudRadius, length: 2.8f * crownBudRadius,
                            initialResources: prevResources, initialProduction: prevProduction)
                        {
                            Water_g = crownBudWater,
                            LateralAngle = lateralPitch
                        };

                        formation.Birth(bud);
                        agent.Energy -= crownBudEnergy;
                        agent.Water_g -= crownBudWater;

                        // A segment boundary is only a graph subdivision of the same living
                        // apex. Continue it as an active meristem; newly formed axillary buds
                        // remain dormant until their independent bud-break event.
                        FormRhizomeContinuationAndAxillaryBuds(plant, agentID, ref agent, formation, prevResources, prevProduction);
                    }

                    if (agent.Organ == OrganTypes.Stem && agent.RhizomeDevelopment.SegmentDepth == 0 && !agent.RhizomeDevelopment.BasalBudsFormed)
                    {
                        FormBasalRhizomeBuds(plant, agentID, ref agent, formation, prevResources, prevProduction);
                        agent.RhizomeDevelopment.BasalBudsFormed = true;
                    }


                    var currentSize = new Vector2(agent.Length, agent.Radius);
                    //dominance factor decreases the growth of structures further away from the dominant branch (based on the tree structure)
                    var dominanceFactor = species.GetDominanceFactor(agent.DominanceLevel);
                    switch (agent.Organ)
                    {
                        case OrganTypes.Meristem:
                            {
                                if (phase == SeasonalPhase.ResetPending)
                                    break;

                                var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                var waterReserve = Math.Min(1f, plant.WaterBalance);
                                var growth = new Vector2(1e-3f, 2e-5f) * (dominanceFactor * energyReserve * waterReserve * world.HoursPerTick * (agent.IsRizome ? species.RhizomeGrowthRateScale : species.HerbaceousGrowthRateScale));
                                // Console.WriteLine($"growm {growth.X} {energyReserve} {waterReserve} {agent.PreviousDayProductionInvariant / formation.DailyProductionMax} {agent.PreviousDayProductionInvariant} {formation.DailyProductionMax}");
                                //assure not to outgrow the parent unless parent is rizome
                                var test = formation.CollisionBvh.QueryOverlaps(formation.ComputeBoundsFromParameters(formation.GetTipPosition(agent.Parent), agent.Orientation, agent.Length + growth.X, agent.Radius+ growth.Y));
                                var blocked = false;
                                if (test != null)
                                {
                                    foreach (var colission in test)
                                    {
                                        // The BVH returns overlapping axis-aligned bounds, not a physical intersection. In particular, perpendicular rhizomes that
                                        // leave the same crown share a bounding-box corner. Confirm the capsule/branch intersection before rejecting a new run.
                                        if (agent.Parent != colission && !formation.GetChildren(agent.Parent).Contains(colission) && formation.GetIsRizome(colission) && formation.BranchIntersect(agentID, colission))
                                        {
                                            blocked = true;
                                            break;
                                        }
                                    }
                                }

                                if (blocked)
                                {
                                    // A creeping apex meeting an established runner is deflected around it;
                                    // terminating on first contact killed exactly the tips that would have
                                    // filled the gaps between the primary axes, leaving isolated rays.
                                    // Killing the agent instead of retiring it removed its whole young
                                    // subtree, so a filled pot periodically collapsed back to a single crown.
                                    if (agent.RhizomeDevelopment.BlockedAttempts >= MaximumBlockedRhizomeAttempts
                                        || !(species.RhizomeObstacleDeflectionAngle > 0f))
                                    {
                                        agent.Organ = OrganTypes.Stem;
                                        return;
                                    }

                                    ++agent.RhizomeDevelopment.BlockedAttempts;
                                    var direction = Vector3.Transform(Vector3.UnitX, agent.Orientation);
                                    // Each deflection is applied to the already deflected heading, so the
                                    // turn direction has to stay fixed for one tip: alternating it would
                                    // just swing the apex back onto the heading that was already blocked.
                                    // Neighbouring axes turn opposite ways, as they do for axillary buds.
                                    var side = agent.RhizomeDevelopment.SegmentDepth % 2 == 0 ? 1f : -1f;
                                    agent.SetStructuralOrientation(HorizontalRhizomeOrientation(
                                        MathF.Atan2(direction.Z, direction.X)
                                        + side * species.RhizomeObstacleDeflectionAngle
                                        + plant.RNG.NextFloatVar(5f * MathF.PI / 180f)));
                                    return;
                                }

                                agent.RhizomeDevelopment.BlockedAttempts = 0;

                                if (plant.Soil.IntersectPoint(formation.GetBaseCenterWorld(agentID) + Vector3.Transform(Vector3.UnitX, formation.GetDirection(agentID)) * agent.Length + Vector3.Transform(Vector3.UnitX, agent.Orientation) * plant.Parameters.RhizomeSegmentLength, plant.SoilIndex) < 0)
                                    return;

                                {
                                    var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                    if (currentSize.Y + growth.Y > parentRadius)
                                        growth.Y = parentRadius - currentSize.Y;
                                    agent.Length += agent.Length <= plant.Parameters.RhizomeSegmentLength ? growth.X : 0f;
                                    if (agent.Radius < species.RhizomeSegmentRadius)
                                        agent.Radius = Math.Min(agent.Radius + growth.Y, species.RhizomeSegmentRadius);
                                }
                            }
                            break;
                        case OrganTypes.Stem:
                            {
                                if (phase != SeasonalPhase.ResetPending)
                                {
                                    var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                    var growth = 2e-5f * dominanceFactor * energyReserve * Math.Min(plant.WaterBalance, energyReserve) * world.HoursPerTick * species.RhizomeGrowthRateScale;

                                    //assure not to outgrow the parent
                                    var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                    if (currentSize.Y + growth > parentRadius)
                                        growth = parentRadius - currentSize.Y;

                                    if (agent.Radius < species.RhizomeSegmentRadius)
                                        agent.Radius = Math.Min(agent.Radius + growth, species.RhizomeSegmentRadius);
                                }
                            }
                            break;
                    }
                }

                //update auxins for meristem and stems that were meristem in the previous step
                agent.Auxins = wasMeristem || agent.Organ == OrganTypes.Meristem ? species.AuxinsProduction : 0;
            }

            static bool TryActivateCrownBud(ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation,
                int agentID, SeasonalPhase phase, float ageHours, uint timestep)
            {
                var plant = formation.Plant;
                var species = plant.Parameters;
                if (ageHours <= 24f || !species.CrownBudActivationPeriod.HasFlag(phase) ||
                    agent.Energy <= agent.EnoughEnergy())
                    return false;

                var date = plant.World.GetTime(timestep);
                var activationCycle = date.Month >= 3 ? date.Year : date.Year - 1;
                if (agent.LastCrownActivationCycle == activationCycle)
                    return false;

                //The seasonal probability describes the whole bud-break window, so it
                //is spread over that window instead of being decided in one instant.
                //Consuming the cycle marker before the roll gave every bud a single
                //attempt per year: at a 0.2 seasonal probability the expected wait to
                //raise a crown was five years, so only the first shoots ever appeared.
                //The marker is now claimed only by an activation that actually happens.
                var activationProbability = SpeciesSettings.SeasonalProbabilityPerTick(
                    species.CrownBudActivationProbabilityPerSeason,
                    species.CrownBudBreakWindowHours, plant.World.HoursPerTick);
                if (plant.RNG.NextFloat() >= activationProbability)
                    return false;

                agent.LastCrownActivationCycle = activationCycle;

                // The dormant bud is a compressed, resource-storing shoot. Preserve its
                // physical radius when it expands instead of replacing it with the generic
                // two-micrometre construction radius.
                var crownOrientation = TurnUpwards(agent.Orientation *
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, species.CrownPitch));
                agent.SetStructuralOrientation(crownOrientation);
                agent.DominanceLevel = 1;
                agent.WoodFactor = 0f;
                agent.Radius = Math.Max(agent.Radius, species.CrownBudRadius);

                // The short basal riser lifts the first compact leaf node above a
                // surface-close rhizome. Subsequent crown internodes use NodeDistance.
                // GetBaseCenter matches the frame the tree cache seeds roots with,
                // which is plant.Position; GetBaseCenterWorld adds the soil field
                // origin and made this subtraction mix two frames. The riser is also
                // floored, because a herbaceous stem only ever grows in radius, so a
                // zero-length riser would stay zero - and have no volume - forever.
                var baseY = formation.GetBaseCenter(agentID).Y;
                agent.Length = Math.Max(species.MinimumInternodeLength,
                    Math.Max(species.CrownNodeHeightAboveSoil,
                        plant.Position.Y - baseY + species.CrownNodeHeightAboveSoil));
                agent.LengthVar = agent.Length;
                agent.Organ = OrganTypes.Stem;
                agent.GrowthTimeVar = plant.World.HoursPerTick /
                    (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));

                var lateralPitch = agent.LateralAngle + species.LateralRoll;
                var previousResources = agent.PreviousDayEnvResourcesInvariant > 0f
                    ? agent.PreviousDayEnvResourcesInvariant
                    : formation.DailyResourceMax;
                var previousProduction = agent.PreviousDayProductionInvariant > 0f
                    ? agent.PreviousDayProductionInvariant
                    : formation.DailyProductionMax;
                CreateVegetativeCrownNode(ref agent, formation, agentID, agent.Orientation, lateralPitch,
                    previousResources, previousProduction);
                return true;
            }

            static int CreateVegetativeCrownNode(ref AboveGroundAgent source,
                PlantSubFormation<AboveGroundAgent> formation, int parentID, Quaternion orientation,
                float lateralPitch, float previousResources, float previousProduction)
            {
                const float StartupReserveFraction = 0.5f;
                var plant = formation.Plant;
                var species = plant.Parameters;
                var organCount = 1 + species.LateralsPerNode * (species.PetioleSegmentCount + 1);
                var transferredEnergy = StartupReserveFraction * Math.Max(source.Energy, 0f);
                var transferredWater = StartupReserveFraction * Math.Max(source.Water_g, 0f);
                var energyPerOrgan = transferredEnergy / organCount;
                var waterPerOrgan = transferredWater / organCount;

                var meristem = formation.Birth(new AboveGroundAgent(plant, parentID, OrganTypes.Meristem,
                    orientation, energyPerOrgan, initialResources: previousResources, initialProduction: previousProduction)
                {
                    Water_g = waterPerOrgan,
                    LateralAngle = lateralPitch,
                    DominanceLevel = source.DominanceLevel
                });

                source.Energy = Math.Max(0f, source.Energy - transferredEnergy);
                source.Water_g = Math.Max(0f, source.Water_g - transferredWater);
                if (species.LateralsPerNode > 0)
                {
                    AboveGroundAgent.CreateLeavesFromReserve(source, plant, lateralPitch, meristem,
                        previousResources, previousProduction, energyPerOrgan, waterPerOrgan);
                }

                return meristem;
            }

            private static bool CommitToFlower(ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID, float prevResources, float prevProduction)
            {
                var flowerOrgans = new List<OrganTypes>() { OrganTypes.FlowerStem, OrganTypes.FlowerPadel, OrganTypes.FlowerPedicel, OrganTypes.FlowerMeristem, OrganTypes.FlowerBud };
                var i = agent.Parent;
                var maximumFlowersPerCrown = Math.Max(1, formation.Plant.Parameters.MaximumFlowersPerCrown);
                var currentFlowersPerCrown = 0;
                while (i >= 0 && !formation.GetIsRizome(i) && currentFlowersPerCrown < maximumFlowersPerCrown)
                {
                    foreach (var child in formation.GetChildren(i))
                        if (flowerOrgans.Contains(formation.GetOrgan(child)))
                            currentFlowersPerCrown++;

                    i = formation.GetParent(i);
                }
                if (currentFlowersPerCrown < maximumFlowersPerCrown)
                {
                    //Console.WriteLine("commit");
                    var lateralPitch = agent.LateralAngle + formation.Plant.Parameters.LateralRoll;
                    // A culm is an upright axial shoot.  TurnUpwards only rotates
                    // the cross-section and deliberately preserves its main axis,
                    // so it cannot make a leaning parent axis vertical.  Use the
                    // gravitropic base orientation for the initial floral culm.
                    var flowerOrientation = PlantFormation2.AdjustUpBase(agent.Orientation, up: true);
                    // FlowerMeristem selects basal versus terminal dimensions in
                    // its constructor.  Supplying this state only in the object
                    // initializer made the first Carex culm use the 4 mm terminal
                    // size, forcing the 18 cm basal segment to grow above it.
                    var flowerAgent = new Flower
                    {
                        BirthTime = formation.Plant.World.Timestep,
                        FlowerStartTime = formation.Plant.World.Timestep,
                        debth = 0,
                        flowerBase = true
                    };
                    var flowerEnergy = 0.1f * Math.Max(agent.Energy, 0f);
                    var flowerWater = 0.1f * Math.Max(agent.Water_g, 0f);
                    formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerMeristem, flowerOrientation,
                        flowerEnergy, initialResources: prevResources, initialProduction: prevProduction, flowerAgent: flowerAgent)
                    {
                        Water_g = flowerWater,
                        LateralAngle = lateralPitch,
                        DominanceLevel = agent.DominanceLevel,
                        FlowerAgent = flowerAgent
                    });
                    agent.Energy = Math.Max(0f, agent.Energy - flowerEnergy);
                    agent.Water_g = Math.Max(0f, agent.Water_g - flowerWater);
                    return true;
                }

                return false;
            }

            static void TryActivateRhizomeBud(ref AboveGroundAgent agent, PlantFormation2 plant, SpeciesSettings species, SeasonalPhase phase, float ageHours)
            {
                if (ageHours <= 24f || !species.RhizomeBudActivationPeriod.HasFlag(phase) || agent.Energy <= agent.EnoughEnergy())
                    return;

                //Lateral branching of a creeping rhizome is an annual developmental
                //event: the apex pauses or terminates in an inflorescence at the end of
                //a season and growth resumes sympodially from an axillary bud below it.
                //Expressed as a constant hourly hazard it has to be set so low that
                //branching effectively never happens, leaving each axis an unbranched
                //ray; repeated annual branching is what closes the gaps into a carpet.
                //The per-season setting therefore takes precedence when it is given.
                float probability;
                if (agent.RhizomeDevelopment.OriginatingBudPosition == RhizomeBudPosition.Axillary
                    && species.RhizomeAxillaryBudActivationProbabilityPerSeason > 0f)
                {
                    probability = SpeciesSettings.SeasonalProbabilityPerTick(
                        species.RhizomeAxillaryBudActivationProbabilityPerSeason,
                        species.RhizomeBudBreakWindowHours, plant.World.HoursPerTick);
                    if (plant.RNG.NextFloat() >= probability)
                        return;
                }
                else
                {
                    probability = agent.RhizomeDevelopment.OriginatingBudPosition switch
                    {
                        RhizomeBudPosition.Basal => species.RhizomeBasalBudActivationProbabilityPerHour,
                        RhizomeBudPosition.Axillary => species.RhizomeAxillaryBudActivationProbabilityPerHour,
                        _ => 0f
                    };
                    if (!plant.RNG.NextFloatAccum(probability, plant.World.HoursPerTick))
                        return;
                }

                agent.Organ = OrganTypes.Meristem;
                agent.Length = Math.Max(agent.Length, InitialLength);
                agent.Radius = Math.Max(agent.Radius, species.RhizomeBudRadius);
                agent.LengthVar = species.RhizomeSegmentLength;
                agent.RadiusVar = species.RhizomeSegmentRadius;
                agent.WoodFactor = 1f;
            }

            static void FormBasalRhizomeBuds(PlantFormation2 plant, int agentID, ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, float prevResources, float prevProduction)
            {
                if (plant.Parameters.RhizomeMaximumSegmentDepth == 0)
                    return;

                var budCount = Math.Clamp(plant.Parameters.InitialBasalRhizomeBuds, 0, 32);
                if (budCount == 0)
                    return;

                var childEnergy = 0.25f * Math.Max(agent.Energy, 0f) / budCount;
                var childWater = 0.1f * Math.Max(agent.Water_g, 0f) / budCount;
                for (var budIndex = 0; budIndex < budCount; ++budIndex)
                {
                    var orientation = BasalRhizomeBudOrientation(agent, budIndex, budCount);
                    CreateRhizomeBud(plant, agentID, orientation, agent.RhizomeDevelopment.SegmentDepth + 1,
                        RhizomeBudPosition.Basal, formation, prevResources, prevProduction, childEnergy, childWater);
                }

                agent.Energy -= childEnergy * budCount;
                agent.Water_g -= childWater * budCount;
            }

            static void FormRhizomeContinuationAndAxillaryBuds(PlantFormation2 plant, int agentID,
                ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation,
                float prevResources, float prevProduction)
            {
                if (agent.RhizomeDevelopment.SegmentDepth >= plant.Parameters.RhizomeMaximumSegmentDepth)
                    return;

                var axillaryBudCount = Math.Clamp(plant.Parameters.RhizomeAxillaryBudsPerSegment, 0, 32);
                var budCount = 1 + axillaryBudCount;
                var childEnergy = 0.25f * Math.Max(agent.Energy, 0f) / budCount;
                var childWater = 0.1f * Math.Max(agent.Water_g, 0f) / budCount;
                var childDepth = agent.RhizomeDevelopment.SegmentDepth + 1;

                CreateRhizomeMeristem(plant, agentID, ApicalRhizomeContinuationOrientation(plant, agent), childDepth,
                    agent.RhizomeDevelopment.OriginatingBudPosition, formation, prevResources, prevProduction,
                    childEnergy, childWater);
                for (var budIndex = 0; budIndex < axillaryBudCount; ++budIndex)
                {
                    var orientation = AxillaryRhizomeBudOrientation(plant, agent, budIndex, axillaryBudCount);
                    CreateRhizomeBud(plant, agentID, orientation, childDepth, RhizomeBudPosition.Axillary,
                        formation, prevResources, prevProduction, childEnergy, childWater);
                }

                agent.Energy -= childEnergy * budCount;
                agent.Water_g -= childWater * budCount;
            }

            static Quaternion BasalRhizomeBudOrientation(AboveGroundAgent agent, int budIndex, int budCount)
            {
                var direction = Vector3.Transform(Vector3.UnitX, agent.Orientation);
                var azimuth = MathF.Atan2(direction.Z, direction.X) + 2f * MathF.PI * budIndex / budCount;
                return HorizontalRhizomeOrientation(azimuth);
            }

            static Quaternion ApicalRhizomeContinuationOrientation(PlantFormation2 plant, AboveGroundAgent agent)
            {
                var direction = Vector3.Transform(Vector3.UnitX, agent.Orientation);
                var azimuth = MathF.Atan2(direction.Z, direction.X) + plant.RNG.NextFloatVar(10f * MathF.PI / 180f);
                return HorizontalRhizomeOrientation(azimuth);
            }

            static Quaternion AxillaryRhizomeBudOrientation(PlantFormation2 plant, AboveGroundAgent agent, int budIndex, int budCount)
            {
                var direction = Vector3.Transform(Vector3.UnitX, agent.Orientation);
                var azimuth = MathF.Atan2(direction.Z, direction.X);
                var rank = budIndex / 2;
                var rankCount = Math.Max(1, (budCount + 1) / 2);
                var magnitude = rankCount == 1
                    ? MathF.PI / 3f
                    : MathF.PI / 3f + rank * MathF.PI / (3f * (rankCount - 1));
                // A single axillary bud alternates sides at successive nodes instead of
                // introducing a permanent clockwise branching bias.
                var side = (budIndex + agent.RhizomeDevelopment.SegmentDepth) % 2 == 0 ? 1f : -1f;
                return HorizontalRhizomeOrientation(azimuth + side * magnitude + plant.RNG.NextFloatVar(5f * MathF.PI / 180f));
            }

            // System.Numerics uses the right-hand convention: a positive rotation
            // around +Y turns +X toward -Z. Negate the geometric X/Z azimuth when
            // constructing its quaternion so successive segments keep their heading.
            static Quaternion HorizontalRhizomeOrientation(float azimuth) =>
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, -azimuth);

            static void CreateRhizomeMeristem(PlantFormation2 plant, int parentID, Quaternion orientation,
                int segmentDepth, RhizomeBudPosition axisOrigin, PlantSubFormation<AboveGroundAgent> formation,
                float prevResources, float prevProduction, float childEnergy, float childWater)
            {
                // This is the uninterrupted apex of the parent rhizome, not a newly
                // initiated bud. Preserve its conducting cross-section across the
                // numerical segment boundary.
                var activeTipRadius = Math.Clamp(formation.GetBaseRadius(parentID),
                    plant.Parameters.RhizomeBudRadius, plant.Parameters.RhizomeSegmentRadius);
                var meristem = new AboveGroundAgent(plant, parentID, OrganTypes.Meristem, orientation, childEnergy,
                    radius: activeTipRadius,
                    initialResources: prevResources, initialProduction: prevProduction, rizome: true)
                {
                    Water_g = childWater,
                    WoodFactor = 1f
                };
                meristem.RhizomeDevelopment.SegmentDepth = segmentDepth;
                meristem.RhizomeDevelopment.OriginatingBudPosition = axisOrigin;
                formation.Birth(meristem);
            }

            static void CreateRhizomeBud(PlantFormation2 plant, int parentID, Quaternion orientation, int segmentDepth,
                RhizomeBudPosition budPosition, PlantSubFormation<AboveGroundAgent> formation, float prevResources,
                float prevProduction, float childEnergy, float childWater)
            {
                var budRadius = plant.Parameters.RhizomeBudRadius;
                var bud = new AboveGroundAgent(plant, parentID, OrganTypes.Bud, orientation, childEnergy,
                    radius: budRadius, length: 2.8f * budRadius,
                    initialResources: prevResources, initialProduction: prevProduction, rizome: true)
                {
                    Water_g = childWater,
                    WoodFactor = 1f
                };
                bud.RhizomeDevelopment.SegmentDepth = segmentDepth;
                bud.RhizomeDevelopment.OriginatingBudPosition = budPosition;
                formation.Birth(bud);
            }

            static void ContinueCrown(ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation,
                int agentID, float prevResources, float prevProduction)
            {
                var lateralPitch = agent.LateralAngle + formation.Plant.Parameters.LateralRoll;
                var orientation = TurnUpwards(agent.RandomOrientation(formation.Plant,
                    formation.Plant.Parameters, agent.Orientation));
                CreateVegetativeCrownNode(ref agent, formation, agentID, orientation, lateralPitch,
                    prevResources, prevProduction);
            }

            static void RenewPetiole(ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID)
            {
                var plant = formation.Plant;
                var replacement = new AboveGroundAgent(
                    plant,
                    agent.Parent,
                    OrganTypes.Petiole,
                    agent.Orientation,
                    0.1f * Math.Max(agent.Energy, 0f),
                    initialResources: agent.PreviousDayEnvResourcesInvariant,
                    initialProduction: agent.PreviousDayProductionInvariant)
                {
                    DominanceLevel = agent.DominanceLevel,
                    ParentRadiusAtBirth = formation.GetBaseRadius(agent.Parent)
                };
                var replacementID = formation.Birth(replacement);
                AboveGroundAgent.CreateLeaf(replacement.Orientation, replacement, plant, replacementID,
                    agent.PreviousDayEnvResourcesInvariant,
                    agent.PreviousDayProductionInvariant);
                formation.Death(agentID);
            }

            static void ReorientPetiole(PlantSubFormation<AboveGroundAgent> formation, int agentID)
            {
                var maxAngle = formation.Plant.Parameters.PetioleReorientationMaximumAngle;
                // Compare parent and child axes in world space.
                var parentRot = formation.GetDirection(formation.GetParent(agentID));
                var parentDir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, parentRot));

                var dir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, formation.GetDirection(agentID)));

                var cos = Math.Clamp(Vector3.Dot(dir, parentDir), -1f, 1f);
                var angleFromParent = MathF.Acos(cos);

                // Increase the divergence gradually, up to the configured maximum.
                if (angleFromParent < maxAngle)
                {
                    var axis = Vector3.Cross(parentDir, dir);
                    if (axis.LengthSquared() > 1e-8f)
                    {
                        axis = Vector3.Normalize(axis);

                        var delta = formation.Plant.Parameters.PetioleReorientationStepAngle;
                        var worldRot = Quaternion.CreateFromAxisAngle(axis, delta);
                        var orientation = formation.GetDirection(agentID);
                        formation.SendProtected(agentID, new AboveGroundAgent.OrientationSet(Quaternion.Normalize(worldRot * orientation)));

                        var children = formation.GetChildren(agentID);
                        if (children.Count > 0)
                        {
                            var firstChild = children[0];
                            formation.SendProtected(firstChild, new AboveGroundAgent.OrientationSet(
                                Quaternion.Normalize(worldRot * formation.GetDirection(firstChild))));
                        }
                    }
                }
            }
        }
    }
}
