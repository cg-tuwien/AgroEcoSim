using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public abstract class PlantAbstractGodot2<T> where T : struct, IPlantAgent
{
	internal bool GodotShow = true;
	protected readonly PlantSubFormation2<T> Formation;
	protected readonly List<MeshInstance> GodotSprites = new();
	protected readonly static CubeMesh PlantCubePrimitive = new();

	protected PlantAbstractGodot2(PlantSubFormation2<T> formation) => Formation = formation;

	protected abstract void UpdateTransformation(MeshInstance sprite, int index);

	static Color DefaultColor = new(0.7f, 0.7f, 0.7f);

	protected virtual Color FormationColor => DefaultColor;
	protected virtual ColorCodingType FormationColorCoding => ColorCodingType.Irradiance;

	public void AddSprites(int count)
	{
		if (GodotShow)
		{
			for (int i = GodotSprites.Count; i < count; ++i)
			{
				var sprite = new MeshInstance();
				SimulationWorld.GodotAddChild(sprite); // Add it as a child of this node.
				sprite.Mesh = PlantCubePrimitive;
				if (sprite.GetSurfaceMaterial(0) == null) //TODO if not visualizing, use a common material for all
				{
					var m = new SpatialMaterial();
					m.AlbedoColor = new Color(0.7f, 0.7f, 0.7f);
					sprite.SetSurfaceMaterial(0, new SpatialMaterial{ AlbedoColor = FormationColor });
				}

				UpdateTransformation(sprite, i);
				GodotSprites.Add(sprite);
			}
		}
	}

	public void RemoveSprite(int index)
	{
		if (GodotShow)
		{
			var sprite = GodotSprites[index];
			GodotSprites.RemoveAt(index);
			SimulationWorld.GodotRemoveChild(sprite);
		}
	}

	public void GodotReady() { }

	public void GodotProcess()
	{
		if (GodotShow)
			for(int i = 0; i < GodotSprites.Count; ++i)
				UpdateTransformation(GodotSprites[i], i);
	}

	protected Color ColorCoding(int index, ColorCodingType vis)
	{
		switch (vis)
		{
			case ColorCodingType.EnergyRatio:
			{
				var r = Math.Min(1f, Formation.GetEnergy(index) / Formation.GetEnergyCapacity(index));
				return r >= 0f ? new Color(r, r * 0.5f, 0f) : Colors.Red;
			}
			case ColorCodingType.WaterRatio:
			{
				var rs = Math.Min(1f, Formation.GetWater(index) / Formation.GetWaterStorageCapacity(index));
				var rt = Math.Min(1f, Formation.GetWater(index) / Formation.GetWaterTotalCapacity(index));
				return rs >= 0f ? new Color(rt, rt, rs) : Colors.Red;
			}
			case ColorCodingType.MixedRatio:
			{
				var re = Math.Clamp(Formation.GetEnergy(index) / Formation.GetEnergyCapacity(index), 0f, 1f);
				var rs = Math.Clamp(Formation.GetWater(index) / Formation.GetWaterStorageCapacity(index), 0f, 1f);
				//var rt = Math.Clamp(Formation.GetWater(index) / Formation.GetWaterTotalCapacity(index), 0f, 1f);
				return new Color(re, Math.Min(re, rs), rs);
			}
			case ColorCodingType.WoodRatio:
			{
				var w = Formation.GetWoodRatio(index);
				return Colors.Green * (1f - w) + Colors.Brown * w;
			}
			case ColorCodingType.Irradiance:
			{
				var w = Math.Clamp(Formation.GetIrradiance(index) * 0.01f, 0, 1);
				return Colors.White * w;
			}
			default: return FormationColor;
		}
	}
}
