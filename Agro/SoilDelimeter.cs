namespace Agro;

public readonly struct SoilDelimeter
{
    public readonly int Index;
    public readonly int[] LeftFace;
    public readonly int[] RightFace;

    public SoilDelimeter(int index, int[] left, int[] right)
    {
        Index = index;
        LeftFace = left;
        RightFace = right;
    }
}