using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

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


public partial class SoilFormation
{

    private void InitializeVisualisation(){
        InitializeCells();
        InitializeMarkers();
    }

    private void InitializeCells(){
        SoilCellInstances = new MeshInstance[SizeX*SizeY*SizeZ];
        for (int x = 0; x < SizeX; x++)
        {
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    InitializeCell(x,y,z);
                }
            }
        }
    }

    private void InitializeMarkers(){
        MarkerDataStorage = new List<MarkerData>();
        MarkerInstances = new MeshInstance[SizeX*SizeY*SizeZ,6];
        Vector3[] rotation = new Vector3[]{
            new Vector3(0,0,-MathF.PI/2),
            new Vector3(0,0,0),
            new Vector3(-MathF.PI/2,0,0),
            new Vector3(0,0,MathF.PI/2),
            new Vector3(-MathF.PI,0,0),
            new Vector3(MathF.PI/2,0,0)
        };

        for (int x = 0; x < SizeX; x++)
        {
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    for(int i = 0; i < 6; i ++){
                        InitializeMarker(Index(x,y,z),rotation[i],(direction)i);
                    }
                }
            }
        }

    }

    private void InitializeMarker(int parent_index, Vector3 rotation, direction dir){
        Vector3 parent_pos = SoilCellInstances[parent_index].Translation;
        MarkerInstances[parent_index,(int) dir] = new MeshInstance();
        MeshInstance marker = MarkerInstances[parent_index,(int) dir];
        marker.Translation = parent_pos;
        marker.Mesh = parameters.MarkerShape;
        marker.RotateX(rotation.x);
        marker.RotateY(rotation.y);
        marker.RotateZ(rotation.z);
        marker.SetSurfaceMaterial(0,(SpatialMaterial)parameters.MarkerMaterial.Duplicate());

        SimulationWorld.GodotAddChild(marker);

        MarkerDataStorage.Add(new MarkerData(dir,parent_pos,parent_index));
    }

    private void InitializeCell(int x, int y, int z){
        SoilCellInstances[Index(x,y,z)] = new MeshInstance();
        SoilCellInstances[Index(x,y,z)].Mesh = parameters.SoilCellShape;
        SoilCellInstances[Index(x,y,z)].Translation = new Vector3(x,-z,y) * AgroWorld.FieldResolution;
        SoilCellInstances[Index(x,y,z)].Scale = Vector3.One * parameters.SoilCellScale * AgroWorld.FieldResolution;
        SoilCellInstances[Index(x,y,z)].SetSurfaceMaterial(0,(SpatialMaterial)parameters.SoilCellMaterial.Duplicate());

        SimulationWorld.GodotAddChild(SoilCellInstances[Index(x,y,z)]);
    }

    private void AnimateCells(){
        for(int i = 0; i < SoilCellInstances.Length; ++i){
            float multiplier = Math.Min(Agents[i].WaterMaxCapacity,Math.Max(0,Agents[i].Water))/Agents[i].WaterMaxCapacity;

            SoilCellInstances[i].Scale = Vector3.One * parameters.SoilCellScale * AgroWorld.FieldResolution * multiplier;
            ((SpatialMaterial)SoilCellInstances[i].GetSurfaceMaterial(0)).AlbedoColor = multiplier*parameters.FullCellColor + (1-multiplier)*parameters.EmptyCellColor;
        }
    }

    private void AnimateMarkers(){
        foreach(MarkerData marker in MarkerDataStorage){
            AnimateMarker(marker);
        }
    }

    private void AnimateMarker(MarkerData marker){
        float marker_scale = 0f;



        Vector3[] offset = {Vector3.Right,Vector3.Up,Vector3.Forward,Vector3.Left,Vector3.Down,Vector3.Back};
        float cell_scale = parameters.SoilCellScale * AgroWorld.FieldResolution * Math.Min(Agents[marker.MarkerOwnerID].WaterMaxCapacity,Math.Max(0,Agents[marker.MarkerOwnerID].Water))/Agents[marker.MarkerOwnerID].WaterMaxCapacity;
        
        float flow = water_flow[marker.MarkerOwnerID,(int)marker.pointing_direction];
        float appearance_multiplier = Math.Max(0,Math.Min(parameters.WaterFlowCapacity,flow))/parameters.WaterFlowCapacity;

        if(parameters.AnimateMarkerSize){
            marker_scale = cell_scale*parameters.MarkerScale*appearance_multiplier;
        }
        else{
            marker_scale = cell_scale*parameters.MarkerScale;
        }

        float offset_multiplier = cell_scale/2 + marker_scale/2;
        var mesh = MarkerInstances[marker.MarkerOwnerID,(int)marker.pointing_direction];
        mesh.Translation = marker.InitialPosition + offset[(int)marker.pointing_direction]*offset_multiplier;
        mesh.Scale = Vector3.One * marker_scale;
        ((SpatialMaterial)mesh.GetSurfaceMaterial(0)).AlbedoColor = appearance_multiplier*parameters.FullFlowColor + (1-appearance_multiplier)*parameters.NoFlowColor;
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
                SetMarkersVisibility(true,(direction)i);
                parameters.IndividualMarkerDirectionVisibility[i] = visibility.visible_waiting;
            }
            else if(parameters.IndividualMarkerDirectionVisibility[i] == visibility.invisible){
                SetMarkersVisibility(false,(direction)i);
                parameters.IndividualMarkerDirectionVisibility[i] = visibility.invisible_waiting;
            }
        }

    }

    private void SetMarkersVisibility(bool flag){
        for(int i = 0; i < 6; i++){
            SetMarkersVisibility(flag,(direction)i);
        }
    }

    private void SetMarkersVisibility(bool flag, direction dir){
        for(int i = 0; i < SoilCellInstances.Length; i++){
            MarkerInstances[i,(int)dir].Visible = flag;
        }
    }


    private void SetCellsVisibility(bool flag){
        for(int i = 0; i < SoilCellInstances.Length; i++){
            SoilCellInstances[i].Visible = flag;
        }
    }

}