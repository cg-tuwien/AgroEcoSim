using System;
using System.Numerics;
using System.Collections.Generic;
using AgentsSystem;
using Utils;

namespace SimpleSoil;
public static class SimpleSoilSettings{
    public static int TicksPerHour = 5;
    public static ushort[] Dimensions = new ushort[]{4,2,4}; //Values in N\{0}
    public static ushort SplitsPerMeter = 1; //Ammount of voxels in one m^3 is SplitsPerMeter^3
    public static float MaxWaterMultiplier = 0.4f;
    public static float WaterContentForDiffusion = 0.1f;
    public static float LateralDiffusionCoefficient = 0.2f;
    public static float DownwardDiffusionCoefficient = 0.8f;
    public static float DiffusionPerHour = 0.2f;
}

public static class SimpleSoilUtility{
    public static Vector3i[] NeighborDirections = new Vector3i[]{
        new(1,0,0),
        new(0,1,0),
        new(0,0,1),
        new(-1,0,0),
        new(0,-1,0),
        new(0,0,-1)
    };
}