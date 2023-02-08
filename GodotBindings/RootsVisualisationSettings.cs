using Godot;
using System;

public class RootsVisualisationSettings
{
    public bool Visualise = true; //This can turn on/off the entire visualisation

    public Visibility RootsVisibility = Visibility.MakeVisible;

    public StandardMaterial3D Material = new() { AlbedoColor = new(0.5f, 0.5f, 0.5f) };

    #region COLOR SETTINGS
    public static Color Segment_Light = new(133f/255f, 121/255f, 106f/255f);
    public static Color Segment_Dark = new(60f/255f, 38/255f, 10f/255f);

    public Agro.ColorCodingType TransferFunc = Agro.ColorCodingType.Default;
    public bool IsUnshaded = true;
    #endregion

    #region SHAPES (SHOULD BE UNIT-SIZED)

    #endregion
}
