using Godot;

enum Direction : byte { Right, Up, Forward, Left, Down, Backward }

class MarkerData
{
    public Direction PointingDirection;
    public Vector3 InitialPosition;
    public int CellIndex;

    public MarkerData(Direction dir, Vector3 initialPosition, int ownerIndex)
    {
        PointingDirection = dir;
        InitialPosition = initialPosition;
        CellIndex = ownerIndex;
    }
}