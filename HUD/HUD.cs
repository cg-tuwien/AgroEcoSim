using Godot;
using Utils;
using System;
using System.Collections.Generic;
using AgentsSystem;
using Agro;

public enum MenuStatus{
	EnteredWaiting,
	LeftWaiting,
	Left,
	Entered
}
public class HUD : CanvasLayer
{
	// [Signal]
	// public delegate void EnteredMenu();

	// [Signal]
	// public delegate void LeftMenu();

	private SoilVisualisationSettings Parameters;
	public bool Paused = false;
	public bool RecentChange = false;
	public bool ColorEditorOpen = false;

	public MenuStatus MenuState = MenuStatus.LeftWaiting;

	public override void _Ready() { }


	public void pause() //must be lower case in order to work with Godot
	{
		Paused = !Paused;
	}

	public void Load(SoilVisualisationSettings parameters)
	{
		Parameters = parameters;

		GetNode<CheckButton>("Visibility/Cells/CheckButton").Pressed = parameters.SoilCellsVisibility == Visibility.VisibleWaiting || parameters.SoilCellsVisibility == Visibility.Visible;
		GetNode<CheckButton>("Visibility/AllMarkers/CheckButton").Pressed = parameters.MarkerVisibility == Visibility.VisibleWaiting || parameters.SoilCellsVisibility == Visibility.Visible;

		GetNode<CheckButton>("Animation/CellSize/CheckButton").Pressed = parameters.AnimateSoilCellSize;
		GetNode<CheckButton>("Animation/CellColor/CheckButton").Pressed = parameters.AnimateSoilCellColor;
		GetNode<CheckButton>("Animation/MarkerSize/CheckButton").Pressed = parameters.AnimateMarkerSize;
		GetNode<CheckButton>("Animation/MarkerColor/CheckButton").Pressed = parameters.AnimateMarkerColor;

		GetNode<HSlider>("Size/Marker/HSlider").Value = parameters.MarkerScale;
		GetNode<HSlider>("Size/Cell/HSlider").Value = parameters.SoilCellScale;

		GetNode<ColorPickerButton>("Color/Cell/Full").Color = parameters.FullCellColor;
		GetNode<ColorPickerButton>("Color/Cell/Empty").Color = parameters.EmptyCellColor;
		GetNode<ColorPickerButton>("Color/Marker/Full").Color = parameters.NoFlowColor;
		GetNode<ColorPickerButton>("Color/Marker/Empty").Color = parameters.FullFlowColor;
	}

	public void AllMarkersVisibility(bool flag)
	{
		Parameters.MarkerVisibility = flag ? Visibility.Visible : Visibility.Invisible;
		RecentChange = true;
	}

	public void XplusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[0] = flag ? Visibility.Visible : Visibility.Invisible;
		RecentChange = true;
	}

	public void XminusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[3] = flag ? Visibility.Visible : Visibility.Invisible;
		RecentChange = true;
	}

	public void YplusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[1] = flag ? Visibility.Visible : Visibility.Invisible;
		RecentChange = true;
	}

	public void YminusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[4] = flag ? Visibility.Visible : Visibility.Invisible;
		RecentChange = true;
	}

	public void ZplusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[5] = flag ? Visibility.Visible : Visibility.Invisible;
		RecentChange = true;
	}

	public void ZminusMarkersVisibility(bool flag)
	{
		Parameters.IndividualMarkerDirectionVisibility[2] = flag ? Visibility.Visible : Visibility.Invisible;
		RecentChange = true;
	}

	public void CellsVisibility(bool flag)
	{
		Parameters.SoilCellsVisibility = flag ? Visibility.Visible : Visibility.Invisible;
		RecentChange = true;
	}

	public void AnimateCellSize(bool flag)
	{
		Parameters.AnimateSoilCellSize = flag;
		RecentChange = true;
	}

	public void AnimateCellColor(bool flag)
	{
		Parameters.AnimateSoilCellColor = flag;
		RecentChange = true;
	}

	public void AnimateMarkerSize(bool flag)
	{
		Parameters.AnimateMarkerSize = flag;
		RecentChange = true;
	}

	public void AnimateMarkerColor(bool flag)
	{
		Parameters.AnimateMarkerColor = flag;
		RecentChange = true;
	}

	public void CellSize(float value)
	{
		Parameters.SoilCellScale = value;
		RecentChange = true;
	}

	public void MarkerSize(float value)
	{
		Parameters.MarkerScale = value;
		RecentChange = true;
	}

	public void CellEmptyColor(Color color)
	{
		Parameters.EmptyCellColor = color;
		RecentChange = true;
	}

	public void CellFullColor(Color color)
	{
		Parameters.FullCellColor = color;
		RecentChange = true;
	}

	public void MarkerFullColor(Color color)
	{
		Parameters.FullFlowColor = color;
		RecentChange = true;
	}

	public void MarkerEmptyColor(Color color)
	{
		Parameters.NoFlowColor = color;
		RecentChange = true;
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
}
