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
	FileDialog SaveDialog;

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
		DateLabel = GetNode<Label>($"Animation/Control/{nameof(DateLabel)}");
		PlayPause = GetNode<Button>($"Animation/Control/{nameof(PlayPause)}");
		ManualSteps = GetNode<Control>($"Animation/Control/{nameof(ManualSteps)}");
		ManualSteps.Hide();

		SaveDialog = GetNode<FileDialog>($"Export/{nameof(SaveDialog)}");
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

		GetNode<HSlider>("Animation/Control/HSlider").Value = Parameters.HiddenSteps;
	}

	public void OneFrame() => ManualStepsRequested = 1U;
	public void OneDay() => ManualStepsRequested = AgroWorld.TicksPerHour * 24;

	internal void ManualStepsDone() => ManualStepsRequested = 0U;

	public void HiddenSteps(float value) => Parameters.HiddenSteps = (uint)Math.Round(value);

	public void MenuEntered() => MenuEvent = MenuEvent.Enter;

	public void MenuLeft() => MenuEvent = MenuEvent.Leave;

	enum SaveModes {None, IrrV1, IrrV2}
	SaveModes SaveMode = SaveModes.None;
	public void IrradianceV1()
	{
		SaveDialog.ClearFilters();
		SaveDialog.AddFilter("*.mesh ; Triangular mesh scene");
		SaveMode = SaveModes.IrrV1;
		SaveDialog.CurrentFile = $"{World.Timestep:D5}.mesh";
		SaveDialog.PopupCentered();
		MenuEntered();
	}

	public void IrradianceV2()
	{
		SaveDialog.ClearFilters();
		SaveDialog.AddFilter("*.prim ; Primitives scene");
		SaveMode = SaveModes.IrrV2;
		SaveDialog.CurrentFile = $"{World.Timestep:D5}.prim";
		SaveDialog.PopupCentered();
		MenuEntered();
	}

	public void OnSave(string path)
	{
		MenuLeft();
		System.Diagnostics.Debug.WriteLine(SaveDialog.CurrentFile);
		switch (SaveMode)
		{
			case SaveModes.IrrV1: IrradianceClient.ExportToFile(path, 1, World.Formations, World.Obstacles); break;
			case SaveModes.IrrV2: IrradianceClient.ExportToFile(path, 2, World.Formations, World.Obstacles); break;
		}
	}
}
