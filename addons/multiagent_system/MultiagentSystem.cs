using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;
using V3 = System.Numerics.Vector3;

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
	readonly List<MeshInstance> Sprites = new();

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
		//Translation = new Vector3(-0.5f * AgroWorld.FieldSize.X, AgroWorld.FieldResolution, -0.5f * AgroWorld.FieldSize.Z);
		Translation = new Vector3(0, AgroWorld.FieldResolution, 0);

		Utils.Json.Vector3XDZ fieldSize = default;
		fieldSize.X = 5;
		fieldSize.D = 3;
		fieldSize.Z = 5;

		var plants = new List<PlantRequest>();
		for(float x = 0.5f; x < fieldSize.X; x += 1f)
			for(float z = 0.5f; z < fieldSize.Z; z += 1f)
				plants.Add(new(){ Position = new (x, -0.1f, z) });

		var obstacles = new ObstacleRequest[] {
			new(){ Type = "Wall", Length = 5f, Height = 4f},
			new(){ Type = "Umbrella", Radius = 1.5f, Height = 2.2f, Position = new(2.5f, 0f, 2.5f)}
		};

		World = Initialize.World(new SimulationRequest(){
			TotalHours = 24 * 31 * 4,
			FieldSize = fieldSize,
			Plants = plants.ToArray(),
			Obstacles = obstacles,
		});

		hud = (HUD)HudScene.Instance();
		hud.Load(((SoilFormation)World.Formations[0]).Parameters);
		AddChild(hud);

		// GetNode<HUD>("HUD").Load((SoilVisualisationSettings)((SoilFormation)World.Formations[0]).Parameters);
	}

	/// <summary>
	/// Called every frame
	/// </summay>
	/// <param name="delta">'Elapsed time since the previous frame</param>
	public override void _Process(float delta)
	{
		Paused = hud.Paused;
		if (hud.MenuState == MenuStatus.Entered){
			EmitSignal("EnteredMenu");
			hud.MenuState = MenuStatus.EnteredWaiting;
		}
		else if (hud.MenuState == MenuStatus.Left && hud.ColorEditorOpen == false){
			EmitSignal("LeftMenu");
			hud.MenuState = MenuStatus.LeftWaiting;
		}

		if (!Paused){
			Time += delta;
			if (World.Timestep < AgroWorld.TimestepsTotal)
			{
				World.Run(1);

				if (World.Timestep == AgroWorld.TimestepsTotal - 1)
					GD.Print($"Simulation successfully finished after {AgroWorld.TimestepsTotal} timesteps.");
			}
		}
		if (hud.RecentChange){
			((SoilFormation)World.Formations[0]).GodotProcess(0);
			hud.RecentChange = false;
		}

	}


}
