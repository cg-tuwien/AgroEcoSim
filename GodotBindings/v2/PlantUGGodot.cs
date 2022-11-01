using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public class Plant_UG_Godot2 : PlantAbstractGodot2<UnderGroundAgent2>
{
	public Plant_UG_Godot2(PlantSubFormation2<UnderGroundAgent2> formation) : base(formation) { }

	protected override Color FormationColor => Colors.Brown;
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

		((SpatialMaterial)sprite.GetSurfaceMaterial(0)).AlbedoColor = ColorCoding(index, AgroWorldGodot.RootsVisualization.TransferFunc);
	}

	protected override Color GetNaturalColor(int index)
    {
		var w = Formation.GetWoodRatio(index);
		return RootsVisualisationSettings.Segment_Light * (1f - w) + RootsVisualisationSettings.Segment_Dark * w;
    }

	public override void GodotProcess()
	{
		if (AgroWorldGodot.RootsVisualization.RootsVisibility == Visibility.MakeVisible)
		{
			for(int i = 0; i < GodotSprites.Count; ++i)
				GodotSprites[i].Show();
		}
		else if (AgroWorldGodot.RootsVisualization.RootsVisibility == Visibility.MakeInvisible)
		{
			for(int i = 0; i < GodotSprites.Count; ++i)
				GodotSprites[i].Hide();
		}

		if (AgroWorldGodot.RootsVisualization.RootsVisibility == Visibility.Visible)
			for(int i = 0; i < GodotSprites.Count; ++i)
				UpdateTransformation(GodotSprites[i], i);
	}
}
