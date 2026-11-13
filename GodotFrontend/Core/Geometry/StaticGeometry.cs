using GodotFrontend.Core.Scene;

namespace GodotFrontend.Core.Geometry;

/// <summary>
/// Literal ports of the three r152 geometry generators ThreeFrontend uses (render/core/StaticGeometry.ts), including
/// where three truncates to float32 (generation in double, storage in float, transforms applied to the stored
/// floats). Exactness matters: a geometry that is merely close moves z-fighting and shifts flat-shaded facets.
/// </summary>
public static class StaticGeometry
{
    /// <summary>Geometry under construction: double lists, exactly as three accumulates them.</summary>
    private sealed class Raw
    {
        public readonly List<double> Positions = new();
        public readonly List<double> Normals = new();
        public readonly List<double> Uvs = new();
        public readonly List<int> Indices = new();

        public MeshData Finish(string name) => new(
            Positions.Select(d => (float)d).ToArray(),
            Normals.Select(d => (float)d).ToArray(),
            Indices.ToArray(),
            name,
            Uvs.Select(d => (float)d).ToArray());
    }

    public static MeshData[] BuildAll()
    {
        var t = new MeshData[Geom.StaticCount];
        t[Geom.BoxCentered] = Box().Finish("BoxCentered");
        t[Geom.BoxCorner] = ApplyMatrix4(Box().Finish("BoxCorner"), Translation(0.5, 0.5, 0.5));
        t[Geom.BoxBottomCentered] = ApplyMatrix4(Box().Finish("BoxBottomCentered"), Translation(0, 0.5, 0));
        t[Geom.Cylinder] = Cylinder(1, 1, 1.0, 8).Finish("Cylinder");
        t[Geom.Sphere] = Sphere(1, 8, 8).Finish("Sphere");
        t[Geom.Plane] = Plane(2, 2).Finish("Plane");
        t[Geom.CircleHalf] = ApplyMatrix4(Circle(0.5, 12).Finish("CircleHalf"), RotationX(-System.Math.PI * 0.5));
        t[Geom.CircleUnit] = ApplyMatrix4(Circle().Finish("CircleUnit"), RotationX(-System.Math.PI * 0.5));
        t[Geom.Dodecahedron] = Dodecahedron(0.03);
        t[Geom.DebugBox] = DebugBoxMesh.Build();
        return t;
    }

    /// <summary>BoxGeometry; also used by the gizmo for its plane handles.</summary>
    public static MeshData BoxMesh(double width, double height, double depth, string name) => Box(width, height, depth).Finish(name);

    private static Raw Box(double width = 1, double height = 1, double depth = 1, int widthSegments = 1, int heightSegments = 1, int depthSegments = 1)
    {
        var r = new Raw();
        var numberOfVertices = 0;
        const int X = 0, Y = 1, Z = 2;

        void BuildPlane(int u, int v, int w, double udir, double vdir, double planeW, double planeH, double planeD, int gridX, int gridY)
        {
            var segmentWidth = planeW / gridX;
            var segmentHeight = planeH / gridY;
            double widthHalf = planeW / 2, heightHalf = planeH / 2, depthHalf = planeD / 2;
            int gridX1 = gridX + 1, gridY1 = gridY + 1;
            var vertexCounter = 0;
            var vec = new double[3];
            for (var iy = 0; iy < gridY1; ++iy)
            {
                var y = iy * segmentHeight - heightHalf;
                for (var ix = 0; ix < gridX1; ++ix)
                {
                    var x = ix * segmentWidth - widthHalf;
                    vec[u] = x * udir; vec[v] = y * vdir; vec[w] = depthHalf;
                    r.Positions.Add(vec[0]); r.Positions.Add(vec[1]); r.Positions.Add(vec[2]);
                    vec[u] = 0; vec[v] = 0; vec[w] = planeD > 0 ? 1 : -1;
                    r.Normals.Add(vec[0]); r.Normals.Add(vec[1]); r.Normals.Add(vec[2]);
                    r.Uvs.Add((double)ix / gridX); r.Uvs.Add(1 - (double)iy / gridY);
                    vertexCounter += 1;
                }
            }
            for (var iy = 0; iy < gridY; ++iy)
                for (var ix = 0; ix < gridX; ++ix)
                {
                    var a = numberOfVertices + ix + gridX1 * iy;
                    var b = numberOfVertices + ix + gridX1 * (iy + 1);
                    var c = numberOfVertices + (ix + 1) + gridX1 * (iy + 1);
                    var d = numberOfVertices + (ix + 1) + gridX1 * iy;
                    r.Indices.Add(a); r.Indices.Add(b); r.Indices.Add(d);
                    r.Indices.Add(b); r.Indices.Add(c); r.Indices.Add(d);
                }
            numberOfVertices += vertexCounter;
        }

        BuildPlane(Z, Y, X, -1, -1, depth, height, width, depthSegments, heightSegments);   // px
        BuildPlane(Z, Y, X, 1, -1, depth, height, -width, depthSegments, heightSegments);   // nx
        BuildPlane(X, Z, Y, 1, 1, width, depth, height, widthSegments, depthSegments);      // py
        BuildPlane(X, Z, Y, 1, -1, width, depth, -height, widthSegments, depthSegments);    // ny
        BuildPlane(X, Y, Z, 1, -1, width, height, depth, widthSegments, heightSegments);    // pz
        BuildPlane(X, Y, Z, -1, -1, width, height, -depth, widthSegments, heightSegments);  // nz
        return r;
    }

