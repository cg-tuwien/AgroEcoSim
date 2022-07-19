using System.Numerics;

public class MinExporter
{
    List<float> Data = new();
    public void Add(Vector3 position, Quaternion orientation, Vector3 scale)
    {
        Data.Add(position.X);
        Data.Add(position.Y);
        Data.Add(position.Z);
        Data.Add(orientation.X);
        Data.Add(orientation.Y);
        Data.Add(orientation.Z);
        Data.Add(orientation.W);
        Data.Add(scale.X);
        Data.Add(scale.Y);
        Data.Add(scale.Z);
    }
}