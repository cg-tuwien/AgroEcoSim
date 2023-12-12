using Utils;

namespace AgentsSystem;

public interface IGrid3D
{
	int Index(Vector3i coords);
	int Index(int x, int y, int z);
	Vector3i Coords(int index);
 	bool Contains(Vector3i coords);
	bool Contains(int x, int y, int z);
}