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
        MarkerInstances = new Tuple<MeshInstance[,,], MeshInstance[,,], MeshInstance[,,]>(
            new MeshInstance[SizeX+1,SizeY,SizeZ],
            new MeshInstance[SizeX,SizeY,SizeZ+1],
            new MeshInstance[SizeX,SizeY+1,SizeZ]
        );

        //Todo: Implement cleaner solution, this is an prototype

        for (int x = 0; x < SizeX; x++)
        {
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z <= SizeZ; z++)
                {
                    marker_direction dir;
                    if(z==SizeZ){
                        dir = marker_direction.negative;
                    }
                    else if(z == 0){
                        dir = marker_direction.positive;
                    }
                    else{
                        dir = marker_direction.bidirectional;
                    }
                    InitializeMarker(dir,marker_axis.Y,x,y,z);
                }
            }
        }

        for (int x = 0; x <= SizeX; x++)
        {
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    marker_direction dir;
                    if(x==SizeX){
                        dir = marker_direction.negative;
                    }
                    else if(z == 0){
                        dir = marker_direction.positive;
                    }
                    else{
                        dir = marker_direction.bidirectional;
                    }
                    InitializeMarker(dir,marker_axis.X,x,y,z);
                }
            }
        }

        for (int x = 0; x < SizeX; x++)
        {
            for (int y = 0; y <= SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    marker_direction dir;
                    if(y==SizeY){
                        dir = marker_direction.negative;
                    }
                    else if(y == 0){
                        dir = marker_direction.positive;
                    }
                    else{
                        dir = marker_direction.bidirectional;
                    }
                    InitializeMarker(dir,marker_axis.Z,x,y,z);
                }
            }
        }
    }

    private void InitializeMarker(marker_direction dir, marker_axis axis, int x, int y, int z){
        //Todo: Implement cleaner solution, this is an prototype

        if(axis == marker_axis.X){
            var right_cell = new Vector3(x,-z,y) * AgroWorld.FieldResolution;
            var marker_position = right_cell + Vector3.Left*AgroWorld.FieldResolution/2;
            
            Tuple<int,int> positive_flow_index = new Tuple<int, int>(Index(x,y,z),3);
            Tuple<int,int> negative_flow_index = new Tuple<int, int>(Index(x-1,y,z),0);

            MarkerInstances.Item1[x,y,z] = new MeshInstance();
            MarkerInstances.Item1[x,y,z].Mesh = parameters.MarkerShape;
            MarkerInstances.Item1[x,y,z].Translation = marker_position;
            MarkerInstances.Item1[x,y,z].Scale = Vector3.One * parameters.LateralMarkerScale;
            MarkerInstances.Item1[x,y,z].RotateZ(Mathf.Pi/2);

            MarkerInstances.Item1[x,y,z].SetSurfaceMaterial(0,(SpatialMaterial)parameters.MarkerMaterial.Duplicate()); 

            SimulationWorld.GodotAddChild(MarkerInstances.Item1[x,y,z]);

            MarkerDataStorage.Add(new MarkerData(dir,new Tuple<int,int,int>(x,y,z),positive_flow_index,negative_flow_index,axis));
        }
        else if(axis == marker_axis.Y){
            var cell_below_position = new Vector3(x,-z,y) * AgroWorld.FieldResolution;
            var marker_position = cell_below_position + Vector3.Up*AgroWorld.FieldResolution/2;
            
            Tuple<int,int> positive_flow_index = new Tuple<int, int>(Index(x,y,z),1);
            Tuple<int,int> negative_flow_index = new Tuple<int, int>(Index(x,y,z-1),4);

            MarkerInstances.Item2[x,y,z] = new MeshInstance();
            MarkerInstances.Item2[x,y,z].Mesh = parameters.MarkerShape;
            MarkerInstances.Item2[x,y,z].Translation = marker_position;
            MarkerInstances.Item2[x,y,z].Scale = Vector3.One * parameters.DownwardMarkerScale;

            MarkerInstances.Item2[x,y,z].SetSurfaceMaterial(0,(SpatialMaterial)parameters.MarkerMaterial.Duplicate()); 

            SimulationWorld.GodotAddChild(MarkerInstances.Item2[x,y,z]);

            MarkerDataStorage.Add(new MarkerData(dir,new Tuple<int,int,int>(x,y,z),positive_flow_index,negative_flow_index,axis));
        }
        else if(axis == marker_axis.Z){
            var front_cell = new Vector3(x,-z,y) * AgroWorld.FieldResolution;
            var marker_position = front_cell + Vector3.Back*AgroWorld.FieldResolution/2;
            
            Tuple<int,int> positive_flow_index = new Tuple<int, int>(Index(x,y,z),5);
            Tuple<int,int> negative_flow_index = new Tuple<int, int>(Index(x,y-1,z),2);

            MarkerInstances.Item3[x,y,z] = new MeshInstance();
            MarkerInstances.Item3[x,y,z].Mesh = parameters.MarkerShape;
            MarkerInstances.Item3[x,y,z].Translation = marker_position;
            MarkerInstances.Item3[x,y,z].Scale = Vector3.One * parameters.LateralMarkerScale;
            MarkerInstances.Item3[x,y,z].RotateX(Mathf.Pi/2);

            MarkerInstances.Item3[x,y,z].SetSurfaceMaterial(0,(SpatialMaterial)parameters.MarkerMaterial.Duplicate()); 

            SimulationWorld.GodotAddChild(MarkerInstances.Item3[x,y,z]);

            MarkerDataStorage.Add(new MarkerData(dir,new Tuple<int,int,int>(x,y,z),positive_flow_index,negative_flow_index,axis));
        }

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
            float multiplier = Math.Min(parameters.CellCapacity,Math.Max(0,Agents[i].Water))/parameters.CellCapacity;

            SoilCellInstances[i].Scale = Vector3.One * parameters.SoilCellScale * AgroWorld.FieldResolution * multiplier;
            ((SpatialMaterial)SoilCellInstances[i].GetSurfaceMaterial(0)).AlbedoColor = multiplier*parameters.FullCellColor + (1-multiplier)*parameters.EmptyCellColor;
        }
    }

    private void AnimateMarkers(){
        for(int i = 0; i < MarkerDataStorage.Count; ++i){
            var data = MarkerDataStorage[i];
            float flow;

            if(data.MarkerDireciton == marker_direction.negative){
                flow = water_flow[data.NegativeFlowIndex.Item1,data.NegativeFlowIndex.Item2];
            }
            else if(data.MarkerDireciton == marker_direction.positive){
                flow = water_flow[data.PositiveFlowIndex.Item1,data.PositiveFlowIndex.Item2];
            }
            else{
                flow = water_flow[data.PositiveFlowIndex.Item1,data.PositiveFlowIndex.Item2]- water_flow[data.NegativeFlowIndex.Item1,data.NegativeFlowIndex.Item2];
            }

            float multiplier = Math.Min(parameters.WaterFlowCapacity,Math.Max(0,Math.Abs(flow)))/parameters.WaterFlowCapacity;

            switch(data.MarkerAxis){
                case marker_axis.X:
                    AnimateMarker(Vector3.Up,flow,multiplier,data,MarkerInstances.Item1);
                    break;
                case marker_axis.Y:
                    AnimateMarker(Vector3.Forward,flow,multiplier,data,MarkerInstances.Item2);
                    break;
                case marker_axis.Z:
                    AnimateMarker(Vector3.Up,flow,multiplier,data,MarkerInstances.Item3);
                    break;
            }
        }
    }

    private void AnimateMarker(Vector3 rot_axis, float flow, float multiplier, MarkerData marker_data, MeshInstance[,,] location){
        var mesh_instance = location[marker_data.MeshInstanceIndex.Item1,marker_data.MeshInstanceIndex.Item2,marker_data.MeshInstanceIndex.Item3];
        
        Color color = multiplier * parameters.FullFlowColor + (1-multiplier) * parameters.NoFlowColor;

        if(marker_data.MarkerAxis == marker_axis.Y){
            if(parameters.AnimateDownwardMarkerSize) mesh_instance.Scale = Vector3.One * multiplier * parameters.DownwardMarkerScale;
            if(parameters.AnimateDownwardMarkerColor) ((SpatialMaterial)mesh_instance.GetSurfaceMaterial(0)).AlbedoColor = color;
        }
        else{
            if(parameters.AnimateLateralMarkerSize) mesh_instance.Scale = Vector3.One * multiplier * parameters.LateralMarkerScale;
            if(parameters.AnimateLateralMarkerColor) ((SpatialMaterial)mesh_instance.GetSurfaceMaterial(0)).AlbedoColor = color;
        }


        if((flow < 0 && !marker_data.was_negative) || (flow > 0 && marker_data.was_negative)){
            mesh_instance.Rotate(rot_axis,Mathf.Pi);
            marker_data.was_negative = !marker_data.was_negative;
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

        if(parameters.DownwardMarkerVisibility == visibility.visible){
            SetDownwardMarkresVisibility(true);
            parameters.DownwardMarkerVisibility = visibility.visible_waiting;
        }
        else if(parameters.DownwardMarkerVisibility == visibility.invisible){
            SetDownwardMarkresVisibility(false);
            parameters.DownwardMarkerVisibility = visibility.invisible_waiting;
        }

        if(parameters.LateralMarkerVisibility == visibility.visible){ 
            SetLateralMarkersVisibility(true);
            parameters.LateralMarkerVisibility = visibility.visible_waiting;
        }
        else if(parameters.LateralMarkerVisibility == visibility.invisible){
            SetLateralMarkersVisibility(false);
            parameters.LateralMarkerVisibility = visibility.invisible_waiting;
        }
    }

    private void SetMarkersVisibility(bool flag){
        SetLateralMarkersVisibility(flag);
        SetDownwardMarkresVisibility(flag);
    }

    private void SetLateralMarkersVisibility(bool flag){
        foreach(MeshInstance m in MarkerInstances.Item1){
            m.Visible = flag;
        }
        foreach(MeshInstance m in MarkerInstances.Item3){
            m.Visible = flag;
        }
    }

    private void SetDownwardMarkresVisibility(bool flag){
        foreach(MeshInstance m in MarkerInstances.Item2){
            m.Visible = flag;
        }
    }

    private void SetCellsVisibility(bool flag){
        for(int i = 0; i < SoilCellInstances.Length; i++){
            SoilCellInstances[i].Visible = flag;
        }
    }

}