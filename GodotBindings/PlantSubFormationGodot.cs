using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using AgentsSystem;
using Utils;

namespace Agro;

public partial class PlantSubFormation<T> : IFormation where T: struct, IPlantAgent
{
	internal Action<int> GodotRemoveSprite;
	internal Action<int> GodotAddSprite;

	public PlantSubFormation(PlantFormation plant, Action<T[], int[]> reindex, Action<int> godotRemove, Action<int> godotAdd) : this(plant, reindex)
	{
		GodotRemoveSprite = godotRemove;
		GodotAddSprite = godotAdd;
	}

	public void GodotReady() {}
	public void GodotProcess(uint timesteps) {}
}