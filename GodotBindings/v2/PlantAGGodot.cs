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
		((SpatialMaterial)sprite.GetSurfaceMaterial(0)).AlbedoColor = ColorCoding(index, FormationColorCoding);
	}
}
