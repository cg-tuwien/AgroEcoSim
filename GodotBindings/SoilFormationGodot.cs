using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;


public partial class SoilFormation
{
	SoilVisualisationSettings parameters = new SoilVisualisationSettings();
	
	MeshInstance[] SoilCellInstances;
	MeshInstance[] MarkerInstances;

	public override void GodotReady()
	{
		if(parameters.Visualise){
			InitializeVisualisation();
			// InitializeMarkers();
		}
	}

	public override void GodotProcess(uint timestep)
	{
		// if(parameters.Visualise){
		// 	if(parameters.AnimateCells) AnimateCells();
		// 	if(parameters.AnimateMarkers) AnimateMarkers();
		// }

	}
}
