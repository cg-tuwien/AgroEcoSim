using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;

public partial class SoilFormation
{
	SoilVisualisationSettings Parameters = new()
	{
		FullCellColor = Colors.Blue,
		EmptyCellColor = Colors.Black,
		FullFlowColor = Colors.Blue,
		NoFlowColor = Colors.Black,
		AnimateMarkerSize = true
	};

	//Todo: Speed of the visualisation could be improved by replacing Mesh with MultiMesh (leveraging identical geo)!!!
	[Newtonsoft.Json.JsonIgnore]
	public MeshInstance[] SoilCellInstances;

	[Newtonsoft.Json.JsonIgnore]
	public MeshInstance[,] MarkerInstances;

	[Newtonsoft.Json.JsonIgnore]
	private List<MarkerData> MarkerDataStorage;

	// float[] FlowTracking;

	public override void GodotReady()
	{
		InitializeCells();
		InitializeMarkers();
	}

	public override void GodotProcess()
	{
		if (AgroWorldGodot.SoilVisualization.Visualise)
		{
			ApplyMarkerVisibility();
			if (AgroWorldGodot.SoilVisualization.MarkerVisibility == Visibility.Visible
			 && AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility.Any(v => v == Visibility.Visible))
				AnimateMarkers();

			ApplyCellVisibility();
			if (AgroWorldGodot.SoilVisualization.SoilCellsVisibility == Visibility.Visible)
				AnimateCells();
		}
	}
}
