using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using GodotHelpers;

namespace Agro;

public partial class PlantFormation
{
	[Flags]
	enum DisplayOptions { None = 0, Seed = 1, UnderGround = 2, AboveGround = 4 }
	const DisplayOptions ShowOrgans = DisplayOptions.Seed | DisplayOptions.UnderGround | DisplayOptions.AboveGround;
	//const DisplayOptions ShowOrgans = DisplayOptions.Seed | DisplayOptions.AboveGround;
	//const DisplayOptions ShowOrgans = DisplayOptions.None;
	MeshInstance GodotSeedSprite;
	List<MeshInstance> GodotUnderGroundSprites;
	List<MeshInstance> GodotAboveGroundSprites;
	CubeMesh PlantCubePrimitive = new CubeMesh();
	public void GodotReady()
	{
		if (ShowOrgans.HasFlag(DisplayOptions.Seed))
		{
			var spherePrimitive = new SphereMesh();
			//spherePrimitive.Height = 1f;
			GodotSeedSprite = new MeshInstance(); // Create a new Sprite.
			SimulationWorld.GodotAddChild(GodotSeedSprite); // Add it as a child of this node.
			GodotSeedSprite.Mesh = spherePrimitive;
			if (GodotSeedSprite.GetSurfaceMaterial(0) == null)
			{
				var m = new SpatialMaterial();
				m.AlbedoColor = new Color(1, 0, 0);
				GodotSeedSprite.SetSurfaceMaterial(0, m);
			}
			var seed = Seed[0];
			GodotSeedSprite.Translation = seed.Center.ToGodot();
			GodotSeedSprite.Scale = Vector3.One * seed.Radius;
		}
		GodotUnderGroundSprites = new List<MeshInstance>();
		GodotAboveGroundSprites = new List<MeshInstance>();
	}

	void UpdateUnderGroundTransformation(MeshInstance sprite, int index)
	{
		var radius = GetBaseRadius_UG(index);
		var orientation = GetDirection_UG(index);
		var length = GetLength_UG(index) * 0.5f; //x0.5f because its the radius of the cube!
		var stableScale = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(length, 0f, 0f), orientation);
		
		var basis = new Basis(orientation.ToGodot());
		sprite.Transform = new Transform(basis, (GetBaseCenter_UG(index) + stableScale).ToGodot());
		sprite.Scale = new Vector3(length, radius, radius);

		const string vis = "energyRatio";
		Color c;
		switch (vis)
		{
			case "energyRatio":
			{
				var r = Math.Min(1f, GetEnergy_UG(index) / GetEnergyCapacity_UG(index));
				if (r >= 0f)
					c = new Color(r, r * 0.5f, 0f);
				else
					c = Colors.Red;
				break;
			}
			case "waterRatio":
			{
				var rs = Math.Min(1f, GetWater_UG(index) / GetWaterStorageCapacity_UG(index));
				var rt = Math.Min(1f, GetWater_UG(index) / GetWaterCapacityPerTick_UG(index));
				if (rs >= 0f)
					c = new Color(rt, rt, rs);
				else
					c = Colors.Red;
				break;
			}
			default:
				c = Colors.Brown;
				break;
		}

		((SpatialMaterial)sprite.GetSurfaceMaterial(0)).AlbedoColor = c;
	}

	void UpdateAboveGroundTransformation(MeshInstance sprite, int index)
	{
		var radius = GetBaseRadius_AG(index);
		var orientation = GetDirection_AG(index);
		var length = GetLength_AG(index) * 0.5f; //x0.5f because its the radius of the cube!
		var stableScale = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(length, 0f, 0f), orientation);

		var basis = new Basis(orientation.ToGodot());
		sprite.Transform = new Transform(basis, (GetBaseCenter_AG(index) + stableScale).ToGodot());
		if (GetOrgan_AG(index) == OrganTypes.Leaf)
			sprite.Scale = new Vector3(length, radius, 0.0001f);
		else
			sprite.Scale = new Vector3(length, radius, radius);
	}

	public void GodotAddUnderGroundSprite(int index)
	{
		if (ShowOrgans.HasFlag(DisplayOptions.UnderGround))
		{		
			var sprite = new MeshInstance();
			SimulationWorld.GodotAddChild(sprite); // Add it as a child of this node.
			sprite.Mesh = PlantCubePrimitive;
			if (sprite.GetSurfaceMaterial(0) == null) //TODO if not visualizing, use a common material for all
			{
				var m = new SpatialMaterial();
				m.AlbedoColor = new Color(1, 0, 0);
				sprite.SetSurfaceMaterial(0, m);
			}
			
			UpdateUnderGroundTransformation(sprite, index);
			GodotUnderGroundSprites.Add(sprite);
		}
	}

	public void GodotAddAboveGroundSprite(int index)
	{
		if (ShowOrgans.HasFlag(DisplayOptions.AboveGround))
		{		
			var sprite = new MeshInstance();
			SimulationWorld.GodotAddChild(sprite); // Add it as a child of this node.
			sprite.Mesh = PlantCubePrimitive;
			if (sprite.GetSurfaceMaterial(0) == null) //TODO if not visualizing, use a common material for all
			{
				var m = new SpatialMaterial();
				m.AlbedoColor = new Color(0, 1, 0);
				sprite.SetSurfaceMaterial(0, m);
			}

			UpdateAboveGroundTransformation(sprite, index);
			GodotAboveGroundSprites.Add(sprite);
		}
	}

	public void GodotRemoveUnderGroundSprite(int index)
	{
		if (ShowOrgans.HasFlag(DisplayOptions.UnderGround))
		{		
			var sprite = GodotUnderGroundSprites[index];
			GodotUnderGroundSprites.RemoveAt(index);
			SimulationWorld.GodotRemoveChild(sprite);
		}
	}

	public void GodotRemoveAboveGroundSprite(int index)
	{
		if (ShowOrgans.HasFlag(DisplayOptions.AboveGround))
		{		
			var sprite = GodotAboveGroundSprites[index];
			GodotAboveGroundSprites.RemoveAt(index);
			SimulationWorld.GodotRemoveChild(sprite);
		}
	}
	
	public void GodotProcess(uint timestep)
	{
		if (ShowOrgans.HasFlag(DisplayOptions.Seed))
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
		}
		
		for(int i = 0; i < GodotUnderGroundSprites.Count; ++i)
			if (ShowOrgans.HasFlag(DisplayOptions.UnderGround))
				UpdateUnderGroundTransformation(GodotUnderGroundSprites[i], i);
		
		for(int i = 0; i < GodotAboveGroundSprites.Count; ++i)
			if (ShowOrgans.HasFlag(DisplayOptions.AboveGround))
				UpdateAboveGroundTransformation(GodotAboveGroundSprites[i], i);
	}
}
