using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;

public static class AgroWorldGodot
{
	public static SoilVisualisationSettings SoilVisualization = new()
	{
		CellTransferFunc = SoilCellTransferFunctionPreset.BlueWater,
		MarkerTransferFunc = SoilMarkerTransferFunctionPreset.BlueWater,
		MarkerVisibility = Visibility.Invisible,
		AnimateMarkerSize = true,
		SoilCellScale = 0.99f,
		//SurfaceCellScale = 0.99f
		GroundVisible = true,
		SoilCellsVisibility = Visibility.MakeInvisible,
		SurfaceCellsVisibility = Visibility.MakeInvisible,
	};

	public static RootsVisualisationSettings RootsVisualization = new();
	public static ShootsVisualisationSettings ShootsVisualization = new();

    public static SimulationSettings SimulationSettings = new();

	internal const string COLOR = "mColor";

	const string MaterialCoreString = $@"
		uniform vec4 { COLOR } : source_color = vec4(0.7, 0.7, 0.7, 1.0);
		void fragment() {{ ALBEDO = {COLOR}.rgb; }}
	";

	static readonly Shader UnshadedShader = new() {
		Code = $@"
			shader_type spatial;
			render_mode unshaded;
			{MaterialCoreString}
		"
	};

	static readonly Shader ShadedShader = new() {
		Code = $@"
			shader_type spatial;
			{MaterialCoreString}
		"
	};

	internal static ShaderMaterial UnshadedMaterial() => new() { Shader = UnshadedShader };

	internal static ShaderMaterial ShadedMaterial() => new() { Shader = ShadedShader };
}