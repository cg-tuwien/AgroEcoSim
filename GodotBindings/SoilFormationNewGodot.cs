using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;

public partial class SoilFormationNew
{
	//Todo: Speed of the visualisation could be improved by replacing Mesh with MultiMesh (leveraging identical geo)!!!
	public MeshInstance3D[] SoilCellInstances;

	public void GodotReady()
	{
		InitializeCells();
		//InitializeMarkers();
	}

	public void GodotProcess()
	{
		if (AgroWorldGodot.SoilVisualization.Visualise)
		{
			// ApplyMarkerVisibility();
			// if (AgroWorldGodot.SoilVisualization.MarkerVisibility == Visibility.Visible
			//  && AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility.Any(v => v == Visibility.Visible))
			// 	AnimateMarkers();

			ApplyCellVisibility();
			if (AgroWorldGodot.SoilVisualization.SoilCellsVisibility == Visibility.Visible)
				AnimateCells();
		}
	}
}
