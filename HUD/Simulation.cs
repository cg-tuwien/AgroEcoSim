using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public class Simulation : CanvasLayer
{
	SimulationWorld World;
	public bool Paused { get; private set; } = false;
	public uint ManualStepsRequested { get; private set; } = 0U;

	Button PlayPause;
	Control ManualSteps;
	Label DateLabel;

	//must be lower case in order to work with Godot
	public void Pause()
	{
		Paused = !Paused;
		if (Paused)
		{
			PlayPause.Text = ">";
			ManualSteps.Show();
		}
		else
		{
			PlayPause.Text = "||";
			ManualSteps.Hide();
		}
	}

	public override void _Ready()
	{
		DateLabel = GetNode<Label>(nameof(DateLabel));
		PlayPause = GetNode<Button>(nameof(PlayPause));
		ManualSteps = GetNode<Control>(nameof(ManualSteps));
		ManualSteps.Hide();
		base._Ready();
	}

	public override void _Process(float delta)
	{
		var datetime = AgroWorld.InitialTime + (AgroWorld.TicksPerHour == 1 ? TimeSpan.FromHours(World.Timestep) : TimeSpan.FromHours(World.Timestep / (float)AgroWorld.TicksPerHour));
		var local = datetime.ToLocalTime();
		DateLabel.Text = $"{local.Year}-{local.Month}-{local.Day} {local.Hour}:{local.Minute}";
		base._Process(delta);
	}

	internal void Load(SimulationWorld world) => World = world;

	public void OneFrame() => ManualStepsRequested = 1U;
	public void OneDay() => ManualStepsRequested = AgroWorld.TicksPerHour * 24;

	internal void ManualStepsDone() => ManualStepsRequested = 0U;
}
