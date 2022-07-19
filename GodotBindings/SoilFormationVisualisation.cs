using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using AgentsSystem;

namespace Agro;

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

    private void InitializeMarker(int parentIndex, Vector3 rotation, direction dir){
        var parentPos = SoilCellInstances[parentIndex].Translation;

        MarkerInstances[parentIndex,(int) dir] = new MeshInstance();

        var markerInstance = MarkerInstances[parentIndex,(int) dir];

        markerInstance.Translation = parentPos;
        markerInstance.Mesh = Parameters.MarkerShape;
        markerInstance.RotateX(rotation.x);
        markerInstance.RotateY(rotation.y);
        markerInstance.RotateZ(rotation.z);
        markerInstance.SetSurfaceMaterial(0,(SpatialMaterial)Parameters.MarkerMaterial.Duplicate());

        SimulationWorld.GodotAddChild(markerInstance);

        MarkerDataStorage.Add(new MarkerData(dir,parentPos,parentIndex));
    }

    private void InitializeCell(int x, int y, int z){
        SoilCellInstances[Index(x,y,z)] = new MeshInstance();

        var cellInstance = SoilCellInstances[Index(x,y,z)];

        cellInstance.Mesh = Parameters.SoilCellShape;
        cellInstance.Translation = new Vector3(x,-z,y) * AgroWorld.FieldResolution;
        cellInstance.Scale = Vector3.One * Parameters.SoilCellScale * AgroWorld.FieldResolution;
        cellInstance.SetSurfaceMaterial(0,(SpatialMaterial)Parameters.SoilCellMaterial.Duplicate());

        SimulationWorld.GodotAddChild(cellInstance);
    }

    private void AnimateCells(){
        for(int i = 0; i < SoilCellInstances.Length; ++i){
            var multiplier = Math.Clamp(Agents[i].Water,0f,Agents[i].WaterMaxCapacity)/Agents[i].WaterMaxCapacity;

            SoilCellInstances[i].Scale = Vector3.One * Parameters.SoilCellScale * AgroWorld.FieldResolution * multiplier;
            ((SpatialMaterial)SoilCellInstances[i].GetSurfaceMaterial(0)).AlbedoColor = multiplier*Parameters.FullCellColor + (1-multiplier)*Parameters.EmptyCellColor;
        }
    }

    private void AnimateMarkers(){
        foreach(MarkerData marker in MarkerDataStorage){
            AnimateMarker(marker);
        }
    }

    private void AnimateMarker(MarkerData marker){
        float markerScale;

        Vector3[] offset  = {Vector3.Right,Vector3.Up,Vector3.Forward,Vector3.Left,Vector3.Down,Vector3.Back};

        float cellScale = Parameters.SoilCellScale * AgroWorld.FieldResolution * Math.Min(Agents[marker.MarkerOwnerID].WaterMaxCapacity,Math.Max(0,Agents[marker.MarkerOwnerID].Water))/Agents[marker.MarkerOwnerID].WaterMaxCapacity;
        
        var flow = WaterFlow[marker.MarkerOwnerID,(int)marker.PointingDirection];
        var appearanceMultiplier = Math.Max(0,Math.Min(Parameters.WaterFlowCapacity,flow))/Parameters.WaterFlowCapacity;

        if(Parameters.AnimateMarkerSize) markerScale = cellScale*Parameters.MarkerScale*appearanceMultiplier;
        else markerScale = cellScale*Parameters.MarkerScale;

        var offsetMultiplier = cellScale/2 + markerScale/2;

        var mesh = MarkerInstances[marker.MarkerOwnerID,(int)marker.PointingDirection];
        mesh.Translation = marker.InitialPosition + offset[(int)marker.PointingDirection]*offsetMultiplier;
        mesh.Scale = Vector3.One * markerScale;

        ((SpatialMaterial)mesh.GetSurfaceMaterial(0)).AlbedoColor = appearanceMultiplier*Parameters.FullFlowColor + (1-appearanceMultiplier)*Parameters.NoFlowColor;
    }

    private void SolveVisibility(){
        if(Parameters.MarkerVisibility == visibility.Visible){
            SetMarkersVisibility(true);
            Parameters.MarkerVisibility = visibility.Waiting;
        }
        else if(Parameters.MarkerVisibility == visibility.Invisible){
            SetMarkersVisibility(false);
            Parameters.MarkerVisibility = visibility.Waiting;
        }

        if(Parameters.SoilCellsVisibility == visibility.Visible){
            SetCellsVisibility(true);
            Parameters.SoilCellsVisibility = visibility.Waiting;
        }
        else if(Parameters.SoilCellsVisibility == visibility.Invisible){
            SetCellsVisibility(false);
            Parameters.SoilCellsVisibility = visibility.Waiting;
        }

        for(int i = 0; i < 6; i++){
            if(Parameters.IndividualMarkerDirectionVisibility[i] == visibility.Visible){
                SetMarkersVisibility(true,(direction)i);
                Parameters.IndividualMarkerDirectionVisibility[i] = visibility.Waiting;
            }
            else if(Parameters.IndividualMarkerDirectionVisibility[i] == visibility.Invisible){
                SetMarkersVisibility(false,(direction)i);
                Parameters.IndividualMarkerDirectionVisibility[i] = visibility.Waiting;
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