    private static Raw Plane(double width = 1, double height = 1, int widthSegments = 1, int heightSegments = 1)
    {
        var r = new Raw();
        double widthHalf = width / 2, heightHalf = height / 2;
        int gridX = widthSegments, gridY = heightSegments;
        int gridX1 = gridX + 1, gridY1 = gridY + 1;
        double segmentWidth = width / gridX, segmentHeight = height / gridY;
        for (var iy = 0; iy < gridY1; ++iy)
        {
            var y = iy * segmentHeight - heightHalf;
            for (var ix = 0; ix < gridX1; ++ix)
            {
                var x = ix * segmentWidth - widthHalf;
                r.Positions.Add(x); r.Positions.Add(-y); r.Positions.Add(0);
                r.Normals.Add(0); r.Normals.Add(0); r.Normals.Add(1);
                r.Uvs.Add((double)ix / gridX); r.Uvs.Add(1 - (double)iy / gridY);
            }
        }
        for (var iy = 0; iy < gridY; ++iy)
            for (var ix = 0; ix < gridX; ++ix)
            {
                var a = ix + gridX1 * iy;
                var b = ix + gridX1 * (iy + 1);
                var c = (ix + 1) + gridX1 * (iy + 1);
                var d = (ix + 1) + gridX1 * iy;
                r.Indices.Add(a); r.Indices.Add(b); r.Indices.Add(d);
                r.Indices.Add(b); r.Indices.Add(c); r.Indices.Add(d);
            }
        return r;
    }

    private static Raw Circle(double radius = 1, int segments = 32, double thetaStart = 0, double thetaLength = System.Math.PI * 2)
    {
        var r = new Raw();
        segments = System.Math.Max(3, segments);
        r.Positions.Add(0); r.Positions.Add(0); r.Positions.Add(0);
        r.Normals.Add(0); r.Normals.Add(0); r.Normals.Add(1);
        r.Uvs.Add(0.5); r.Uvs.Add(0.5);
        for (int s = 0, i = 3; s <= segments; ++s, i += 3)
        {
            var segment = thetaStart + (double)s / segments * thetaLength;
            r.Positions.Add(radius * System.Math.Cos(segment));
            r.Positions.Add(radius * System.Math.Sin(segment));
            r.Positions.Add(0);
            r.Normals.Add(0); r.Normals.Add(0); r.Normals.Add(1);
            r.Uvs.Add((r.Positions[i] / radius + 1) / 2);
            r.Uvs.Add((r.Positions[i + 1] / radius + 1) / 2);
        }
        for (var i = 1; i <= segments; ++i) { r.Indices.Add(i); r.Indices.Add(i + 1); r.Indices.Add(0); }
        return r;
    }

