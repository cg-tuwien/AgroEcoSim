using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using GodotHelpers;

namespace Agro;

public class Plant_UG_Godot  : PlantAbstractGodot<UnderGroundAgent>
{
	public Plant_UG_Godot(PlantSubFormation<UnderGroundAgent> formation) : base(formation) { }
    protected override void UpdateTransformation(MeshInstance sprite, int index)
	{
		var radius = Formation.GetBaseRadius(index);
		var orientation = Formation.GetDirection(index);
		var length = Formation.GetLength(index) * 0.5f; //x0.5f because its the radius of the cube!
		var stableScale = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(length, 0f, 0f), orientation);

		var basis = new Basis(orientation.ToGodot());
		sprite.Transform = new Transform(basis, (Formation.GetBaseCenter(index) + stableScale).ToGodot());
		sprite.Scale = new Vector3(length, radius, radius);

		const string vis = "energyRatio";
		Color c;
		switch (vis)
		{
			case "energyRatio":
			{
				var r = Math.Min(1f, Formation.GetEnergy(index) / Formation.GetEnergyCapacity(index));
				if (r >= 0f)
					c = new Color(r, r * 0.5f, 0f);
				else
					c = Colors.Red;
				break;
			}
			case "waterRatio":
			{
				var rs = Math.Min(1f, Formation.GetWater(index) / Formation.GetWaterStorageCapacity(index));
				var rt = Math.Min(1f, Formation.GetWater(index) / Formation.GetWaterCapacityPerTick(index));
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
}
