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

    public override void _Ready()
    {
        
    }


    public void pause(){
        Paused = !Paused;
    }

    public void Load(SoilVisualisationSettings parameters){
        Parameters = parameters;
        parameters.FullCellColor = GetNode<ColorPickerButton>("Color/Cell/Full").Color;
        parameters.EmptyCellColor = GetNode<ColorPickerButton>("Color/Cell/Empty").Color;
        parameters.NoFlowColor = GetNode<ColorPickerButton>("Color/Marker/Full").Color;
        parameters.FullFlowColor = GetNode<ColorPickerButton>("Color/Marker/Empty").Color;
    }

    public void AllMarkersVisibility(bool flag){
        Parameters.MarkerVisibility = flag ? visibility.Visible : visibility.Invisible;
        RecentChange = true;
        // GD.Print("asdas");
    }

    public void XplusMarkersVisibility(bool flag){
        Parameters.IndividualMarkerDirectionVisibility[0] = flag ? visibility.Visible : visibility.Invisible;
        RecentChange = true;
    }

    public void XminusMarkersVisibility(bool flag){
        Parameters.IndividualMarkerDirectionVisibility[3] = flag ? visibility.Visible : visibility.Invisible;
        RecentChange = true;
    }

    public void YplusMarkersVisibility(bool flag){
        Parameters.IndividualMarkerDirectionVisibility[1] = flag ? visibility.Visible : visibility.Invisible;
        RecentChange = true;
    }

    public void YminusMarkersVisibility(bool flag){
        Parameters.IndividualMarkerDirectionVisibility[4] = flag ? visibility.Visible : visibility.Invisible;
        RecentChange = true;
    }

    public void ZplusMarkersVisibility(bool flag){
        Parameters.IndividualMarkerDirectionVisibility[5] = flag ? visibility.Visible : visibility.Invisible;
        RecentChange = true;
    }

    public void ZminusMarkersVisibility(bool flag){
        Parameters.IndividualMarkerDirectionVisibility[2] = flag ? visibility.Visible : visibility.Invisible;
        RecentChange = true;
    }

    public void CellsVisibility(bool flag){
        Parameters.SoilCellsVisibility = flag ? visibility.Visible : visibility.Invisible;
        RecentChange = true;
    }

    public void AnimateCellSize(bool flag){
        Parameters.AnimateSoilCellSize = flag;
        RecentChange = true;
    }

    public void AnimateCellColor(bool flag){
        Parameters.AnimateSoilCellColor = flag;
        RecentChange = true;
    }

    public void AnimateMarkerSize(bool flag){
        Parameters.AnimateMarkerSize = flag;
        RecentChange = true;
    }

    public void AnimateMarkerColor(bool flag){
        Parameters.AnimateMarkerColor = flag;
        RecentChange = true;
    }

    public void CellSize(float value){
        Parameters.SoilCellScale = value;
        RecentChange = true;
    }

    public void MarkerSize(float value){
        Parameters.MarkerScale = value;
        RecentChange = true;
    }

    public void CellEmptyColor(Color color){
        Parameters.EmptyCellColor = color;
        RecentChange = true;
    }

    public void CellFullColor(Color color){
        Parameters.FullCellColor = color;
        RecentChange = true;
    }

    public void MarkerFullColor(Color color){
        Parameters.FullFlowColor = color;
        RecentChange = true;
    }

    public void MarkerEmptyColor(Color color){
        Parameters.NoFlowColor = color;
        RecentChange = true;
    }

    public void MenuEntered(){
        if(MenuState == MenuStatus.LeftWaiting)
            MenuState = MenuStatus.Entered;
    }

    public void MenuLeft(){
        if(MenuState == MenuStatus.EnteredWaiting)
            MenuState = MenuStatus.Left;
    }

    public void ColorOpen(){
        ColorEditorOpen = true;
    }

    public void ColorClosed(){
        ColorEditorOpen = false;
    }
}
