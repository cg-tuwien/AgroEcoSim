using Godot;
using System;

public class ShootsVisualisationSettings
{
    public bool Visualise = true; //This can turn on/off the entire visualisation

    public Visibility StemsVisibility = Visibility.MakeVisible;
    public Visibility LeafsVisibility = Visibility.MakeVisible;
    public Visibility BudsVisibility = Visibility.MakeInvisible;

    public SpatialMaterial Material = new() { AlbedoColor = new(0.5f, 0.5f, 0.5f) };


    #region COLOR SETTINGS
    public static Color Segment_NaturalWood = new(93f/255f, 63/255f, 1f/255f);
    public static Color Segment_NaturalLeaf = new(1f/255f, 153/255f, 52f/255f);

    public Agro.ColorCodingType TransferFunc = Agro.ColorCodingType.Natural;

    public float LightCutOff = 1e-3f;
    #endregion

    #region SHAPES (SHOULD BE UNIT-SIZED)

    #endregion
}
