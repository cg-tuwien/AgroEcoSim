using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;


public partial class SoilFormation
{
	SoilVisualisationSettings parameters = new SoilVisualisationSettings();
	

	//Todo: Speed of the visualisation could be improved by replacing Mesh with MultiMesh (leveraging identical geo)!!!
	MeshInstance[] SoilCellInstances;

	MeshInstance[,] MarkerInstances;

	List<MarkerData> MarkerDataStorage;


	float[] FlowTracking;

	public override void GodotReady()
	{
		if(parameters.Visualise){ //Todo: Check whether the previous condition wasn't dependent on something besides this file
			InitializeVisualisation();
		}

		// parameters.CellCapacity = Agro.SoilAgent.

	}

	public override void GodotProcess(uint timestep)
	{
		if(parameters.Visualise){
			SolveVisibility();
			if(parameters.MarkerVisibility == visibility.visible_waiting) AnimateMarkers();
			if(parameters.SoilCellsVisibility == visibility.visible_waiting) AnimateCells();
		}
	}
}
