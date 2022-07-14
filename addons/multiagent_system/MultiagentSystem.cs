using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public class MultiagentSystem : Spatial
{
	bool paused = false;
	bool pressed = false;
	List<MeshInstance> Sprites = new List<MeshInstance>();
	
	SimulationWorld World;
	
	float Time = 0f;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
#if GODOT
		GD.Print("GODOT is defined properly.");
#else
		GD.Print("ERROR: GODOT is not defined!");
#endif		
		SimulationWorld.GodotAddChild = node => AddChild(node);
		SimulationWorld.GodotRemoveChild = node => RemoveChild(node);
		Translation = new Vector3(-0.5f * AgroWorld.FieldSize.X, 0f, -0.5f * AgroWorld.FieldSize.Z);

		World = Initialize.World();	
	}

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(float delta)
	{
		solve_input();

		if(!paused){
			Time += delta;
			if (World.Timestep < AgroWorld.TimestepsTotal)
			{
				World.Run(1);
			
				if (World.Timestep == AgroWorld.TimestepsTotal - 1)
					GD.Print($"Simulation successfully finished after {AgroWorld.TimestepsTotal} timesteps.");
			}
		}
	}

	private void solve_input(){
		if(Input.IsActionPressed("stop") && !pressed){
			paused = !paused;
			pressed = true;
		}
		else if(!Input.IsActionPressed("stop") && pressed){
			pressed = false;
		}
	}
}
