using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using NumericHelpers;
using System.Diagnostics;

namespace Agro;

public partial class Plant_AG_Godot2 : PlantAbstractGodot2<AboveGroundAgent2>
{
	public Plant_AG_Godot2(PlantSubFormation2<AboveGroundAgent2> formation) : base(formation) { }

	protected override Color FormationColor => Colors.Green;
	protected override ColorCodingType FormationColorCoding => ColorCodingType.Default;

	protected override void UpdateTransformation(MeshInstance sprite, int index)
	{
		//var radius = Formation.GetBaseRadius(index);
		var orientation = Formation.GetDirection(index);
		var length = Formation.GetLength(index) * 0.5f; //x0.5f because its the radius of the cube!
		var stableScale = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(length, 0f, 0f), orientation);

		var basis = new Basis(orientation.ToGodot());
		sprite.Transform = new Transform(basis, (Formation.GetBaseCenter(index) + stableScale).ToGodot());
		sprite.Scale = (Formation.GetScale(index) * 0.5f).ToGodot();

		//Debug.WriteLine( ColorCoding(index, FormationColorCoding).g);
		var material = (SpatialMaterial)sprite.GetSurfaceMaterial(0);
		material.AlbedoColor = ColorCoding(index, AgroWorldGodot.ShootsVisualization.TransferFunc);
		material.FlagsUnshaded = AgroWorldGodot.ShootsVisualization.Unshaded;
	}

	protected override Color GetNaturalColor(int index)
    {
		var w = Formation.GetWoodRatio(index);
		return ShootsVisualisationSettings.Segment_NaturalLeaf * (1f - w) + ShootsVisualisationSettings.Segment_NaturalWood * w;
    }

	public override void GodotProcess()
	{
		if (AgroWorldGodot.ShootsVisualization.StemsVisibility == Visibility.MakeVisible)
		{
			for(int i = 0; i < GodotSprites.Count; ++i)
				if (Formation.GetOrgan(i) == OrganTypes.Stem)
					GodotSprites[i].Show();
		}
		else if (AgroWorldGodot.ShootsVisualization.StemsVisibility == Visibility.MakeInvisible)
		{
			for(int i = 0; i < GodotSprites.Count; ++i)
				if (Formation.GetOrgan(i) == OrganTypes.Stem)
					GodotSprites[i].Hide();
		}

		if (AgroWorldGodot.ShootsVisualization.LeafsVisibility == Visibility.MakeVisible)
		{
			for(int i = 0; i < GodotSprites.Count; ++i)
				if (Formation.GetOrgan(i) == OrganTypes.Leaf)
					GodotSprites[i].Show();
		}
		else if (AgroWorldGodot.ShootsVisualization.LeafsVisibility == Visibility.MakeInvisible)
		{
			for(int i = 0; i < GodotSprites.Count; ++i)
				if (Formation.GetOrgan(i) == OrganTypes.Leaf)
					GodotSprites[i].Hide();
		}

		if (AgroWorldGodot.ShootsVisualization.BudsVisibility == Visibility.MakeVisible)
		{
			for(int i = 0; i < GodotSprites.Count; ++i)
				if (Formation.GetOrgan(i) == OrganTypes.Bud)
					GodotSprites[i].Show();
		}
		else if (AgroWorldGodot.ShootsVisualization.BudsVisibility == Visibility.MakeInvisible)
		{
			for(int i = 0; i < GodotSprites.Count; ++i)
				if (Formation.GetOrgan(i) == OrganTypes.Bud)
					GodotSprites[i].Hide();
		}

		for(int i = 0; i < GodotSprites.Count; ++i)
			UpdateTransformation(GodotSprites[i], i);

		// if (AgroWorldGodot.ShootsVisualization.StemsVisibility == Visibility.Visible ||
		// 	AgroWorldGodot.ShootsVisualization.LeafsVisibility == Visibility.Visible ||
		// 	AgroWorldGodot.ShootsVisualization.BudsVisibility == Visibility.Visible)
		// {
		// 	for(int i = 0; i < GodotSprites.Count; ++i)
		// 		switch (Formation.GetOrgan(i))
		// 		{
		// 			case OrganTypes.Stem:
		// 				if (AgroWorldGodot.ShootsVisualization.StemsVisibility == Visibility.Visible)
		// 					UpdateTransformation(GodotSprites[i], i);
		// 			break;
		// 			case OrganTypes.Leaf:
		// 				if (AgroWorldGodot.ShootsVisualization.LeafsVisibility == Visibility.Visible)
		// 					UpdateTransformation(GodotSprites[i], i);
		// 			break;
		// 			case OrganTypes.Bud:
		// 				if (AgroWorldGodot.ShootsVisualization.BudsVisibility == Visibility.Visible)
		// 					UpdateTransformation(GodotSprites[i], i);
		// 			break;
		// 			default: UpdateTransformation(GodotSprites[i], i); break;
		// 		}
		// }
	}
}
