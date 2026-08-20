using System.Diagnostics;
using System.Numerics;

namespace Agro
{
    public partial struct AboveGroundAgent
    {
        public partial struct Herbaceous
        {
            static bool IsFlowerOrgan(OrganTypes organ) => OrganTypes.FlowerBud <= organ && organ <= OrganTypes.FlowerBaseBud;
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
                    if (species.CrownBudActivationPeriod.HasFlag(phase) && agent.CrownActivationAttemptAvailable && ageHours > 24)
                    {
                        agent.CrownActivationAttemptAvailable = false;
                        if (plant.RNG.NextFloat() < species.CrownBudActivationProbabilityPerSeason)
                        {
                            agent.Organ = OrganTypes.Meristem;
                            // CrownPitch is measured from the rhizome axis. It used to be calculated but then overwritten by a
                            // hard-coded vertical orientation, so neither species setting could control its crown habit.
                            agent.Orientation = TurnUpwards(agent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, species.CrownPitch));
                            agent.DominanceLevel = 1;
                            agent.Radius = AboveGroundAgent.InitialRadius;
                            agent.Energy = 0.1f * formation.GetEnergy(agent.Parent); //MI 20260819 why this?
                            agent.Water_g = 0.1f * formation.GetWater(agent.Parent);

                            // Rhizomes run slightly below the surface.  A crown therefore needs a short riser before it can make
                            // leaves; attaching the first pair directly to the rhizome put them underground.  This basal segment
                            // is distinct from the compact leaf-node internodes.
                            var baseY = formation.GetBaseCenterWorld(agentID).Y;
                            agent.Length = Math.Max(species.CrownNodeHeightAboveSoil, plant.Position.Y - baseY + species.CrownNodeHeightAboveSoil);
                            agent.LengthVar = agent.Length;
                            agent.Organ = OrganTypes.Stem;
                            agent.GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
                            var lateralPitch = agent.LateralAngle + species.LateralRoll;
                            var childEnergy = 0.1f * agent.Energy;
                            var childWater = 0.1f * agent.Water_g;
                            var meristem = formation.Birth(new AboveGroundAgent(plant, agentID, OrganTypes.Meristem, agent.Orientation, childEnergy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax)
                            {
                                Water_g = childWater,
                                LateralAngle = lateralPitch,
                                DominanceLevel = agent.DominanceLevel
                            });
                            agent.Energy -= childEnergy;
                            agent.Water_g -= childWater;

                            // Leaf organs receive their growth allocation in the next census.  Giving each one a fraction of the
                            // same parent reserve here would create energy rather than transfer it, particularly visible once many rhizome crowns are active.
                            if (species.LateralsPerNode > 0)
                            {
                                var leafSource = agent;
                                leafSource.Energy = 0f;
                                leafSource.CreateLeaves(leafSource, plant, lateralPitch, meristem);
                            }

                        }
                    }
                    else if (phase == SeasonalPhase.ResetPending)
                        agent.CrownActivationAttemptAvailable = true;

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
                            var probabilityChaning = 0f;
                            var probabilityFlower = 0f;

                            //chaining (meristem then continues in a new segment)
                            switch (phase) {
                                case SeasonalPhase.PreFlower:
                                    probabilityFlower = species.PreFloweringCommitmentProbabilityPerHour;
                                    probabilityChaning = species.PreFloweringCrownContinuationProbabilityPerHour;
                                    break;
                                case SeasonalPhase.Flowering:
                                    probabilityFlower = species.FloweringCommitmentProbabilityPerHour;
                                    probabilityChaning = species.FloweringCrownContinuationProbabilityPerHour;
                                    break;
                                case SeasonalPhase.PostFlower:
                                    probabilityFlower = species.PostFloweringCommitmentProbabilityPerHour;
                                    probabilityChaning = species.PostFloweringCrownContinuationProbabilityPerHour;
                                    break;
                                case SeasonalPhase.ResetPending:
                                    probabilityFlower = species.DormantFloweringCommitmentProbabilityPerHour;
                                    probabilityChaning = species.DormantCrownContinuationProbabilityPerHour;
                                    break;
                            }

                            if (agent.Organ == OrganTypes.Meristem && agent.Length > agent.LengthVar)
                            {
                                if (plant.RNG.NextFloatAccum(probabilityFlower, world.HoursPerTick))
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
                                    if (species.ContinueCrownAfterFlowerCommitment)
                                        doChaning(agent, formation, agentID, prevResources, prevProduction);
                                }
                                else if (plant.RNG.NextFloatAccum(probabilityChaning, world.HoursPerTick))
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
                                    while (parent >= 0 && !formation.GetIsRizome(parent))
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
                    if(agent.Energy <= 0)
                    {
                        // A perennial rhizome may exhaust its immediately available reserve in dormancy, but it does not vanish
                        // organ by organ. Keep the living structure available for the next season's crown buds and resource flow.
                        if (species.RetainDormantRhizomes || phase == SeasonalPhase.ResetPending)
                            agent.Energy = 0f;
                        else
                            formation.Death(agentID);
                        return;
                    }

