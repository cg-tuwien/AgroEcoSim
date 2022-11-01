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

	[Export]
	public PackedScene SimulationScene;
	[Export]
	public PackedScene SoilScene;
	[Export]
	public PackedScene RootsScene;
	[Export]
	public PackedScene ShootsScene;

	[Signal]
	public delegate void EnteredMenu();

	[Signal]
	public delegate void LeftMenu();


	bool Paused = false;
	bool MouseNavigationActive = true;
	bool MenuInactive = true;
	bool ColorPickerInactive = true;
	int AsyncLock = 0;

	HUD Hud;
	Simulation Simulation;
	Soil Soil;
	Roots Roots;
	Shoots Shoots;

	readonly List<MeshInstance> Sprites = new();
	GodotGround Ground;

	SimulationWorld World;

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
			new(){ Type = "Wall", Length = 5f, Height = 3.2f, Position = new(2.5f, 0f, 0f)},
			new(){ Type = "Umbrella", Radius = 1.5f, Height = 2.2f, Position = new(2.5f, 0f, 2.5f)}
		};

		World = Initialize.World(new SimulationRequest(){
			TotalHours = 24 * 31 * 4,
			FieldSize = fieldSize,
			Plants = plants.ToArray(),
			Obstacles = obstacles,
		});

		Ground = new GodotGround();


		Hud = (HUD)HudScene.Instance();

		Simulation = (Simulation)SimulationScene.Instance();
		Simulation.Load(World);

		Soil = (Soil)SoilScene.Instance();
		Soil.Load(AgroWorldGodot.SoilVisualization, Ground);

		Roots = (Roots)RootsScene.Instance();
		Roots.Load(AgroWorldGodot.RootsVisualization);

		Shoots = (Shoots)ShootsScene.Instance();
		Shoots.Load(AgroWorldGodot.ShootsVisualization);

		Hud.Load(Simulation, Soil, Roots, Shoots);
		AddChild(Hud);

		//Throws errors, freezes after a few seconds
		//Task.Run(() => World.Run(AgroWorld.TimestepsTotal));
	}

	/// <summary>
	/// Called every frame
	/// </summay>
	/// <param name="delta">'Elapsed time since the previous frame</param>
	public override void _Process(float delta)
	{
		Paused = Simulation.Paused;

		if (ColorPickerInactive)
		{
			if (Soil.ColorEvent == MenuEvent.Enter)
			{
				ColorPickerInactive = false;
				MenuInactive = true;
				EmitSignal("EnteredMenu");
			}
			else if (MenuInactive)
			{
				if (Soil.MenuEvent == MenuEvent.Enter || Roots.MenuEvent == MenuEvent.Enter || Shoots.MenuEvent == MenuEvent.Enter)
				{
					MenuInactive = false;
					EmitSignal("EnteredMenu");
				}
			}
			else
			{
				if (Soil.MenuEvent == MenuEvent.Leave || Roots.MenuEvent == MenuEvent.Leave || Shoots.MenuEvent == MenuEvent.Leave)
				{
					MenuInactive = true;
					EmitSignal("LeftMenu");
				}
			}
		}
		else
		{
			if (Soil.ColorEvent == MenuEvent.Leave)
			{
				ColorPickerInactive = true;
				if (Soil.MenuEvent == MenuEvent.Enter || Roots.MenuEvent == MenuEvent.Enter || Shoots.MenuEvent == MenuEvent.Enter)
					MenuInactive = false;
				else
					EmitSignal("LeftMenu");
			}
		}

		Soil.MenuEvent = MenuEvent.None;
		Soil.ColorEvent = MenuEvent.None;
		Roots.MenuEvent = MenuEvent.None;
		Shoots.MenuEvent = MenuEvent.None;

//Throws errors, freezes after a few seconds
// #if ASYNC
// 		if (!Paused && World.Timestep < AgroWorld.TimestepsTotal && AsyncLock == 0)
// 		{
// 			Interlocked.Increment(ref AsyncLock);
// 			Task.Run(() =>
// 			{
// 				World.Run(1);
// 				if (World.Timestep == AgroWorld.TimestepsTotal - 1)
// 					GD.Print($"Simulation successfully finished after {AgroWorld.TimestepsTotal} timesteps.");
// 				Interlocked.Decrement(ref AsyncLock);
// 			});
// 		}
// #else
		if (!Paused && World.Timestep < AgroWorld.TimestepsTotal)
		{
			World.Run(1);
			if (World.Timestep == AgroWorld.TimestepsTotal - 1)
				GD.Print($"Simulation successfully finished after {AgroWorld.TimestepsTotal} timesteps.");
		}
		else if (Simulation.ManualStepsRequested > 0)
		{
			World.Run(Simulation.ManualStepsRequested);
			Simulation.ManualStepsDone();
		}
// #endif

		if (Paused)
		{
			if (Soil.UpdateRequest)
			{
				foreach(var formation in World.Formations)
					if (formation is SoilFormation soil)
						soil.GodotProcess();
				Soil.UpdateRequest = false;
			}

			if (Roots.UpdateRequest || Shoots.UpdateRequest)
			{
				foreach(var formation in World.Formations)
					if (formation is PlantFormation2 plant)
						plant.GodotProcess();
				Roots.UpdateRequest = false;
				Shoots.UpdateRequest = false;
			}
		}

		if (AgroWorldGodot.RootsVisualization.RootsVisibility == Visibility.MakeVisible)
			AgroWorldGodot.RootsVisualization.RootsVisibility = Visibility.Visible;

		if (AgroWorldGodot.RootsVisualization.RootsVisibility == Visibility.MakeInvisible)
			AgroWorldGodot.RootsVisualization.RootsVisibility = Visibility.Invisible;

		if (AgroWorldGodot.ShootsVisualization.StemsVisibility == Visibility.MakeVisible)
			AgroWorldGodot.ShootsVisualization.StemsVisibility = Visibility.Visible;

		if (AgroWorldGodot.ShootsVisualization.StemsVisibility == Visibility.MakeInvisible)
			AgroWorldGodot.ShootsVisualization.StemsVisibility = Visibility.Invisible;

		if (AgroWorldGodot.ShootsVisualization.LeafsVisibility == Visibility.MakeVisible)
			AgroWorldGodot.ShootsVisualization.LeafsVisibility = Visibility.Visible;

		if (AgroWorldGodot.ShootsVisualization.LeafsVisibility == Visibility.MakeInvisible)
			AgroWorldGodot.ShootsVisualization.LeafsVisibility = Visibility.Invisible;

		if (AgroWorldGodot.ShootsVisualization.BudsVisibility == Visibility.MakeVisible)
			AgroWorldGodot.ShootsVisualization.BudsVisibility = Visibility.Visible;

		if (AgroWorldGodot.ShootsVisualization.BudsVisibility == Visibility.MakeInvisible)
			AgroWorldGodot.ShootsVisualization.BudsVisibility = Visibility.Invisible;
	}
}
