using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using Utils;
using System.Diagnostics;
using NumericHelpers;

namespace Agro;

/*
TASKS:
- Cleanup the code
- Gather lateral diffusion data
- Fix color issue with the markers
- Add lateral markers
- Add full cell outlines
- Create godot HUD
*/

/* Flow directions
Note: y is up
0 ... [1,0,0]
1 ... [0,1,0]
2 ... [0,0,1]
3 ... [-1,0,0]
4 ... [0,-1,0]
5 ... [0,0,-1]
*/

public partial class SoilFormationNew
{
	public float[] SteamFlow;// = new float[Agents.Length,6]; //Might save some space by having only 5 elements in the nested array, but I am keeping 6 for better indexing
	public float[] WaterFlow;// = new float[Agents.Length,6];
	int[] IDToIndex;
	ulong MinID = ulong.MaxValue;

	static readonly Vector3[] Rotations = {
		new Vector3(0, 0, -MathF.PI/2),
		Vector3.Zero,
		new Vector3(-MathF.PI/2, 0, 0),
		new Vector3(0, 0, MathF.PI/2),
		new Vector3(MathF.PI, 0, 0),
		new Vector3(MathF.PI/2, 0, 0)
	};

	static readonly Vector3[] MarkerOffsets = { Vector3.Right, Vector3.Up, Vector3.Forward, Vector3.Left, Vector3.Down, Vector3.Back };

	int GetDir(int srcIndex, int dstIndex)
	{
		var coordsDiff = Coords(dstIndex) - Coords(srcIndex);
		return coordsDiff switch
		{
			Utils.Vector3i(1, 0, 0) => 0,
			Utils.Vector3i(0, 1, 0) => 5,
			Utils.Vector3i(0, 0, 1) => 4,
			Utils.Vector3i(-1, 0, 0) => 3,
			Utils.Vector3i(0, -1, 0) => 2,
			Utils.Vector3i(0, 0, -1) => 1,
			_ => throw new Exception("Cells passed to GetDir are no direct neighbors.")
		};
	}

	static int InvertDir(int dir) => dir switch {
		0 => 3,
		1 => 4,
		2 => 5,
		3 => 0,
		4 => 1,
		5 => 2,
		_ => throw new Exception("directionassed to InvertDir was invalid.")
	};

	private void InitializeCells()
	{
		if (AgroWorldGodot.SoilVisualization.SoilCellsVisibility == Visibility.Invisible)
			AgroWorldGodot.SoilVisualization.SoilCellsVisibility = Visibility.MakeInvisible;

		if (AgroWorldGodot.SoilVisualization.SurfaceCellsVisibility == Visibility.Invisible)
			AgroWorldGodot.SoilVisualization.SurfaceCellsVisibility = Visibility.MakeInvisible;

		SoilCellInstances = new MeshInstance3D[Count]; //no need for multiplication here, it's a complete 3D grid
		for (int x = 0; x < Size.X; x++)
			for (int y = 0; y < Size.Y; y++)
			{
				var height = Height(x, y);
				for (int z = 0; z <= height; z++)
					InitializeCell(x, y, height - z, z == height);
			}

		ApplyCellVisibility();
	}

	private void InitializeMarkers() { }

	private void InitializeMarker(int parent_index, Direction dir) { }


	static readonly Vector3 SoilCellUncenter = new(0.5f, 0.5f, 0.5f);
	static readonly Vector3 SurfaceCellUncenter = new(0.5f, 0f, 0.5f);
	private void InitializeCell(int x, int y, int depth, bool isGround)
	{
		var cellSize = AgroWorldGodot.SoilVisualization.SoilCellScale * AgroWorld.FieldResolution;
		var mesh = new MeshInstance3D()
		{
			Mesh = isGround ? AgroWorldGodot.SoilVisualization.SurfaceCellShape : AgroWorldGodot.SoilVisualization.SoilCellShape,
			Position = depth == 0
				? new Vector3(x, 0, y) * AgroWorld.FieldResolution + SurfaceCellUncenter * cellSize
				: new Vector3(x, -depth, y) * AgroWorld.FieldResolution + SoilCellUncenter * cellSize,
			Scale = new(cellSize, depth > 0 ? cellSize : 1e-6f, cellSize),
			MaterialOverride = AgroWorldGodot.UnshadedMaterial(),
		};

		SoilCellInstances[Index(x, y, depth)] = mesh;
		SimulationWorld.GodotAddChild(mesh);
	}

	private float ComputeCellMultiplier(int index) => Math.Clamp(GetWater(index) / GetWaterCapacity(index), 0f, 1f);

	static float ComputeCellScale(float multiplier) => AgroWorldGodot.SoilVisualization.AnimateSoilCellSize
		? AgroWorldGodot.SoilVisualization.SoilCellScale * AgroWorld.FieldResolution * multiplier
		: AgroWorldGodot.SoilVisualization.SoilCellScale * AgroWorld.FieldResolution;

	static readonly float FieldCellSurface = AgroWorld.FieldResolution * AgroWorld.FieldResolution;