                    if (agent.Organ == OrganTypes.Meristem && agent.Length >= plant.Parameters.RhizomeSegmentLength)
                    {
                        agent.Organ = OrganTypes.Stem;
                        // A completed creeping segment is an eligible source for its successor. Previously this flag was only restored in PreFlower below,
                        // so segments that completed during flowering or afterwards became permanent dead ends and the carpet could never move beyond the seed vicinity.
                        agent.RizomeInfo.test3 = true;
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
                                var intersect = false;
                                if (test != null)
                                {
                                    foreach (var colission in test)
                                    {
                                        // The BVH returns overlapping axis-aligned bounds, not a physical intersection. In particular, perpendicular rhizomes that
                                        // leave the same crown share a bounding-box corner. Confirm the capsule/branch intersection before rejecting a new run.
                                        if (agent.Parent != colission && !formation.GetChildren(agent.Parent).Contains(colission) && formation.GetIsRizome(colission) && formation.BranchIntersect(agentID, colission))
                                        {
                                            // Contact with an established runner terminates this growing tip. Killing the agent recursively removed its
                                            // entire young rhizome subtree, so a filled pot periodically collapsed back to a single crown.
                                            agent.Organ = OrganTypes.Stem;
                                            agent.RizomeInfo.test3 = false;
                                            return;
                                        }
                                    }
                                }

                                if (plant.Soil.IntersectPoint(formation.GetBaseCenterWorld(agentID) + Vector3.Transform(Vector3.UnitX, formation.GetDirection(agentID)) * agent.Length + Vector3.Transform(Vector3.UnitX, agent.Orientation) * plant.Parameters.RhizomeSegmentLength, plant.SoilIndex) < 0)
                                    return;

                                if (!intersect)
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
                                    var growth = 2e-5f * dominanceFactor * energyReserve * Math.Min(plant.WaterBalance, energyReserve) * world.HoursPerTick * species.HerbaceousGrowthRateScale;

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

                    if (agent.Organ.Equals(OrganTypes.Stem))
                    {
                        //Console.WriteLine("chan");
                        if ((!phase.Equals(SeasonalPhase.ResetPending)) && agent.RizomeInfo.test3 && agent.RizomeInfo.Depth < species.RhizomeMaximumSegmentDepth)
                        {
                            var initialAxis = agent.RizomeInfo.Depth == 0;
                            var requestedOffshoots = initialAxis ? species.InitialRhizomeAxes : species.RhizomeOffshootsPerSegment;
                            var offshoots = Math.Clamp(requestedOffshoots, 0, 32);
                            var initialAxisEnergy = agent.Energy;
                            var initialAxisWater = agent.Water_g;

                            // A species' declared initial axes are established
                            // together at germination.  Treating them as four
                            // independent, short-lived chance events often left
                            // only one or two runners before the seed reserve was
                            // spent, so a nominally radial Geranium became a
                            // narrow strip.  Later segments still branch at the
                            // species' hourly rate.
                            for (var offshoot = 0; offshoot < offshoots; ++offshoot)
                            {
                                var offshootBit = 1u << offshoot;
                                if ((agent.RizomeInfo.OffshootsCreated & offshootBit) != 0 ||
                                    (!initialAxis && !ShouldExtendRizome(plant, species)))
                                    continue;

                                var rizomOrientation = RizomeOrientation(plant, agent, initialAxis, offshoot, offshoots);
                                // Split the initial crown's investment evenly
                                // between its radial axes.  Taking 25% of the
                                // dwindling parent at each birth systematically
                                // starved the later directions, turning a nominal
                                // radial carpet into one dominant runner.
                                var childEnergy = initialAxis ? Math.Min(agent.Energy, 0.8f * initialAxisEnergy / offshoots) : 0.25f * agent.Energy;
                                var childWater = initialAxis ? Math.Min(agent.Water_g, 0.8f * initialAxisWater / offshoots) : 0.1f * agent.Water_g;
                                CreateRizome(plant, agentID, rizomOrientation, ref agent, formation, prevResources, prevProduction, childEnergy, childWater);
                                agent.RizomeInfo.OffshootsCreated |= offshootBit;
                            }

                            // A ground-cover rhizome needs a few side shoots to
                            // occupy the spaces between its primary rays.  This is
                            // deliberately a one-time, low-probability event per
                            // segment, unlike the old unconditional branching
                            // that produced an implausibly dense binary thicket.
                            if (!initialAxis && !agent.RizomeInfo.LateralCreated && species.RhizomeSecondaryLateralProbabilityPerHour > 0f && plant.RNG.NextFloatAccum(species.RhizomeSecondaryLateralProbabilityPerHour, world.HoursPerTick))
                            {
                                var side = plant.RNG.NextFloat() < 0.5f ? -1f : 1f;
                                var lateralOrientation = RizomeLateralOrientation(agent, side);
                                CreateRizome(plant, agentID, lateralOrientation, ref agent, formation, prevResources, prevProduction, 0.2f * agent.Energy, 0.1f * agent.Water_g);
                                agent.RizomeInfo.LateralCreated = true;
                            }
                        }
                    }
                }

