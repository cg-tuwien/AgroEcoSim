using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;


public partial class SoilFormation
{

    private void InitializeVisualisation(){
        SoilCellInstances = new MeshInstance[SizeX*SizeY*SizeZ];
        MarkerInstancesX = new MeshInstance[SizeX+1,SizeY,SizeZ];
        MarkerInstancesY = new MeshInstance[SizeX,SizeY,SizeZ+1];
        MarkerInstancesZ = new MeshInstance[SizeX,SizeY+1,SizeZ];

        MarkerDataStorage = new List<MarkerData>();
        
        // MarkerInstances = new MeshInstance[SoilCellInstances.Length*6];
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

        // for (int x = 0; x < SizeX; x++)
        // {
        //     for (int y = 0; y <= SizeY; y++)
        //     {
        //         for (int z = 0; z < SizeZ; z++)
        //         {
        //             marker_direction dir;
        //             if(y==SizeY){
        //                 dir = marker_direction.negative;
        //             }
        //             else if(y == 0){
        //                 dir = marker_direction.positive;
        //             }
        //             else{
        //                 dir = marker_direction.bidirectional;
        //             }
        //             InitializeMarker(dir,marker_axis.Z,x,y,z);
        //         }
        //     }
        // }

        // for (int x = 0; x <= SizeX; x++)
        // {
        //     for (int y = 0; y < SizeY; y++)
        //     {
        //         for (int z = 0; z < SizeZ; z++)
        //         {
        //             marker_direction dir;
        //             if(x==SizeX){
        //                 dir = marker_direction.negative;
        //             }
        //             else if(x == 0){
        //                 dir = marker_direction.positive;
        //             }
        //             else{
        //                 dir = marker_direction.bidirectional;
        //             }
        //             InitializeMarker(dir,marker_axis.X,x,y,z);
        //         }
        //     }
        // }


    }

    private void InitializeMarker(marker_direction dir, marker_axis axis, int x, int y, int z){
        if(axis == marker_axis.X){
            // var cell_right_position = new Vector3(x,-z,y)*AgroWorld.FieldResolution;
            // var marker_position = cell_right_position + Vector3.Left*AgroWorld.FieldResolution/2;

            // Tuple<int,int> positive_flow_index = new Tuple<int, int>(Index(x,y,z),3); //TODO fix this
            // Tuple<int,int> negative_flow_index = new Tuple<int, int>(Index(x-1,y,z),1);

            // MarkerInstancesX[x,y,z] = new MeshInstance();
            // MarkerInstancesX[x,y,z].Mesh = parameters.MarkerShape;
            // MarkerInstancesX[x,y,z].Translation = marker_position;
            // MarkerInstancesX[x,y,z].Scale = Vector3.One * 0.025f; 

            // MarkerInstancesX[x,y,z].SetSurfaceMaterial(0,parameters.MarkerMaterial); 

            // MarkerInstancesX[x,y,z].RotateZ(Mathf.Pi/2f);

            // SimulationWorld.GodotAddChild(MarkerInstancesX[x,y,z]);

            // MarkerDataStorage.Add(new MarkerData(dir,new Tuple<int,int,int>(x,y,z),positive_flow_index,negative_flow_index,axis));

        }
        else if(axis == marker_axis.Y){
            var cell_below_position = new Vector3(x,-z,y) * AgroWorld.FieldResolution;
            var marker_position = cell_below_position + Vector3.Up*AgroWorld.FieldResolution/2;
            
            Tuple<int,int> positive_flow_index = new Tuple<int, int>(Index(x,y,z),1);
            Tuple<int,int> negative_flow_index = new Tuple<int, int>(Index(x,y,z-1),4);

            MarkerInstancesY[x,y,z] = new MeshInstance();
            MarkerInstancesY[x,y,z].Mesh = parameters.MarkerShape;
            MarkerInstancesY[x,y,z].Translation = marker_position;
            MarkerInstancesY[x,y,z].Scale = Vector3.One * 0.1f;

            MarkerInstancesY[x,y,z].SetSurfaceMaterial(0,parameters.MarkerMaterial); 

            SimulationWorld.GodotAddChild(MarkerInstancesY[x,y,z]);

            MarkerDataStorage.Add(new MarkerData(dir,new Tuple<int,int,int>(x,y,z),positive_flow_index,negative_flow_index,axis));
        }
        else if(axis == marker_axis.Z){
            
        }

    }

