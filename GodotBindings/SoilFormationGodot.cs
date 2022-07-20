using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;


public partial class SoilFormation
{
	public SoilVisualisationSettings Parameters = new()
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

	// float[] FlowTracking;

	public override void GodotReady()
	{
		if(Parameters.Visualise){ //Todo: Check whether the previous condition wasn't dependent on something besides this file
			InitializeVisualisation();
		}

		// SetMarkersVisibility(false);
	}

	public override void GodotProcess(uint timestep)
	{
		if(Parameters.Visualise)
		{
			SolveVisibility();
			//if(Parameters.MarkerVisibility == visibility.VisibleWaiting) 
			AnimateMarkers();
			//if(Parameters.SoilCellsVisibility == visibility.VisibleWaiting) 
			AnimateCells();
		}
	}
}
