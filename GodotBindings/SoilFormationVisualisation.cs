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
	static Vector3[] Rotations = {
		new Vector3(0, 0, -MathF.PI/2),
		Vector3.Zero,
		new Vector3(-MathF.PI/2, 0, 0),
		new Vector3(0, 0, MathF.PI/2),
		new Vector3(0, 0, MathF.PI),
		new Vector3(MathF.PI/2,0,0)
	};

	//CodeReview: This is used frequently in AnimateMaker, but there is no noeed to recompute it for each marker in each frame
	static Vector3[] MarkerOffsets = { Vector3.Right, Vector3.Up, Vector3.Forward, Vector3.Left, Vector3.Down, Vector3.Back };

	int GetDir(int srcIndex, int dstIndex)
	{
		var coordsDiff = Coords(dstIndex) - Coords(srcIndex);
		return coordsDiff switch
		{
			Vector3i(1, 0, 0) => 3, //CodeReview: this is really strange, didn't find the reason yet
			Vector3i(0, 1, 0) => 2,
			Vector3i(0, 0, 1) => 1,
			Vector3i(-1, 0, 0) => 0,
			Vector3i(0, -1, 0) => 5,
			Vector3i(0, 0, -1) => 4,
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

	private void InitializeVisualisation(){
		InitializeCells();
		InitializeMarkers();
	}

	private void InitializeCells(){
		SoilCellInstances = new MeshInstance[Agents.Length]; //no need for multiplication here, it's a complete 3D grid
		for (int x = 0; x < SizeX; x++)
			for (int y = 0; y < SizeY; y++)
				for (int z = 0; z < SizeZ; z++)
					InitializeCell(x, y, z);
	}

	private void InitializeMarkers(){
		MarkerDataStorage = new();
		MarkerInstances = new MeshInstance[Agents.Length, 6];

		var maxID = ulong.MinValue;
		foreach(Direction dir in Enum.GetValues(typeof(Direction))) //CodeReview: this iterates an array, not sure if it is created each time GetValues is called, so I make it the outer loop
			for (int a = 0; a < Agents.Length; a++) //CodeReview: no need for 3 for cycles + Index()
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

	private void InitializeMarker(int parent_index, Direction dir){
		var parent_pos = SoilCellInstances[parent_index].Translation;
		var dirIndex = (int)dir;
		var marker = new MeshInstance() {
			Translation = parent_pos + MarkerOffsets[dirIndex],
			Rotation = Rotations[dirIndex],
			Mesh = parameters.MarkerShape
		};
		marker.SetSurfaceMaterial(0, (SpatialMaterial)parameters.MarkerMaterial.Duplicate());

		//CodeReview: This is the correct order. Otherwise you make one unnecessary array lookup.
		MarkerInstances[parent_index, dirIndex] = marker;

		SimulationWorld.GodotAddChild(marker);

		MarkerDataStorage.Add(new(dir, parent_pos, parent_index));
	}

	private void InitializeCell(int x, int y, int z){
		var mesh = new MeshInstance()
		{
			Mesh = parameters.SoilCellShape,
			Translation = new Vector3(x, -z, y) * AgroWorld.FieldResolution,
			Scale = Vector3.One * (parameters.SoilCellScale * AgroWorld.FieldResolution),
		};
		mesh.SetSurfaceMaterial(0, (SpatialMaterial)parameters.SoilCellMaterial.Duplicate());

		SoilCellInstances[Index(x, y, z)] = mesh;
		SimulationWorld.GodotAddChild(mesh);
	}

	private void AnimateCells()
	{
		for(int i = 0; i < SoilCellInstances.Length; ++i)
		{
			var multiplier = Math.Clamp(Agents[i].Water / Agents[i].WaterMaxCapacity, 0f, 1f);

			SoilCellInstances[i].Scale = Vector3.One * (parameters.SoilCellScale * AgroWorld.FieldResolution * multiplier);
			((SpatialMaterial)SoilCellInstances[i].GetSurfaceMaterial(0)).AlbedoColor = multiplier * parameters.FullCellColor + (1f - multiplier) * parameters.EmptyCellColor;
		}
	}

	private void AnimateMarkers()
	{
		//fetch data from messages
		Array.Fill(WaterFlow, 0f);
		var transactions = SoilAgent.Water_PullFrom.TransactionsHistory;
		for(int i = 0; i < transactions.Count; ++i) //a for will not copy the whole value type, foreach would
		{
			var amount = transactions[i].Amount;
			var srcIndex = IDToIndex[transactions[i].SourceID - MinID];
			var dstIndex = IDToIndex[transactions[i].TargetID - MinID];
			var dir = GetDir(srcIndex, dstIndex);
			WaterFlow[srcIndex * 6 + dir] -= amount;
			WaterFlow[dstIndex * 6 + InvertDir(dir)] += amount;
		}

		foreach(var marker in MarkerDataStorage)
			AnimateMarker(marker);
	}

	private void AnimateMarker(MarkerData marker)
	{
		float marker_scale = 0f;
		var dir = (int)marker.PointingDirection;

		var cell_scale = parameters.SoilCellScale * AgroWorld.FieldResolution * Math.Clamp(Agents[marker.CellIndex].Water/Agents[marker.CellIndex].WaterMaxCapacity, 0f, 1f);

		var flow = WaterFlow[marker.CellIndex * 6 + dir];
		var appearance_multiplier = Math.Clamp(flow / (Agents[marker.CellIndex].WaterMaxCapacity * SoilAgent.SoilDiffusionCoefPerTick * 5f), 0f, 1f);

		var mesh = MarkerInstances[marker.CellIndex, dir];
		//CodeReview: Many markers will be zero! Perhaps just use the else branch to hide them
		if (parameters.AnimateMarkerSize && flow == 0f)
		{
			mesh.Visible = false;
			mesh.Scale = Vector3.Zero;
		}
		else
		{
			//CodeReview: for simple conditions better use ? : then if else
			marker_scale = cell_scale * (parameters.AnimateMarkerSize ? parameters.MarkerScale * appearance_multiplier : parameters.MarkerScale);
			//CodeReview: this way you can save one division
			var offset_multiplier = (cell_scale + marker_scale) * 0.5f;
			mesh.Translation = marker.InitialPosition + MarkerOffsets[dir] * offset_multiplier;
			mesh.Scale = Vector3.One * marker_scale;
			mesh.Visible = true;

			((SpatialMaterial)mesh.GetSurfaceMaterial(0)).AlbedoColor = appearance_multiplier * parameters.FullFlowColor + (1f - appearance_multiplier) * parameters.NoFlowColor;
		}
	}

	private void SolveVisibility(){
		if(parameters.MarkerVisibility == visibility.visible){
			SetMarkersVisibility(true);
			parameters.MarkerVisibility = visibility.visible_waiting;
		}
		else if(parameters.MarkerVisibility == visibility.invisible){
			SetMarkersVisibility(false);
			parameters.MarkerVisibility = visibility.invisible_waiting;
		}

		if(parameters.SoilCellsVisibility == visibility.visible){
			SetCellsVisibility(true);
			parameters.SoilCellsVisibility = visibility.visible_waiting;
		}
		else if(parameters.SoilCellsVisibility == visibility.invisible){
			SetCellsVisibility(false);
			parameters.SoilCellsVisibility = visibility.invisible_waiting;
		}

		for(int i = 0; i < 6; i++){
			if(parameters.IndividualMarkerDirectionVisibility[i] == visibility.visible){
				SetMarkersVisibility(true,(Direction)i);
				parameters.IndividualMarkerDirectionVisibility[i] = visibility.visible_waiting;
			}
			else if(parameters.IndividualMarkerDirectionVisibility[i] == visibility.invisible){
				SetMarkersVisibility(false,(Direction)i);
				parameters.IndividualMarkerDirectionVisibility[i] = visibility.invisible_waiting;
			}
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
