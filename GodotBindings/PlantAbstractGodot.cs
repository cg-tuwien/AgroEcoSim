using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using GodotHelpers;

namespace Agro;

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

	public void AddSprite(int index)
	{
		if (GodotShow)
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

			UpdateTransformation(sprite, index);
			GodotSprites.Add(sprite);
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
}