    /// <summary>CylinderGeometry; also used by the gizmo for its cones and shafts.</summary>
    public static MeshData CylinderMesh(double radiusTop, double radiusBottom, double height, int radialSegments, string name)
        => Cylinder(radiusTop, radiusBottom, height, radialSegments).Finish(name);

    private static Raw Cylinder(double radiusTop = 1, double radiusBottom = 1, double height = 1, int radialSegments = 32,
        int heightSegments = 1, bool openEnded = false, double thetaStart = 0, double thetaLength = System.Math.PI * 2)
    {
        var r = new Raw();
        var index = 0;
        var indexArray = new List<int[]>();
        var halfHeight = height / 2;
        var slope = (radiusBottom - radiusTop) / height;
        for (var y = 0; y <= heightSegments; ++y)
        {
            var indexRow = new int[radialSegments + 1];
            var v = (double)y / heightSegments;
            var radius = v * (radiusBottom - radiusTop) + radiusTop;
            for (var x = 0; x <= radialSegments; ++x)
            {
                var u = (double)x / radialSegments;
                var theta = u * thetaLength + thetaStart;
                double sinTheta = System.Math.Sin(theta), cosTheta = System.Math.Cos(theta);
                r.Positions.Add(radius * sinTheta); r.Positions.Add(-v * height + halfHeight); r.Positions.Add(radius * cosTheta);
                double nx = sinTheta, ny = slope, nz = cosTheta;
                var len = System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len == 0) len = 1;
                r.Normals.Add(nx / len); r.Normals.Add(ny / len); r.Normals.Add(nz / len);
                r.Uvs.Add(u); r.Uvs.Add(1 - v);
                indexRow[x] = index++;
            }
            indexArray.Add(indexRow);
        }
        for (var x = 0; x < radialSegments; ++x)
            for (var y = 0; y < heightSegments; ++y)
            {
                var a = indexArray[y][x];
                var b = indexArray[y + 1][x];
                var c = indexArray[y + 1][x + 1];
                var d = indexArray[y][x + 1];
                r.Indices.Add(a); r.Indices.Add(b); r.Indices.Add(d);
                r.Indices.Add(b); r.Indices.Add(c); r.Indices.Add(d);
            }

        void GenerateCap(bool top)
        {
            var centerIndexStart = index;
            var radius = top ? radiusTop : radiusBottom;
            var sign = top ? 1 : -1;
            for (var x = 1; x <= radialSegments; ++x)
            {
                r.Positions.Add(0); r.Positions.Add(halfHeight * sign); r.Positions.Add(0);
                r.Normals.Add(0); r.Normals.Add(sign); r.Normals.Add(0);
                r.Uvs.Add(0.5); r.Uvs.Add(0.5);
                index++;
            }
            var centerIndexEnd = index;
            for (var x = 0; x <= radialSegments; ++x)
            {
                var u = (double)x / radialSegments;
                var theta = u * thetaLength + thetaStart;
                double cosTheta = System.Math.Cos(theta), sinTheta = System.Math.Sin(theta);
                r.Positions.Add(radius * sinTheta); r.Positions.Add(halfHeight * sign); r.Positions.Add(radius * cosTheta);
                r.Normals.Add(0); r.Normals.Add(sign); r.Normals.Add(0);
                r.Uvs.Add(cosTheta * 0.5 + 0.5); r.Uvs.Add(sinTheta * 0.5 * sign + 0.5);
                index++;
            }
            for (var x = 0; x < radialSegments; ++x)
            {
                var c = centerIndexStart + x;
                var i = centerIndexEnd + x;
                if (top) { r.Indices.Add(i); r.Indices.Add(i + 1); r.Indices.Add(c); }
                else { r.Indices.Add(i + 1); r.Indices.Add(i); r.Indices.Add(c); }
            }
        }

