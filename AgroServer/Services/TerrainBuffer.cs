using System.Globalization;
using Agro;
using Utils;

namespace AgroServer.Services;

public readonly struct TerrainBufferItem
{
    public readonly DateTime Modified = DateTime.UtcNow;
    public readonly ImportedObjDataV2 DataV2;
    internal readonly ImportedObjData Data;

    public TerrainBufferItem(ImportedObjData terrain)
    {
        Data = terrain;
    }

    public TerrainBufferItem(ImportedObjDataV2 terrain)
    {
        DataV2 = terrain;
    }
}

public interface ITerrainBuffer
{
    string Add(ImportedObjData terrain);
    string Add(string terrain);
    bool TryGetV2(string key, out ImportedObjDataV2 data);
}

public class TerrainBuffer : ITerrainBuffer
{
    readonly Dictionary<string, TerrainBufferItem> TerrainPerConnection = [];
    static readonly TimeSpan CacheTimeout = TimeSpan.FromMinutes(5);

    public string Add(ImportedObjData terrain)
    {
        lock (TerrainPerConnection)
        {
            var key = Guid.NewGuid().ToString();
            TerrainPerConnection[key] = new(terrain);
            var toRemove = new List<string>();
            var now = DateTime.UtcNow;

            foreach (var item in TerrainPerConnection)
                if (now - item.Value.Modified > CacheTimeout)
                    toRemove.Add(item.Key);

            foreach (var item in toRemove)
                TerrainPerConnection.Remove(item);

            return key;
        }
    }

    public string Add(string obj)
    {
        var lines = obj.Replace("\r", "").Split('\n');

        var vertices = new List<string>(lines.Length >> 1);
        var vertexLookup = new Dictionary<string, int>(StringComparer.Ordinal);
        var vertexMap = new List<int>();
        var objectNames = new List<string>();
        var objectTriangles = new List<List<Vector3i>>();
        var objectFaces = new List<List<int[]>>();

        List<Vector3i> currentObjectTriangles = null; //faces of the current object
        List<int[]> currentObjectFaces = null; //faces of the current object
        Span<int> faceVertices = stackalloc int[4];

        ReadOnlySpan<char> text = obj.AsSpan();
        int pos = 0;

        while (pos < text.Length)
        {
            int lineStart = pos;
            int newLine = obj.IndexOf('\n', pos);

            if (newLine < 0)
            {
                newLine = text.Length;
                pos = text.Length;
            }
            else
                pos = newLine + 1;

            var line = text[lineStart..newLine].Trim();

            if (line.Length == 0)
                continue;

            switch(line[0])
            {
                case 'v':
                {
                    // Important: OBJ also has vt/vn lines. Only handle real vertex lines: "v ..."
                    if (line.Length < 2 || !char.IsWhiteSpace(line[1]))
                        break;

                    var vertexSpan = line[1..].Trim();
                    var vertexKey = vertexSpan.ToString();

                    if (!vertexLookup.TryGetValue(vertexKey, out int index))
                    {
                        index = vertices.Count;
                        vertices.Add(vertexKey);
                        vertexLookup.Add(vertexKey, index);
                    }

                    vertexMap.Add(index);
                }
                break;
                case 'f':
                {
                    if (line.Length < 2 || !char.IsWhiteSpace(line[1]))
                        break;

                    var faceSpan = line[1..].Trim();
                    int faceVerticesCount = 0;
                    int i = 0;
                    while(i < faceSpan.Length)
                    {
                        //TODO check this whole block
                        while (i < faceSpan.Length && char.IsWhiteSpace(faceSpan[i])) ++i;
                        if (i >= faceSpan.Length) break;

                        if (faceVerticesCount >= 4)
                            throw new Exception("Imported OBJ file may only contain triangular and quad faces. More than 4 vertices per face are not supported. Please triangulate the model before exporting to OBJ.");

                        int tokenStart = i;

                        while (i < faceSpan.Length && !char.IsWhiteSpace(faceSpan[i])) i++;
                        var token = faceSpan[tokenStart..i];


                            //const components = vertex.split('/');
                            // switch (components.length) {
                            //     default: vertexString.push(components[0]); break;
                            //     case 3: vertexString.push(`${vertexMap[parseInt(components[0])]}:${normalMap[parseInt(components[2])]}`); break;
                            // }
                            //vertexString.push(components[0]);

                        var slashIndex = token.IndexOf('/');
                        var vertexIndexSpan = slashIndex >= 0 ? token[..slashIndex] : token;
                        int objIndex = int.Parse(vertexIndexSpan, CultureInfo.InvariantCulture);

                        // OBJ indices are 1-based.
                        // Negative indices are relative to the current vertex count.
                        int originalVertexIndex = objIndex > 0 ? objIndex - 1 : vertexMap.Count + objIndex;

                        var mappedIndex = vertexMap[originalVertexIndex];

                        faceVertices[faceVerticesCount++] = mappedIndex;
                    }

                    if (faceVerticesCount < 3) throw new Exception("Imported OBJ contains an invalid face with less than 3 vertices. Please fix the model before exporting to OBJ.");

                    currentObjectFaces?.Add([..faceVertices]);

                    currentObjectTriangles?.Add(new Vector3i(faceVertices[0], faceVertices[1], faceVertices[2]));
                    if (faceVerticesCount == 4)
                        currentObjectTriangles?.Add(new Vector3i(faceVertices[0], faceVertices[2], faceVertices[3]));
                }
                break;
                case 'o':
                {
                    if (line.Length < 2 || !char.IsWhiteSpace(line[1]))
                        break;

                    if (currentObjectFaces != null)
                    {
                        objectFaces.Add(currentObjectFaces);
                        objectTriangles.Add(currentObjectTriangles);
                    }
                    currentObjectFaces = [];
                    currentObjectTriangles = [];
                    objectNames.Add(line[1..].TrimStart().ToString());
                }
                break;
            }
        }

        //finish the last face
        if (currentObjectFaces != null)
        {
            objectFaces.Add(currentObjectFaces);
            objectTriangles.Add(currentObjectTriangles);
        }

        var terrain = new ImportedObjDataV2()
        {
            Vertices = [..vertices],
            Faces = [],
        };

        for(int i = 0; i < objectNames.Count; ++i)
            if (objectNames[i] != null && objectFaces[i] != null)
                terrain.Faces.Add(objectNames[i], (objectTriangles[i], objectFaces[i]));

        lock (TerrainPerConnection)
        {
            var key = Guid.NewGuid().ToString();
            TerrainPerConnection[key] = new(terrain);
            var toRemove = new List<string>();
            var now = DateTime.UtcNow;

            foreach (var item in TerrainPerConnection)
                if (now - item.Value.Modified > CacheTimeout)
                    toRemove.Add(item.Key);

            foreach (var item in toRemove)
                TerrainPerConnection.Remove(item);

            return key;
        }
    }

    public bool TryGet(string key, out ImportedObjData data)
    {
        lock (TerrainPerConnection)
        {
            if (TerrainPerConnection.TryGetValue(key, out var result))
            {
                TerrainPerConnection.Remove(key);
                data = result.Data;
                return true;
            }
            else
            {
                data = default;
                return false;
            }
        }
    }

    public bool TryGetV2(string key, out ImportedObjDataV2 data)
    {
        lock (TerrainPerConnection)
        {
            if (TerrainPerConnection.TryGetValue(key, out var result))
            {
                TerrainPerConnection.Remove(key);
                data = result.DataV2;
                return true;
            }
            else
            {
                data = default;
                return false;
            }
        }
    }
}