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
	protected virtual ColorCodingType FormationColorCoding => ColorCodingType.Light;

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
					sprite.SetSurfaceMaterial(0, new SpatialMaterial{ AlbedoColor = FormationColor, FlagsUnshaded = true });

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

	public abstract void GodotProcess();

	protected Color ColorCoding(int index, ColorCodingType vis)
	{
		switch (vis)
		{
			case ColorCodingType.Energy:
			{
				var r = Math.Min(1f, Formation.GetEnergy(index) / Formation.GetEnergyCapacity(index));
				return r >= 0f ? new Color(r, r * 0.5f, 0f) : Colors.Red;
			}
			case ColorCodingType.Light:
			{
				var w = Math.Clamp(Formation.GetIrradiance(index) * AgroWorldGodot.ShootsVisualization.LightCutOff, 0, 1);
				return Colors.White * w;
			}
			case ColorCodingType.Water:
			{
				var rs = Math.Min(1f, Formation.GetWater(index) / Formation.GetWaterStorageCapacity(index));
				var rt = Math.Min(1f, Formation.GetWater(index) / Formation.GetWaterTotalCapacity(index));
				return rs >= 0f ? new Color(rt, rt, rs) : Colors.Red;
			}
			case ColorCodingType.All:
			{
				var re = Math.Clamp(Formation.GetEnergy(index) / Formation.GetEnergyCapacity(index), 0f, 1f);
				var rl = Math.Clamp(Formation.GetIrradiance(index) * AgroWorldGodot.ShootsVisualization.LightCutOff, 0f, 1f);
				var rs = Math.Clamp(Formation.GetWater(index) / Formation.GetWaterStorageCapacity(index), 0f, 1f);
				//var rt = Math.Clamp(Formation.GetWater(index) / Formation.GetWaterTotalCapacity(index), 0f, 1f);
				return new Color(re, rl, rs);
			}
			case ColorCodingType.Natural:
				return GetNaturalColor(index);

			default: return FormationColor;
		}
	}

	protected abstract Color GetNaturalColor(int index);
}
