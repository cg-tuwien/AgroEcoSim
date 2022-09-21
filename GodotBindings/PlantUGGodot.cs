using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public class Plant_UG_Godot  : PlantAbstractGodot<UnderGroundAgent>
{
	public Plant_UG_Godot(PlantSubFormation<UnderGroundAgent> formation) : base(formation) { }

	protected override Color FormationColor => Colors.Brown;
	protected override ColorCodingType FormationColorCoding => ColorCodingType.MixedRatio;

	protected override void UpdateTransformation(MeshInstance3D sprite, int index)
	{
		var radius = Formation.GetBaseRadius(index);
		var orientation = Formation.GetDirection(index);
		var length = Formation.GetLength(index) * 0.5f; //x0.5f because its the radius of the cube!
		var stableScale = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(length, 0f, 0f), orientation);

		var basis = new Basis(orientation.ToGodot());
		sprite.Transform = new Transform3D(basis, (Formation.GetBaseCenter(index) + stableScale).ToGodot());
		sprite.Scale = (Formation.GetScale(index) * 0.5f).ToGodot();

		((StandardMaterial3D)sprite.GetSurfaceOverrideMaterial(0)).AlbedoColor = ColorCoding(index, FormationColorCoding);
	}
}
