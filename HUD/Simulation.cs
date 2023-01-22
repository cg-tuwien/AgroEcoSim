using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;
using System.Diagnostics;

public partial class Simulation : CanvasLayer
{
	enum DoubleScreenPhase { Idle, ProcessingOverlay, RequestedBackground }

	DoubleScreenPhase ScreenPhase = DoubleScreenPhase.Idle;

	SimulationWorld World;
	private SimulationSettings Parameters;
	public bool Paused { get; private set; } = false;
	public MenuEvent MenuEvent = MenuEvent.None;
	public uint ManualStepsRequested { get; private set; } = 0U;

	Button PlayPauseButton;
	Control ManualSteps;
	Label DateLabel;
	Button ScreenshotButton;
	Label HiddenStepsLabel;

	FileDialog SaveDialog;

	GodotDebugOverlay DebugOverlay;
	Camera3D SceneCamera;

	Button IrradianceDebug;

	Label ShootSegmentsValue;
	Label TrianglesValue;
	Label SensorsValue;

	const string ScreensDir = "Screens";
	readonly string SimulationTimestamp = FormatedTimestamp(precise: false);
	string SimulationDir;
	uint ScreensCounter = 0;

	static string FormatedTimestamp(bool precise)
	{
		var now = DateTime.UtcNow;
		return now.ToString("yyyy-MM-dd_HH-mm_ss") + (precise ? now.Ticks.ToString() : "");
	}

	public void Pause()
	{
		Paused = !Paused;
		if (Paused)
		{
			PlayPauseButton.Text = ">";
			ManualSteps.Show();
			IrradianceDebug.Disabled = false;
		}
		else
		{
			PlayPauseButton.Text = "||";
			IrradianceDebug.Disabled = true;
			ManualSteps.Hide();
			SwitchOffOverlay();
		}
	}

	void SwitchOffOverlay()
	{
		IrradianceDebug.ButtonPressed = false;
		DebugOverlay.Hide();
	}

	public override void _Ready()
	{
		DateLabel = GetNode<Label>($"Animation/Control/{nameof(DateLabel)}");
		PlayPauseButton = GetNode<Button>($"Animation/Control/{nameof(PlayPauseButton)}");
		ManualSteps = GetNode<Control>($"Animation/Control/{nameof(ManualSteps)}");
		ManualSteps.Hide();
		ScreenshotButton = GetNode<Button>($"Export/{nameof(ScreenshotButton)}");

		SaveDialog = GetNode<FileDialog>($"Export/{nameof(SaveDialog)}");
		SaveDialog.RootSubfolder = Directory.GetCurrentDirectory();
		IrradianceDebug = GetNode<Button>("Debug/IrradianceButton");
		IrradianceDebug.Disabled = !Paused;

		ShootSegmentsValue = GetNode<Label>("Debug/ShootSegsValue");
		TrianglesValue = GetNode<Label>("Debug/TrianglesValue");
		SensorsValue = GetNode<Label>("Debug/SensorsValue");

		if (!System.IO.Directory.Exists(ScreensDir))
			System.IO.Directory.CreateDirectory(ScreensDir);

		var ignoreFile = $"{ScreensDir}/.gdignore";
		if (!System.IO.File.Exists(ignoreFile))
			System.IO.File.Create(ignoreFile);


		if (!System.IO.Directory.Exists(ScreensDir))
			System.IO.Directory.CreateDirectory(ScreensDir);

		SimulationDir = $"{ScreensDir}/{SimulationTimestamp}";

		if (!System.IO.Directory.Exists(SimulationDir))
		{
			System.IO.Directory.CreateDirectory(SimulationDir);
			var proc = new ProcessStartInfo
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				Arguments = "-Rf o+w ${SimulationDir}",
				FileName = "chmod"
			};
			Process.Start(proc);
		}

		IrradianceDebug.Disabled = false;

