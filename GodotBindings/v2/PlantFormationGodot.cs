using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public partial class PlantFormation2
{
	[Newtonsoft.Json.JsonIgnore] MeshInstance GodotSeedSprite;
	[Newtonsoft.Json.JsonIgnore] Plant_UG_Godot2 UG_Godot;
	[Newtonsoft.Json.JsonIgnore] Plant_AG_Godot2 AG_Godot;

	public void GodotReady()
	{
		UG_Godot = new(UG);
		AG_Godot = new(AG);

		var spherePrimitive = new SphereMesh();
		//spherePrimitive.Height = 1f;
		GodotSeedSprite = new MeshInstance(); // Create a new Sprite.
		SimulationWorld.GodotAddChild(GodotSeedSprite); // Add it as a child of this node.
		GodotSeedSprite.Mesh = spherePrimitive;
		if (GodotSeedSprite.GetSurfaceMaterial(0) == null)
			GodotSeedSprite.SetSurfaceMaterial(0, new SpatialMaterial { AlbedoColor = new Color(1, 0, 0) });
		var seed = Seed[0];
		GodotSeedSprite.Translation = seed.Center.ToGodot();
		GodotSeedSprite.Scale = Vector3.One * seed.Radius;

		UG_Godot.GodotReady();
		AG_Godot.GodotReady();
	}

	public void GodotProcess()
	{
		if (Seed.Length == 1)
		{
			GodotSeedSprite.Scale = Vector3.One * Seed[0].Radius;
			var seedColor = 0.5f * Seed[0].GerminationProgress + 0.5f;
			((SpatialMaterial)GodotSeedSprite.GetSurfaceMaterial(0)).AlbedoColor = new Color(seedColor, seedColor, seedColor);
		}
		else if (GodotSeedSprite != null)
		{
			SimulationWorld.GodotRemoveChild(GodotSeedSprite);
			GodotSeedSprite = null;
		}

		UG_Godot.GodotProcess();
		AG_Godot.GodotProcess();
	}
}