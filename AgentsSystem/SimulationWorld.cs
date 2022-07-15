using System;
using System.Collections.Generic;

namespace AgentsSystem;
public partial class SimulationWorld
{
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
            Census();
            Tick(Timestep);
            DeliverPost();
#if GODOT
            foreach(var item in Formations)
                item.GodotProcess(Timestep);
#endif
        }
    }

    void Census()
    {
        for(int i = 0; i < Formations.Count; ++i)
            Formations[i].Census();
    }

    void Tick(uint timestep)
    {
        //Console.WriteLine($"TIMESTEP: {timestep}");
        for(int i = 0; i < Formations.Count; ++i)
            Formations[i].Tick(this, timestep);
    }

    public void DeliverPost()
    {
        var anyDelivered = true;
        while(anyDelivered)
        {
            anyDelivered = false;
            for(int i = 0; i < Formations.Count; ++i)
                if (Formations[i].HasUndeliveredPost)
                {
                    Formations[i].DeliverPost();
                    anyDelivered = true;
                }
        }
    }
}
