using Godot;
using System;


public enum direction{
    right,
    up,
    forward,
    left,
    down,
    backward
}

class MarkerData{
    public direction PointingDirection;
    public Vector3 InitialPosition;
    public int MarkerOwnerID; //This id is reffering to index in array of soil cell instances

    public MarkerData(direction dir, Vector3 initialPosition, int ownerID){
        PointingDirection = dir;
        InitialPosition = initialPosition;
        MarkerOwnerID = ownerID;
    }
}