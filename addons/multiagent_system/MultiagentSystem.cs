using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public class MultiagentSystem : Spatial
{
	[Export]
	public PackedScene HudScene;

	[Signal]
	public delegate void EnteredMenu();

	[Signal]
	public delegate void LeftMenu();


	bool Paused = false;
	bool Notified = false;

	HUD hud;
	List<MeshInstance> Sprites = new List<MeshInstance>();
	
	SimulationWorld World;
	
	float Time = 0f;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// EmitSignal("LeftMenu");
#if GODOT
		GD.Print("GODOT is defined properly.");
#else
		GD.Print("ERROR: GODOT is not defined!");
#endif		
		SimulationWorld.GodotAddChild = node => AddChild(node);
		SimulationWorld.GodotRemoveChild = node => RemoveChild(node);
		Translation = new Vector3(-0.5f * AgroWorld.FieldSize.X, 0f, -0.5f * AgroWorld.FieldSize.Z);

		World = Initialize.World();

		hud = (HUD)HudScene.Instance();
		hud.Load((SoilVisualisationSettings)((SoilFormation)World.Formations[0]).Parameters);
		AddChild(hud);

		// GetNode<HUD>("HUD").Load((SoilVisualisationSettings)((SoilFormation)World.Formations[0]).Parameters);
	}

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(float delta)
	{
		Paused = hud.Paused;
		if(hud.MenuState == MenuStatus.Entered){
			EmitSignal("EnteredMenu");
			hud.MenuState = MenuStatus.EnteredWaiting;
		}
		else if(hud.MenuState == MenuStatus.Left && hud.ColorEditorOpen == false){
			EmitSignal("LeftMenu");
			hud.MenuState = MenuStatus.LeftWaiting;
		}

		if(!Paused){
			Time += delta;
			if (World.Timestep < AgroWorld.TimestepsTotal)
			{
				World.Run(1);
			
				if (World.Timestep == AgroWorld.TimestepsTotal - 1)
					GD.Print($"Simulation successfully finished after {AgroWorld.TimestepsTotal} timesteps.");
			}
		}
		if(hud.RecentChange){
			((SoilFormation)World.Formations[0]).GodotProcess(0);
			hud.RecentChange = false;
		}

	}

	
}
