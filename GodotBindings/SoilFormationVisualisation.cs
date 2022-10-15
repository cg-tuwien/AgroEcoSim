using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;
using Utils;

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
	public float[] SteamFlow;// = new float[Agents.Length,6]; //Might save some space by having only 5 elements in the nested array, but I am keeping 6 for better indexing
	public float[] WaterFlow;// = new float[Agents.Length,6];
	int[] IDToIndex;
	ulong MinID = ulong.MaxValue;

	//CodeReview: This is not local to InitializeMarkers, but it is always the same.
	//  So it could be reused by other instances of this class (e.g. if having more fields).
	//  Hence, it makes more sense to make it static or const (for Vector3 only static is possible).
	static readonly Vector3[] Rotations = {
		new Vector3(0, 0, -MathF.PI/2),
		Vector3.Zero,
		new Vector3(-MathF.PI/2, 0, 0),
		new Vector3(0, 0, MathF.PI/2),
		new Vector3(MathF.PI, 0, 0),
		new Vector3(MathF.PI/2, 0, 0)
	};

	//CodeReview: This is used frequently in AnimateMaker, but there is no noeed to recompute it for each marker in each frame
	static readonly Vector3[] MarkerOffsets = { Vector3.Right, Vector3.Up, Vector3.Forward, Vector3.Left, Vector3.Down, Vector3.Back };

	int GetDir(int srcIndex, int dstIndex)
	{
		var coordsDiff = Coords(dstIndex) - Coords(srcIndex);
		return coordsDiff switch
		{
			Vector3i(1, 0, 0) => 0,
			Vector3i(0, 1, 0) => 5,
			Vector3i(0, 0, 1) => 4,
			Vector3i(-1, 0, 0) => 3,
			Vector3i(0, -1, 0) => 2,
			Vector3i(0, 0, -1) => 1,
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

	private void InitializeVisualisation()
	{
		InitializeCells();
		InitializeMarkers();
	}

	private void InitializeCells()
	{
		SoilCellInstances = new MeshInstance[Agents.Length]; //no need for multiplication here, it's a complete 3D grid
		for (int x = 0; x < SizeX; x++)
			for (int y = 0; y < SizeY; y++)
				for (int z = 0; z < SizeZ; z++)
					InitializeCell(x, y, z);
	}

	private void InitializeMarkers()
	{
		MarkerDataStorage = new();
		MarkerInstances = new MeshInstance[Agents.Length, 6];

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
		var parent_pos = SoilCellInstances[parent_index].Translation;
		var dirIndex = (int)dir;

		var marker = new MeshInstance() {
			Translation = parent_pos + MarkerOffsets[dirIndex],
			Rotation = Rotations[dirIndex],
			Mesh = Parameters.MarkerShape
		};
		marker.SetSurfaceMaterial(0, (SpatialMaterial)Parameters.MarkerMaterial.Duplicate());

		MarkerInstances[parent_index, dirIndex] = marker;

		SimulationWorld.GodotAddChild(marker);

		MarkerDataStorage.Add(new(dir, parent_pos, parent_index));
	}

	static Vector3 SoilUncenter = new(0.5f, -0.5f, 0.5f);
	private void InitializeCell(int x, int y, int z)
	{
		var cellSize = Parameters.SoilCellScale * AgroWorld.FieldResolution;
		var mesh = new MeshInstance()
		{
			Mesh = Parameters.SoilCellShape,
			Translation = new Vector3(x, -z, y) * AgroWorld.FieldResolution + SoilUncenter * cellSize,
			Scale = new(cellSize, cellSize, cellSize),
		};
		mesh.SetSurfaceMaterial(0, (SpatialMaterial)Parameters.SoilCellMaterial.Duplicate());

		SoilCellInstances[Index(x, y, z)] = mesh;
		SimulationWorld.GodotAddChild(mesh);
	}

    private float ComputeCellMultiplier(int id) => Math.Clamp(Agents[id].Water / Agents[id].WaterMaxCapacity, 0f, 1f);

    private float ComputeCellScale(float multiplier) => Parameters.AnimateSoilCellSize
		? Parameters.SoilCellScale * AgroWorld.FieldResolution * multiplier
		: Parameters.SoilCellScale * AgroWorld.FieldResolution;

    private void AnimateCells()
	{
		//Note: Temporary solution (redundant resizing)
		for(int i = 0; i < SoilCellInstances.Length; ++i)
		{
			var multiplier = ComputeCellMultiplier(i);

			SoilCellInstances[i].Scale = Vector3.One * ComputeCellScale(multiplier);

			if (Parameters.AnimateSoilCellColor)
				((SpatialMaterial)SoilCellInstances[i].GetSurfaceMaterial(0)).AlbedoColor = multiplier * Parameters.FullCellColor + (1f - multiplier) * Parameters.EmptyCellColor;
			else
				((SpatialMaterial)SoilCellInstances[i].GetSurfaceMaterial(0)).AlbedoColor = Parameters.FullCellColor;
		}
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
		var markerScale = 0f;
		var dir = (int)marker.PointingDirection;
		var cellScale = ComputeCellScale(ComputeCellMultiplier(marker.CellIndex));

		var flow = WaterFlow[marker.CellIndex * 6 + dir];
		var appearanceMultiplier = Math.Clamp(flow / (Agents[marker.CellIndex].WaterMaxCapacity * SoilAgent.SoilDiffusionCoefPerTick * 5f), 0f, 1f);

		var mesh = MarkerInstances[marker.CellIndex, dir];

		//HUD if (Parameters.AnimateMarkerSize && flow == 0f) //many markers will be 0, hide them to improve performance
		//HUD {
		//HUD	mesh.Visible = false;
		//HUD	mesh.Scale = Vector3.Zero;
		//HUD }
		//HUD else
		//HUD {
			markerScale = cellScale * (Parameters.AnimateMarkerSize ? Parameters.MarkerScale * appearanceMultiplier : Parameters.MarkerScale);
			var offset_multiplier = (cellScale + markerScale) * 0.5f;
			mesh.Translation = marker.InitialPosition + MarkerOffsets[dir] * offset_multiplier;
			mesh.Scale = Vector3.One * markerScale;
		//HUD 	mesh.Visible = true;

		//HUD	((SpatialMaterial)mesh.GetSurfaceMaterial(0)).AlbedoColor = appearance_multiplier * Parameters.FullFlowColor + (1f - appearance_multiplier) * Parameters.NoFlowColor;
		//HUD }

		if (Parameters.AnimateMarkerColor)
			((SpatialMaterial)mesh.GetSurfaceMaterial(0)).AlbedoColor = appearanceMultiplier * Parameters.FullFlowColor + (1f - appearanceMultiplier) * Parameters.NoFlowColor;
		else
			((SpatialMaterial)mesh.GetSurfaceMaterial(0)).AlbedoColor = Parameters.FullFlowColor;

		if (Parameters.MarkerVisibility == Visibility.VisibleWaiting)
			SolveAnimationMarkerVisibility(mesh, dir, flow == 0f);

		if (flow == 0f)
			mesh.Scale = Vector3.Zero;
	}

	public void SolveAnimationMarkerVisibility(MeshInstance mesh, int direction, bool vis)
	{
		if (vis && Parameters.IndividualMarkerDirectionVisibility[direction] == Visibility.VisibleWaiting)
			mesh.Visible = false;
		else if (Parameters.IndividualMarkerDirectionVisibility[direction] == Visibility.VisibleWaiting)
			mesh.Visible = true;
	}

	public void SolveVisibility()
	{
		SolveCellVisibility();
		SolveMarkerVisibility();
	}

	private void SolveMarkerVisibility()
	{
		if (Parameters.MarkerVisibility == Visibility.Visible)
		{
			for(int i = 0; i < 6; i++)
			{
				if (Parameters.IndividualMarkerDirectionVisibility[i] == Visibility.VisibleWaiting)
					SetMarkersVisibility(true, (Direction)i);
				else if (Parameters.IndividualMarkerDirectionVisibility[i] == Visibility.InvisibleWaiting)
					SetMarkersVisibility(false, (Direction)i);

				Parameters.MarkerVisibility = Visibility.VisibleWaiting;
			}
		}
		else if (Parameters.MarkerVisibility == Visibility.Invisible)
			SetMarkersVisibility(false);

		if (Parameters.MarkerVisibility == Visibility.VisibleWaiting)
			for(int i = 0; i < 6; i++)
			{
				if (Parameters.IndividualMarkerDirectionVisibility[i] == Visibility.Visible)
				{
					SetMarkersVisibility(true,(Direction)i);
					Parameters.IndividualMarkerDirectionVisibility[i] = Visibility.VisibleWaiting;
				}
				else if (Parameters.IndividualMarkerDirectionVisibility[i] == Visibility.Invisible)
				{
					SetMarkersVisibility(false,(Direction)i);
					Parameters.IndividualMarkerDirectionVisibility[i] = Visibility.InvisibleWaiting;
				}
			}
	}

	private void SolveCellVisibility()
	{
		if (Parameters.SoilCellsVisibility == Visibility.Visible)
		{
			SetCellsVisibility(true);
			Parameters.SoilCellsVisibility = Visibility.VisibleWaiting;
		}
		else if (Parameters.SoilCellsVisibility == Visibility.Invisible)
		{
			SetCellsVisibility(false);
			Parameters.SoilCellsVisibility = Visibility.InvisibleWaiting;
		}
	}

	private void SetMarkersVisibility(bool flag)
	{
		for(int i = 0; i < 6; i++)
			SetMarkersVisibility(flag,(Direction)i);
	}

	private void SetMarkersVisibility(bool flag, Direction dir)
	{
		for(int i = 0; i < SoilCellInstances.Length; i++)
			MarkerInstances[i,(int)dir].Visible = flag;
	}


	private void SetCellsVisibility(bool flag)
	{
		for(int i = 0; i < SoilCellInstances.Length; i++)
			SoilCellInstances[i].Visible = flag;
	}

}
