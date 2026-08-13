using System.Numerics;

namespace Agro
{
    public partial struct AboveGroundAgent
    {
        public partial struct GeraniumCantabrigiense
        {
            // Tunables specific to × cantabrigiense
            const float kVegGrowthScale = 0.16f;   // slower stem/radius growth
            const float kMeristemAxialScale = 0.8e-3f; // meristem length step
            const float kMeristemRadialScale = 1.6e-5f; // meristem radius step
            const float kRhizomeBirthP = 0.0015f; // rhizome branching chance (per tick, when allowed)
            const int kRhizomeDepthMax = 2;       // short, clumping rhizome
            const float kSpringBudBaseP = 0.45f;   // spring shoot recruitment from rhizome
            const float kWinterLeafDrain = 0.12f;   // mild winter drain
            const float kWinterPetioleDrain = 0.10f;
            const float kWinterLignifyRate = 0.006f;  // small lignification
            const float kLeafThinP = 0.25f;   // chance to drop a leaf in winter (semi-evergreen)

            public static void Tick(ref AboveGroundAgent agent, PlantSubFormation<AboveGroundAgent> formation, int agentID, uint timestep)
            {
                var plant = formation.Plant;
                var species = plant.Parameters;
                var world = plant.World;

                var ageHours = (timestep - agent.BirthTime) * world.HoursPerTick;
                var phase = formation.GetFloweringPhase(species, timestep);

                var lifeSupportPerHour = agent.LifeSupportPerHour();
                var lifeSupportPerTick = agent.LifeSupportPerTick(world);
                if (!agent.isRizome)
                    agent.Energy -= lifeSupportPerTick;

                var children = formation.GetChildren(agentID);
                var enoughEnergyState = agent.EnoughEnergy(lifeSupportPerHour);
                var wasMeristem = false;

                // Photosynthesis (unchanged)
                if (agent.Organ == OrganTypes.Leaf && agent.Water_g > 0f)
                {
                    var approxLight = world.Irradiance.GetIrradiance(formation, agentID);
                    if (approxLight > 0.01f)
                    {
                        var surface = agent.Length * agent.Radius * 2f;
                        var possibleByLight = surface * approxLight;
                        var photosynthesized = Math.Min(possibleByLight * mPhotoEfficiency, agent.Water_g);

                        agent.Water_g -= photosynthesized;
                        agent.Energy += photosynthesized;
                        agent.CurrentDayEnvResources += approxLight * surface;
                        agent.CurrentDayEnvResourcesInv += approxLight;
                        agent.CurrentDayProductionInv += photosynthesized / surface;
                    }
                }

                // Aging bits kept minimal; stems termination: avoid on rhizome
                switch (agent.Organ)
                {
                    case OrganTypes.Petiole:
                        {
                            if (ageHours > 36 && formation.GetOrgan(agent.Parent) != OrganTypes.Meristem)
                            {
                                var p = ageHours / 4032f;
                                if (plant.RNG.NextFloatAccum(p * p, world.HoursPerTick))
                                    agent.MakeBud(formation, children);
                            }
                        }
                        break;

                    case OrganTypes.Stem:
                        {
                            if (!agent.isRizome && agent.DominanceLevel > 1 && formation.GetDominance(agent.Parent) < agent.DominanceLevel)
                            {
                                var h = 5f * formation.GetBaseCenter(agentID).Y / formation.Height;
                                var e = 4f * agent.PreviousDayEnvResources / formation.DailyEfficiencyMax;
                                var q = 1f + h * h + 20f * agent.Radius + e * e + agent.WoodFactor;
                                var p = 0.0025f / (q * q); // slightly lower than sanguineum
                                if (plant.RNG.NextFloatAccum(p, world.HoursPerTick))
                                    agent.Energy = 0f;
                            }
                        }
                        break;

                    case OrganTypes.Meristem: wasMeristem = true; break;


                }

                // --- Spring recruitment from rhizome buds (once each spring) ---
                if (!agent.isRizome && formation.GetIsRizome(agent.Parent) && agent.Organ == OrganTypes.Bud)
                {
                    if (phase == SeasonalPhase.PreFlower && agent.trySpawn)
                    {
                        // scale a bit by reserve fullness (clamped 0..1)
                        float reserve = Math.Clamp(agent.Energy / Math.Max(1e-6f, agent.EnergyStorageCapacity()), 0f, 1f);
                        float p = kSpringBudBaseP * (0.7f + 0.6f * reserve); // ~0.315..0.72

                        if (plant.RNG.NextFloat(0, 1) < p)
                        {
                            // vertical-ish new stem (scape/leafy petioles will form from nodes)
                            var yaw = plant.RNG.NextFloat(-MathF.PI, MathF.PI);
                            var baseQ = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) *
                                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.45f * MathF.PI); // gentle cant

                            agent.Organ = OrganTypes.Meristem;
                            agent.Orientation = baseQ;
                            agent.DominanceLevel = 1;
                            agent.Radius = 0.0035f;
                            agent.LengthVar = species.NodeDistance + plant.RNG.NextFloatVar(species.NodeDistanceVar);

                            agent.Energy = Math.Max(agent.Energy, 0.25f * agent.EnergyStorageCapacity());
                            if (species.LateralsPerNode > 0)
                                agent.CreateLeaves(agent, plant, agent.LateralAngle + species.LateralRoll, agentID);
                        }
                        agent.trySpawn = false; // decide only once per spring per bud
                    }
                    if (phase == SeasonalPhase.ResetPending)
                        agent.trySpawn = true; // re-arm for next spring
                }

