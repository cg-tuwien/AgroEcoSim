using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;
using System.Diagnostics;

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

	GodotDebugOverlay DebugOverlay;
	Camera SceneCamera;

	Button IrradianceDebug;

	public void Pause()
	{
		Paused = !Paused;
		if (Paused)
		{
			PlayPause.Text = ">";
			ManualSteps.Show();
			IrradianceDebug.Disabled = false;
		}
		else
		{
			PlayPause.Text = "||";
			ManualSteps.Hide();
			IrradianceDebug.Disabled = true;
			IrradianceDebug.Pressed = false;
			DebugOverlay.Hide();
		}
	}

	public override void _Ready()
	{
		DateLabel = GetNode<Label>($"Animation/Control/{nameof(DateLabel)}");
		PlayPause = GetNode<Button>($"Animation/Control/{nameof(PlayPause)}");
		ManualSteps = GetNode<Control>($"Animation/Control/{nameof(ManualSteps)}");
		ManualSteps.Hide();

		SaveDialog = GetNode<FileDialog>($"Export/{nameof(SaveDialog)}");
		IrradianceDebug = GetNode<Button>("Debug/Control/IrradianceButton");
		IrradianceDebug.Disabled = !Paused;
		base._Ready();
	}

	public override void _Process(float delta)
	{
		var datetime = AgroWorld.InitialTime + (AgroWorld.TicksPerHour == 1 ? TimeSpan.FromHours(World.Timestep) : TimeSpan.FromHours(World.Timestep / (float)AgroWorld.TicksPerHour));
		var local = datetime.ToLocalTime();
		DateLabel.Text = $"{local.Year}-{local.Month}-{local.Day} {local.Hour}:{local.Minute}";
		base._Process(delta);
	}

	internal void Load(SimulationWorld world, GodotDebugOverlay overlay, Camera sceneCamera, SimulationSettings parameters)
	{
		World = world;
		DebugOverlay = overlay;
		Parameters = parameters;
		SceneCamera = sceneCamera;
		GetNode<HSlider>("Animation/Control/HSlider").Value = Parameters.HiddenSteps;
		GetNode<HSlider>("Debug/Control/IrradiancHSlider").Value = Parameters.IrradianceOverlayOpacity;
	}

	public void OneFrame() => ManualStepsRequested = 1U;
	public void OneDay() => ManualStepsRequested = AgroWorld.TicksPerHour * 12;

	internal void ManualStepsDone() => ManualStepsRequested = 0U;

	public void HiddenSteps(float value) => Parameters.HiddenSteps = (uint)Math.Round(value);
	public void IrradianceOpacity(float value)
	{
		Parameters.IrradianceOverlayOpacity = value;
		DebugOverlay.Opacity = value;
	}

	public void MenuEntered() => MenuEvent = MenuEvent.Enter;

	public void MenuLeft() => MenuEvent = MenuEvent.Leave;

	enum SaveModes {None, IrrV1, IrrV2, IrrV3, JSON}
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

	public void IrradianceV3()
	{
		SaveDialog.ClearFilters();
		SaveDialog.AddFilter("*.prim ; Primitives scene");
		SaveMode = SaveModes.IrrV3;
		SaveDialog.CurrentFile = $"{World.Timestep:D5}.prim";
		SaveDialog.PopupCentered();
		MenuEntered();
	}

	public void Complete()
	{
		SaveDialog.ClearFilters();
		SaveDialog.AddFilter("*.json ; JSON Serialization");
		SaveMode = SaveModes.JSON;
		SaveDialog.CurrentFile = $"{World.Timestep:D5}.json";
		SaveDialog.PopupCentered();
		MenuEntered();
	}

	public void OnSave(string path)
	{
		MenuLeft();
		//Debug.WriteLine(SaveDialog.CurrentFile);
		switch (SaveMode)
		{
			case SaveModes.IrrV1:
			case SaveModes.IrrV2:
			case SaveModes.IrrV3:
				IrradianceClient.ExportToFile(path, (byte)SaveMode, World.Formations, World.Obstacles);
				break;
			case SaveModes.JSON:
				System.IO.File.WriteAllText(path, World.ToJson());
				break;
		}
	}

	public void DebugIrradiance(bool flag)
	{
		if (flag)
		{
			var b = SceneCamera.Transform.basis;
			var o = SceneCamera.GlobalTranslation;
			var v = GetViewport().Size;
			var matrix = new float[] { o.x, o.y, o.z, b.z.x, b.z.y, b.z.z, SceneCamera.Fov, v.x, v.y };

			var image = new Image();
			var texture = new ImageTexture();

			var imgData = IrradianceClient.DebugIrradiance(World.Timestep, World.Formations, World.Obstacles, matrix);
			if (imgData != null)
				image.CreateFromData((int)Math.Round(v.x), (int)Math.Round(v.y), false, Image.Format.Rgbf, imgData);
			else
				image.CreateFromData(2, 2, false, Image.Format.R8, new byte[]{255, 255, 255, 255});

			texture.CreateFromImage(image);

			DebugOverlay.Texture?.Dispose();
			DebugOverlay.Texture = texture;

			DebugOverlay.Show();
		}
		else
			DebugOverlay.Hide();
	}
}
