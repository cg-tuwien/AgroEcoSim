using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public abstract class PlantAbstractGodot2<T> where T : struct, IPlantAgent
{
	[Newtonsoft.Json.JsonIgnore] protected readonly PlantSubFormation2<T> Formation;
	[Newtonsoft.Json.JsonIgnore] protected readonly List<MeshInstance> GodotSprites = new();
	[Newtonsoft.Json.JsonIgnore] protected readonly static CubeMesh PlantCubePrimitive = new();

	protected PlantAbstractGodot2(PlantSubFormation2<T> formation) => Formation = formation;

	protected abstract void UpdateTransformation(MeshInstance sprite, int index, bool justCreated);

	static Color DefaultColor = new(0.7f, 0.7f, 0.7f);

	[Newtonsoft.Json.JsonIgnore] protected virtual Color FormationColor => DefaultColor;
	[Newtonsoft.Json.JsonIgnore] protected virtual ColorCodingType FormationColorCoding => ColorCodingType.Light;

	public void AddSprites(int count)
	{
		for (int i = GodotSprites.Count; i < count; ++i)
		{
			var sprite = new MeshInstance();
			SimulationWorld.GodotAddChild(sprite); // Add it as a child of this node.
			sprite.Mesh = PlantCubePrimitive;
			if (sprite.GetSurfaceMaterial(0) == null) //TODO if not visualizing, use a common material for all
				sprite.SetSurfaceMaterial(0, new SpatialMaterial{ AlbedoColor = FormationColor, FlagsUnshaded = true });

			UpdateTransformation(sprite, i, true);
			GodotSprites.Add(sprite);
		}
	}

	public void RemoveSprite(int index)
	{
		var sprite = GodotSprites[index];
		GodotSprites.RemoveAt(index);
		SimulationWorld.GodotRemoveChild(sprite);
	}

	public void GodotReady() { }

	public abstract void GodotProcess();

	protected Color ColorCoding(int index, ColorCodingType vis, bool justCreated)
	{
		switch (vis)
		{
			case ColorCodingType.Energy:
			{
				var r = Math.Min(1f, Formation.GetEnergy(index) / Formation.GetEnergyCapacity(index));
				return r >= 0f ? new Color(Math.Clamp(r, 0, 1), Math.Clamp(r * 0.1f, 0, 1), 0f) : Colors.Red;
			}
			case ColorCodingType.Light:
			{
				if (justCreated) return Colors.Black;
				var w = Math.Clamp(Formation.GetIrradiance(index) * AgroWorldGodot.ShootsVisualization.LightCutOff, 0, 1);
				return new Color(Math.Clamp(w, 0, 1), Math.Clamp(w * 0.8f, 0, 1), Math.Clamp(w * 0.64f, 0, 1));
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
			case ColorCodingType.EnergyEfficiency:
			{
				if (justCreated) return Colors.Black;
				var w = Math.Clamp(GetEnergyEfficiency(index), 0, 1);
				return Colors.Yellow * w + Colors.Blue * (1-w);
			}
			case ColorCodingType.DailyEnergyProduction:
			{
				if (justCreated) return Colors.Black;
				var r = Formation.GetDailyEnergyProduction(index) / 100f;
				return r >= 0f ? new Color(Math.Clamp(r, 0, 1), Math.Clamp(r * 0.1f, 0, 1), 0f) : Colors.Red;
			}
			case ColorCodingType.DailyLightExposure:
			{
				if (justCreated) return Colors.Black;
				// if (Formation.GetDailyLightExposure(index) > 1f)
				// 	System.Diagnostics.Debug.WriteLine($"D: {Formation.GetDailyLightExposure(index)} f: {AgroWorldGodot.ShootsVisualization.LightCutOff}");
				var w = Formation.GetDailyLightExposure(index) * AgroWorldGodot.ShootsVisualization.LightCutOff / (12f * AgroWorld.TicksPerHour);
				return new Color(Math.Clamp(w, 0, 1), Math.Clamp(w * 0.8f, 0, 1), Math.Clamp(w * 0.64f, 0, 1));
			}
			case ColorCodingType.LightEfficiency:
			{
				if (justCreated) return Colors.Black;
				var w = Math.Clamp(GetLightEfficiency(index), 0, 1);
				return Colors.Yellow * w + Colors.Blue * (1-w);
			}
			default: return FormationColor;
		}
	}

	protected abstract Color GetNaturalColor(int index);
	protected abstract float GetLightEfficiency(int index);
	protected abstract float GetEnergyEfficiency(int index);
}
