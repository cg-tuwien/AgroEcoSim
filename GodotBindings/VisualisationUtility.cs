using Godot;
using System;


enum Direction{
    right,
    up,
    forward,
    left,
    down,
    backward
}

class MarkerData{
    public Direction PointingDirection;
    public Vector3 InitialPosition;
    public int CellIndex;

    public MarkerData(Direction dir, Vector3 init_pos, int owner_index){
        PointingDirection = dir;
        InitialPosition = init_pos;
        CellIndex = owner_index;
    }
}
