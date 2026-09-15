using System.Globalization;
using Agro;
using Utils;

namespace AgroServer.Services;

public readonly struct TerrainBufferItem
{
    public readonly DateTime Modified = DateTime.UtcNow;
    public readonly ImportedObjDataV2 DataV2;
    internal readonly ImportedObjData Data;

    /// <summary>
    /// Where the OBJ behind this entry was filed in the run store, or null when it was not.
    ///
    /// Carried through the buffer rather than looked up later because this is the ONLY moment the
    /// source text and the parsed model are known to be the same thing. The buffer entry is gone
    /// five minutes after the upload and the parse is not reversible, so a run that wants to be
    /// re-runnable has to record the source's identity here or never.
    /// </summary>
    public readonly string? ModelHash;

    public TerrainBufferItem(ImportedObjData terrain)
    {
        Data = terrain;
    }

    public TerrainBufferItem(ImportedObjDataV2 terrain, string? modelHash = null)
    {
        DataV2 = terrain;
        ModelHash = modelHash;
    }
}

public interface ITerrainBuffer
{
    string Add(ImportedObjData terrain);
    string Add(string terrain, CoordinateSystem coords, string? modelHash = null);
    bool TryGetV2(string key, out ImportedObjDataV2 data);
    /// <summary>As <see cref="TryGetV2(string, out ImportedObjDataV2)"/>, and also reports where the
    /// source OBJ was filed, so the run that uses this model can be re-run later.</summary>
    bool TryGetV2(string key, out ImportedObjDataV2 data, out string? modelHash);
}

public enum CoordinateSystem : byte { Unknown = 0,
    LH_rightNX_upNY, LH_rightNX_upPY, LH_rightPX_upNY, LH_rightPX_upPY,
    LH_rightNY_upNX, LH_rightNY_upPX, LH_rightPY_upNX, LH_rightPY_upPX,

    LH_rightNX_upNZ, LH_rightNX_upPZ, LH_rightPX_upNZ, LH_rightPX_upPZ,
    LH_rightNZ_upNX, LH_rightNZ_upPX, LH_rightPZ_upNX, LH_rightPZ_upPX,

    LH_rightNY_upNZ, LH_rightNY_upPZ, LH_rightPY_upNZ, LH_rightPY_upPZ,
    LH_rightNZ_upNY, LH_rightNZ_upPY, LH_rightPZ_upNY, LH_rightPZ_upPY,

    RH_rightNX_upNY, RH_rightNX_upPY, RH_rightPX_upNY, RH_rightPX_upPY,
    RH_rightNY_upNX, RH_rightNY_upPX, RH_rightPY_upNX, RH_rightPY_upPX,

    RH_rightNX_upNZ, RH_rightNX_upPZ, RH_rightPX_upNZ, RH_rightPX_upPZ,
    RH_rightNZ_upNX, RH_rightNZ_upPX, RH_rightPZ_upNX, RH_rightPZ_upPX,

    RH_rightNY_upNZ, RH_rightNY_upPZ, RH_rightPY_upNZ, RH_rightPY_upPZ,
    RH_rightNZ_upNY, RH_rightNZ_upPY, RH_rightPZ_upNY, RH_rightPZ_upPY,
}

