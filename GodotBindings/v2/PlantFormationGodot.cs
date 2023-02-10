using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public partial class PlantFormation2
{
	MeshInstance3D GodotSeedSprite;
	Plant_UG_Godot2 UG_Godot;
	Plant_AG_Godot2 AG_Godot;

	public void GodotReady()
	{
		UG_Godot = new(UG);
		AG_Godot = new(AG);

		var seed = Seed[0];
		var spherePrimitive = new SphereMesh();
		GodotSeedSprite = new ()
        {
            Mesh = spherePrimitive,
			Position = seed.Center.ToGodot(),
			Scale = Vector3.One * seed.Radius,
			MaterialOverride = AgroWorldGodot.UnshadedMaterial()
        }; // Create a new Sprite.

		SimulationWorld.GodotAddChild(GodotSeedSprite); // Add it as a child of this node.
		UG_Godot.GodotReady();
		AG_Godot.GodotReady();
	}

	public void GodotProcess()
	{
		if (Seed.Length == 1)
		{
			GodotSeedSprite.Scale = Vector3.One * Seed[0].Radius;
			var seedColor = 0.5f * Seed[0].GerminationProgress + 0.5f;
			((ShaderMaterial)GodotSeedSprite.MaterialOverride).SetShaderParameter(AgroWorldGodot.COLOR, new Color(seedColor, seedColor, seedColor));
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