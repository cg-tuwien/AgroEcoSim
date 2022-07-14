using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;

public struct SoilVisualisationParameters{
	//I've removed the const modifier because I think it could be useful to change appearance of the soil during the simulation
	public bool SpriteShow;
	public float SpriteScale;

	public Color MaxWaterColor;
	public Color MinWaterColor;

	public float MinCapacity;
	public float MaxCapacity;
	public float TotalCapacity;

	public SoilVisualisationParameters(){
		SpriteScale = 0.2f;
		SpriteShow = true;

		MaxWaterColor = new Color(0f,0f,1f);
		MinWaterColor = new Color(1f,0f,0f);

		MinCapacity = 0f;
		MaxCapacity = 1f;
		TotalCapacity = 1f;
	}

}

public partial class SoilFormation
{
	SoilVisualisationParameters parameters = new SoilVisualisationParameters();




	MeshInstance[] GodotSprites;
	public override void GodotReady()
	{
		if (parameters.SpriteShow)
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
						sprite.Scale = Vector3.One * (AgroWorld.FieldResolution * 0.5f * parameters.SpriteScale);
					}
			
			parameters.MaxCapacity = SoilAgent.FieldCellVolume; //Todo: This isn't correct - it has to be changed with the introduction of upper bound for the ammount of water that can be contained in a single cell
			parameters.TotalCapacity = parameters.MaxCapacity - parameters.MinCapacity;
		}
	}

	public override void GodotProcess(uint timestep)
	{
		if(!parameters.SpriteShow) return;

		for(int i = 0; i < GodotSprites.Length; ++i){
			solve_color(i);
		}
		

		// if (parameters.SpriteShow)
		// 	for(int i = 0; i < GodotSprites.Length; ++i)
		// 	{
		// 		((SpatialMaterial)GodotSprites[i].GetSurfaceMaterial(0)).AlbedoColor = new Color(0, 0f, Math.Min(Agents[i].Water * 0.01f / SoilAgent.FieldCellSurface, 1f));
		// 	}
	}

	private void solve_color(int i){
		float mix = Math.Min(parameters.MaxCapacity,Math.Max(parameters.MinCapacity,Agents[i].Water))/parameters.TotalCapacity;
		((SpatialMaterial)GodotSprites[i].GetSurfaceMaterial(0)).AlbedoColor = parameters.MaxWaterColor*mix + parameters.MinWaterColor*(1-mix);
	}
}
