using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public enum ColorCodingType : byte { Default, EnergyRatio, WaterRatio, MixedRatio, WoodRatio, Irradiance };

public abstract class PlantAbstractGodot<T> where T : struct, IPlantAgent
{
	internal bool GodotShow = true;
	protected readonly PlantSubFormation<T> Formation;
	protected readonly List<MeshInstance> GodotSprites = new();
	protected readonly static CubeMesh PlantCubePrimitive = new CubeMesh();

	protected PlantAbstractGodot(PlantSubFormation<T> formation) => Formation = formation;

	protected abstract void UpdateTransformation(MeshInstance sprite, int index);

	static Color DefaultColor = new Color(0.7f, 0.7f, 0.7f);

	protected virtual Color FormationColor => DefaultColor;
	protected virtual ColorCodingType FormationColorCoding => ColorCodingType.Default;

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

	public void GodotProcess(uint timestep)
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
				var re = Math.Min(1f, Formation.GetEnergy(index) / Formation.GetEnergyCapacity(index));
				var rs = Math.Min(1f, Formation.GetWater(index) / Formation.GetWaterStorageCapacity(index));
				var rt = Math.Min(1f, Formation.GetWater(index) / Formation.GetWaterTotalCapacity(index));
				return (re >= 0f && rs >= 0f && rt >= 0f) ? new Color(re, rt, rs) : Colors.Black;
			}
			case ColorCodingType.WoodRatio:
			{
				var w = Formation.GetWoodRatio(index);
				return Colors.Green * (1f - w) + Colors.Brown * w;
			}
			case ColorCodingType.Irradiance:
			{
				var w = Math.Clamp(Formation.GetIrradiance(index) * 0.5f, 0, 1);
				return Colors.White * w;
			}
			default: return FormationColor;
		}
	}
}
