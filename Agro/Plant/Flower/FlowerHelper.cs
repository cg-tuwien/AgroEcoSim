using AgentsSystem;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Agro
{
    public partial struct AboveGroundAgent
    {
        public partial struct FlowerHelper
        {
            private FlowerSettings _settings;
            public FlowerHelper(FlowerSettings flowerSettings)
            {
                _settings = flowerSettings;
            }

            private const float DegreesToRadians = MathF.PI / 180f;

            private static Quaternion CreateLateralOrientation(
                Quaternion parentOrientation,
                int lateralIndex,
                int lateralCount,
                float inclinationDegrees,
                float azimuthOffsetDegrees)
            {
                var azimuth = azimuthOffsetDegrees * DegreesToRadians +
                    MathF.Tau * lateralIndex / lateralCount;
                var inclination = inclinationDegrees * DegreesToRadians;
                return parentOrientation *
                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, azimuth) *
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -inclination);
            }
            public void grow(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation)
            {
                var currentSize = new Vector2(agent.Length, agent.Radius);

                switch (agent.Organ)
                {
                    case OrganTypes.FlowerStem:
                        {
                            // Converted meristems have already reached these
                            // targets; directly-created stems have not.  Grow both
                            // axes independently so a radius restriction cannot
                            // strand a stem at its short initial length.
                            var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent) && !formation.GetOrgan(agent.Parent).Equals(OrganTypes.Stem))
                                ? formation.GetBaseRadius(agent.Parent)
                                : float.MaxValue;
                            var sizeLimit = new Vector2(
                                MathF.Max(currentSize.X, agent.GetLengthVar()),
                                MathF.Max(currentSize.Y, MathF.Min(agent.GetRadiusVar(), parentRadius)));
                            var growth = sizeLimit * agent.GrowthTimeVar;
                            var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                            growth = Vector2.Max(resultingSize - currentSize, Vector2.Zero);
                            agent.Length += growth.X;
                            agent.Radius += growth.Y;

                        }
                        break;
                    case OrganTypes.FlowerPedicel: {
                            var sizeLimit = new Vector2(agent.GetLengthVar(), agent.GetRadiusVar());

                            var growth = Math.Min(1f, formation.Plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar;
                            var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                            growth = resultingSize - currentSize;

                            var parentRadius = formation.GetBaseRadius(agent.Parent);
                            growth = LimitPetioleGrowthToSupport(currentSize, sizeLimit, growth, parentRadius);
                            agent.Length += growth.X;
                            agent.Radius += growth.Y;

                        } break;
                    case OrganTypes.FlowerPadel: {

                            var sizeLimit =  new Vector2(agent.GetLengthVar(), agent.GetRadiusVar());


                            if (currentSize.X < sizeLimit.X || currentSize.Y < sizeLimit.Y)
                            {
                                //HoursPerTick are included in GrowthTimeVar
                                var growth = Math.Min(1f, formation.Plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar ;

                                var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                                growth = resultingSize - currentSize;
                                agent.Length += growth.X;
                                agent.Radius += growth.Y;

                            }
                            float rim = formation.GetBaseRadius(agent.Parent) * 0.85f;
                            var parentTip = formation.GetTipPosition(agent.Parent);
                            //agent.BaseOffset = parentTip + Vector3.Normalize(agent.BaseOffset - parentTip) * rim;
                        } break;
                    case OrganTypes.FlowerBud: {
                            var sizeLimit = new Vector2(agent.GetLengthVar(), agent.GetRadiusVar());
                            var growth = Math.Min(1f, formation.Plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar;
                            var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                            growth = Vector2.Max(resultingSize - currentSize, Vector2.Zero);
                            agent.Length += growth.X;
                            agent.Radius += growth.Y;


                        } break;


                    case OrganTypes.FlowerMeristem: {

                            // A flower meristem may keep the same diameter as its
                            // supporting stem, but must still attain its configured
                            // length. Scaling its length by the parent-radius ratio
                            // made a slightly wider child stall forever, preventing
                            // all downstream buds and petals from being created.
                            var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent) && !formation.GetOrgan(agent.Parent).Equals(OrganTypes.Stem))
                                ? formation.GetBaseRadius(agent.Parent)
                                : float.MaxValue;
                            var sizeLimit = new Vector2(agent.GetLengthVar(), MathF.Min(agent.GetRadiusVar(), parentRadius));
                            var growth = sizeLimit * agent.GrowthTimeVar;
                            var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                            growth = resultingSize - currentSize;
                            agent.Length += growth.X;
                            agent.Radius += growth.Y;
                        } break;

                }
            }

            // shrinking when max age
            public void receit(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation)
            {
                var childrenInReceit = true;
                if (!agent.Organ.Equals(OrganTypes.FlowerBud) && agent.flowerSupport)
                {

                    foreach (var child in formation.GetChildren(agentID))
                    {
                        if (formation.GetHasFlowerSupport(child))
                        {
                            childrenInReceit = false;
                            break;
                        }
                    }
                }
                if ((agent.flowerSupport && childrenInReceit))
                {
                    if (!agent.Organ.Equals(OrganTypes.FlowerBud) && !agent.Organ.Equals(OrganTypes.FlowerPadel))
                        agent.flowerSupport = false;
                    else if (agent.Organ.Equals(OrganTypes.FlowerPadel) && !formation.GetHasFlowerSupport(agent.Parent))
                        agent.flowerSupport = false;
                    else if (agent.Organ.Equals(OrganTypes.FlowerBud) && formation.Plant.RNG.NextFloat(0, 1) < 0.05f)
                        agent.flowerSupport = false;

                }
                if (agent.flowerSupport)
                {
                    var suppport = false;
                    foreach (var child in formation.GetChildren(agentID))
                    {
                        if (formation.GetOrgan(child).Equals(OrganTypes.Petiole))
                            suppport = false;
                        if (!formation.GetOrgan(child).Equals(OrganTypes.Petiole))
                        {
                            suppport = true;
                            break;
                        }
                    }
                    agent.flowerSupport = suppport;

                }
                if (!agent.flowerSupport)
                {
                    agent.CurrentDayEnvResources = formation.DailyResourceMax;

                    if (agent.Organ == OrganTypes.FlowerPadel)
                    {
                        // Start senescence once — colour fade is handled by FlowerPhenology.WriteColor
                        if (agent.SenescenceStart == 0)
                        {
                            agent.SenescenceStart = formation.Plant.World.Timestep;
                            // Convert hours → ticks correctly
                            agent.SenescenceDuration = (ushort)(_settings.PetalSenescenceDurationHours / formation.Plant.World.HoursPerTick);
                        }
                        // Die only once the senescence curve has completed
                        if (agent.Senescence(formation.Plant.World.Timestep) >= 1f)
                            agent.Energy = 0f;
                    }
                    else
                    {
                        // Non-petal organs: advance WoodFactor monotonically toward 1.
                        // Use Math.Max to prevent WoodFactor from resetting when child
                        // senescence returns 0 (e.g. child not yet senescing or already dead).
                        if (agent.WoodFactor < 1f)
                        {
                            var children = formation.GetChildren(agentID);
                            if (children.Count() > 0)
                            {
                                if (formation.GetOrgan(children.First()).Equals(OrganTypes.FlowerPadel))
                                {
                                    float childSenescence = formation.GetSenescence(children.First(), formation.Plant.World.Timestep);
                                    agent.WoodFactor = Math.Max(agent.WoodFactor, childSenescence);
                                } else
                                    agent.WoodFactor = Math.Min(agent.WoodFactor + formation.Plant.World.HoursPerTick / _settings.PetalSenescenceDurationHours, 1f);

                            }
                            else
                                agent.WoodFactor = Math.Min(agent.WoodFactor + formation.Plant.World.HoursPerTick / _settings.PetalSenescenceDurationHours, 1f);
                        }
                        else
                            agent.Energy = 0f;
                    }
                }
                if (agent.Energy <= 0)
                    formation.Death(agentID);
            }

            public void handleAgent(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation, uint timestep)
            {

                //Console.WriteLine($"{timestep} {agent.BirthTime} {formation.Plant.World.HoursPerTick} {(timestep - agent.BirthTime) * formation.Plant.World.HoursPerTick} {30 * 24}");
                if ((timestep - agent.BirthTime) * formation.Plant.World.HoursPerTick < formation.Plant.Parameters.FlowerSettings.FlowerMaximumAgeHours)
                {

                    grow(ref agent, agentID, formation);
                    chaning(ref agent, agentID, formation, timestep);
                }
                else
                    receit(ref agent, agentID, formation);
                //if (agent.Energy <= 0)
                   // formation.Death(agentID);

            }

            private void CreateFlowerBaseLaterals(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation)
            {
                if (_settings.FlowerBaseLateralCount == 0 ||
                    _settings.FlowerBaseLateralProbability == 0f ||
                    _settings.FlowerBaseLateralActiveTopFraction == 0f)
                {
                    return;
                }

                var depth01 = _settings.FlowerBaseSegmentCount == 1
                    ? 1f
                    : agent.FlowerAgent.debth / (float)(_settings.FlowerBaseSegmentCount - 1);
                var activeTopFraction = _settings.FlowerBaseLateralActiveTopFraction;
                var activeStart = 1f - activeTopFraction;
                var activeFraction = Math.Clamp((depth01 - activeStart) / activeTopFraction, 0f, 1f);
                var lateralProbability = _settings.FlowerBaseLateralProbability *
                    activeFraction * activeFraction * (3f - 2f * activeFraction);
                if (formation.Plant.RNG.NextFloat() >= lateralProbability)
                    return;

                var lateralPitch = _settings.FlowerBaseLateralInclinationDegrees * DegreesToRadians;
                for (var lateral = 0; lateral < _settings.FlowerBaseLateralCount; lateral++)
                {
                    var orientation = CreateLateralOrientation(
                        agent.Orientation,
                        lateral,
                        _settings.FlowerBaseLateralCount,
                        _settings.FlowerBaseLateralInclinationDegrees,
                        _settings.FlowerBaseLateralAzimuthOffsetDegrees);
                    var flowerAgent = new Flower
                    {
                        BirthTime = agent.FlowerAgent.BirthTime,
                        debth = 0,
                        FlowerStartTime = agent.FlowerAgent.FlowerStartTime,
                        flowerBase = false
                    };
                    formation.Birth(new AboveGroundAgent(
                        formation.Plant,
                        agentID,
                        OrganTypes.FlowerMeristem,
                        agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, orientation),
                        0.1f * agent.Energy,
                        initialResources: formation.DailyResourceMax,
                        initialProduction: formation.DailyProductionMax,
                        flowerAgent: flowerAgent)
                    {
                        Water_g = 0.1f * agent.Water_g,
                        LateralAngle = lateralPitch,
                        DominanceLevel = agent.DominanceLevel,
                        FlowerAgent = flowerAgent
                    });
                    agent.Energy *= 0.9f;
                    agent.Water_g *= 0.9f;
                }
            }

            private void chaning(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation, uint timestep)
            {
                if ((agent.Organ.Equals(OrganTypes.FlowerMeristem) || agent.Organ.Equals(OrganTypes.FlowerBaseBud)) && agent.Length >= agent.GetLengthVar())
                {
                    agent.Organ = OrganTypes.FlowerStem;
                    if (agent.FlowerAgent.flowerBase)
                    {
                        CreateFlowerBaseLaterals(ref agent, agentID, formation);

                        // The current meristem is already one basal segment.  A
                        // configured depth of one therefore means one culm, not a
                        // short starter plus an additional basal segment.
                        if (agent.FlowerAgent.debth + 1 < _settings.FlowerBaseSegmentCount)
                        {

                            var or = PlantFormation2.AdjustUpBase(agent.Orientation, up: true);
                            var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = agent.FlowerAgent.debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime, flowerBase = true };
                            var mari = new AboveGroundAgent(formation.Plant, agentID, OrganTypes.FlowerMeristem, or, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = _settings.FlowerLateralInclinationDegrees * DegreesToRadians, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent };
                            var meristem = formation.Birth(mari);
                            agent.Energy *= 0.9f;
                            agent.Water_g *= 0.9f;
                            return;
                        }
                        else if (_settings.FlowerBranchMaximumDepth <= 0)
                        {
                            createFlower(ref agent, agentID, formation, _settings.FlowersPerCluster, agent.Orientation, _settings.FlowerClusterMaximumSpreadDegrees);
                        }
                        else if (_settings.CreateLeavesOnFlowerBase) {
                            agent.CreateFlowerBaseLeaves(agent, formation.Plant, 25f, agentID);
                        }

                        //laterals mit tiefen wahrscheinlichkeit

                    }
                    var debth = agent.FlowerAgent.flowerBase ? 0 : agent.FlowerAgent.debth;
                    if (debth < _settings.FlowerBranchMaximumDepth)
                    {
                        if (_settings.UseDeterministicBranching && _settings.CreateFlowersAtInternodes)
                        {
                            Debug.WriteLine($"ff {agentID}");
                            if (_settings.InternodeFlowersHaveStems)
                            {
                                var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };

                                var lateralPitch = _settings.FlowerLateralInclinationDegrees * DegreesToRadians;
                                var or = CreateLateralOrientation(
                                    agent.Orientation,
                                    0,
                                    1,
                                    _settings.FlowerLateralInclinationDegrees,
                                    _settings.FlowerLateralAzimuthOffsetDegrees);

                                var flowerStem = new AboveGroundAgent(formation.Plant, agentID, OrganTypes.FlowerStem, agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, or), 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent , Length = 0.005f/2f, Radius= 0.00025f };
                                var meristem = formation.Birth(flowerStem);
                                var flowerStem1 = new AboveGroundAgent(formation.Plant, meristem, OrganTypes.FlowerStem, agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, or), 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent, Length = 0.005f / 2f, Radius = 0.00025f };
                                var meristem1 = formation.Birth(flowerStem1);

                                agent.Energy *= 0.9f;
                                agent.Water_g *= 0.9f;
                                createFlower(ref flowerStem1, meristem1, formation, _settings.FlowersPerCluster, agent.Orientation, _settings.FlowerClusterMaximumSpreadDegrees); // straight
                            } else
                                createFlower(ref agent, agentID, formation, _settings.FlowersPerCluster, agent.Orientation, _settings.FlowerClusterMaximumSpreadDegrees); // straight
                            Debug.WriteLine($"det {agentID}");

                        }
                        else if (_settings.UseDeterministicBranching)
                        {
							var orientation = AboveGroundAgent.TurnUpwards(agent.Orientation);
							if (debth + 1 >= _settings.FlowerBranchMaximumDepth)
							{
								createFlower(ref agent, agentID, formation, _settings.FlowersPerCluster, orientation, _settings.FlowerClusterMaximumSpreadDegrees);
							}
							else
							{
								var flowerAgent = new Flower()
								{
									BirthTime = agent.FlowerAgent.BirthTime,
									debth = debth + 1,
									FlowerStartTime = agent.FlowerAgent.FlowerStartTime
								};
								formation.Birth(new AboveGroundAgent(formation.Plant, agentID, OrganTypes.FlowerMeristem, orientation, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: flowerAgent)
								{
									Water_g = 0.1f * agent.Water_g,
									LateralAngle = _settings.FlowerLateralInclinationDegrees * DegreesToRadians,
									DominanceLevel = agent.DominanceLevel,
									FlowerAgent = flowerAgent
								});
								agent.Energy *= 0.9f;
								agent.Water_g *= 0.9f;
							}
						}
                        else if (!_settings.UseDeterministicBranching)
                        {
                            Debug.WriteLine($"up {agentID}");
                            var lateralPitch = _settings.FlowerLateralInclinationDegrees * DegreesToRadians;

                            var or = AboveGroundAgent.TurnUpwards(agent.Orientation);
                            var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };

                            var meristem = formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerMeristem, or, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent });
                            agent.Energy *= 0.9f;
                            agent.Water_g *= 0.9f;
                        }
                        if (_settings.FlowerBranchLateralsPerNode > 0)
                        {
                            Debug.WriteLine($"lat {agentID}");
                            for (var lat = 0; lat < _settings.FlowerBranchLateralsPerNode; lat++)
                            {
                                var orientation1 = CreateLateralOrientation(
                                    agent.Orientation,
                                    lat,
                                    _settings.FlowerBranchLateralsPerNode,
                                    _settings.FlowerLateralInclinationDegrees,
                                    _settings.FlowerLateralAzimuthOffsetDegrees);
                                var lateralPitch = _settings.FlowerLateralInclinationDegrees * DegreesToRadians;
                                var terminalProbability = _settings.TerminalFlowerProbabilityAtMaximumBranchDepth *
                                    Math.Clamp((debth + 1f) / _settings.FlowerBranchMaximumDepth, 0f, 1f);
                                if (formation.Plant.RNG.NextFloat() < terminalProbability)
                                {
                                    createFlower(ref agent, agentID, formation, _settings.FlowersPerCluster, orientation1, _settings.FlowerClusterMaximumSpreadDegrees);
                                    continue;
                                }

                                var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };
                                var meristem = formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerMeristem, agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, orientation1), 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent });
                                agent.Energy *= 0.9f;
                                agent.Water_g *= 0.9f;
                            }
                        }

                    }
                    else
                    {

                        createFlower(ref agent, agentID, formation, _settings.FlowersPerCluster, AboveGroundAgent.TurnUpwards(agent.Orientation), _settings.FlowerClusterMaximumSpreadDegrees);

                    }
                }
                if (agent.Organ == OrganTypes.FlowerBud &&  agent.Length >= agent.GetLengthVar() && formation.GetChildren(agentID).Count() <= 0)
                {

                    int petalCount = _settings.PetalCount;
                    float angleStep = MathF.Tau / petalCount;

                    float basePetalPitch = 0.95f;      // how much petals tilt outward/down
                    float pitchVarRange = 0.05f;      // random pitch jitter
                    float rollVarRange = 0.05f;      // random roll jitter


                    for (int i = 0; i < petalCount; i++)
                    {

                        // Ring angle around bud
                        float a = i * angleStep;

                        // Like your leaf variance
                        float roll = formation.Plant.RNG.NextFloatVar(rollVarRange);
                        float pitch = formation.Plant.RNG.NextFloatVar(pitchVarRange);
                        var or = agent.Orientation;
                        Quaternion o = or * Quaternion.CreateFromAxisAngle(Vector3.UnitX, a) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (basePetalPitch + pitch)) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll);
                        float rim = agent.Radius * 0.85f;


                        Vector3 offset = Vector3.Transform(new Vector3(0f, MathF.Cos(a) * rim, MathF.Sin(a) * rim), or);
                        var flower = formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerPadel, o, agent.Energy * (0.1f / petalCount), initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax) { DominanceLevel = agent.DominanceLevel, BaseOffset = offset });

                        agent.Energy *= 0.9f;
                    }
                }
            }

            private void createFlower(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation, int clustersize, Quaternion baseOr, float clusterMaximumSpreadDegrees)
            {
                var lateralPitch = _settings.FlowerLateralInclinationDegrees * DegreesToRadians;
                var clusterMaximumSpreadRadians = clusterMaximumSpreadDegrees * DegreesToRadians;
                var rng = formation.Plant.RNG;
                for (var c = 1; c <= clustersize; c++)
                {
                    var or = baseOr * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rng.NextPositiveFloat(MathF.Tau)) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -rng.NextPositiveFloat(clusterMaximumSpreadRadians));
                    var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = agent.FlowerAgent.debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };
                    var meristem = formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerPedicel, or, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent});

                    for (var s = 2; s <= formation.Plant.Parameters.FlowerSettings.FlowerPedicelSegmentCount; s++)
                    {
                        floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = agent.FlowerAgent.debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };
                        meristem = formation.Birth(new(formation.Plant, meristem, OrganTypes.FlowerPedicel, or, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent});

                    }


                    agent.Energy *= 0.9f;
                    agent.Water_g *= 0.9f;


                    var floweragentbud = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = agent.FlowerAgent.debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };
                    var bud = new AboveGroundAgent(formation.Plant, meristem, OrganTypes.FlowerBud, or, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragentbud) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent};
                    var budId = formation.Birth(bud);
                    agent.Energy *= 0.9f;
                    agent.Water_g *= 0.9f;

                }

            }
        }

        public struct Flower
        {
            public bool flowerBase { get; set; } = false;

            public Flower()
            {
            }

            public int debth { get; set; }
            public uint BirthTime { get; set; }

            public uint FlowerStartTime { get; set; }
        }
    }
}
