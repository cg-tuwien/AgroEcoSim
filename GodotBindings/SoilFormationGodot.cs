using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;

public partial class SoilFormation
{
	const bool SpriteShow = true;
	const float SpriteScale = 0.2f;
	MeshInstance[] GodotSprites;
	public override void GodotReady()
	{
		if (SpriteShow)
		{
			GodotSprites = new MeshInstance[SizeX * SizeY * SizeZ];
			var cubePrimitive = new CubeMesh();
			for( int x = 0; x < SizeX; ++x)
				for( int y = 0; y < SizeY; ++y)
					for( int z = 0; z < SizeZ; ++z)
					{
						var sprite = new MeshInstance(); // Create a new Sprite.
						SimulationWorld.GodotAddChild(sprite); // Add it as a child of this node.
						GodotSprites[Index(x, y, z)] = sprite;
						sprite.Mesh = cubePrimitive;
						if (sprite.GetSurfaceMaterial(0) == null)
						{
							var m = new SpatialMaterial();
							m.AlbedoColor = new Color(1, 0, 0);
							sprite.SetSurfaceMaterial(0, m);
						}
						sprite.Translation = new Vector3(x, -z, y) * AgroWorld.FieldResolution;
						sprite.Scale = Vector3.One * (AgroWorld.FieldResolution * 0.5f * SpriteScale);
					}
		}
	}

	public override void GodotProcess(uint timestep)
	{
		if (SpriteShow)
			for(int i = 0; i < GodotSprites.Length; ++i)
			{
				((SpatialMaterial)GodotSprites[i].GetSurfaceMaterial(0)).AlbedoColor = new Color(0, 0f, Math.Min(Agents[i].Water * 0.01f / SoilAgent.FieldCellSurface, 1f));
			}
	}
}