	private void AnimateCells()
	{
		for(int x = 0; x < Size.X; ++x)
			for(int y = 0; y < Size.Y; ++y)
			{
				var ground = Height(x, y);
				//Soil cells
				for(int z = 1; z <= ground; ++z)
				{
					var idx = Index(x, y, z);
					var multiplier = ComputeCellMultiplier(idx);
					var mesh = SoilCellInstances[idx];
					mesh.Scale = Vector3.One * ComputeCellScale(multiplier);

					if (AgroWorldGodot.SoilVisualization.AnimateSoilCellColor)
						((ShaderMaterial)mesh.MaterialOverride).SetShaderParameter(AgroWorldGodot.COLOR, multiplier * AgroWorldGodot.SoilVisualization.CellColorHigh + (1f - multiplier) * AgroWorldGodot.SoilVisualization.CellColorLow);
					else
						((ShaderMaterial)mesh.MaterialOverride).SetShaderParameter(AgroWorldGodot.COLOR, AgroWorldGodot.SoilVisualization.CellColorHigh);
				}

				//Surface cell
				{
					var surfaceIdx = Index(x, y, 0);
					var height = Math.Max(1e-6f, GetWater(surfaceIdx) / (FieldCellSurface * 1e6f));
					var mesh = SoilCellInstances[surfaceIdx];
					mesh.Scale = new(CellSize.X, height, CellSize.Z);
					if (AgroWorldGodot.SoilVisualization.AnimateSoilCellColor)
					{
						var multiplier = Math.Clamp(height / AgroWorldGodot.SoilVisualization.SurfaceFullThreshold, 0f, 1f);
						((ShaderMaterial)mesh.MaterialOverride).SetShaderParameter(AgroWorldGodot.COLOR, multiplier * AgroWorldGodot.SoilVisualization.SurfaceFullColor + (1f - multiplier) * AgroWorldGodot.SoilVisualization.SurfaceEmptyColor);
					}
					else
						((ShaderMaterial)mesh.MaterialOverride).SetShaderParameter(AgroWorldGodot.COLOR, AgroWorldGodot.SoilVisualization.SurfaceFullColor);
				}
			}
	}

	private void AnimateMarkers() { }

	private void AnimateMarker(MarkerData marker) { }

	static bool IsVisible(Visibility flag) => flag == Visibility.MakeVisible || flag == Visibility.Visible;

	private void ApplyMarkerVisibility()
	{
		switch (AgroWorldGodot.SoilVisualization.MarkerVisibility)
		{
			case Visibility.MakeVisible:
			{
				for(int i = 0; i < 6; i++)
				{
					if (IsVisible(AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[i]))
					{
						SetMarkersVisibility(true, i);
						AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[i] = Visibility.Visible;
					}
					else if (AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[i] == Visibility.MakeInvisible)
						AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[i] = Visibility.Invisible;

					AgroWorldGodot.SoilVisualization.MarkerVisibility = Visibility.Visible;
				}
			}
			break;

			case Visibility.Visible:
			{
				for(int i = 0; i < 6; i++)
				{
					if (AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[i] == Visibility.MakeVisible)
					{
						SetMarkersVisibility(true, i);
						AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[i] = Visibility.Visible;
					}
					else if (AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[i] == Visibility.MakeInvisible)
					{
						SetMarkersVisibility(false, i);
						AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[i] = Visibility.Invisible;
					}
				}
			}
			break;

			case Visibility.MakeInvisible:
			{
				for(int i = 0; i < 6; i++)
					SetMarkersVisibility(false, i);

				AgroWorldGodot.SoilVisualization.MarkerVisibility = Visibility.Invisible;
			}
			break;
		}
	}

	private void ApplyCellVisibility()
	{
		//Soil cells
		if (AgroWorldGodot.SoilVisualization.SoilCellsVisibility == Visibility.MakeVisible)
		{
			SetSoilCellsVisibility(true);
			AgroWorldGodot.SoilVisualization.SoilCellsVisibility = Visibility.Visible;
		}
		else if (AgroWorldGodot.SoilVisualization.SoilCellsVisibility == Visibility.MakeInvisible)
		{
			SetSoilCellsVisibility(false);
			AgroWorldGodot.SoilVisualization.SoilCellsVisibility = Visibility.Invisible;
		}

		//Surface cells
		if (AgroWorldGodot.SoilVisualization.SurfaceCellsVisibility == Visibility.MakeVisible)
		{
			SetSurfaceCellsVisibility(true);
			AgroWorldGodot.SoilVisualization.SurfaceCellsVisibility = Visibility.Visible;
		}
		else if (AgroWorldGodot.SoilVisualization.SurfaceCellsVisibility == Visibility.MakeInvisible)
		{
			SetSurfaceCellsVisibility(false);
			AgroWorldGodot.SoilVisualization.SurfaceCellsVisibility = Visibility.Invisible;
		}
	}

	private void SetMarkersVisibility(bool flag, int dir) { }

	private void SetSurfaceCellsVisibility(bool flag)
	{
		foreach(var i in GroundAddr)
			SoilCellInstances[i].Visible = flag;
	}

	private void SetSoilCellsVisibility(bool flag)
	{
		var limit = GroundAddr[0];
		for(int i = 0; i < limit; ++i)
			SoilCellInstances[i].Visible = flag;

		for(int g = 1; g < GroundAddr.Length; ++g)
		{
			limit = GroundAddr[g];
			for(int i = GroundAddr[g - 1] + 1; i < limit; ++i)
				SoilCellInstances[i].Visible = flag;
		}
	}
}
