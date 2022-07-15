using Godot;
using System;

public class SoilVisualisationSettings{
    public bool Visualise = true;
    public bool MarkersVisible = true;
    public bool SoilCellsVisible = true;


    public float CellCapacity = 1; //Todo: Temporary solution, this attribute should be accessible from the simulation itself
    
    public Color FullColor = new Color(0f,0f,1f);
    public Color EmptyColor = new Color(1f,0f,0f);

    public Mesh SoilCellShape = GeoBuilder.UnitCube();// Todo has to be changed this shape isnt unit sized
    public Mesh MarkerShape = GeoBuilder.UnitPyramid4();//Also temp


    public float SoilCellScale = 0.25f;
    public float MarkerScale = 0.5f;
    
}