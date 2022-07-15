using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;


public partial class SoilFormation
{
	SoilVisualisationSettings parameters = new SoilVisualisationSettings();
	

	//Todo: Speed of the visualisation could be improved by replacing Mesh with MultiMesh!!!
	MeshInstance[] SoilCellInstances;
	MeshInstance[] MarkerInstances;

	public override void GodotReady()
	{
		if(parameters.Visualise){ //Todo: Check whether the previous condition wasn't dependent on something besides this file
			InitializeVisualisation();
		}
	}

	public override void GodotProcess(uint timestep)
	{
		if(parameters.Visualise){
			AnimateCells();
			AnimateMarkers();
		}

	}
}
