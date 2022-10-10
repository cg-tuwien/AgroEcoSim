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
	internal Action<int> GodotAddSprites;

	public PlantSubFormation(PlantFormation1 plant, Action<T[], int[]> reindex, Action<int> godotRemove, Action<int> godotAdd) : this(plant, reindex)
	{
		GodotRemoveSprite = godotRemove;
		GodotAddSprites = godotAdd;
	}

	public void GodotReady() {}
	public void GodotProcess(uint timesteps) {}
}
