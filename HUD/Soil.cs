using Godot;
using System.Linq;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public class Soil : CanvasLayer
{
	// [Signal]
	// public delegate void EnteredMenu();

	// [Signal]
	// public delegate void LeftMenu();

	private SoilVisualisationSettings Parameters;
	public bool Paused = false;
	public bool UpdateRequest = false;
	public bool ColorEditorOpen = false;

	public MenuStatus MenuState = MenuStatus.LeftWaiting;

	static readonly SoilMarkerTransferFunctionPreset[] MarkerTransferOptions= (SoilMarkerTransferFunctionPreset[])Enum.GetValues(typeof(SoilMarkerTransferFunctionPreset));
	static readonly SoilCellTransferFunctionPreset[] CellTransferOptions = (SoilCellTransferFunctionPreset[])Enum.GetValues(typeof(SoilCellTransferFunctionPreset));

	Control MarkerCustomColorNode;
	Control CellCustomColorNode;
	GodotGround Ground;

	bool IsVisible(Visibility visibility) => visibility == Visibility.Visible || visibility == Visibility.MakeVisible;
	public void Load(SoilVisualisationSettings parameters, GodotGround ground)
	{
		Parameters = parameters;
		Ground = ground;

		var markerTransferNode = GetNode<OptionButton>("FlowMarkers/Color/ColorCombo");
		foreach(var item in MarkerTransferOptions)
			markerTransferNode.AddItem(item.ToString());

		var cellTransferNode = GetNode<OptionButton>("Cells/Color/ColorCombo");
		foreach(var item in CellTransferOptions)
			cellTransferNode.AddItem(item.ToString());

		MarkerCustomColorNode = GetNode<Control>("FlowMarkers/Color/Custom");
		CellCustomColorNode = GetNode<Control>("Cells/Color/Custom");

		GetNode<CheckButton>("FlowMarkers/Visibility/AllMarkers/CheckButton").Pressed = IsVisible(parameters.MarkerVisibility);
		GetNode<HSlider>("FlowMarkers/Visibility/Scale/HSlider").Value = parameters.MarkerScale;
		GetNode<CheckButton>("FlowMarkers/Visibility/MarkerDirs/X-Button").Pressed = IsVisible(parameters.IndividualMarkerDirectionVisibility[3]);
		GetNode<CheckButton>("FlowMarkers/Visibility/MarkerDirs/X+Button").Pressed = IsVisible(parameters.IndividualMarkerDirectionVisibility[0]);
		GetNode<CheckButton>("FlowMarkers/Visibility/MarkerDirs/Y-Button").Pressed = IsVisible(parameters.IndividualMarkerDirectionVisibility[4]);
		GetNode<CheckButton>("FlowMarkers/Visibility/MarkerDirs/Y+Button").Pressed = IsVisible(parameters.IndividualMarkerDirectionVisibility[1]);
		GetNode<CheckButton>("FlowMarkers/Visibility/MarkerDirs/Z-Button").Pressed = IsVisible(parameters.IndividualMarkerDirectionVisibility[5]);
		GetNode<CheckButton>("FlowMarkers/Visibility/MarkerDirs/Z+Button").Pressed = IsVisible(parameters.IndividualMarkerDirectionVisibility[2]);
		GetNode<CheckButton>("FlowMarkers/Animation/MarkerSize/CheckButton").Pressed = parameters.AnimateMarkerSize;
		GetNode<CheckButton>("FlowMarkers/Animation/MarkerColor/CheckButton").Pressed = parameters.AnimateMarkerColor;

		if (IsVisible(parameters.MarkerVisibility))
			GetNode<Control>("FlowMarkers/Visibility/MarkerDirs").Show();
		else
			GetNode<Control>("FlowMarkers/Visibility/MarkerDirs").Hide();

		GetNode<OptionButton>("FlowMarkers/Color/ColorCombo").Select(Array.IndexOf(MarkerTransferOptions, parameters.MarkerTransferFunc));
		if (parameters.MarkerTransferFunc == SoilMarkerTransferFunctionPreset.Custom)
			MarkerCustomColorNode.Show();
		else
			MarkerCustomColorNode.Hide();

		GetNode<ColorPickerButton>("FlowMarkers/Color/Custom/Full").Color = parameters.MarkerNoFlowColor;
		GetNode<ColorPickerButton>("FlowMarkers/Color/Custom/Empty").Color = parameters.MarkerFullFlowColor;

		GetNode<CheckButton>("Cells/Visibility/CheckButton").Pressed = IsVisible(parameters.SoilCellsVisibility);
		GetNode<HSlider>("Cells/Scale/HSlider").Value = parameters.SoilCellScale;

		GetNode<CheckButton>("Cells/Animation/CellSize/CheckButton").Pressed = parameters.AnimateSoilCellSize;
		GetNode<CheckButton>("Cells/Animation/CellColor/CheckButton").Pressed = parameters.AnimateSoilCellColor;

		GetNode<OptionButton>("Cells/Color/ColorCombo").Select(Array.IndexOf(CellTransferOptions, parameters.CellTransferFunc));
		if (parameters.CellTransferFunc == SoilCellTransferFunctionPreset.Custom)
			CellCustomColorNode.Show();
		else
			CellCustomColorNode.Hide();

		GetNode<ColorPickerButton>("Cells/Color/Custom/Full").Color = parameters.CellFullColor;
		GetNode<ColorPickerButton>("Cells/Color/Custom/Empty").Color = parameters.CellEmptyColor;

		GetNode<Button>("Ground/Visibility/CheckButton").Pressed = parameters.GroundVisible;
		if (parameters.GroundVisible)
			Ground.Show();
		else
			Ground.Hide();
	}

	Visibility Vis(bool flag) => flag ? Visibility.MakeVisible : Visibility.MakeInvisible;

	public void AllMarkersVisibility(bool flag)
	{
		Parameters.MarkerVisibility = Vis(flag);
		UpdateRequest = true;
		if (IsVisible(Parameters.MarkerVisibility))
			GetNode<Control>("FlowMarkers/Visibility/MarkerDirs").Show();
		else
			GetNode<Control>("FlowMarkers/Visibility/MarkerDirs").Hide();
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

	public void CellsVisibility(bool flag)
	{
		Parameters.SoilCellsVisibility = Vis(flag);
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
				Parameters.CellEmptyColor = SoilVisualisationSettings.BrownWater_CellEmpty;
				Parameters.CellFullColor = SoilVisualisationSettings.BrownWater_CellFull;
				CellCustomColorNode.Hide();
			break;
			case SoilCellTransferFunctionPreset.BlueWater:
				Parameters.CellEmptyColor = SoilVisualisationSettings.BlueWater_CellEmpty;
				Parameters.CellFullColor = SoilVisualisationSettings.BlueWater_CellFull;
				CellCustomColorNode.Hide();
			break;
			default:
				Parameters.CellEmptyColor = Parameters.Custom_CellEmpty;
				Parameters.CellFullColor = Parameters.Custom_CellFull;
				CellCustomColorNode.Show();
			break;
		}
		UpdateRequest = true;
	}

	public void CellEmptyColor(Color color)
	{
		Parameters.CellEmptyColor = color;
		UpdateRequest = true;
	}

	public void CellFullColor(Color color)
	{
		Parameters.CellFullColor = color;
		UpdateRequest = true;
	}

	public void MarkerTransferFunction(int index)
	{
		switch(MarkerTransferOptions[index])
		{
			case SoilMarkerTransferFunctionPreset.BrownWater:
				Parameters.MarkerNoFlowColor = SoilVisualisationSettings.BrownWater_MarkerNoFlow;
				Parameters.MarkerFullFlowColor = SoilVisualisationSettings.BrownWater_MarkerFullFlow;
				MarkerCustomColorNode.Hide();
			break;
			case SoilMarkerTransferFunctionPreset.BlueWater:
				Parameters.MarkerNoFlowColor = SoilVisualisationSettings.BlueWater_MarkerNoFlow;
				Parameters.MarkerFullFlowColor = SoilVisualisationSettings.BlueWater_MarkerFullFlow;
				MarkerCustomColorNode.Hide();
			break;
			default:
				Parameters.MarkerNoFlowColor = Parameters.Custom_MarkerNoFlow;
				Parameters.MarkerFullFlowColor = Parameters.Custom_MarkerFullFlow;
				MarkerCustomColorNode.Show();
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

	public void MenuEntered()
	{
		if (MenuState == MenuStatus.LeftWaiting)
			MenuState = MenuStatus.Entered;
	}

	public void MenuLeft()
	{
		if (MenuState == MenuStatus.EnteredWaiting)
			MenuState = MenuStatus.Left;
	}

	public void ColorOpen() => ColorEditorOpen = true;

	public void ColorClosed() => ColorEditorOpen = false;

	public void GroundVisibility(bool flag)
	{
		Parameters.GroundVisible = flag;
		if (Parameters.GroundVisible)
			Ground.Show();
		else
			Ground.Hide();
	}
}