                // --- Growth ---
                if (agent.Energy > enoughEnergyState && !agent.isRizome)
                {
                    if (agent.Organ != OrganTypes.Bud)
                    {
                        var currentSize = new Vector2(agent.Length, agent.Radius);
                        var dominanceFactor = agent.DominanceLevel < species.DominanceFactors.Length ? species.DominanceFactors[agent.DominanceLevel] : species.DominanceFactors[^1];

                        switch (agent.Organ)
                        {
                            case OrganTypes.Leaf:
                                {
                                    var sizeLimit = new Vector2(species.LeafLength + agent.LengthVar, species.LeafRadius + agent.RadiusVar);
                                    if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y && phase != SeasonalPhase.ResetPending)
                                    {
                                        var growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * (agent.PreviousDayProductionInvariant / formation.DailyProductionMax);
                                        var target = Vector2.Min(currentSize + growth, sizeLimit);
                                        growth = target - currentSize;
                                        agent.Length += growth.X;
                                        agent.Radius += growth.Y;
                                    }
                                    else if (phase == SeasonalPhase.ResetPending)
                                    {
                                        // Semi-evergreen: mild drain + occasional thinning
                                        agent.Energy -= kWinterLeafDrain * agent.LifeSupportPerHour() * world.HoursPerTick;
                                        agent.WoodFactor = Math.Min(agent.WoodFactor + kWinterLignifyRate, 1f);

                                        if (plant.RNG.NextFloat(0, 1) < kLeafThinP)
                                        {
                                            //formation.Death(agentID); // thin some leaves
                                            if (agent.Parent >= 0 && formation.GetOrgan(agent.Parent) == OrganTypes.Petiole)
                                                //formation.Death(agent.Parent); // drop petiole too
                                            return;
                                        }
                                    }
                                }
                                break;

                            case OrganTypes.Petiole:
                                {
                                    var sizeLimit = new Vector2(species.PetioleLength + agent.LengthVar, species.PetioleRadius + agent.RadiusVar);
                                    if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y && phase != SeasonalPhase.ResetPending)
                                    {
                                        var growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * (agent.PreviousDayProductionInvariant / formation.DailyProductionMax);
                                        var target = Vector2.Min(currentSize + growth, sizeLimit);
                                        growth = target - currentSize;

                                        var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                        if (currentSize.Y + growth.Y > parentRadius)
                                            growth.Y = parentRadius - currentSize.Y;

                                        agent.Length += growth.X;
                                        agent.Radius += growth.Y;
                                    }
                                    else if (phase == SeasonalPhase.ResetPending)
                                    {
                                        agent.Energy -= kWinterPetioleDrain * agent.LifeSupportPerHour() * world.HoursPerTick;
                                        var parentWood = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetWoodRatio(agent.Parent) : agent.WoodFactor;
                                        agent.WoodFactor = Math.Min((agent.WoodFactor <= parentWood ? agent.WoodFactor : parentWood) + kWinterLignifyRate, 1f);
                                    }
                                }
                                break;

                            case OrganTypes.Meristem:
                                {
                                    var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                                    var waterReserve = Math.Min(1f, plant.WaterBalance);
                                    var growth = new Vector2(kMeristemAxialScale, kMeristemRadialScale) *
                                                 (dominanceFactor * energyReserve * waterReserve * world.HoursPerTick *
                                                  (agent.PreviousDayProductionInvariant / formation.DailyProductionMax));

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
                                        var growth = 2e-5f * dominanceFactor * energyReserve * Math.Min(plant.WaterBalance, energyReserve) * world.HoursPerTick * kVegGrowthScale;

                                        var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                        if (currentSize.Y + growth > parentRadius)
                                            growth = parentRadius - currentSize.Y;

                                        if (agent.Radius < 0.0055f)
                                            agent.Radius += growth;
                                    }
                                    else
                                    {
                                        // mild winter drain only (no hard reset)
                                        agent.Energy -= 0.10f * agent.LifeSupportPerHour() * world.HoursPerTick;
                                    }
                                }
                                break;



