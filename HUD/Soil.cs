using Godot;
using System.Linq;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public partial class Soil : CanvasLayer
{
	// [Signal]
	// public delegate void EnteredMenu();

	// [Signal]
	// public delegate void LeftMenu();

	private SoilVisualisationSettings Parameters;
	public bool UpdateRequest = false;

	public MenuEvent MenuEvent = MenuEvent.None;
	public MenuEvent ColorEvent = MenuEvent.None;

	static readonly SoilMarkerTransferFunctionPreset[] MarkerTransferOptions= (SoilMarkerTransferFunctionPreset[])Enum.GetValues(typeof(SoilMarkerTransferFunctionPreset));
	static readonly SoilCellTransferFunctionPreset[] CellTransferOptions = (SoilCellTransferFunctionPreset[])Enum.GetValues(typeof(SoilCellTransferFunctionPreset));
	static readonly SurfaceCellTransferFunctionPreset[] SurfaceTransferOptions = (SurfaceCellTransferFunctionPreset[])Enum.GetValues(typeof(SurfaceCellTransferFunctionPreset));

	Control MarkerCustomColorButtom;
	Control CellCustomColorNode;
	Control SurfaceCustomColorNode;
	GodotGround Ground;

	List<CheckButton> DirMarkerVisibility = new(6);

	static bool IsVisible(Visibility visibility) => visibility == Visibility.Visible || visibility == Visibility.MakeVisible;

	const string CustomColorPath = "Controls/TransferFunction/CustomColor";
	const string CustomColorLoPath = $"{CustomColorPath}/LoColorPickerButton";
	const string CustomColorHiPath = $"{CustomColorPath}/HiColorPickerButton";
	const string TransferFunctionPath = "Controls/TransferFunction/OptionButton";
	const string ScaleSliderPath = "Controls/ScaleHSlider";
	const string AnimateSizeButtonPath = "Controls/SizeCheckButton";
	const string AnimateColorButtonPath = "Controls/ColorCheckButton";
	public void Load(SoilVisualisationSettings parameters, GodotGround ground)
	{
		Parameters = parameters;
		Ground = ground;

		#region MARKERS
		var markerTransferNode = GetNode<OptionButton>($"FlowMarkers/{TransferFunctionPath}");
		foreach(var item in MarkerTransferOptions)
			markerTransferNode.AddItem(item.ToString());

		MarkerCustomColorButtom = GetNode<Control>($"FlowMarkers/{CustomColorPath}");

		GetNode<CheckButton>("FlowMarkers/VisibilityButton").ButtonPressed = IsVisible(parameters.MarkerVisibility);
		GetNode<HSlider>($"FlowMarkers/{ScaleSliderPath}").Value = parameters.MarkerScale;
		GetNode<CheckButton>($"FlowMarkers/{AnimateSizeButtonPath}").ButtonPressed = parameters.AnimateMarkerSize;
		GetNode<CheckButton>($"FlowMarkers/{AnimateColorButtonPath}").ButtonPressed = parameters.AnimateMarkerColor;

		DirMarkerVisibility.Add(GetNode<CheckButton>("FlowMarkers/Visibility/X+Button"));
		DirMarkerVisibility.Add(GetNode<CheckButton>("FlowMarkers/Visibility/Y+Button"));
		DirMarkerVisibility.Add(GetNode<CheckButton>("FlowMarkers/Visibility/Z+Button"));
		DirMarkerVisibility.Add(GetNode<CheckButton>("FlowMarkers/Visibility/X-Button"));
		DirMarkerVisibility.Add(GetNode<CheckButton>("FlowMarkers/Visibility/Y-Button"));
		DirMarkerVisibility.Add(GetNode<CheckButton>("FlowMarkers/Visibility/Z-Button"));
		for(int i = 0; i < DirMarkerVisibility.Count; ++i)
			DirMarkerVisibility[i].ButtonPressed = IsVisible(parameters.IndividualMarkerDirectionVisibility[i]);

		// if (IsVisible(parameters.MarkerVisibility))
		// 	GetNode<Control>("FlowMarkers/Visibility/MarkerDirs").Show();
		// else
		// 	GetNode<Control>("FlowMarkers/Visibility/MarkerDirs").Hide();

		markerTransferNode.Select(Array.IndexOf(MarkerTransferOptions, parameters.MarkerTransferFunc));
		if (parameters.MarkerTransferFunc == SoilMarkerTransferFunctionPreset.Custom)
			MarkerCustomColorButtom.Show();
		else
			MarkerCustomColorButtom.Hide();

		GetNode<ColorPickerButton>($"FlowMarkers/{CustomColorHiPath}").Color = parameters.Custom_MarkerFullFlow;
		GetNode<ColorPickerButton>($"FlowMarkers/{CustomColorLoPath}").Color = parameters.Custom_MarkerNoFlow;
		#endregion

		#region SOIL CELLS
		var cellTransferNode = GetNode<OptionButton>($"FlowMarkers/{TransferFunctionPath}");
		foreach(var item in CellTransferOptions)
			cellTransferNode.AddItem(item.ToString());

		CellCustomColorNode = GetNode<Control>($"SoilCells/{CustomColorPath}");

		GetNode<CheckButton>("SoilCells/VisibilityButton").ButtonPressed = IsVisible(parameters.SoilCellsVisibility);
		GetNode<HSlider>($"SoilCells/{ScaleSliderPath}").Value = parameters.SoilCellScale;

		GetNode<CheckButton>($"SoilCells/{AnimateSizeButtonPath}").ButtonPressed = parameters.AnimateSoilCellSize;
		GetNode<CheckButton>($"SoilCells/{AnimateColorButtonPath}").ButtonPressed = parameters.AnimateSoilCellColor;

		cellTransferNode.Select(Array.IndexOf(CellTransferOptions, parameters.CellTransferFunc));
		if (parameters.CellTransferFunc == SoilCellTransferFunctionPreset.Custom)
			CellCustomColorNode.Show();
		else
			CellCustomColorNode.Hide();

		GetNode<ColorPickerButton>($"SoilCells/{CustomColorHiPath}").Color = parameters.Custom_CellFull;
		GetNode<ColorPickerButton>($"SoilCells/{CustomColorLoPath}").Color = parameters.Custom_CellEmpty;
		#endregion

		#region SURFACE CELLS
		var surfaceTransferNode = GetNode<OptionButton>($"SurfaceCells/{TransferFunctionPath}");
		foreach(var item in SurfaceTransferOptions)
			surfaceTransferNode.AddItem(item.ToString());

		SurfaceCustomColorNode = GetNode<Control>($"SurfaceCells/{CustomColorPath}");

		GetNode<CheckButton>("SurfaceCells/VisibilityButton").ButtonPressed = IsVisible(parameters.SurfaceCellsVisibility);
		GetNode<HSlider>($"SurfaceCells/{ScaleSliderPath}").Value = Math.Sqrt(parameters.SurfaceFullThreshold);

		GetNode<CheckButton>($"SurfaceCells/{AnimateSizeButtonPath}").ButtonPressed = parameters.AnimateSurfaceCellSize;
		GetNode<CheckButton>($"SurfaceCells/{AnimateColorButtonPath}").ButtonPressed = parameters.AnimateSurfaceCellColor;

		surfaceTransferNode.Select(Array.IndexOf(SurfaceTransferOptions, parameters.SurfaceTransferFunc));
		if (parameters.SurfaceTransferFunc == SurfaceCellTransferFunctionPreset.Custom)
			SurfaceCustomColorNode.Show();
		else
			SurfaceCustomColorNode.Hide();

		GetNode<ColorPickerButton>($"SurfaceCells/{CustomColorHiPath}").Color = parameters.Custom_SurfaceFull;
		GetNode<ColorPickerButton>($"SurfaceCells/{CustomColorLoPath}").Color = parameters.Custom_SurfaceEmpty;
		#endregion


		GetNode<Button>("Ground/VisibilityButton").ButtonPressed = parameters.GroundVisible;
		if (parameters.GroundVisible)
			Ground.Show();
		else
			Ground.Hide();
	}

	static Visibility Vis(bool flag) => flag ? Visibility.MakeVisible : Visibility.MakeInvisible;

	public void AllMarkersVisibility(bool flag)
	{
		Parameters.MarkerVisibility = Vis(flag);
		UpdateRequest = true;
		if (IsVisible(Parameters.MarkerVisibility))
			for(int i = 0; i < DirMarkerVisibility.Count; ++i)
				DirMarkerVisibility[i].Disabled = false;
		else
			for(int i = 0; i < DirMarkerVisibility.Count; ++i)
				DirMarkerVisibility[i].Disabled = true;
	}

	public void XplusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[0] = Vis(flag);
		UpdateRequest = true;
	}

	public void XminusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[3] = Vis(flag);
		UpdateRequest = true;
	}

	public void YplusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[1] = Vis(flag);
		UpdateRequest = true;
	}

	public void YminusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[4] = Vis(flag);
		UpdateRequest = true;
	}

	public void ZplusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[5] = Vis(flag);
		UpdateRequest = true;
	}

	public void ZminusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[2] = Vis(flag);
		UpdateRequest = true;
	}

	public void SurfaceCellsVisibility(bool flag)
	{
		Parameters.SurfaceCellsVisibility = Vis(flag);
		UpdateRequest = true;
	}

	public void CellsVisibility(bool flag)
	{
		Parameters.SoilCellsVisibility = Vis(flag);
		UpdateRequest = true;
	}

	public void AnimateSurfaceCellSize(bool flag)
	{
		Parameters.AnimateSurfaceCellSize = flag;
		UpdateRequest = true;
	}

	public void AnimateSurfaceCellColor(bool flag)
	{
		Parameters.AnimateSurfaceCellColor = flag;
		UpdateRequest = true;
	}
	public void AnimateCellSize(bool flag)
	{
		Parameters.AnimateSoilCellSize = flag;
		UpdateRequest = true;
	}

	public void AnimateCellColor(bool flag)
	{
		Parameters.AnimateSoilCellColor = flag;
		UpdateRequest = true;
	}

	public void AnimateMarkerSize(bool flag)
	{
		Parameters.AnimateMarkerSize = flag;
		UpdateRequest = true;
	}

	public void AnimateMarkerColor(bool flag)
	{
		Parameters.AnimateMarkerColor = flag;
		UpdateRequest = true;
	}

	public void SurfaceCellSize(float value)
	{
		Parameters.SurfaceFullThreshold = value * value;
		UpdateRequest = true;
	}

	public void CellSize(float value)
	{
		Parameters.SoilCellScale = value;
		UpdateRequest = true;
	}

	public void MarkerSize(float value)
	{
		Parameters.MarkerScale = value;
		UpdateRequest = true;
	}

	public void CellTransferFunction(int index)
	{
		switch(CellTransferOptions[index])
		{
			case SoilCellTransferFunctionPreset.BrownWater:
				Parameters.CellColorLow = SoilVisualisationSettings.BrownWater_CellEmpty;
				Parameters.CellColorHigh = SoilVisualisationSettings.BrownWater_CellFull;
				CellCustomColorNode.Hide();
			break;
			case SoilCellTransferFunctionPreset.BlueWater:
				Parameters.CellColorLow = SoilVisualisationSettings.BlueWater_CellEmpty;
				Parameters.CellColorHigh = SoilVisualisationSettings.BlueWater_CellFull;
				CellCustomColorNode.Hide();
			break;
			default:
				Parameters.CellColorLow = Parameters.Custom_CellEmpty;
				Parameters.CellColorHigh = Parameters.Custom_CellFull;
				CellCustomColorNode.Show();
			break;
		}
		UpdateRequest = true;
	}

	public void CellColorLow(Color color)
	{
		Parameters.CellColorLow = color;
		UpdateRequest = true;
	}

	public void CellColorHigh(Color color)
	{
		Parameters.CellColorHigh = color;
		UpdateRequest = true;
	}

	public void SurfaceCellTransferFunction(int index)
	{
		switch(SurfaceTransferOptions[index])
		{
			case SurfaceCellTransferFunctionPreset.BlueWater:
				Parameters.SurfaceEmptyColor = SoilVisualisationSettings.BlueWater_SurfaceEmpty;
				Parameters.SurfaceFullColor = SoilVisualisationSettings.BlueWater_SurfaceFull;
				SurfaceCustomColorNode.Hide();
			break;
			default:
				Parameters.SurfaceEmptyColor = Parameters.Custom_SurfaceEmpty;
				Parameters.SurfaceFullColor = Parameters.Custom_SurfaceFull;
				SurfaceCustomColorNode.Show();
			break;
		}
		UpdateRequest = true;
	}

	public void SurfaceCellEmptyColor(Color color)
	{
		Parameters.SurfaceEmptyColor = color;
		UpdateRequest = true;
	}

	public void SurfaceCellFullColor(Color color)
	{
		Parameters.SurfaceFullColor = color;
		UpdateRequest = true;
	}

	public void MarkerTransferFunction(int index)
	{
		switch(MarkerTransferOptions[index])
		{
			case SoilMarkerTransferFunctionPreset.BrownWater:
				Parameters.MarkerNoFlowColor = SoilVisualisationSettings.BrownWater_MarkerNoFlow;
				Parameters.MarkerFullFlowColor = SoilVisualisationSettings.BrownWater_MarkerFullFlow;
				MarkerCustomColorButtom.Hide();
			break;
			case SoilMarkerTransferFunctionPreset.BlueWater:
				Parameters.MarkerNoFlowColor = SoilVisualisationSettings.BlueWater_MarkerNoFlow;
				Parameters.MarkerFullFlowColor = SoilVisualisationSettings.BlueWater_MarkerFullFlow;
				MarkerCustomColorButtom.Hide();
			break;
			default:
				Parameters.MarkerNoFlowColor = Parameters.Custom_MarkerNoFlow;
				Parameters.MarkerFullFlowColor = Parameters.Custom_MarkerFullFlow;
				MarkerCustomColorButtom.Show();
			break;
		}
		UpdateRequest = true;
	}

	public void MarkerFullColor(Color color)
	{
		Parameters.MarkerFullFlowColor = color;
		UpdateRequest = true;
	}

	public void MarkerEmptyColor(Color color)
	{
		Parameters.MarkerNoFlowColor = color;
		UpdateRequest = true;
	}

	public void MenuEntered() => MenuEvent = MenuEvent.Enter;

	public void MenuLeft(bool dummy = false) => MenuEvent = MenuEvent.Leave;

	public void ColorOpen() => ColorEvent = MenuEvent.Enter;

	public void ColorClosed() => ColorEvent = MenuEvent.Leave;

	public void GroundVisibility(bool flag)
	{
		Parameters.GroundVisible = flag;
		if (Parameters.GroundVisible)
			Ground.Show();
		else
			Ground.Hide();
	}
}

