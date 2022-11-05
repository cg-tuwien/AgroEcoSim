using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public class Simulation : CanvasLayer
{
	SimulationWorld World;
	private SimulationSettings Parameters;
	public bool Paused { get; private set; } = false;
	public MenuEvent MenuEvent = MenuEvent.None;
	public uint ManualStepsRequested { get; private set; } = 0U;

	Button PlayPause;
	Control ManualSteps;
	Label DateLabel;

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
		DateLabel = GetNode<Label>($"Control/{nameof(DateLabel)}");
		PlayPause = GetNode<Button>($"Control/{nameof(PlayPause)}");
		ManualSteps = GetNode<Control>($"Control/{nameof(ManualSteps)}");
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

	internal void Load(SimulationWorld world, SimulationSettings parameters)
	{
		World = world;
		Parameters = parameters;

		GetNode<HSlider>("Control/HSlider").Value = Parameters.HiddenSteps;
	}

	public void OneFrame() => ManualStepsRequested = 1U;
	public void OneDay() => ManualStepsRequested = AgroWorld.TicksPerHour * 24;

	internal void ManualStepsDone() => ManualStepsRequested = 0U;

	public void HiddenSteps(float value) => Parameters.HiddenSteps = (uint)Math.Round(value);

	public void MenuEntered() => MenuEvent = MenuEvent.Enter;

	public void MenuLeft() => MenuEvent = MenuEvent.Leave;
}
