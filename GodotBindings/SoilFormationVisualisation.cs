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
        MarkerInstances = new MeshInstance[SoilCellInstances.Length*6];
        for (int x = 0; x < SizeX; x++)
        {
            for (int y = 0; y < SizeY; y++)
            {
                for (int z = 0; z < SizeZ; z++)
                {
                    InitializeCell(x,y,z);
                    InitializeMarker(x,y,z);
                }
            }
        }
    }

    private void InitializeMarker(int x, int y, int z){
        float[,] rotation_offset = {
            {0,0,0,(float)Math.PI,(float)Math.PI/2,-(float)Math.PI/2},
            {0,0,0,0,0,0},
            {-(float)Math.PI/2,(float)Math.PI/2,0,0,0,0}};

        float offset_ammount = (parameters.SoilCellScale + parameters.SoilCellScale*parameters.MarkerScale)*0.25f;

        Vector3[] translation_offset = {
            new Vector3(offset_ammount,0,0),
            new Vector3(-offset_ammount,0,0),
            new Vector3(0,offset_ammount,0),
            new Vector3(0,-offset_ammount,0),
            new Vector3(0,0,offset_ammount),
            new Vector3(0,0,-offset_ammount)
        };

        for(int i = 0; i < 6; i++){
            int index = Index(x,y,z)+i*SoilCellInstances.Length;
            MarkerInstances[index] = new MeshInstance();
            MarkerInstances[index].Mesh = parameters.MarkerShape;

            SimulationWorld.GodotAddChild(MarkerInstances[index]);

            MarkerInstances[index].Translation = new Vector3(x,-z,y) * AgroWorld.FieldResolution + translation_offset[i];
            MarkerInstances[index].Scale = Vector3.One * parameters.SoilCellScale * AgroWorld.FieldResolution * parameters.MarkerScale;

            MarkerInstances[index].RotateX(rotation_offset[0,i]);
            MarkerInstances[index].RotateY(rotation_offset[1,i]);
            MarkerInstances[index].RotateZ(rotation_offset[2,i]);
        }
        
    }

    private void InitializeCell(int x, int y, int z){
        SoilCellInstances[Index(x,y,z)] = new MeshInstance();
        SoilCellInstances[Index(x,y,z)].Mesh = parameters.SoilCellShape;

        SimulationWorld.GodotAddChild(SoilCellInstances[Index(x,y,z)]);

        SoilCellInstances[Index(x,y,z)].Translation = new Vector3(x,-z,y) * AgroWorld.FieldResolution;
        SoilCellInstances[Index(x,y,z)].Scale = Vector3.One * parameters.SoilCellScale * AgroWorld.FieldResolution;

        
    }

    private void AnimateCells(){
        
    }

    private void AnimateMarkers(){

    }

}