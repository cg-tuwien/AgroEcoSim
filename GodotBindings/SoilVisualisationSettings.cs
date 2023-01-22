using Godot;
using System;

enum MarkerMode : byte { Steam, Water, Sum };

//Make... states are used to prevent visibility changes every frame
public enum Visibility : byte { Visible, Invisible, MakeVisible, MakeInvisible };

public enum SurfaceCellTransferFunctionPreset : byte { Custom, BlueWater };
public enum SoilCellTransferFunctionPreset : byte { Custom, BrownWater, BlueWater };
public enum SoilMarkerTransferFunctionPreset : byte { Custom, BrownWater, BlueWater };

public class SoilVisualisationSettings
{
    public bool Visualise = true; //This can turn on/off the entire visualisation

    #region VISIBILITY SETTINGS
    public Visibility MarkerVisibility = Visibility.MakeVisible;
    public Visibility[] IndividualMarkerDirectionVisibility = new Visibility[] {
        Visibility.MakeVisible, //X+
        Visibility.MakeVisible, //Y+
        Visibility.MakeVisible, //Z+
        Visibility.MakeVisible, //X-
        Visibility.MakeVisible, //Y-
        Visibility.MakeVisible //Z-
    };
    public Visibility SoilCellsVisibility = Visibility.MakeVisible;
    public Visibility SurfaceCellsVisibility = Visibility.MakeVisible;
    public bool GroundVisible = false;
    #endregion

    #region ANIMATION VISIBILITY SETTINGS
    public bool AnimateSurfaceCellSize = true;
    public bool AnimateSoilCellSize = true;

    public bool AnimateSurfaceCellColor = true;
    public bool AnimateSoilCellColor = true;

    public bool AnimateMarkerSize = false;
    public bool AnimateMarkerColor = true;
    #endregion

    #region MATERIAL SETTINGS
    public StandardMaterial3D SoilCellMaterial = new() { AlbedoColor = new(0.5f, 0.5f, 0.5f) };
    public StandardMaterial3D SurfaceCellMaterial = new() { AlbedoColor = new(0.5f, 0.5f, 0.5f) };

    public StandardMaterial3D MarkerMaterial = new() { AlbedoColor = new(0f, 0.2f, 0.6f) };
    #endregion

    #region COLOR SETTINGS
    public static readonly Color BrownWater_CellFull = new(71f/255f, 39f/255f, 11f/255f);
    public static readonly Color BrownWater_CellEmpty = new(148f/255f, 143f/255f, 130f/255f);

    public static readonly Color BrownWater_MarkerNoFlow = Colors.Black;
    public static readonly Color BrownWater_MarkerFullFlow = Colors.White;

    public static readonly Color BlueWater_CellFull = Colors.Blue;
    public static readonly Color BlueWater_CellEmpty = Colors.Black;

    public static readonly Color BlueWater_SurfaceFull = Colors.Blue;
    public static readonly Color BlueWater_SurfaceEmpty = Colors.Black;

    public static readonly Color BlueWater_MarkerNoFlow = Colors.Black;
    public static readonly Color BlueWater_MarkerFullFlow = Colors.Blue;

    public Color Custom_SurfaceFull = BrownWater_CellFull;
    public Color Custom_SurfaceEmpty = BrownWater_CellEmpty;

    public Color Custom_CellFull = BrownWater_CellFull;
    public Color Custom_CellEmpty = BrownWater_CellEmpty;

    public Color Custom_MarkerNoFlow = BrownWater_MarkerNoFlow;
    public Color Custom_MarkerFullFlow = BrownWater_MarkerFullFlow;

    SurfaceCellTransferFunctionPreset mSurfaceTransferFunc = SurfaceCellTransferFunctionPreset.BlueWater;
    public SurfaceCellTransferFunctionPreset SurfaceTransferFunc
    {
        get => mSurfaceTransferFunc;
        set
        {
            mSurfaceTransferFunc = value;
            switch (mSurfaceTransferFunc)
            {
                case SurfaceCellTransferFunctionPreset.BlueWater:
                    SurfaceFullColor = BlueWater_SurfaceFull;
                    SurfaceEmptyColor = BlueWater_SurfaceEmpty;
                    break;
                default:
                    SurfaceFullColor = Custom_SurfaceFull;
                    SurfaceEmptyColor = Custom_SurfaceEmpty;
                    break;
            }
        }
    }

    SoilCellTransferFunctionPreset mCellTransferFunc = SoilCellTransferFunctionPreset.BrownWater;
    public SoilCellTransferFunctionPreset CellTransferFunc
    {
        get => mCellTransferFunc;
        set
        {
            mCellTransferFunc = value;
            switch (mCellTransferFunc)
            {
                case SoilCellTransferFunctionPreset.BrownWater:
                    CellFullColor = BrownWater_CellFull;
                    CellEmptyColor = BrownWater_CellEmpty;
                    break;
                case SoilCellTransferFunctionPreset.BlueWater:
                    CellFullColor = BlueWater_CellFull;
                    CellEmptyColor = BlueWater_CellEmpty;
                    break;
                default:
                    CellFullColor = Custom_CellFull;
                    CellEmptyColor = Custom_CellEmpty;
                    break;
            }
        }
    }

    SoilMarkerTransferFunctionPreset mMarkerTransferFunc = SoilMarkerTransferFunctionPreset.BrownWater;
    public SoilMarkerTransferFunctionPreset MarkerTransferFunc
    {
        get => mMarkerTransferFunc;
        set
        {
            mMarkerTransferFunc = value;
            switch (mMarkerTransferFunc)
            {
                case SoilMarkerTransferFunctionPreset.BrownWater:
                    MarkerNoFlowColor = BrownWater_MarkerNoFlow;
                    MarkerFullFlowColor = BrownWater_MarkerFullFlow;
                    break;
                case SoilMarkerTransferFunctionPreset.BlueWater:
                    MarkerNoFlowColor = BlueWater_MarkerNoFlow;
                    MarkerFullFlowColor = BlueWater_MarkerFullFlow;
                    break;
                default:
                    MarkerNoFlowColor = Custom_MarkerNoFlow;
                    MarkerFullFlowColor = Custom_MarkerFullFlow;
                    break;
            }
        }
    }

    public Color SurfaceFullColor = BlueWater_SurfaceFull;
    public Color SurfaceEmptyColor = BlueWater_SurfaceEmpty;

    public Color CellFullColor = BrownWater_CellFull;
    public Color CellEmptyColor = BrownWater_CellEmpty;

    public Color MarkerNoFlowColor = BrownWater_MarkerNoFlow;
    public Color MarkerFullFlowColor = BrownWater_MarkerFullFlow;

    #endregion

    #region SHAPES (SHOULD BE UNIT-SIZED)
    public Mesh SoilCellShape = GeoBuilder.UnitCube(verticalCenter: true);
    public Mesh SurfaceCellShape = GeoBuilder.UnitCube(verticalCenter: false);
    public Mesh MarkerShape = GeoBuilder.UnitPyramid4();
    #endregion

    #region SCALE SETTINGS

    public float SoilCellScale = 0.1f;
    public float MarkerScale = 0.75f;
    #endregion

    public float SurfaceFullThreshold = 0.03f;
}