        if (!openEnded)
        {
            if (radiusTop > 0) GenerateCap(true);
            if (radiusBottom > 0) GenerateCap(false);
        }
        return r;
    }

    private static Raw Sphere(double radius = 1, int widthSegments = 32, int heightSegments = 16, double phiStart = 0,
        double phiLength = System.Math.PI * 2, double thetaStart = 0, double thetaLength = System.Math.PI)
    {
        var r = new Raw();
        widthSegments = System.Math.Max(3, widthSegments);
        heightSegments = System.Math.Max(2, heightSegments);
        var thetaEnd = System.Math.Min(thetaStart + thetaLength, System.Math.PI);
        var index = 0;
        var grid = new List<int[]>();
        for (var iy = 0; iy <= heightSegments; ++iy)
        {
            var verticesRow = new int[widthSegments + 1];
            var v = (double)iy / heightSegments;
            double uOffset = 0;
            if (iy == 0 && thetaStart == 0) uOffset = 0.5 / widthSegments;
            else if (iy == heightSegments && thetaEnd == System.Math.PI) uOffset = -0.5 / widthSegments;
            for (var ix = 0; ix <= widthSegments; ++ix)
            {
                var u = (double)ix / widthSegments;
                var px = -radius * System.Math.Cos(phiStart + u * phiLength) * System.Math.Sin(thetaStart + v * thetaLength);
                var py = radius * System.Math.Cos(thetaStart + v * thetaLength);
                var pz = radius * System.Math.Sin(phiStart + u * phiLength) * System.Math.Sin(thetaStart + v * thetaLength);
                r.Positions.Add(px); r.Positions.Add(py); r.Positions.Add(pz);
                var len = System.Math.Sqrt(px * px + py * py + pz * pz);
                if (len == 0) len = 1;
                r.Normals.Add(px / len); r.Normals.Add(py / len); r.Normals.Add(pz / len);
                r.Uvs.Add(u + uOffset); r.Uvs.Add(1 - v);
                verticesRow[ix] = index++;
            }
            grid.Add(verticesRow);
        }
        for (var iy = 0; iy < heightSegments; ++iy)
            for (var ix = 0; ix < widthSegments; ++ix)
            {
                var a = grid[iy][ix + 1];
                var b = grid[iy][ix];
                var c = grid[iy + 1][ix];
                var d = grid[iy + 1][ix + 1];
                if (iy != 0 || thetaStart > 0) { r.Indices.Add(a); r.Indices.Add(b); r.Indices.Add(d); }
                if (iy != heightSegments - 1 || thetaEnd < System.Math.PI) { r.Indices.Add(b); r.Indices.Add(c); r.Indices.Add(d); }
            }
        return r;
    }

    private static readonly double DodecT = (1 + System.Math.Sqrt(5)) / 2;
    private static readonly double DodecR = 1 / DodecT;
    private static readonly double[] DodecVertices =
    {
        -1, -1, -1, -1, -1, 1, -1, 1, -1, -1, 1, 1, 1, -1, -1, 1, -1, 1, 1, 1, -1, 1, 1, 1,
        0, -DodecR, -DodecT, 0, -DodecR, DodecT, 0, DodecR, -DodecT, 0, DodecR, DodecT,
        -DodecR, -DodecT, 0, -DodecR, DodecT, 0, DodecR, -DodecT, 0, DodecR, DodecT, 0,
        -DodecT, 0, -DodecR, DodecT, 0, -DodecR, -DodecT, 0, DodecR, DodecT, 0, DodecR,
    };
    private static readonly int[] DodecFaces =
    {
        3, 11, 7, 3, 7, 15, 3, 15, 13, 7, 19, 17, 7, 17, 6, 7, 6, 15, 17, 4, 8, 17, 8, 10, 17, 10, 6,
        8, 0, 16, 8, 16, 2, 8, 2, 10, 0, 12, 1, 0, 1, 18, 0, 18, 16, 6, 10, 2, 6, 2, 13, 6, 13, 15,
        2, 16, 18, 2, 18, 3, 2, 3, 13, 18, 1, 9, 18, 9, 11, 18, 11, 3, 4, 14, 12, 4, 12, 0, 4, 0, 8,
        11, 9, 5, 11, 5, 19, 11, 19, 7, 19, 5, 14, 19, 14, 4, 19, 4, 17, 1, 12, 14, 1, 14, 5, 1, 5, 9,
    };

    private static double Azimuth(double x, double z) => System.Math.Atan2(z, -x);
    private static double Inclination(double x, double y, double z) => System.Math.Atan2(-y, System.Math.Sqrt(x * x + z * z));

    /// <summary>PolyhedronGeometry at detail 0 with three's (b, c, a) corner order and flat normals from the f32 positions.</summary>
    public static MeshData Dodecahedron(double radius = 1, string name = "Dodecahedron")
    {
        var vb = new List<double>();
        var V = DodecVertices;
        double Lerp1(int pi, int qi, int c) => V[pi * 3 + c] + (V[qi * 3 + c] - V[pi * 3 + c]);
        for (var f = 0; f < DodecFaces.Length; f += 3)
        {
            int a = DodecFaces[f], b = DodecFaces[f + 1], c = DodecFaces[f + 2];
            vb.Add(Lerp1(a, b, 0)); vb.Add(Lerp1(a, b, 1)); vb.Add(Lerp1(a, b, 2));
            vb.Add(Lerp1(a, c, 0)); vb.Add(Lerp1(a, c, 1)); vb.Add(Lerp1(a, c, 2));
            vb.Add(V[a * 3]); vb.Add(V[a * 3 + 1]); vb.Add(V[a * 3 + 2]);
        }
        for (var i = 0; i < vb.Count; i += 3)
        {
            double x = vb[i], y = vb[i + 1], z = vb[i + 2];
            var len = System.Math.Sqrt(x * x + y * y + z * z);
            if (len == 0) len = 1;
            var s = radius / len;
            vb[i] = x * s; vb[i + 1] = y * s; vb[i + 2] = z * s;
        }
        var uv = new List<double>();
        for (var i = 0; i < vb.Count; i += 3)
        {
            double x = vb[i], y = vb[i + 1], z = vb[i + 2];
            uv.Add(Azimuth(x, z) / 2 / System.Math.PI + 0.5);
            uv.Add(1 - (Inclination(x, y, z) / System.Math.PI + 0.5));
        }
        for (int i = 0, j = 0; i < vb.Count; i += 9, j += 6)
        {
            var cx = (vb[i] + vb[i + 3] + vb[i + 6]) / 3;
            var cz = (vb[i + 2] + vb[i + 5] + vb[i + 8]) / 3;
            var azi = Azimuth(cx, cz);
            for (var k = 0; k < 3; ++k)
            {
                int vs = i + k * 3, us = j + k * 2;
                if (azi < 0 && uv[us] == 1) uv[us] = uv[us] - 1;
                if (vb[vs] == 0 && vb[vs + 2] == 0) uv[us] = azi / 2 / System.Math.PI + 0.5;
            }
        }
        for (var i = 0; i < uv.Count; i += 6)
        {
            double x0 = uv[i], x1 = uv[i + 2], x2 = uv[i + 4];
            var max = System.Math.Max(x0, System.Math.Max(x1, x2));
            var min = System.Math.Min(x0, System.Math.Min(x1, x2));
            if (max > 0.9 && min < 0.1)
            {
                if (x0 < 0.2) uv[i] += 1;
                if (x1 < 0.2) uv[i + 2] += 1;
                if (x2 < 0.2) uv[i + 4] += 1;
            }
        }
        var positions = vb.Select(d => (float)d).ToArray();
        var vertices = positions.Length / 3;
        var indices = new int[vertices];
        for (var i = 0; i < vertices; ++i) indices[i] = i;
        return new MeshData(positions, FlatNormals(positions), indices, name, uv.Select(d => (float)d).ToArray());
    }

    /// <summary>three's non-indexed computeVertexNormals: the raw (C-B) x (A-B) cross, truncated to f32, then normalised.</summary>
    private static float[] FlatNormals(float[] positions)
    {
        var normals = new float[positions.Length];
        for (var i = 0; i < positions.Length; i += 9)
        {
            double ax = positions[i], ay = positions[i + 1], az = positions[i + 2];
            double bx = positions[i + 3], by = positions[i + 4], bz = positions[i + 5];
            double cx = positions[i + 6], cy = positions[i + 7], cz = positions[i + 8];
            double cbx = cx - bx, cby = cy - by, cbz = cz - bz;
            double abx = ax - bx, aby = ay - by, abz = az - bz;
            var nx = (float)(cby * abz - cbz * aby);
            var ny = (float)(cbz * abx - cbx * abz);
            var nz = (float)(cbx * aby - cby * abx);
            for (var k = 0; k < 9; k += 3)
            {
                normals[i + k] = nx; normals[i + k + 1] = ny; normals[i + k + 2] = nz;
            }
        }
        MeshData.NormalizeInPlace(normals);
        return normals;
    }

    /// <summary>BufferGeometry.applyMatrix4 on a finished (float32) mesh; m is column-major like Matrix4.elements.</summary>
    public static MeshData ApplyMatrix4(MeshData r, double[] m)
    {
        var p = r.Positions;
        for (var i = 0; i < p.Length; i += 3)
        {
            double x = p[i], y = p[i + 1], z = p[i + 2];
            var w = 1 / (m[3] * x + m[7] * y + m[11] * z + m[15]);
            p[i] = (float)((m[0] * x + m[4] * y + m[8] * z + m[12]) * w);
            p[i + 1] = (float)((m[1] * x + m[5] * y + m[9] * z + m[13]) * w);
            p[i + 2] = (float)((m[2] * x + m[6] * y + m[10] * z + m[14]) * w);
        }
        var n = NormalMatrix(m);
        var nrm = r.Normals;
        for (var i = 0; i < nrm.Length; i += 3)
        {
            double x = nrm[i], y = nrm[i + 1], z = nrm[i + 2];
            var rx = n[0] * x + n[3] * y + n[6] * z;
            var ry = n[1] * x + n[4] * y + n[7] * z;
            var rz = n[2] * x + n[5] * y + n[8] * z;
            var len = System.Math.Sqrt(rx * rx + ry * ry + rz * rz);
            if (len == 0) len = 1;
            nrm[i] = (float)(rx / len); nrm[i + 1] = (float)(ry / len); nrm[i + 2] = (float)(rz / len);
        }
        return r;
    }

    /// <summary>Matrix3.getNormalMatrix: setFromMatrix4, invert, transpose. Column-major throughout.</summary>
    private static double[] NormalMatrix(double[] m)
    {
        double n11 = m[0], n21 = m[1], n31 = m[2];
        double n12 = m[4], n22 = m[5], n32 = m[6];
        double n13 = m[8], n23 = m[9], n33 = m[10];
        var t11 = n33 * n22 - n32 * n23;
        var t12 = n32 * n13 - n33 * n12;
        var t13 = n23 * n12 - n22 * n13;
        var det = n11 * t11 + n21 * t12 + n31 * t13;
        if (det == 0) return new double[9];
        var d = 1 / det;
        var inv = new[]
        {
            t11 * d, (n31 * n23 - n33 * n21) * d, (n32 * n21 - n31 * n22) * d,
            t12 * d, (n33 * n11 - n31 * n13) * d, (n31 * n12 - n32 * n11) * d,
            t13 * d, (n21 * n13 - n23 * n11) * d, (n22 * n11 - n21 * n12) * d,
        };
        return new[] { inv[0], inv[3], inv[6], inv[1], inv[4], inv[7], inv[2], inv[5], inv[8] };
    }

    public static double[] Translation(double x, double y, double z) => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1 };

    /// <summary>Matrix4.makeRotationX; cos(-PI/2) is 6.1e-17, not 0, and that is reproduced.</summary>
    public static double[] RotationX(double theta)
    {
        double c = System.Math.Cos(theta), s = System.Math.Sin(theta);
        return new[] { 1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1 };
    }
}