                            case OrganTypes.FlowerStem:
                                {
                                    // Thin scape; modest elongation then stop
                                    if (ageHours < 5f * 24f)
                                    {
                                        var sizeLimit = new Vector2(0.20f + agent.LengthVar, 0.0045f + agent.RadiusVar); // ~20 cm scape, thin radius
                                        var growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * (agent.PreviousDayProductionInvariant / formation.DailyProductionMax);
                                        var target = Vector2.Min(currentSize + growth, sizeLimit);
                                        growth = target - currentSize;

                                        var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                                        if (currentSize.Y + growth.Y > parentRadius)
                                            growth.Y = parentRadius - currentSize.Y;

                                        agent.Length += growth.X;
                                        agent.Radius += growth.Y;
                                    }
                                }
                                break;
                        }

                        // wood factor update
                        if (agent.Organ == OrganTypes.Stem && agent.WoodFactor < 1f)
                        {
                            var pw = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent)) ? formation.GetWoodRatio(agent.Parent) : agent.WoodFactor;
                            agent.WoodFactor = Math.Min((agent.WoodFactor <= pw ? agent.WoodFactor : pw) + agent.GrowthTimeVar, 1f);
                        }

                        // chaining: meristem -> new segment
                        if (agent.Organ == OrganTypes.Meristem && agent.Length > agent.LengthVar)
                        {
                            agent.Organ = OrganTypes.Stem;
                            agent.GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
                            wasMeristem = true;

                            float prevRes = (timestep - agent.BirthTime > world.HoursPerTick) ? agent.PreviousDayProductionInvariant : formation.DailyResourceMax;
                            float prevProd = (timestep - agent.BirthTime > world.HoursPerTick) ? agent.PreviousDayProductionInvariant : formation.DailyProductionMax;

                            switch (phase)
                            {
                                case SeasonalPhase.PreFlower:
                                    {
                                        // Rare lateral vegetative twig (× cantabrigiense is tight/clumping)
                                        if (plant.RNG.NextFloat(0, 1) < 0.05f)
                                        {
                                            var ou = TurnUpwards(agent.Orientation);
                                            var q = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, plant.RNG.NextFloat(-0.35f, 0.35f));
                                            var mer = formation.Birth(new(plant, agentID, OrganTypes.Meristem, agent.RandomOrientation(plant, species, q),
                                                                         0.08f * agent.Energy, initialResources: prevRes, initialProduction: prevProd)
                                            { Water_g = 0.08f * agent.Water_g, DominanceLevel = agent.DominanceLevel });
                                            agent.Energy *= 0.92f;
                                            agent.Water_g *= 0.92f;
                                            if (species.LateralsPerNode > 0) agent.CreateLeaves(agent, plant, agent.LateralAngle + species.LateralRoll, mer);
                                        }
                                    }
                                    break;

                                case SeasonalPhase.Flowering:
                                    {
                                        /*
                                        // One scape (flower stem) per mature rosette, once per season
                                        if (agent.trySpawn) // reuse flag for "flower this season?"
                                        {
                                            var ou = TurnUpwards(agent.Orientation);
                                            var qScape = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, plant.RNG.NextFloat(-0.2f, 0.2f))
                                                            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.18f * MathF.PI); // gentle tilt up
                                            var scape = formation.Birth(new(plant, agentID, OrganTypes.FlowerStem, agent.RandomOrientation(plant, species, qScape),
                                                                           0.10f * agent.Energy, initialResources: prevRes, initialProduction: prevProd)
                                            { Water_g = 0.10f * agent.Water_g, DominanceLevel = agent.DominanceLevel });
                                            // flower at scape tip
                                            formation.Birth(new(plant, scape, OrganTypes.Flower, agent.RandomOrientation(plant, species, qScape),
                                                                0.06f * agent.Energy, initialResources: prevRes, initialProduction: prevProd)
                                            { Water_g = 0.06f * agent.Water_g, DominanceLevel = agent.DominanceLevel });

                                            agent.Energy *= 0.84f;
                                            agent.Water_g *= 0.84f;
                                            agent.trySpawn = false; // only once this season
                                        }*/
                                    }
                                    break;

                                case SeasonalPhase.PostFlower:
                                default: break;
                            }
                        }
                        else
                        {
                            // housekeeping similar to your code
                            if (agent.Organ == OrganTypes.Petiole &&
                                agent.ParentRadiusAtBirth + species.PetioleCoverThreshold < formation.GetBaseRadius(agent.Parent))
                                agent.MakeBud(formation, children);

                            if (agent.Organ == OrganTypes.Petiole && ageHours > 48 && formation.GetOrgan(agent.Parent) != OrganTypes.Meristem && children != null)
                            {
                                var production = 0f;
                                for (int c = 0; c < children.Count; ++c)
                                    production += formation.GetDailyProductionInv(children[c]);

                                production /= plant.EnergyProductionMax;
                                if (production < 0.5f)
                                {
                                    var p = 1f - 2f * production;
                                    p *= p;
                                    if (plant.RNG.NextFloatAccum(p, world.HoursPerTick))
                                    {
                                        //formation.Death(agentID);
                                    }
                                }
                            }
                        }
                    }
                }
                else if (agent.Energy <= 0f && !agent.isRizome)
                {
                    switch (agent.Organ)
                    {
                        case OrganTypes.Petiole: agent.MakeBud(formation, children); break;
                        case OrganTypes.Leaf:
                            formation.Death(agentID);
                            if (agent.Parent >= 0) formation.Death(agent.Parent);
                            break;

                        case OrganTypes.Stem:
                            if (formation.GetIsRizome(agent.Parent)) agent.MakeBud(formation, children);
                            else formation.Death(agentID);
                            break;
                        default:
                            if (formation.GetIsRizome(agent.Parent) && agent.Organ != OrganTypes.Bud) agent.MakeBud(formation, children);
                            break;
                    }
                    return;
                }
                else if (agent.isRizome)
                {
                    // short, clumping rhizome: two optional arms to build a small mat
                    if (phase == SeasonalPhase.ResetPending && agent.rizomeInfo.test3 && agent.rizomeInfo.rizomeDepth < kRhizomeDepthMax)
                    {
                        if (plant.RNG.NextFloat(0, 1) < kRhizomeBirthP && agent.rizomeInfo.test)
                        {
                            var q = agent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, +MathF.PI / 6f);
                            CreateRhizome(plant, agentID, q, agent, formation);
                            agent.rizomeInfo.test = false;
                        }
                        if (plant.RNG.NextFloat(0, 1) < kRhizomeBirthP && agent.rizomeInfo.test2)
                        {
                            var q = agent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 6f);
                            CreateRhizome(plant, agentID, q, agent, formation);
                            agent.rizomeInfo.test2 = false;
                        }
                    }
                    else if (phase == SeasonalPhase.PreFlower && !agent.rizomeInfo.test3)
                        agent.rizomeInfo.test3 = true;
                }

                // Auxins
                agent.Auxins = wasMeristem || agent.Organ == OrganTypes.Meristem ? species.AuxinsProduction : 0;
            }

            static void CreateRhizome(PlantFormation2 plant, int agentID, Quaternion q, AboveGroundAgent parent, PlantSubFormation<AboveGroundAgent> formation)
            {
                var rh = new AboveGroundAgent(plant, agentID, OrganTypes.Stem, q, 0, length: 0.02f, radius: 0.009f);
                rh.isRizome = true;
                rh.rizomeInfo.rizomeDepth = parent.rizomeInfo.rizomeDepth + 1;
                var idx = formation.Birth(rh);

                var bud = new AboveGroundAgent(plant, idx, OrganTypes.Bud, q, 0, initialResources: 1f, initialProduction: 1f);
                bud.trySpawn = true; // allow recruiting next spring
                formation.Birth(bud);
            }
        }
    }
}