                //update auxins for meristem and stems that were meristem in the previous step
                agent.Auxins = wasMeristem || agent.Organ == OrganTypes.Meristem ? species.AuxinsProduction : 0;
            }

            private static void commitToFlower(AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID, float prevResources, float prevProduction)
            {
                var flowerOrgans = new List<OrganTypes>() { OrganTypes.FlowerStem, OrganTypes.FlowerPadel, OrganTypes.FlowerPedicel, OrganTypes.FlowerMeristem, OrganTypes.FlowerBud };
                var i = agent.Parent;
                var flowerPerCrown = 1;
                var currentflowerPerCrown = 0;
                while (i >= 0 && !formation.GetIsRizome(i) && currentflowerPerCrown < flowerPerCrown) {
                    foreach (var child in formation.GetChildren(i))
                        if (flowerOrgans.Contains(formation.GetOrgan(child)))
                            currentflowerPerCrown++;

                    i = formation.GetParent(i);
                }
                if (currentflowerPerCrown < flowerPerCrown)
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
                    formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerMeristem, flowerOrientation, 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction, flowerAgent: flowerAgent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = flowerAgent });
                    agent.Energy *= 0.9f;
                    agent.Water_g *= 0.9f;
                }

            }

            static bool ShouldExtendRizome(PlantFormation2 plant, SpeciesSettings species) => plant.RNG.NextFloatAccum(species.RhizomeOffshootProbabilityPerHour, plant.World.HoursPerTick);

            static Quaternion RizomeOrientation(PlantFormation2 plant, AboveGroundAgent agent, bool initialAxis, int offshoot, int offshoots)
            {
                var direction = Vector3.Transform(Vector3.UnitX, agent.Orientation);
                var yaw = MathF.Atan2(direction.Z, direction.X);

                // The first generation fans evenly from the crown.  A descendant
                // keeps its first offshoot on course with only a small turn;
                // optional extra laterals occupy the remaining sector angles.
                var turn = initialAxis
                    ? 2f * MathF.PI * offshoot / offshoots
                    : 2f * MathF.PI * offshoot / (offshoots + 1) + plant.RNG.NextFloatVar(10f * MathF.PI / 180f);
                return Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw + turn);
            }

            static Quaternion RizomeLateralOrientation(AboveGroundAgent agent, float side)
            {
                var direction = Vector3.Transform(Vector3.UnitX, agent.Orientation);
                var yaw = MathF.Atan2(direction.Z, direction.X);
                return Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw + side * MathF.PI / 3f);
            }

            static void CreateRizome(PlantFormation2 plant, int agentID, Quaternion rizomOrientation, ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, float prevResources, float prevProduction, float childEnergy, float childWater)
            {
                var rizomeAgent = new AboveGroundAgent(plant, agentID, OrganTypes.Meristem, rizomOrientation, childEnergy, initialResources: prevResources, initialProduction: prevProduction, rizome: true, length:0.0005f) { Water_g = childWater, WoodFactor = 1f };
                rizomeAgent.RizomeInfo.Depth = agent.RizomeInfo.Depth + 1;
                formation.Birth(rizomeAgent);
                agent.Energy -= childEnergy;
                agent.Water_g -= childWater;

            }

            static void doChaning(AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID, float prevResources, float prevProduction) {
                var lateralPitch = agent.LateralAngle + formation.Plant.Parameters.LateralRoll;
                var meristem = formation.Birth(new(formation.Plant, agentID, OrganTypes.Meristem, TurnUpwards(agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, agent.Orientation)), 0.1f * agent.Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel });
                agent.Energy *= 0.9f;
                agent.Water_g *= 0.9f;

                if (formation.Plant.Parameters.LateralsPerNode > 0)
                    agent.CreateLeaves(agent, formation.Plant, lateralPitch, meristem);
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

            static void bendPetiol(PlantSubFormation<AboveGroundAgent> formation, int agentID)
            {
                float maxAngle = formation.Plant.Parameters.PetioleReorientationMaximumAngle;
                // Parent-Orientierung → Parent-Richtung (im Worldspace)
                var parentRot = formation.GetDirection(formation.GetParent(agentID));
                var parentDir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, parentRot));

                // Child-Orientierung → Child-Richtung (im Worldspace)
                var dir = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, formation.GetDirection(agentID)));

                // Winkel zwischen Parent und Child
                float cos = Vector3.Dot(dir, parentDir);
                cos = Math.Clamp(cos, -1f, 1f);      // numerische Sicherheit
                float angleFromParent = MathF.Acos(cos);

                // nur weiter „runterbiegen", solange der Winkel noch kleiner als maxAngle ist
                if (angleFromParent < maxAngle)
                {
                    // Achse senkrecht zu Parent- und Child-Richtung
                    var axis = Vector3.Cross(parentDir, dir);
                    if (axis.LengthSquared() > 1e-8f)
                    {
                        axis = Vector3.Normalize(axis);

                        float delta = formation.Plant.Parameters.PetioleReorientationStepAngle;
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