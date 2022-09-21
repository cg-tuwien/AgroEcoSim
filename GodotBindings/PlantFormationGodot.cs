using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public partial class PlantFormation
{
	[Flags]
	enum DisplayOptions { None = 0, Seed = 1, UnderGround = 2, AboveGround = 4 }
	const DisplayOptions ShowOrgans = DisplayOptions.Seed | DisplayOptions.UnderGround | DisplayOptions.AboveGround;
	//const DisplayOptions ShowOrgans = DisplayOptions.Seed | DisplayOptions.AboveGround;
	//const DisplayOptions ShowOrgans = DisplayOptions.None;
	MeshInstance3D GodotSeedSprite;
	Plant_UG_Godot UG_Godot;
	Plant_AG_Godot AG_Godot;

	public void GodotReady()
	{
		UG_Godot = new(UG);
		AG_Godot = new(AG);
		if (ShowOrgans.HasFlag(DisplayOptions.Seed))
		{
			var spherePrimitive = new SphereMesh();
			//spherePrimitive.Height = 1f;
			GodotSeedSprite = new MeshInstance3D(); // Create a new Sprite.
			SimulationWorld.GodotAddChild(GodotSeedSprite); // Add it as a child of this node.
			GodotSeedSprite.Mesh = spherePrimitive;
			if (GodotSeedSprite.GetSurfaceOverrideMaterial(0) == null)
			{
				var m = new StandardMaterial3D();
				m.AlbedoColor = new Color(1, 0, 0);
				GodotSeedSprite.SetSurfaceOverrideMaterial(0, m);
			}
			var seed = Seed[0];
			GodotSeedSprite.Position = seed.Center.ToGodot();
			GodotSeedSprite.Scale = Vector3.One * seed.Radius;
		}

		UG_Godot.GodotShow = ShowOrgans.HasFlag(DisplayOptions.UnderGround);
		AG_Godot.GodotShow = ShowOrgans.HasFlag(DisplayOptions.AboveGround);

		UG_Godot.GodotReady();
		AG_Godot.GodotReady();
	}

	public void GodotProcess(uint timestep)
	{
		if (ShowOrgans.HasFlag(DisplayOptions.Seed))
		{
			if (Seed.Length == 1)
			{
				GodotSeedSprite.Scale = Vector3.One * Seed[0].Radius;
				var seedColor = 0.5f * Seed[0].GerminationProgress + 0.5f;
				((StandardMaterial3D)GodotSeedSprite.GetSurfaceOverrideMaterial(0)).AlbedoColor = new Color(seedColor, seedColor, seedColor);
			}
			else if (GodotSeedSprite != null)
			{
				SimulationWorld.GodotRemoveChild(GodotSeedSprite);
				GodotSeedSprite = null;
			}
		}

		UG_Godot.GodotProcess(timestep);
		AG_Godot.GodotProcess(timestep);
	}
}
