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
		var radius = GetUnderGroundBaseRadius(index);
		var direction = GetUnderGroundDirection(index);
		var length = GetUnderGroundLength(index) * 0.5f; //x0.5f because its the radius of the cube!
		var stableScale = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(length, 0f, 0f), direction);
		
		var basis = new Basis(direction.ToGodot());
		sprite.Transform = new Transform(basis, (GetUnderGroundBaseCenter(index) + stableScale).ToGodot());
		sprite.Scale = new Vector3(length, radius, radius);
	}

	void UpdateAboveGroundTransformation(MeshInstance sprite, int index)
	{
		var radius = GetAboveGroundBaseRadius(index);
		var direction = GetAboveGroundDirection(index);
		var length = GetAboveGroundLength(index) * 0.5f; //x0.5f because its the radius of the cube!
		var stableScale = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(length, 0f, 0f), direction);

		var basis = new Basis(direction.ToGodot());
		sprite.Transform = new Transform(basis, (GetAboveGroundBaseCenter(index) + stableScale).ToGodot());
		if (GetAboveGroundOrgan(index) == OrganTypes.Leaf)
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
		//GD.Print(Agents.Select(x => x.Water).Sum());
		if (ShowOrgans.HasFlag(DisplayOptions.Seed))
		{
			if (Seed.Length == 1)
			{
				GodotSeedSprite.Scale = Vector3.One * Seed[0].Radius;
				var seedColor = 0.5f * Seed[0].EnergyAccumulationProgress + 0.5f;
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
