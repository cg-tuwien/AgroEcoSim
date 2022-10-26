using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;

public static class AgroWorldGodot
{
	public static SoilVisualisationSettings SoilVisualization = new()
	{
		CellTransferFunc = SoilCellTransferFunctionPreset.BlueWater,
		MarkerTransferFunc = SoilMarkerTransferFunctionPreset.BlueWater,
		MarkerVisibility = Visibility.Invisible,
		AnimateMarkerSize = true,
		SoilCellScale = 0.99f
	};
}