public static class CoordinateConverter
{
    /// <summary>
    /// Converts a whitespace-separated XYZ coordinate from the given coordinate
    /// system to RH_rightPX_upPY.
    ///
    /// Example:
    ///     "0.5 4.2 1.123"
    /// </summary>
    public static string ConvertCoordsTo_RH_rightPX_upPY(string vertex, CoordinateSystem coords)
    {
        var p = vertex.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (p.Length != 3)
            throw new ArgumentException($"Expected exactly 3 coordinates, got {p.Length}.", nameof(vertex));

        // Textual negation only. No numeric parsing or arithmetic.
        static string N(string s) => s.StartsWith('-') ? s[1..] : s.StartsWith('+') ? "-" + s[1..] : "-" + s;

        var x = p[0];
        var y = p[1];
        var z = p[2];

        return coords switch
        {
            // Left-handed
            CoordinateSystem.LH_rightNX_upNY => $"{N(x)} {N(y)} {N(z)}",
            CoordinateSystem.LH_rightNX_upPY => $"{N(x)} {y} {z}",
            CoordinateSystem.LH_rightPX_upNY => $"{x} {N(y)} {z}",
            CoordinateSystem.LH_rightPX_upPY => $"{x} {y} {N(z)}",

            CoordinateSystem.LH_rightNY_upNX => $"{N(y)} {N(x)} {z}",
            CoordinateSystem.LH_rightNY_upPX => $"{N(y)} {x} {N(z)}",
            CoordinateSystem.LH_rightPY_upNX => $"{y} {N(x)} {N(z)}",
            CoordinateSystem.LH_rightPY_upPX => $"{y} {x} {z}",


            CoordinateSystem.LH_rightNX_upNZ => $"{N(x)} {N(z)} {y}",
            CoordinateSystem.LH_rightNX_upPZ => $"{N(x)} {z} {N(y)}",
            CoordinateSystem.LH_rightPX_upNZ => $"{x} {N(z)} {N(y)}",
            CoordinateSystem.LH_rightPX_upPZ => $"{x} {z} {y}",

            CoordinateSystem.LH_rightNZ_upNX => $"{N(z)} {N(x)} {N(y)}",
            CoordinateSystem.LH_rightNZ_upPX => $"{N(z)} {x} {y}",
            CoordinateSystem.LH_rightPZ_upNX => $"{z} {N(x)} {y}",
            CoordinateSystem.LH_rightPZ_upPX => $"{z} {x} {N(y)}",


            CoordinateSystem.LH_rightNY_upNZ => $"{N(y)} {N(z)} {N(x)}",
            CoordinateSystem.LH_rightNY_upPZ => $"{N(y)} {z} {x}",
            CoordinateSystem.LH_rightPY_upNZ => $"{y} {N(z)} {x}",
            CoordinateSystem.LH_rightPY_upPZ => $"{y} {z} {N(x)}",

            CoordinateSystem.LH_rightNZ_upNY => $"{N(z)} {N(y)} {x}",
            CoordinateSystem.LH_rightNZ_upPY => $"{N(z)} {y} {N(x)}",
            CoordinateSystem.LH_rightPZ_upNY => $"{z} {N(y)} {N(x)}",
            CoordinateSystem.LH_rightPZ_upPY => $"{z} {y} {x}",

            // Right-handed

            CoordinateSystem.RH_rightNX_upNY => $"{N(x)} {N(y)} {z}",
            CoordinateSystem.RH_rightNX_upPY => $"{N(x)} {y} {N(z)}",
            CoordinateSystem.RH_rightPX_upNY => $"{x} {N(y)} {N(z)}",
            CoordinateSystem.RH_rightPX_upPY => $"{x} {y} {z}",

            CoordinateSystem.RH_rightNY_upNX => $"{N(y)} {N(x)} {N(z)}",
            CoordinateSystem.RH_rightNY_upPX => $"{N(y)} {x} {z}",
            CoordinateSystem.RH_rightPY_upNX => $"{y} {N(x)} {z}",
            CoordinateSystem.RH_rightPY_upPX => $"{y} {x} {N(z)}",


            CoordinateSystem.RH_rightNX_upNZ => $"{N(x)} {N(z)} {N(y)}",
            CoordinateSystem.RH_rightNX_upPZ => $"{N(x)} {z} {y}",
            CoordinateSystem.RH_rightPX_upNZ => $"{x} {N(z)} {y}",
            CoordinateSystem.RH_rightPX_upPZ => $"{x} {z} {N(y)}",

            CoordinateSystem.RH_rightNZ_upNX => $"{N(z)} {N(x)} {y}",
            CoordinateSystem.RH_rightNZ_upPX => $"{N(z)} {x} {N(y)}",
            CoordinateSystem.RH_rightPZ_upNX => $"{z} {N(x)} {N(y)}",
            CoordinateSystem.RH_rightPZ_upPX => $"{z} {x} {y}",


            CoordinateSystem.RH_rightNY_upNZ => $"{N(y)} {N(z)} {x}",
            CoordinateSystem.RH_rightNY_upPZ => $"{N(y)} {z} {N(x)}",
            CoordinateSystem.RH_rightPY_upNZ => $"{y} {N(z)} {N(x)}",
            CoordinateSystem.RH_rightPY_upPZ => $"{y} {z} {x}",

            CoordinateSystem.RH_rightNZ_upNY => $"{N(z)} {N(y)} {N(x)}",
            CoordinateSystem.RH_rightNZ_upPY => $"{N(z)} {y} {x}",
            CoordinateSystem.RH_rightPZ_upNY => $"{z} {N(y)} {x}",
            CoordinateSystem.RH_rightPZ_upPY => $"{z} {y} {N(x)}",

            _ => throw new ArgumentOutOfRangeException(
                nameof(coords), coords, "Unknown coordinate system.")
        };
    }

    /// <summary>
    /// Returns true if triangle/polygon winding must be reversed when converting to RH_rightPX_upPY.
    /// </summary>
    public static bool NeedsReverseTo_RH_rightPX_upPY(CoordinateSystem coords)
    {
        if (coords == CoordinateSystem.Unknown)
            throw new ArgumentException("Coordinate system must not be Unknown.", nameof(coords));

        // Axis permutations/sign changes inside the same handedness have determinant +1. Switching LH -> RH has determinant -1.
        return coords < CoordinateSystem.RH_rightNX_upNY;
    }
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

    public string Add(string obj, CoordinateSystem coords, string? modelHash = null)
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

        var needsFaceFlip = CoordinateConverter.NeedsReverseTo_RH_rightPX_upPY(coords);

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

                    if (coords != CoordinateSystem.RH_rightPX_upPY)
                        vertexKey = CoordinateConverter.ConvertCoordsTo_RH_rightPX_upPY(vertexKey, coords);

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

                    if (needsFaceFlip)
                    {
                        currentObjectTriangles?.Add(new Vector3i(faceVertices[2], faceVertices[1], faceVertices[0]));
                        if (faceVerticesCount == 4)
                            currentObjectTriangles?.Add(new Vector3i(faceVertices[3], faceVertices[2], faceVertices[0]));
                    }
                    else
                    {
                        currentObjectTriangles?.Add(new Vector3i(faceVertices[0], faceVertices[1], faceVertices[2]));
                        if (faceVerticesCount == 4)
                            currentObjectTriangles?.Add(new Vector3i(faceVertices[0], faceVertices[2], faceVertices[3]));
                    }
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
            TerrainPerConnection[key] = new(terrain, modelHash);
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

    public bool TryGetV2(string key, out ImportedObjDataV2 data) => TryGetV2(key, out data, out _);

    public bool TryGetV2(string key, out ImportedObjDataV2 data, out string? modelHash)
    {
        lock (TerrainPerConnection)
        {
            if (TerrainPerConnection.TryGetValue(key, out var result))
            {
                TerrainPerConnection.Remove(key);
                data = result.DataV2;
                modelHash = result.ModelHash;
                return true;
            }
            else
            {
                data = default;
                modelHash = null;
                return false;
            }
        }
    }
}