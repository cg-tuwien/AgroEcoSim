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
            public void grow(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation)
            {
                var currentSize = new Vector2(agent.Length, agent.Radius);

                switch (agent.Organ)
                {
                    case OrganTypes.FlowerStem:
                        {

                            var energyReserve = Math.Clamp(agent.Energy / agent.EnergyStorageCapacity(), 0f, 1f);
                            var growth =agent.GetRadiusVar()* agent.GrowthTimeVar * formation.Plant.World.HoursPerTick;
                            //assure not to outgrow the parent
                            var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent) && !formation.GetOrgan(agent.Parent).Equals(OrganTypes.Stem)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                            if (currentSize.Y + growth > parentRadius)
                                growth = parentRadius - currentSize.Y;

                            if (agent.Radius < agent.GetRadiusVar())
                                agent.Radius += growth;



                        }
                        break;
                    case OrganTypes.FlowerPetiol: {
                            var sizeLimit = new Vector2(_settings.PetiolLength + agent.LengthVar, _settings.PetiolRadius + agent.RadiusVar);

                            var growth = Math.Min(1f, formation.Plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar;
                            //assure not to outgrow the parent
                            var parentRadius = formation.GetBaseRadius(agent.Parent);
                            if (currentSize.Y + growth.Y > parentRadius)
                                growth.Y = parentRadius - currentSize.Y;

                            agent.Length += agent.Length <= agent.GetLengthVar() ? growth.X : 0f;

                            if (agent.Radius <  agent.GetRadiusVar())
                                agent.Radius += growth.Y;

                        } break;
                    case OrganTypes.FlowerPadel: {

                            var sizeLimit =  new Vector2(agent.GetLengthVar(), agent.GetRadiusVar());

                            
                            if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y)
                            {
                                //HoursPerTick are included in GrowthTimeVar
                                var growth = Math.Min(1f, formation.Plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar ;
                                
                                var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
                                growth = resultingSize - currentSize;
                                agent.Length = agent.GetLengthVar();
                                agent.Radius = agent.GetRadiusVar();

                                                               
                               
                            }
                            float rim = formation.GetBaseRadius(agent.Parent) * 0.85f;
                            var parentTip = formation.GetTipPosition(agent.Parent);
                            //agent.BaseOffset = parentTip + Vector3.Normalize(agent.BaseOffset - parentTip) * rim;
                        } break;
                    case OrganTypes.FlowerBud: {
                            var sizeLimit = Math.Min(1f, formation.Plant.WaterBalance) * new Vector2(agent.LengthVar, agent.RadiusVar);

                            var growth = Math.Min(1f, formation.Plant.WaterBalance) * sizeLimit * agent.GrowthTimeVar * formation.Plant.World.HoursPerTick;
                            //assure not to outgrow the parent
                            var parentRadius = formation.GetBaseRadius(agent.Parent);
                            if (currentSize.Y + growth.Y > parentRadius)
                                growth.Y = parentRadius - currentSize.Y;

                            agent.Length += agent.Length <= agent.GetLengthVar() ? growth.X : 0f;
                            if (agent.Radius <= agent.GetRadiusVar())
                            {
                                agent.Radius += growth.Y;
                            
                            }


                        } break;
                    

                    case OrganTypes.FlowerMeristem: {

                            var growth = new Vector2(agent.GetLengthVar() , agent.GetRadiusVar()) * agent.GrowthTimeVar * formation.Plant.World.HoursPerTick;
                            //assure not to outgrow the parent
                            var parentRadius = (agent.Parent >= 0 && !formation.GetIsRizome(agent.Parent) && !formation.GetOrgan(agent.Parent).Equals(OrganTypes.Stem)) ? formation.GetBaseRadius(agent.Parent) : float.MaxValue;
                            if (currentSize.Y + growth.Y > parentRadius)
                                growth.Y = parentRadius - currentSize.Y;

                            agent.Length += agent.Length <= agent.GetLengthVar() ? growth.X : 0f;

                            if (agent.Radius < agent.GetRadiusVar())
                                agent.Radius += growth.Y;
                        } break;

                }
            }

            // shrinking when max age 
            public void receit(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation)
            {
                System.Console.WriteLine("ddd");
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
                            agent.SenescenceDuration = (ushort)(_settings.PetalSenescenceDurationH / formation.Plant.World.HoursPerTick);
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
                                    agent.WoodFactor = Math.Min(agent.WoodFactor + formation.Plant.World.HoursPerTick / _settings.PetalSenescenceDurationH, 1f);
                                
                            }
                            else
                                agent.WoodFactor = Math.Min(agent.WoodFactor + formation.Plant.World.HoursPerTick / _settings.PetalSenescenceDurationH, 1f);
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
                if ((timestep - agent.BirthTime) * formation.Plant.World.HoursPerTick < formation.Plant.Parameters.FlowerSettings.FlowerMaxAge)
                {
                    
                    grow(ref agent, agentID, formation);
                    chaning(ref agent, agentID, formation, timestep);
                }
                else
                    receit(ref agent, agentID, formation);
                //if (agent.Energy <= 0)
                   // formation.Death(agentID);

            }


            private void chaning(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation, uint timestep)
            {
                if ((agent.Organ.Equals(OrganTypes.FlowerMeristem) || agent.Organ.Equals(OrganTypes.FlowerBaseBud)) && agent.Length >= agent.GetLengthVar())
                {
                    agent.Organ = OrganTypes.FlowerStem;
                    //Console.WriteLine($"ttt{agent.FlowerAgent.debth}< {_settings.flowerDebth}");
                    if (agent.FlowerAgent.flowerBase)
                    {
                        if (agent.FlowerAgent.debth < _settings.flowerBaseDebth)
                        {

                            var or = AboveGroundAgent.TurnUpwards(agent.Orientation);
                            var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = agent.FlowerAgent.debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime, flowerBase = true };
                            var mari = new AboveGroundAgent(formation.Plant, agentID, OrganTypes.FlowerMeristem, or, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = _settings.LateralAngle, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent };
                            var meristem = formation.Birth(mari);
                            agent.Energy *= 0.9f;
                            agent.Water_g *= 0.9f;
                           // Console.WriteLine($"ttt{agent.FlowerAgent.debth}< {_settings.flowerBaseDebth}");

                            float depth01 = _settings.flowerBaseDebth > 0 ? Math.Clamp(agent.FlowerAgent.debth / (float)_settings.flowerBaseDebth, 0f, 1f) : 1f;
                            float p = Math.Clamp(_settings.pBaseFlowerHeight, 0f, 1f);
                            float pLat = _settings.pFlowerBaseDebth;
                            if (p <= 0f) pLat = 0f;
                            else if (p >= 1f) pLat = 1f;
                            else
                            {
                                float activeDepthStart = 1f - p;
                                float u = Math.Clamp((depth01 - activeDepthStart) / p, 0f, 1f);
                                pLat = u * u * (3f - 2f * u);
                            }

                            if (_settings.BaseLaterals > 0 && formation.Plant.RNG.NextFloat(0, 1) < pLat)
                            {
                                for (var lat = 0; lat < _settings.BaseLaterals; lat++)
                                {

                                    var lateralPitch = _settings.BaseLateralAngle + _settings.BaseLateralRoll;

                                    var latOr = AboveGroundAgent.TurnUpwards(agent.Orientation);
                                    var roll = formation.Plant.RNG.NextFloatVar(180f * MathF.PI);
                                    var orientation1 = latOr * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.20f * MathF.PI);

                                    var flowerLatAgent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = 0, FlowerStartTime = agent.FlowerAgent.FlowerStartTime, flowerBase = false };
                                    var latmari = new AboveGroundAgent(formation.Plant, agentID, OrganTypes.FlowerMeristem, agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, orientation1), 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: flowerLatAgent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = flowerLatAgent };
                                    var latmeristem = formation.Birth(latmari);
                                    agent.Energy *= 0.9f;
                                    agent.Water_g *= 0.9f;

                                }
                            }

                            return;
                        }
                        else if (_settings.flowerDebth <= 0)
                        {
                            createFlower(ref agent, agentID, formation, _settings.clusterSize, agent.Orientation, _settings.clusterAngle);
                        }
                        else if (_settings.HasFlowerBaseLeaves) {
                            agent.CreateFlowerBaseLeaves(agent, formation.Plant, 25f, agentID);
                        }

                        //laterals mit tiefen wahrscheinlichkeit

                    }
                    var debth = agent.FlowerAgent.flowerBase ? 0 : agent.FlowerAgent.debth;
                    if (debth < _settings.flowerDebth)
                    {
                        if (_settings.deterministic && _settings.internodeFlower)
                        {
                            Console.WriteLine($"ff {agentID}");
                            if (_settings.internodeFlowerWithStem)
                            {
                                var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };

                                var lateralPitch = _settings.LateralAngle + _settings.LateralRoll;
                                var or = AboveGroundAgent.TurnUpwards(agent.Orientation);
                                // Quaternion.CreateFromAxisAngle(Vector3.UnitZ, _settings.LateralAngle * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (MathF.Tau / _settings.LateralsPerNode) * lat);

                                var flowerStem = new AboveGroundAgent(formation.Plant, agentID, OrganTypes.FlowerStem, agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, or), 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent , Length = 0.005f/2f, Radius= 0.00025f }; 
                                var meristem = formation.Birth(flowerStem);
                                var flowerStem1 = new AboveGroundAgent(formation.Plant, meristem, OrganTypes.FlowerStem, agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, or), 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent, Length = 0.005f / 2f, Radius = 0.00025f };
                                var meristem1 = formation.Birth(flowerStem1);

                                agent.Energy *= 0.9f;
                                agent.Water_g *= 0.9f;
                                createFlower(ref flowerStem1, meristem1, formation, _settings.clusterSize, agent.Orientation, _settings.clusterAngle); // straight
                            } else
                                createFlower(ref agent, agentID, formation, _settings.clusterSize, agent.Orientation, _settings.clusterAngle); // straight
                            Console.WriteLine($"det {agentID}");

                        }
                        else if (!_settings.deterministic)
                        {
                            Console.WriteLine($"up {agentID}");
                            var lateralPitch = _settings.LateralAngle + _settings.LateralRoll;

                            var or = AboveGroundAgent.TurnUpwards(agent.Orientation);
                            var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };

                            var meristem = formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerMeristem, or, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent });
                            agent.Energy *= 0.9f;
                            agent.Water_g *= 0.9f;
                        }
                        if (_settings.LateralsPerNode > 0)
                        {
                            Console.WriteLine($"lat{agentID}");
                            for (var lat = 1; lat <= _settings.LateralsPerNode; lat++)
                            {
                                float t = (_settings.flowerDebth <= 0f) ? 1f : Math.Clamp(debth / _settings.flowerDebth, 0, 1);
                                float depthMultiplier = (1f - _settings.floralDepthFactor) + (_settings.floralDepthFactor * t); ;
                                var or = AboveGroundAgent.TurnUpwards(agent.Orientation);
                                var roll = 180 * MathF.PI;
                                var orientation1 = or * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (MathF.Tau / _settings.LateralsPerNode) * lat * roll) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.350f * MathF.PI);

                                var lateralPitch = 0.3f * MathF.PI;
                                float p = _settings.pFlowerDebth * (agent.FlowerAgent.debth / _settings.flowerDebth);
                                if (_settings.pFlowerDebth == 1) p = 1;
                                if (_settings.pFlowerDebth == 0) p = 0;
                                if (formation.Plant.RNG.NextFloat(0, 1) < p)
                                {
                                    createFlower(ref agent, agentID, formation, _settings.clusterSize, orientation1, _settings.clusterAngle);
                                    continue;
                                }

                                var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };



                                // Quaternion.CreateFromAxisAngle(Vector3.UnitZ, _settings.LateralAngle * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (MathF.Tau / _settings.LateralsPerNode) * lat);


                                var meristem = formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerMeristem, agent.RandomOrientation(formation.Plant, formation.Plant.Parameters, orientation1), 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent });
                                agent.Energy *= 0.9f;
                                agent.Water_g *= 0.9f;
                            }
                        }

                    }
                    else
                    {

                        createFlower(ref agent, agentID, formation, _settings.clusterSize, AboveGroundAgent.TurnUpwards(agent.Orientation), _settings.clusterAngle);

                    }
                }
                if (agent.Organ == OrganTypes.FlowerBud &&  agent.Length >= agent.GetLengthVar() && formation.GetChildren(agentID).Count() <= 0)
                {

                    int petalCount = 4;
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

            private void createFlower(ref AboveGroundAgent agent, int agentID, PlantSubFormation<AboveGroundAgent> formation, int clustersize, Quaternion baseOr, float clusterAngle)
            {
                var lateralPitch = _settings.LateralAngle + _settings.LateralRoll;
                var rng = formation.Plant.RNG;
                for (var c = 1; c <= clustersize; c++)
                {
                    var or = baseOr * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rng.NextPositiveFloat(MathF.Tau)) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -rng.NextPositiveFloat(clusterAngle) * MathF.PI);
                    var floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = agent.FlowerAgent.debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };
                    var meristem = formation.Birth(new(formation.Plant, agentID, OrganTypes.FlowerPetiol, or, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent});

                    for (var s = 2; s <= formation.Plant.Parameters.FlowerSettings.petiolSegments; s++)
                    {
                        floweragent = new Flower() { BirthTime = agent.FlowerAgent.BirthTime, debth = agent.FlowerAgent.debth + 1, FlowerStartTime = agent.FlowerAgent.FlowerStartTime };
                        meristem = formation.Birth(new(formation.Plant, meristem, OrganTypes.FlowerPetiol, or, 0.1f * agent.Energy, initialResources: formation.DailyResourceMax, initialProduction: formation.DailyProductionMax, flowerAgent: floweragent) { Water_g = 0.1f * agent.Water_g, LateralAngle = lateralPitch, DominanceLevel = agent.DominanceLevel, FlowerAgent = floweragent});

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
