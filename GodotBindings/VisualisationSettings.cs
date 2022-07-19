using Godot;
using System;

enum marker_mode{steam,water,sum};

public enum visibility{visible,invisible,visible_waiting,invisible_waiting}; //Logic is that when is the state changed to visible/invisible all components are affected and the state is then set to waiting

public class SoilVisualisationSettings{
    public bool Visualise = true; //This can turn on/off the entire visualisation

    // VISIBILITY SETTINGS
    public visibility MarkerVisibility = visibility.visible_waiting;
    public visibility[] IndividualMarkerDirectionVisibility = new visibility[]{
        visibility.visible_waiting, //X+
        visibility.visible_waiting, //Y+
        visibility.visible_waiting, //Z+
        visibility.visible_waiting, //X-
        visibility.visible_waiting, //Y-
        visibility.visible_waiting //Z-
        };
    public visibility SoilCellsVisibility = visibility.visible_waiting;

    // ANIMATION VISIBILITY SETTINGS
    public bool AnimateSoilCellSize = true;
    public bool AnimateMarkerSize = false;
    public bool AnimateSoilCellColor = true;
    public bool AnimateDownwardMarkerColor = true;
    public bool AnimateLateralMarkerColor = true;

    // MATERIAL SETTINGS
    public SpatialMaterial SoilCellMaterial = new SpatialMaterial {AlbedoColor = new(0.5f,0.5f,0.5f)};

    public SpatialMaterial MarkerMaterial = new SpatialMaterial {AlbedoColor = new(0f,0.2f,0.6f)};

    // COLOR SETTINGS
    public Color FullCellColor = new(71f/255f,39f/255f,11f/255f);
    public Color EmptyCellColor = new(148f/255f,143f/255f,130f/255f);

    public Color NoFlowColor = new(0.2f,0.2f,0.2f);
    public Color FullFlowColor = new(0.2f,0.2f,1f);

    // SHAPES (SHOULD BE UNIT-SIZED)
    public Mesh SoilCellShape = GeoBuilder.UnitCube();
    public Mesh MarkerShape = GeoBuilder.UnitPyramid4();

    // SCALE SETTINGS
    public float SoilCellScale = 0.25f;
    public float MarkerScale = 0.75f;

}