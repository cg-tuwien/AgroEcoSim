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
	public MeshInstance[] SoilCellInstances;

	public MeshInstance[,] MarkerInstances;

	private List<MarkerData> MarkerDataStorage;

	public override void GodotReady()
	{
		if(Parameters.Visualise) //Todo: Check whether the previous condition wasn't dependent on something besides this file
			InitializeVisualisation();
	}

	public override void GodotProcess(uint timestep)
	{
		if(Parameters.Visualise)
		{
			SolveVisibility();
			if(Parameters.MarkerVisibility == Visibility.Waiting) AnimateMarkers();
			if(Parameters.SoilCellsVisibility == Visibility.Waiting) AnimateCells();
		}
	}
}
