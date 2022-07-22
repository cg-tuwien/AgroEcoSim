using Godot;
using System;

enum marker_mode{Steam,Water,Sum};

public enum visibility{Visible,Invisible,VisibleWaiting,InivisibleWaiting}; //Waiting state is used to prevent visibility changes once set to desired visibility

public class SoilVisualisationSettings{
    public bool Visualise = true; //This can turn on/off the entire visualisation

    // VISIBILITY SETTINGS
    public visibility MarkerVisibility = visibility.VisibleWaiting;
    public visibility[] IndividualMarkerDirectionVisibility = new visibility[]{
        visibility.VisibleWaiting, //X+
        visibility.VisibleWaiting, //Y+
        visibility.VisibleWaiting, //Z+
        visibility.VisibleWaiting, //X-
        visibility.VisibleWaiting, //Y-
        visibility.VisibleWaiting //Z-
        };
    public visibility SoilCellsVisibility = visibility.VisibleWaiting;

    // ANIMATION VISIBILITY SETTINGS
    public bool AnimateSoilCellSize = true;
    public bool AnimateMarkerSize = false;
    public bool AnimateSoilCellColor = true;

    public bool AnimateMarkerColor = true;
    // public bool AnimateDownwardMarkerColor = true;
    // public bool AnimateLateralMarkerColor = true;

    // MATERIAL SETTINGS
    public SpatialMaterial SoilCellMaterial = new SpatialMaterial {AlbedoColor = new(0.5f,0.5f,0.5f)};

    public SpatialMaterial MarkerMaterial = new SpatialMaterial {AlbedoColor = new(0f,0.2f,0.6f)};

    // COLOR SETTINGS
    public Color FullCellColor = new(57f/255f,37f/255f,29f/255f);
    public Color EmptyCellColor = new(124/255f,124f/255f,124f/255f);

    public Color NoFlowColor = new(173f/255f,160f/255f,139f/255f);
    public Color FullFlowColor = new(40f/255f,101f/255f,179f/255f);

    // SHAPES (SHOULD BE UNIT-SIZED)
    public Mesh SoilCellShape = GeoBuilder.UnitCube();
    public Mesh MarkerShape = GeoBuilder.UnitPyramid4();

    // SCALE SETTINGS
    public float SoilCellScale = 0.5f;
    public float MarkerScale = 0.5f;

}