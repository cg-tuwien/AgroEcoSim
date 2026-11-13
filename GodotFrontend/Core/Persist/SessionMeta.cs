using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Agro;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Lighting;
using GodotFrontend.Core.Model;
using GodotFrontend.Core.Run;
using GodotFrontend.Core.Scene;
using MeshData = GodotFrontend.Core.Scene.MeshData;

namespace GodotFrontend.Core.Persist;

/// <summary>Everything a session file describes besides its frames.</summary>
public sealed class SessionContents
{
    public RequestState Request = new();
    public ViewState View = new();
    public TerrainData? Terrain;
    public int SimHoursPerTick = 4;
    public SiteInfo Site = new();
    public JsonElement? ObstacleLight;
    public string[] Behaviors = Array.Empty<string>();
    /// <summary>The loaded metadata, kept so keys this app does not interpret survive a save.</summary>
    public JsonObject? Original;
}

/// <summary>
/// Maps the session metadata the web app writes (appstate.ts save()/load()) to and from the app's state. Unknown keys
/// of a loaded session are preserved on save; keys the web's load() requires (species, seeds, obstacles) are always
/// written, so a session saved here opens in the web app and the other way round.
/// </summary>
public static class SessionMeta
{
    /// <summary>camelCase, only the members a request can set (the species blobs with internal setters are skipped).</summary>
    private static readonly JsonSerializerOptions SpeciesOut = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OnlySettable } },
    };

    private static void OnlySettable(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        for (var i = info.Properties.Count - 1; i >= 0; --i)
            if (info.Properties[i].Set is null) info.Properties.RemoveAt(i);
    }

    public static JsonObject Build(SessionContents c, long historyBytes)
    {
        var r = c.Request;
        var o = c.Original?.DeepClone().AsObject() ?? new JsonObject();
        o["hoursPerTick"] = r.HoursPerTick;
        o["totalHours"] = r.TotalHours;
        o["fieldResolution"] = r.FieldResolution;
        o["fieldSizeX"] = r.FieldSizeX;
        o["fieldSizeZ"] = r.FieldSizeZ;
        o["fieldSizeD"] = r.FieldSizeD;
        o["terrainList"] = TerrainList(c.Terrain);
        o["terrainTimestamp"] ??= 0;
        o["initNumber"] = r.InitNumber;
        o["randomize"] = r.Randomize;
        o["renderMode"] = r.RenderMode;
        o["initialDate"] = r.InitialDate;
        if (r.DailyAirTemperatures != null)
            o["dailyAirTemperatures"] = new JsonArray(r.DailyAirTemperatures.Select(t => (JsonNode)new JsonObject { ["Date"] = t.Date.ToString("yyyy-MM-dd"), ["MeanC"] = t.MeanC }).ToArray());
        o["latitude"] = r.Latitude;
        o["longitude"] = r.Longitude;
        o["altitude"] = r.Altitude;
        o["azimuthBase"] = r.AzimuthBase;
        o["azimuthOffset"] = (int)r.AzimuthOffset;
        o["siteTimeZone"] = c.Site.TimeZone;
        o["weather"] = c.Site.Coverage.Length > 0 ? new JsonArray(c.Site.Coverage.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()) : null;
        o["leafAreaIndexObstacles"] = r.LeafAreaIndexObstacles;
        o["leafAreaIndexSensors"] = r.LeafAreaIndexSensors;
        o["leafAreaIndexDepth"] = r.LeafAreaIndexDepth;
        o["leafAreaIndexBandHeight"] = r.LeafAreaIndexBandHeight;
        var l = c.View.Lighting;
        o["lighting"] = new JsonObject
        {
            ["daylight"] = l.Daylight, ["shadows"] = l.Shadows, ["occlusion"] = l.Occlusion, ["indirect"] = l.Indirect,
            ["secondBounce"] = l.SecondBounce, ["leafTranslucency"] = l.LeafTranslucency, ["shadowBiasTexels"] = l.ShadowBiasTexels,
        };
        o.Remove("lightingTier");
        o["lightSky"] = l.Sky;
        o["viewportBackend"] ??= "auto";
        o["downloadRoots"] = r.DownloadRoots;
        o["virtualRoots"] = r.VirtualRoots;
        o["exactPreview"] = r.ExactPreview;
        o["species"] = new JsonArray(r.Species.Select(SpeciesNode).ToArray());
        o["behaviors"] = new JsonArray(c.Behaviors.Select(b => (JsonNode)JsonValue.Create(b)!).ToArray());
        o["seedsPerField"] ??= 1;
        o["seedsOptimalDistance"] ??= 0.165;
        o["seeds"] = new JsonArray(r.Seeds.Select(s => (JsonNode)new JsonObject
        {
            ["species"] = s.Species, ["px"] = s.Px, ["py"] = s.Py, ["pz"] = s.Pz, ["fi"] = s.Field,
            ["startOffsetHours"] = s.StartOffsetHours, ["ageUnit"] = "days",
        }).ToArray());
        o["obstacles"] = Obstacles(r, c.Terrain);
        o["visualMapping"] = PlantColors.MappingName(c.View.Mapping);
        o["debugDisplayOrientations"] = c.View.DebugBoxes;
        o["showLeaves"] = c.View.ShowLeaves;
        o["showTerrain"] = c.View.ShowTerrain;
        o["showRoots"] = c.View.ShowRoots;
        o["showObstacles"] = c.View.ShowObstacles;
        o["showSeeds"] = c.View.ShowSeeds;
        o["performPicking"] = c.View.PerformPicking;
        o["plants"] ??= new JsonArray();
        o["obstacleLight"] = c.ObstacleLight is { } ol ? JsonNode.Parse(ol.GetRawText()) : null;
        o["obstacleMapping"] = c.View.ObstacleMapping;
        o["historySize"] = historyBytes;
        o["simHoursPerTick"] = c.SimHoursPerTick;
        o["fieldModelPath"] = r.FieldModelPath;
        o["fieldItemRegex"] = r.FieldItemRegex;
        o["fieldItemRegexMaterial"] = r.FieldItemRegexMaterial;
        o["fieldModelData"] ??= null;
        return o;
    }

    private static JsonNode SpeciesNode(SpeciesSettings s)
    {
        var node = JsonSerializer.SerializeToNode(s, SpeciesOut)!.AsObject();
        node["includeInRndGen"] = true;
        return node;
    }

    private static JsonArray TerrainList(TerrainData? t)
    {
        var a = new JsonArray();
        if (t == null) return a;
        foreach (var item in t.Items)
        {
            switch (item)
            {
                case BoxTerrainItem b:
                    a.Add(new JsonObject
                    {
                        ["id"] = b.Id,
                        ["data"] = new JsonArray(new JsonNode[] { b.Px, b.Py, b.Pz, b.Sx, b.Sy, b.Sz, b.Qx, b.Qy, b.Qz, b.Qw }),
                    });
                    break;
                case MeshTerrainItem m:
                    a.Add(new JsonObject
                    {
                        ["id"] = m.Id, ["px"] = m.Px, ["py"] = m.Py, ["pz"] = m.Pz,
                        ["points"] = new JsonArray(m.Points.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
                        ["triangles"] = new JsonArray(m.Triangles.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
                    });
                    break;
            }
        }
        return a;
    }

    private static JsonArray Obstacles(RequestState r, TerrainData? t)
    {
        var a = new JsonArray();
        foreach (var ob in r.Obstacles)
        {
            if (ob.Type == "mesh") continue; // the imported mesh is written from the terrain below
            a.Add(new JsonObject
            {
                ["px"] = ob.Position?.X ?? 0, ["py"] = ob.Position?.Y ?? 0, ["pz"] = ob.Position?.Z ?? 0,
                ["type"] = ob.Type ?? "wall",
                ["l"] = ob.Type == "umbrella" ? ob.Radius ?? 0 : ob.Length ?? 0,
                ["h"] = ob.Height ?? 0, ["t"] = ob.Thickness ?? 0,
                ["ax"] = 0, ["ay"] = ob.Orientation ?? 0,
                ["vt"] = new JsonArray(), ["fc"] = new JsonArray(), ["gr"] = null,
            });
        }
        if (t?.ObstacleMesh is { } mesh)
        {
            a.Add(new JsonObject
            {
                ["px"] = 0, ["py"] = 0, ["pz"] = 0, ["type"] = "mesh", ["l"] = 0, ["h"] = 0, ["t"] = 0, ["ax"] = 0, ["ay"] = 0,
                ["vt"] = new JsonArray(mesh.Positions.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
                ["fc"] = new JsonArray(mesh.Indices.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
                ["gr"] = t.Groups.Count == 0 ? null : new JsonArray(t.Groups.Select(g => (JsonNode)new JsonObject
                {
                    ["name"] = g.Name, ["vertexFirst"] = g.VertexFirst, ["vertexCount"] = g.VertexCount,
                    ["indexFirst"] = g.IndexFirst, ["indexCount"] = g.IndexCount,
                }).ToArray()),
            });
        }
        return a;
    }

    // ------------------------------------------------------------------ load

    private static T Get<T>(JsonNode? n, T fallback)
    {
        if (n is null) return fallback;
        try
        {
            if (n is JsonValue v && v.TryGetValue<T>(out var t)) return t;
            return n.Deserialize<T>(Net.Json.WireIn) ?? fallback;
        }
        catch { return fallback; }
    }

    public static SessionContents Apply(JsonNode meta, IReadOnlyList<SpeciesSettings>? knownSpecies)
    {
        var o = meta.AsObject();
        var c = new SessionContents { Original = o };
        var r = c.Request;
        r.HoursPerTick = Get(o["hoursPerTick"], 4);
        r.TotalHours = Get(o["totalHours"], 1440);
        r.FieldResolution = Get(o["fieldResolution"], 0.05f);
        r.FieldSizeX = Get(o["fieldSizeX"], 10f);
        r.FieldSizeZ = Get(o["fieldSizeZ"], 10f);
        r.FieldSizeD = Get(o["fieldSizeD"], 4f);
        r.InitNumber = o["initNumber"] is JsonValue seed ? seed.ToJsonString().Trim('"') : "42";
        r.Randomize = Get(o["randomize"], false);
        r.RenderMode = Get(o["renderMode"], (byte)3);
        r.InitialDate = Get(o["initialDate"], "2026-02-01") ?? "2026-02-01";
        r.Latitude = Get(o["latitude"], 48.208333f);
        r.Longitude = Get(o["longitude"], 16.3725f);
        r.Altitude = Get(o["altitude"], 188f);
        r.AzimuthBase = Get(o["azimuthBase"], 0f);
        r.AzimuthOffset = (TerrainOrientation)Get(o["azimuthOffset"], 0);
        r.LeafAreaIndexObstacles = Get(o["leafAreaIndexObstacles"], "") ?? "";
        r.LeafAreaIndexSensors = Get(o["leafAreaIndexSensors"], 32768);
        r.LeafAreaIndexDepth = Get(o["leafAreaIndexDepth"], 3f);
        r.LeafAreaIndexBandHeight = Get(o["leafAreaIndexBandHeight"], 0.5f);
        r.DownloadRoots = Get(o["downloadRoots"], false);
        r.VirtualRoots = Get(o["virtualRoots"], false);
        r.ExactPreview = Get(o["exactPreview"], false);
        r.FieldModelPath = Get(o["fieldModelPath"], "") ?? "";
        r.FieldItemRegex = Get(o["fieldItemRegex"], "erde") ?? "erde";
        r.FieldItemRegexMaterial = Get(o["fieldItemRegexMaterial"], true);
        if (o["dailyAirTemperatures"] is JsonArray temps)
            r.DailyAirTemperatures = temps.Select(t => new DailyAirTemperature
            {
                Date = DateTime.Parse(Get(t?["Date"] ?? t?["date"], "2026-01-01")!),
                MeanC = Get(t?["MeanC"] ?? t?["meanC"], 0f),
            }).ToList();

        if (o["species"] is JsonArray species)
        {
            var list = new List<SpeciesSettings>();
            foreach (var s in species)
            {
                if (s is null) continue;
                try
                {
                    var ss = s.Deserialize<SpeciesSettings>(Net.Json.WireIn);
                    if (ss != null) list.Add(ss);
                }
                catch (JsonException) { }
            }
            if (list.Count > 0) r.Species = list;
        }
        if (r.Species.Count == 0 && knownSpecies != null) r.Species = knownSpecies.ToList();

        if (o["seeds"] is JsonArray seeds)
            r.Seeds = seeds.Where(s => s != null).Select(s => new SeedModel
            {
                Species = Get(s!["species"], "") ?? "",
                Px = Get(s["px"], 0.0), Py = Get(s["py"], 0.0), Pz = Get(s["pz"], 0.0),
                Field = Get(s["fi"], 0),
                StartOffsetHours = Get(s["startOffsetHours"], 0.0),
            }).ToList();

        var terrain = ReadTerrainList(o["terrainList"] as JsonArray);
        if (o["obstacles"] is JsonArray obstacles)
        {
            foreach (var ob in obstacles)
            {
                if (ob is null) continue;
                var type = Get(ob["type"], "wall") ?? "wall";
                if (type == "mesh")
                {
                    var vt = Get(ob["vt"], Array.Empty<float>());
                    var fc = Get(ob["fc"], Array.Empty<int>());
                    if (vt.Length == 0) continue;
                    terrain ??= new TerrainData { Version = 1 };
                    terrain.ObstacleMesh = new MeshData(vt, MeshData.ComputeVertexNormals(vt, fc), fc, "obstacle:mesh");
                    if (ob["gr"] is JsonArray groups)
                        terrain.Groups = groups.Where(g => g != null).Select(g => new ObstacleGroup(
                            Get(g!["name"], "") ?? "", Get(g["vertexFirst"], 0), Get(g["vertexCount"], 0),
                            Get(g["indexFirst"], 0), Get(g["indexCount"], 0))).Where(g => g.VertexCount > 0).ToList();
                    continue;
                }
                var l = Get(ob["l"], 0f);
                r.Obstacles.Add(new ObstacleRequest
                {
                    Type = type,
                    Position = new Utils.Json.Vector3XYZ { X = Get(ob["px"], 0f), Y = Get(ob["py"], 0f), Z = Get(ob["pz"], 0f) },
                    Height = Get(ob["h"], 0f),
                    Thickness = Get(ob["t"], 0f),
                    Orientation = Get(ob["ay"], 0f),
                    Length = type == "umbrella" ? null : l,
                    Radius = type == "umbrella" ? l : null,
                });
            }
        }
        c.Terrain = terrain;

        var v = c.View;
        v.Mapping = PlantColors.ParseMapping(Get(o["visualMapping"], "natural"));
        v.DebugBoxes = Get(o["debugDisplayOrientations"], false);
        v.ShowLeaves = Get(o["showLeaves"], true);
        v.ShowTerrain = Get(o["showTerrain"], true);
        v.ShowRoots = Get(o["showRoots"], true);
        v.ShowObstacles = Get(o["showObstacles"], true);
        v.ShowSeeds = Get(o["showSeeds"], true);
        v.PerformPicking = Get(o["performPicking"], true);
        v.ObstacleMapping = Get(o["obstacleMapping"], "none") ?? "none";
        var sky = Get(o["lightSky"], false);
        if (o["lighting"] is JsonObject lo)
            v.Lighting = new LightingOptions(Get(lo["daylight"], false), Get(lo["shadows"], false), Get(lo["occlusion"], false),
                Get(lo["indirect"], false), Get(lo["secondBounce"], false), Get(lo["leafTranslucency"], false), sky,
                Get(lo["shadowBiasTexels"], 4.0));
        else if (o["lightingTier"] is JsonNode tierNode)
        {
            var tier = Get(tierNode, 0);
            v.Lighting = new LightingOptions(Shadows: tier >= 1, Occlusion: tier >= 2, Indirect: tier >= 3, SecondBounce: tier >= 4, Sky: sky);
        }
        else v.Lighting = LightingOptions.Off with { Sky = sky };

        c.SimHoursPerTick = Get<int?>(o["simHoursPerTick"], null) ?? Time.SimClock.Snap(r.HoursPerTick);
        c.Site = new SiteInfo
        {
            Latitude = r.Latitude, Longitude = r.Longitude, Altitude = r.Altitude, AzimuthBase = r.AzimuthBase,
            AzimuthOffset = (int)r.AzimuthOffset, TimeZone = Get<string?>(o["siteTimeZone"], null),
            Coverage = Get(o["weather"], Array.Empty<float>()),
        };
        if (o["obstacleLight"] is JsonObject lightDoc)
            c.ObstacleLight = JsonDocument.Parse(lightDoc.ToJsonString()).RootElement.Clone();
        c.Behaviors = Get(o["behaviors"], Array.Empty<string>());
        return c;
    }

    private static TerrainData? ReadTerrainList(JsonArray? list)
    {
        if (list is null || list.Count == 0) return null;
        var t = new TerrainData { Version = 1 };
        var i = 0;
        foreach (var item in list)
        {
            if (item is null) { i++; continue; }
            if (item["points"] is not null)
            {
                t.Items.Add(new MeshTerrainItem(Get(item["id"], "") ?? "", Get(item["px"], 0f), Get(item["py"], 0f), Get(item["pz"], 0f),
                    Get(item["points"], Array.Empty<float>()), Get(item["triangles"], Array.Empty<int>()), i));
            }
            else
            {
                var d = Get(item["data"], new float[10]);
                if (d.Length >= 10)
                    t.Items.Add(new BoxTerrainItem(Get(item["id"], "") ?? "", d[0], d[1], d[2], d[3], d[4], d[5], d[6], d[7], d[8], d[9], i));
            }
            i++;
        }
        return t;
    }
}
