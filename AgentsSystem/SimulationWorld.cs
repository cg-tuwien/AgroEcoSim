using System;
using System.Collections.Generic;

namespace AgentsSystem;
public partial class SimulationWorld
{
    //public static readonly Dictionary<string, List<Agent>> Agents;
    //public static readonly Dictionary<string, List<Formation>> Formations;

    internal readonly List<IFormation> Formations = new();
    public uint Timestep { get; private set; }

    public SimulationWorld()
    {
        Formations = new List<IFormation>();
		Timestep = 0;        
    }

    public void Add(IFormation formation)
    {
        Formations.Add(formation);
#if GODOT
        formation.GodotReady();
#endif
    }

    public void AddRange(IEnumerable<IFormation> formations)
    {
        Formations.AddRange(formations);
#if GODOT
        foreach(var item in formations)
            item.GodotReady();
#endif
    }

    public void Run(uint simulationLength)
    {
        for(uint i = 0U; i < simulationLength; ++i, ++Timestep)
        {
            Tick(Timestep);
            DeliverPost();
#if GODOT
            foreach(var item in Formations)
                item.GodotProcess(Timestep);
#endif
        }
    }

    void Tick(uint timestep)
    {
        //Console.WriteLine($"TIMESTEP: {timestep}");
        for(int i = 0; i < Formations.Count; ++i)            
            Formations[i].Tick(this, timestep);            
    }

    public void DeliverPost()
    {
        for(int i = 0; i < Formations.Count; ++i)
            Formations[i].DeliverPost();
    }
}