    private void InitializeCell(int x, int y, int z){
        SoilCellInstances[Index(x,y,z)] = new MeshInstance();
        SoilCellInstances[Index(x,y,z)].Mesh = parameters.SoilCellShape;

        SimulationWorld.GodotAddChild(SoilCellInstances[Index(x,y,z)]);

        SoilCellInstances[Index(x,y,z)].Translation = new Vector3(x,-z,y) * AgroWorld.FieldResolution;
        SoilCellInstances[Index(x,y,z)].Scale = Vector3.One * parameters.SoilCellScale * AgroWorld.FieldResolution;

        SoilCellInstances[Index(x,y,z)].SetSurfaceMaterial(0,parameters.SoilCellMaterial);
    }

    private void AnimateCells(){
        for(int i = 0; i < SoilCellInstances.Length; ++i){
            float multiplier = Math.Min(parameters.CellCapacity,Math.Max(0,Agents[i].Water))/parameters.CellCapacity;

            SoilCellInstances[i].Scale = Vector3.One * parameters.SoilCellScale * AgroWorld.FieldResolution * multiplier;
            ((SpatialMaterial)SoilCellInstances[i].GetSurfaceMaterial(0)).AlbedoColor = multiplier*parameters.FullCellColor + (1-multiplier)*parameters.EmptyCellColor;
        }
    }

    private void AnimateMarkers(){


        for(int i = 0; i < MarkerDataStorage.Count; i++){
            var data = MarkerDataStorage[i];
            float flow = 0;

            if(data.MarkerDireciton == marker_direction.negative){
                flow = water_flow[data.NegativeFlowIndex.Item1,data.NegativeFlowIndex.Item2];
            }
            else if(data.MarkerDireciton == marker_direction.positive){
                flow = water_flow[data.PositiveFlowIndex.Item1,data.PositiveFlowIndex.Item2];
            }
            else{
                flow = water_flow[data.PositiveFlowIndex.Item1,data.PositiveFlowIndex.Item2]- water_flow[data.NegativeFlowIndex.Item1,data.NegativeFlowIndex.Item2];
            }

            //Todo: Fix that weird bug with float

            float multiplier = Math.Min(parameters.WaterFlowCapacity,Math.Max(0,Math.Abs(flow)))/parameters.WaterFlowCapacity;
            // float mult2 = multiplier;


            if(data.MarkerAxis == marker_axis.X){
                // MarkerInstancesX[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].Scale = Vector3.One*multiplier*parameters.MarkerScale;

                // ((SpatialMaterial)MarkerInstancesX[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].GetSurfaceMaterial(0)).AlbedoColor = multiplier*parameters.FullFlowColor + (1-multiplier)*parameters.NoFlowColor;

                // if((flow < 0 && !data.was_negative) || (flow > 0 && data.was_negative)){
                // MarkerInstancesX[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].RotateObjectLocal(Vector3.Up,Mathf.Pi);
                // data.was_negative = !data.was_negative;
                // }
            }
            else if(data.MarkerAxis == marker_axis.Y){
                MarkerInstancesY[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].Scale = Vector3.One*multiplier*parameters.MarkerScale;

                // ((SpatialMaterial)MarkerInstancesY[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].GetSurfaceMaterial(0)).AlbedoColor = multiplier*parameters.FullFlowColor + (1-multiplier)*parameters.NoFlowColor;
                // ((SpatialMaterial)MarkerInstancesY[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].GetSurfaceMaterial(0)).AlbedoColor = (1-mult2)*parameters.FullFlowColor;

                if((flow < 0 && !data.was_negative) || (flow > 0 && data.was_negative)){
                MarkerInstancesY[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].RotateZ(Mathf.Pi);
                data.was_negative = !data.was_negative;
                }
            }
            else{
                // MarkerInstancesZ[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].Scale = Vector3.One*multiplier*parameters.MarkerScale;

                // ((SpatialMaterial)MarkerInstancesZ[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].GetSurfaceMaterial(0)).AlbedoColor = multiplier*parameters.FullFlowColor + (1-multiplier)*parameters.NoFlowColor;
                                
                // if((flow < 0 && !data.was_negative) || (flow > 0 && data.was_negative)){
                // MarkerInstancesZ[data.MeshInstanceIndex.Item1,data.MeshInstanceIndex.Item2,data.MeshInstanceIndex.Item3].RotateObjectLocal(Vector3.Left,Mathf.Pi);
                // data.was_negative = !data.was_negative;
                // }
            }


    
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
    }

    private void SetMarkersVisibility(bool flag){
        //TODO: Fix markers visibility
        // for(int i = 0; i < MarkerInstancesY.Length; i++){
        //     MarkerInstancesY[i].Visible = flag;
        // }
    }

    private void SetCellsVisibility(bool flag){
        for(int i = 0; i < SoilCellInstances.Length; i++){
            SoilCellInstances[i].Visible = flag;
        }
    }

}