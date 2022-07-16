using Godot;
using System;


public enum visibility{visible,invisible,visible_waiting,invisible_waiting}; //Logic is that when is the state changed to visible/invisible all components are affected and the state is then set to waiting

public class SoilVisualisationSettings{
    public bool Visualise = true; //This can turn on/off the entire visualisation
    public visibility MarkerVisibility = visibility.invisible; //waiting because meshinstances are initialized as visible
    public visibility SoilCellsVisibility = visibility.visible_waiting;

    public bool AnimateSoilCellSize = true;
    public bool AnimateMarkerSize = true;
    public bool AnimateSoilCellColor = true;
    public bool AnimateMarkerColor = true;


    public float CellCapacity = 15; //Todo: Temporary solution, this attribute should be accessible from the simulation itself
    
    // public SpatialMaterial SoilCellMaterial = new SpatialMaterial {AlbedoColor = new Color(0.4f,0.3f,0.1f)};
    public SpatialMaterial SoilCellMaterial = new SpatialMaterial {FlagsUnshaded = true};

    public SpatialMaterial MarkerMaterial = new SpatialMaterial {AlbedoColor = new Color(0f,0.8f,0f)};

    public Color FullCellColor = new Color(0f,0f,1f);
    public Color EmptyCellColor = new Color(1f,0f,0f);

    public Color FullFloColor = new Color(0f,1f,0f);
    public Color NoFlowColor = new Color(1f,0f,0f);

    public Mesh SoilCellShape = GeoBuilder.UnitCube();// Todo has to be changed this shape isnt unit sized
    public Mesh MarkerShape = GeoBuilder.UnitPyramid4();//Also temp


    public float SoilCellScale = 0.25f;
    public float MarkerScale = 0.3f;
    
}