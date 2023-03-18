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

	protected override void UpdateTransformation(MeshInstance3D sprite, int index, bool justCreated)
	{
		//var radius = Formation.GetBaseRadius(index);
		var orientation = Formation.GetDirection(index);
		var length = Formation.GetLength(index) * 0.5f; //x0.5f because its the radius of the cube!
		var stableScale = System.Numerics.Vector3.Transform(new(length, 0f, 0f), orientation);

		var basis = new Basis(orientation.ToGodot());
		sprite.Transform = new Transform3D(basis, (Formation.GetBaseCenter(index) + stableScale).ToGodot());
		sprite.Scale = Formation.GetScale(index).ToGodot();

		var material = AgroWorldGodot.ShootsVisualization.IsUnshaded ? UnshadedMaterial : ShadedMaterial;
		material.SetShaderParameter(AgroWorldGodot.COLOR, ColorCoding(index, AgroWorldGodot.ShootsVisualization.TransferFunc, justCreated));
		sprite.MaterialOverride = material;
	}

	protected override Color GetNaturalColor(int index)
    {
		var w = Formation.GetWoodRatio(index);
		return ShootsVisualisationSettings.Segment_NaturalLeaf * (1f - w) + ShootsVisualisationSettings.Segment_NaturalWood * w;
    }

	protected override float GetLightEfficiency(int index) => Formation.GetLightEfficiency(index);
	protected override float GetEnergyEfficiency(int index) => Formation.GetEnergyEfficiency(index);

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
			UpdateTransformation(GodotSprites[i], i, false);
	}
}
