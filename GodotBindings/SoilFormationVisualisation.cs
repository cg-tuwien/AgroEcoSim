using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using Utils;
using System.Diagnostics;

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

public partial class SoilFormation
{
	[Newtonsoft.Json.JsonIgnore] public float[] SteamFlow;// = new float[Agents.Length,6]; //Might save some space by having only 5 elements in the nested array, but I am keeping 6 for better indexing
	[Newtonsoft.Json.JsonIgnore] public float[] WaterFlow;// = new float[Agents.Length,6];
	[Newtonsoft.Json.JsonIgnore] int[] IDToIndex;
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

		SoilCellInstances = new MeshInstance3D[Agents.Length]; //no need for multiplication here, it's a complete 3D grid
		for (int x = 0; x < SizeX; x++)
			for (int y = 0; y < SizeY; y++)
				for (int z = 0; z < SizeZ; z++)
					InitializeCell(x, y, z);

		ApplyCellVisibility();
	}

	private void InitializeMarkers()
	{
		if (AgroWorldGodot.SoilVisualization.MarkerVisibility == Visibility.Invisible)
			AgroWorldGodot.SoilVisualization.MarkerVisibility = Visibility.MakeInvisible;

		MarkerDataStorage = new();
		MarkerInstances = new MeshInstance3D[Agents.Length, 6];

		var maxID = ulong.MinValue;
		foreach(Direction dir in Enum.GetValues(typeof(Direction)))
			for (int a = 0; a < Agents.Length; a++)
			{
				InitializeMarker(a, dir);
				var id = Agents[a].ID;
				if (id < MinID) MinID = id;
				if (id > maxID) maxID = id;
			}

		WaterFlow = new float[Agents.Length * 6];
		IDToIndex = new int[maxID - MinID + 1];
		for (int a = 0; a < Agents.Length; a++)
			IDToIndex[Agents[a].ID - MinID] = a;
	}

	private void InitializeMarker(int parent_index, Direction dir)
	{
		var dirIndex = (int)dir;

		if (AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[dirIndex] == Visibility.Invisible)
			AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[dirIndex] = Visibility.MakeInvisible;

		var parent_pos = SoilCellInstances[parent_index].Position;

		var marker = new MeshInstance3D() {
			Position = parent_pos + MarkerOffsets[dirIndex],
			Rotation = Rotations[dirIndex],
			Mesh = AgroWorldGodot.SoilVisualization.MarkerShape,
			MaterialOverride = (Material)AgroWorldGodot.SoilVisualization.MarkerMaterial.Duplicate(),
		};

		MarkerInstances[parent_index, dirIndex] = marker;

		SimulationWorld.GodotAddChild(marker);

		MarkerDataStorage.Add(new(dir, parent_pos, parent_index));
	}


	static readonly Vector3 SoilCellUncenter = new(0.5f, -0.5f, 0.5f);
	static readonly Vector3 SurfaceCellUncenter = new(0.5f, 0f, 0.5f);
	private void InitializeCell(int x, int y, int z)
	{
		var cellSize = AgroWorldGodot.SoilVisualization.SoilCellScale * AgroWorld.FieldResolution;
		var mesh = new MeshInstance3D()
		{
			Mesh = z == 0 ? AgroWorldGodot.SoilVisualization.SurfaceCellShape : AgroWorldGodot.SoilVisualization.SoilCellShape,
			Position = z > 0
				? new Vector3(x, 1-z, y) * AgroWorld.FieldResolution + SoilCellUncenter * cellSize
				: new Vector3(x, 0, y) * AgroWorld.FieldResolution + SurfaceCellUncenter * cellSize,
			Scale = new(cellSize, z > 0 ? cellSize : 1e-6f, cellSize),
			MaterialOverride = AgroWorldGodot.UnshadedMaterial(),
		};
		//mesh.SetSurfaceMaterial(0, (SpatialMaterial)AgroWorldGodot.SoilVisualization.SoilCellMaterial.Duplicate());

		SoilCellInstances[Index(x, y, z)] = mesh;
		SimulationWorld.GodotAddChild(mesh);
	}

	private float ComputeCellMultiplier(int id) => Math.Clamp(Agents[id].Water / Agents[id].WaterMaxCapacity, 0f, 1f);

	static float ComputeCellScale(float multiplier) => AgroWorldGodot.SoilVisualization.AnimateSoilCellSize
		? AgroWorldGodot.SoilVisualization.SoilCellScale * AgroWorld.FieldResolution * multiplier
		: AgroWorldGodot.SoilVisualization.SoilCellScale * AgroWorld.FieldResolution;

	static readonly float FieldCellSurface = AgroWorld.FieldResolution * AgroWorld.FieldResolution;

	private void AnimateCells()
	{
		var cellSize = AgroWorldGodot.SoilVisualization.SoilCellScale * AgroWorld.FieldResolution;
		//surface cells
		for(int i = 0; i < SizeXY; ++i)
		{
			var height = Math.Max(1e-6f, (Agents[i].Water * 1e-6f) / FieldCellSurface); //the firt term converts water in gramms to m³

			SoilCellInstances[i].Scale = new Vector3(cellSize, height, cellSize);

			if (AgroWorldGodot.SoilVisualization.AnimateSoilCellColor)
			{
				var multiplier = Math.Clamp(height / AgroWorldGodot.SoilVisualization.SurfaceFullThreshold, 0f, 1f);
				((ShaderMaterial)SoilCellInstances[i].MaterialOverride).SetShaderParameter(AgroWorldGodot.COLOR, multiplier * AgroWorldGodot.SoilVisualization.SurfaceFullColor + (1f - multiplier) * AgroWorldGodot.SoilVisualization.SurfaceEmptyColor);
			}
			else
				((ShaderMaterial)SoilCellInstances[i].MaterialOverride).SetShaderParameter(AgroWorldGodot.COLOR, AgroWorldGodot.SoilVisualization.SurfaceFullColor);
		}
		//Note: Temporary solution (redundant resizing)
		//soil cells
		for(int i = SizeXY; i < SoilCellInstances.Length; ++i)
		{
			var multiplier = ComputeCellMultiplier(i);

			SoilCellInstances[i].Scale = Vector3.One * ComputeCellScale(multiplier);

			if (AgroWorldGodot.SoilVisualization.AnimateSoilCellColor)
				((ShaderMaterial)SoilCellInstances[i].MaterialOverride).SetShaderParameter(AgroWorldGodot.COLOR, multiplier * AgroWorldGodot.SoilVisualization.CellFullColor + (1f - multiplier) * AgroWorldGodot.SoilVisualization.CellEmptyColor);
			else
				((ShaderMaterial)SoilCellInstances[i].MaterialOverride).SetShaderParameter(AgroWorldGodot.COLOR, AgroWorldGodot.SoilVisualization.CellFullColor);
		}
		//((ShaderMaterial)SoilCellInstances[i].MaterialOverride).SetShaderParameter(COLOR, multiplier * Parameters.FullCellColor + (1f - multiplier) * Parameters.EmptyCellColor);
	}

	private void AnimateMarkers()
	{
		//fetch data from messages
		Array.Fill(WaterFlow, 0f);
		var messages = SoilAgent.Water_PullFrom.MessagesHistory;
		for(int i = 0; i < messages.Count; ++i) //a for will not copy the whole value type, foreach would
		{
			var amount = messages[i].Amount;
			var srcIndex = IDToIndex[messages[i].SourceID - MinID];
			var dstIndex = IDToIndex[messages[i].TargetID - MinID];
			var dir = GetDir(srcIndex, dstIndex);
			WaterFlow[srcIndex * 6 + dir] += amount;
			WaterFlow[dstIndex * 6 + InvertDir(dir)] -= amount;
		}

		foreach(var marker in MarkerDataStorage)
			AnimateMarker(marker);
	}

	private void AnimateMarker(MarkerData marker)
	{
		//var markerScale = 0f;
		var dir = (int)marker.PointingDirection;
		if (AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[dir] == Visibility.Visible)
		{
			var cellScale = ComputeCellScale(ComputeCellMultiplier(marker.CellIndex));

			var flow = WaterFlow[marker.CellIndex * 6 + dir];
			var appearanceMultiplier = Math.Clamp(flow / (Agents[marker.CellIndex].WaterMaxCapacity * SoilAgent.SoilDiffusionCoefPerTick * 5f), 0f, 1f);

			var mesh = MarkerInstances[marker.CellIndex, dir];

			if (flow == 0f) //many markers will be 0, hide them to improve performance
				mesh.Hide();
			else if (AgroWorldGodot.SoilVisualization.AnimateMarkerSize)
			{
				var markerScale = cellScale * (AgroWorldGodot.SoilVisualization.AnimateMarkerSize ? AgroWorldGodot.SoilVisualization.MarkerScale * appearanceMultiplier : AgroWorldGodot.SoilVisualization.MarkerScale);
				var offset_multiplier = (cellScale + markerScale) * 0.5f;
				mesh.Position = marker.InitialPosition + MarkerOffsets[dir] * offset_multiplier;
				mesh.Scale = Vector3.One * markerScale;
				mesh.Show();

				//((SpatialMaterial)mesh.GetSurfaceMaterial(0)).AlbedoColor = appearance_multiplier * AgroWorldGodot.SoilVisualization.FullFlowColor + (1f - appearance_multiplier) * AgroWorldGodot.SoilVisualization.NoFlowColor;

				((ShaderMaterial)mesh.MaterialOverlay).SetShaderParameter(AgroWorldGodot.COLOR, AgroWorldGodot.SoilVisualization.AnimateMarkerColor
					? appearanceMultiplier * AgroWorldGodot.SoilVisualization.MarkerFullFlowColor + (1f - appearanceMultiplier) * AgroWorldGodot.SoilVisualization.MarkerNoFlowColor
					: AgroWorldGodot.SoilVisualization.MarkerFullFlowColor);

				// if (AgroWorldGodot.SoilVisualization.MarkerVisibility == Visibility.MakeVisible)
				// 	IndividualAnimationMarkerVisibility(mesh, dir, flow == 0f);
			}
		}

		// if (flow == 0f)
		// 	mesh.Scale = Vector3.Zero;
	}

	// public void IndividualAnimationMarkerVisibility(MeshInstance3D mesh, int direction, bool vis)
	// {
	// 	if (vis && AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[direction] == Visibility.MakeVisible)
	// 		mesh.Visible = false;
	// 	else if (AgroWorldGodot.SoilVisualization.IndividualMarkerDirectionVisibility[direction] == Visibility.MakeVisible)
	// 		mesh.Visible = true;
	// }

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

	private void SetMarkersVisibility(bool flag, int dir)
	{
		for(int i = 0; i < SoilCellInstances.Length; i++)
			MarkerInstances[i, dir].Visible = flag;
	}

	private void SetSurfaceCellsVisibility(bool flag)
	{
		for(int i = 0; i < SizeXY; i++)
			SoilCellInstances[i].Visible = flag;
	}

	private void SetSoilCellsVisibility(bool flag)
	{
		for(int i = SizeXY; i < SoilCellInstances.Length; i++)
			SoilCellInstances[i].Visible = flag;
	}
}