		base._Ready();
	}

	uint PrevTimestep = 0;
	DateTime CoolDown = DateTime.UtcNow;

	public override void _Process(double delta)
	{
		if (DateTime.UtcNow >= CoolDown)
		{
			var datetime = AgroWorld.InitialTime + (AgroWorld.TicksPerHour == 1 ? TimeSpan.FromHours(World.Timestep) : TimeSpan.FromHours(World.Timestep / (float)AgroWorld.TicksPerHour));
			var local = datetime.ToLocalTime();
			DateLabel.Text = $"{local.Year}-{local.Month}-{local.Day} {local.Hour}:{local.Minute}";

			uint segments = 0, triangles = 0, sensors = 0;
			foreach(var formation in World.Formations)
				if (formation is PlantFormation2 plant)
				{
					var (sg, tr, sn) = plant.GeometryStats();
					segments += sg;
					triangles += tr;
					sensors += sn;
				}

			ShootSegmentsValue.Text = segments.ToString();
			TrianglesValue.Text = triangles.ToString();
			SensorsValue.Text = sensors.ToString();

			if (DebugOverlay.Visible)
			{
				if (World.Timestep > PrevTimestep)
					ComputeIrradince();
			}
			else
			{
				if (ScreenPhase != DoubleScreenPhase.RequestedBackground)
					ScreenPhase = DoubleScreenPhase.Idle;
			}

			if (ScreenshotButton.ButtonPressed && DebugOverlay.Visible && World.Timestep > PrevTimestep && ScreenPhase == DoubleScreenPhase.Idle)
			{
				ScreenPhase = DoubleScreenPhase.ProcessingOverlay;
				Debug.WriteLine("DoubleScreenPhase.ProcessingOverlay");
				DebugOverlay.Texture.GetImage().SavePng($"{SimulationDir}/r{ScreensCounter:D8}.png");
				DebugOverlay.Hide();
			}

			if (ScreenPhase != DoubleScreenPhase.Idle)
				Debug.WriteLine($"phase {ScreenPhase}");

			if (ScreenshotButton.ButtonPressed && ((World.Timestep > PrevTimestep && ScreenPhase == DoubleScreenPhase.Idle) || ScreenPhase == DoubleScreenPhase.RequestedBackground))
			{
				var imgInternal = GetViewport().GetTexture().GetImage();
				imgInternal.FlipY();
				imgInternal.SavePng($"{SimulationDir}/g{ScreensCounter++:D8}.png");
				if (ScreenPhase == DoubleScreenPhase.RequestedBackground)
				{
					DebugOverlay.Show();
					ScreenPhase = DoubleScreenPhase.Idle;
					Debug.WriteLine("DoubleScreenPhase.Idle");

					if (!Paused)
						CoolDown = DateTime.UtcNow + TimeSpan.FromSeconds(1);
				}
			}

			if (ScreenPhase == DoubleScreenPhase.ProcessingOverlay)
			{
				ScreenPhase = DoubleScreenPhase.RequestedBackground;
				Debug.WriteLine("DoubleScreenPhase.RequestedBackground");
			}

			PrevTimestep = World.Timestep;
		}
		base._Process(delta);
	}

	internal void Load(SimulationWorld world, GodotDebugOverlay overlay, Camera3D sceneCamera, SimulationSettings parameters)
	{
		World = world;
		DebugOverlay = overlay;
		Parameters = parameters;
		SceneCamera = sceneCamera;
		GetNode<HSlider>("Animation/Control/HSlider").Value = Parameters.HiddenSteps;
		GetNode<HSlider>("Debug/HSlider").Value = Parameters.IrradianceOverlayOpacity;

		HiddenStepsLabel = GetNode<Label>("Animation/Control/HiddenStepsLabel");
		UpdateHiddenStepsLabel();
	}

	void UpdateHiddenStepsLabel()
	{
		HiddenStepsLabel.Text = Parameters.HiddenSteps == 1 ? "Show all" : $"Batch {Parameters.HiddenSteps} steps";
	}

	public void OneFrame() => ManualStepsRequested = 1U;
	public void OneDay() => ManualStepsRequested = AgroWorld.TicksPerHour * 12;

	internal void ManualStepsDone() => ManualStepsRequested = 0U;

	public void HiddenSteps(float value)
	{
		Parameters.HiddenSteps = Math.Max(1, (uint)Math.Round(value));
		UpdateHiddenStepsLabel();
	}
	public void IrradianceOpacity(float value)
	{
		Parameters.IrradianceOverlayOpacity = value;
		DebugOverlay.Opacity = value;
	}

	public void MenuEntered() => MenuEvent = MenuEvent.Enter;

	//public void MenuLeft() => MenuEvent = MenuEvent.Leave;
	public void MenuLeft(bool dummy = false) => MenuEvent = MenuEvent.Leave;

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

	public void ComputeIrradince()
	{
		var b = SceneCamera.Transform.basis;
		var o = SceneCamera.GlobalPosition;
		var v = GetViewport().GetVisibleRect().Size; //Godot3 has .Size
		var matrix = new float[] { o.x, o.y, o.z, b.z.x, b.z.y, b.z.z, SceneCamera.Fov, v.x, v.y };

		Image image;
		var imgData = IrradianceClient.DebugIrradiance(World.Timestep, World.Formations, World.Obstacles, matrix);

		if (imgData != null)
			image = Image.CreateFromData((int)Math.Round(v.x), (int)Math.Round(v.y), false, Image.Format.Rgbf, imgData);
		else
			image = Image.CreateFromData(2, 2, false, Image.Format.R8, new byte[]{ 255, 255, 255, 255 });

		var texture = ImageTexture.CreateFromImage(image);

		DebugOverlay.Texture?.Dispose();
		DebugOverlay.Texture = texture;
	}

	public void DebugIrradiance(bool flag)
	{
		if (flag)
		{
			ComputeIrradince();
			DebugOverlay.Show();
		}
		else
			DebugOverlay.Hide();
	}
}
