using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;
using V3 = System.Numerics.Vector3;

[Tool]
public partial class MultiagentSystem : Node3D
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
	public delegate void EnteredMenuEventHandler();

	[Signal]
	public delegate void LeftMenuEventHandler();


	bool Paused = false;
	bool MenuInactive = true;
	bool ColorPickerInactive = true;
	int AsyncLock = 0;

	HUD Hud;
	Simulation Simulation;
	Soil Soil;
	Roots Roots;
	Shoots Shoots;

	GodotGround Ground;
	GodotDebugOverlay DebugOverlay;
	Camera3D SceneCamera;

	SimulationWorld World;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		if (Engine.IsEditorHint())
			return;

		// EmitSignal("LeftMenu");
#if GODOT
		GD.Print("GODOT is defined properly.");
#else
		GD.Print("ERROR: GODOT is not defined!");
#endif
		SimulationWorld.GodotAddChild = node => AddChild(node);
		SimulationWorld.GodotRemoveChild = RemoveChild;
		//Translation = new Vector3(-0.5f * AgroWorld.FieldSize.X, AgroWorld.FieldResolution, -0.5f * AgroWorld.FieldSize.Z);
		//Translation = new Vector3(0, AgroWorld.FieldResolution, 0);

		var fieldSize = new Utils.Json.Vector3XDZ{ X = 5, D = 3, Z = 5 };

		var plants = new List<PlantRequest>();
		//for(float x = 0.5f; x < fieldSize.X; x += 1f)
		var x = fieldSize.X * 0.5f;
			for(float z = 0.5f; z < fieldSize.Z; z += 1f)
				plants.Add(new(){ Position = new Utils.Json.Vector3XYZ{ X = x, Y = -0.01f, Z = z }});
		var obstacles = new ObstacleRequest[] {
			new(){ Type = "Wall", Length = 5f, Height = 3.2f, Position = new Utils.Json.Vector3XYZ{ X = 2.5f, Y = 0f, Z = 0.1f }},
			new(){ Type = "Umbrella", Radius = 1.5f, Height = 2.2f, Position = new Utils.Json.Vector3XYZ{ X = 2.5f, Y = 0f, Z = 2.5f }}
		};

		World = Initialize.World(new SimulationRequest(){
			TotalHours = 24 * 31 * 12,
			FieldSize = fieldSize,
			Plants = plants.ToArray(),
			Obstacles = obstacles,
		});

		Ground = new ();
		DebugOverlay = new ();
		DebugOverlay.Hide();

		foreach(var item in GetParent().GetChildren())
			if (item != null && item is Camera3D camera && camera.Visible)
			{
				SceneCamera = camera;
				break;
			}

		System.Diagnostics.Debug.WriteLine(SceneCamera == null ? "NO CAMERA" : "has camera");

		Hud = (HUD)HudScene.Instantiate();

		Simulation = (Simulation)SimulationScene.Instantiate();
		Simulation.Load(World, DebugOverlay, SceneCamera, AgroWorldGodot.SimulationSettings);

		Soil = (Soil)SoilScene.Instantiate();
		Soil.Load(AgroWorldGodot.SoilVisualization, Ground);

		Roots = (Roots)RootsScene.Instantiate();
		Roots.Load(AgroWorldGodot.RootsVisualization);

		Shoots = (Shoots)ShootsScene.Instantiate();
		Shoots.Load(AgroWorldGodot.ShootsVisualization);

		Hud.Load(Simulation, Soil, Roots, Shoots);
		AddChild(Hud);

		//Throws errors, freezes after a few seconds
		//Task.Run(() => World.Run(AgroWorld.TimestepsTotal));
	}

	bool AnyMenuEntered() => Soil.MenuEvent == MenuEvent.Enter || Roots.MenuEvent == MenuEvent.Enter || Shoots.MenuEvent == MenuEvent.Enter || Simulation.MenuEvent == MenuEvent.Enter;
	bool AnyMenuLeft() => Soil.MenuEvent == MenuEvent.Leave || Roots.MenuEvent == MenuEvent.Leave || Shoots.MenuEvent == MenuEvent.Leave || Simulation.MenuEvent == MenuEvent.Leave;

	/// <summary>
	/// Called every frame
	/// </summay>
	/// <param name="delta">'Elapsed time since the previous frame</param>
	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
			return;

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
				if (AnyMenuEntered())
				{
					MenuInactive = false;
					EmitSignal("EnteredMenu");
				}
			}
			else
			{
				if (AnyMenuLeft())
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
				if (AnyMenuEntered())
					MenuInactive = false;
				else
					EmitSignal("LeftMenu");
			}
		}

		Soil.MenuEvent = MenuEvent.None;
		Soil.ColorEvent = MenuEvent.None;
		Roots.MenuEvent = MenuEvent.None;
		Shoots.MenuEvent = MenuEvent.None;
		Simulation.MenuEvent = MenuEvent.None;

//Throws errors, freezes after a few seconds
// #if ASYNC
// 		if (!Paused && World.Timestep < AgroWorld.TimestepsTotal && AsyncLock == 0)
// 		{
// 			Interlocked.Increment(ref AsyncLock);
// 			Task.Run(() =>
// 			{
// 				World.Run(AgroWorldGodot.SimulationSettings.HiddenSteps);
// 				if (World.Timestep == AgroWorld.TimestepsTotal - 1)
// 					GD.Print($"Simulation successfully finished after {AgroWorld.TimestepsTotal} timesteps.");
// 				Interlocked.Decrement(ref AsyncLock);
// 			});
// 		}
// #else

		if (World.Timestep < AgroWorld.TimestepsTotal)
		{
			if (!Paused)
			{
				World.Run(AgroWorldGodot.SimulationSettings.HiddenSteps);
				if (World.Timestep == AgroWorld.TimestepsTotal - 1)
					GD.Print($"Simulation successfully finished after {AgroWorld.TimestepsTotal} timesteps.");
			}
			else if (Simulation.ManualStepsRequested > 0)
			{
				World.Run(Simulation.ManualStepsRequested);
				Simulation.ManualStepsDone();
			}
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
