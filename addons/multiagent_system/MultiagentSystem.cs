using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public class MultiagentSystem : Spatial
{
	bool Paused = false;
	bool Pressed = false;
	bool SingleStep = false;
	int Delay = 0;
	List<MeshInstance> Sprites = new List<MeshInstance>();

	SimulationWorld World;

	//float Time = 0f;
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
		//Translation = new Vector3(-0.5f * AgroWorld.FieldSize.X, AgroWorld.FieldResolution, -0.5f * AgroWorld.FieldSize.Z);
		Translation = new Vector3(0, AgroWorld.FieldResolution, 0);

		World = Initialize.World();
	}

	/// <summary>
	/// Called every frame
	/// </summay>
	/// <param name="delta">'Elapsed time since the previous frame</param>
	public override void _Process(float delta)
	{
		SolveInput();

		if(!Paused)
		{
			//Time += delta;
			if (World.Timestep < AgroWorld.TimestepsTotal)
			{
				World.Run(1);

				if (World.Timestep == AgroWorld.TimestepsTotal - 1)
					GD.Print($"Simulation successfully finished after {AgroWorld.TimestepsTotal} timesteps.");
			}
			Paused = SingleStep;
		}
	}

	private void SolveInput()
	{
		if(Input.IsActionPressed("stop") && !Pressed)
		{
			if (Paused)
				SingleStep = Input.IsActionPressed("ctrl");

			Paused = !Paused;
			Pressed = true;
		}
		else if(!Input.IsActionPressed("stop") && Pressed)
			Pressed = false;

		if (GlobalPauseRequest)
		{
			Paused = true;
			GlobalPauseRequest = false;
		}
	}

	static bool GlobalPauseRequest;
	/// <summary>
	//Useful for debug to trigger pause as follows:<br/>
	//<c>#if GODOT<br/>
	//MultiagentSystem.TriggerPause();<br/>
	//#endif<br/><c/>
	/// </summary>
	public static void TriggerPause() => GlobalPauseRequest = true;
}
