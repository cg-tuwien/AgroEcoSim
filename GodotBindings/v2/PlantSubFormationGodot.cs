using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using AgentsSystem;
using Utils;

namespace Agro;

public partial class PlantSubFormation2<T> : IFormation where T: struct, IPlantAgent
{
	[Newtonsoft.Json.JsonIgnore] internal Action<int> GodotRemoveSprite;
	[Newtonsoft.Json.JsonIgnore] internal Action<int> GodotAddSprites;

	public PlantSubFormation2(PlantFormation2 plant, Action<T[], int[]> reindex, Action<int> godotRemove, Action<int> godotAdd) : this(plant, reindex)
	{
		GodotRemoveSprite = godotRemove;
		GodotAddSprites = godotAdd;
	}

	public void GodotReady() {}
	public void GodotProcess() {}

	[Newtonsoft.Json.JsonIgnore] internal IList<float> LightEfficiency;
	[Newtonsoft.Json.JsonIgnore] internal IList<float> EnergyEfficiency;
	internal float GetLightEfficiency(int index) => index < LightEfficiency?.Count ? LightEfficiency[index] : 0f;
	internal float GetEnergyEfficiency(int index) => index < EnergyEfficiency?.Count ? EnergyEfficiency[index] : 0f;
}
