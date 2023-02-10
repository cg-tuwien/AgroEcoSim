using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using AgentsSystem;
using glTFLoader.Schema;
using NumericHelpers;
using Utils;

namespace Agro;

public class PlantSettings
{
    public float StdLeafLength; //TODO { get; init; } with .NET6.0
    public float StdLeafRadius;
    public float StdPetioleLength;
    public float StdPetioleRadius;
    public float StdRootRadiusGrowthPerH;
    public float StdRootLengthGrowthPerH;
    public float StdLeafLengthGrowthPerH;

    public float StdHeight;

    public int StdFirstFruitHour;

    public static PlantSettings Avocado;

    static PlantSettings()
    {
        //Just gueesing
        Avocado = new() {
            StdLeafLength = 0.2f,
            StdLeafRadius = 0.04f,
            StdPetioleLength = 0.05f,
            StdPetioleRadius = 0.007f,
            StdRootLengthGrowthPerH = 0.023148148f,
            StdRootRadiusGrowthPerH = 0.00297619f,
            StdLeafLengthGrowthPerH = 0.0001f,
            StdHeight = 12f,
            StdFirstFruitHour = 113952,
        };
    }
}