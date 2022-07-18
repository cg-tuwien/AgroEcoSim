using Godot;
using System;


enum direction{
    right,
    up,
    forward,
    left,
    down,
    backward
}

class MarkerData{
    public direction pointing_direction;
    public Vector3 InitialPosition;
    public int MarkerOwnerID;

    public MarkerData(direction dir, Vector3 init_pos, int owner_id){
        pointing_direction = dir;
        InitialPosition = init_pos;
        MarkerOwnerID = owner_id;
    }
}