using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using AgentsSystem;
using Agro;
using System.Globalization;

public class IrradianceClient
{
    readonly HttpClient Client;
    readonly List<float> Irradiances = new();
    readonly List<int> SkipPlants = new();
    readonly List<Vector3> IrradiancePoints = new();
    readonly Dictionary<IFormation, int> IrradianceFormationOffsets = new ();

/*
#INDEXED DATA
uint32 entitiesCount
    #foreach ENTITY
    uint32 surfacesCount
        #foreach SURFACE (for now, each surface is an irradiancemeter)
        uint8 trianglesCount
        #foreach TRIANGLE
            uint32 index0
            uint32 index1
            uint32 index2
#POINTS DATA
uint32 pointsCount
    #foreach POINT
    float32 x
    float32 y
    float32 z
*/
    private IrradianceClient()
    {
        Client = new();
        Client.BaseAddress = new Uri("http://localhost:9000");
        Client.DefaultRequestHeaders.Add("La", AgroWorld.Latitude.ToString());
        Client.DefaultRequestHeaders.Add("Lo", AgroWorld.Longitude.ToString());
    }

    public static void Tick(uint timestep, IList<IFormation> formations) => Singleton.DoTick(timestep, formations);

    void DoTick(uint timestep, IList<IFormation> formations)
    {
        SkipPlants.Clear();
        for(int i = 0; i < formations.Count; ++i)
            if (!(formations[i] is PlantFormation plant && plant.AG.Alive))
                SkipPlants.Add(i);

        int offsetCounter = 0;
        if (SkipPlants.Count < formations.Count)
        {
            Irradiances.Clear();
            IrradianceFormationOffsets.Clear();
            IrradiancePoints.Clear();

            // var objFileName = $"t{timestep}.obj";
            // using var objStream = File.Open(objFileName, FileMode.Create);
            // using var objWriter = new StreamWriter(objStream, Encoding.UTF8);
            // var obji = new StringBuilder();
            // objWriter.WriteLine("o Field");

            var meshFileName = $"t{timestep}.mesh";
#if GODOT
            var meshFileFullPath = Path.Combine("agroeco-mts3", meshFileName);
#else
            var meshFileFullPath = Path.Combine("..", "agroeco-mts3", meshFileName);
#endif
            using var meshStream = new MemoryStream();
            using var writer = new BinaryWriter(meshStream);
            writer.WriteU32(formations.Count - SkipPlants.Count); //WRITE NUMBER OF PLANTS in this system
            var skipPointer = 0;
            for(int pi = 0; pi < formations.Count; ++pi)
            {
                if (skipPointer < SkipPlants.Count && SkipPlants[skipPointer] == pi)
                    ++skipPointer;
                else
                {
                    var plant = formations[pi] as PlantFormation;
                    var ag = plant.AG;
                    var count = ag.Count;

                    IrradianceFormationOffsets.Add(ag, offsetCounter);
                    offsetCounter += count;

                    //IrradianceSurfacesPerPlant.Add(count);
                    writer.WriteU32(count); //WRITE NUMBER OF SURFACES in this plant

                    for(int i = 0; i < count; ++i)
                    {
                        var organ = ag.GetOrgan(i);
                        var center = ag.GetBaseCenter(i);
                        var scale = ag.GetScale(i);
                        var halfRadiusX = new Vector3(0f, 0f, scale.Z * 0.5f);
                        var orientation = ag.GetDirection(i);
                        //var length = ag.GetLength(i) * 0.5f; //x0.5f because its the radius of the cube!
                        var lengthVector = new Vector3(scale.X, 0f, 0f);

                        //sprite.Transform = new Transform(basis, (Formation.GetBaseCenter(index) + stableScale).ToGodot());
                        //sprite.Scale = (Formation.GetScale(index) * 0.5f).ToGodot();
                        switch(organ)
                        {
                            case OrganTypes.Leaf:
                            {
                                writer.WriteU8(2); //WRITE NUMBER OF TRIANGLES in this surface

                                var p = IrradiancePoints.Count;
                                IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(lengthVector + halfRadiusX, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(lengthVector - halfRadiusX, orientation));

                                //var t = IrradianceTriangles.Count;
                                writer.WriteU32(p);
                                writer.WriteU32(p + 1);
                                writer.WriteU32(p + 2);
                                writer.WriteU32(p);
                                writer.WriteU32(p + 2);
                                writer.WriteU32(p + 3);

                                // obji.AppendLine(OF(p, p+1, p+2));
                                // obji.AppendLine(OF(p, p+2, p+3));

                                //IrradianceTriangles.Add(new (p, p + 1, p + 2));
                                //IrradianceTriangles.Add(new (p, p + 2, p + 3));

                                //IrradianceGroupOffsets.Add(IrradianceGroups.Count);
                                //IrradianceSurfaceSize.Add(2);
                                //IrradianceGroups.Add(t + 1);
                            }
                            break;
                            case OrganTypes.Stem:
                            {
                                var halfRadiusY = new Vector3(0f, scale.Y * 0.5f, 0f);
                                writer.WriteU8(8); //WRITE NUMBER OF TRIANGLES in this surface
                                var p = IrradiancePoints.Count;
                                IrradiancePoints.Add(center + Vector3.Transform(- halfRadiusX - halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX - halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX + halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX + halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(lengthVector - halfRadiusX - halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(lengthVector + halfRadiusX - halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(lengthVector + halfRadiusX + halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(lengthVector - halfRadiusX + halfRadiusY, orientation));

                                //front face
                                writer.WriteU32(p);
                                writer.WriteU32(p + 1);
                                writer.WriteU32(p + 5);
                                writer.WriteU32(p);
                                writer.WriteU32(p + 5);
                                writer.WriteU32(p + 4);
                                //right face
                                writer.WriteU32(p + 1);
                                writer.WriteU32(p + 2);
                                writer.WriteU32(p + 6);
                                writer.WriteU32(p + 1);
                                writer.WriteU32(p + 6);
                                writer.WriteU32(p + 5);
                                //back face
                                writer.WriteU32(p + 2);
                                writer.WriteU32(p + 3);
                                writer.WriteU32(p + 7);
                                writer.WriteU32(p + 2);
                                writer.WriteU32(p + 7);
                                writer.WriteU32(p + 6);
                                //left face
                                writer.WriteU32(p + 3);
                                writer.WriteU32(p);
                                writer.WriteU32(p + 4);
                                writer.WriteU32(p + 3);
                                writer.WriteU32(p + 4);
                                writer.WriteU32(p + 7);

                                // obji.AppendLine(OF(p, p+1, p+5));
                                // obji.AppendLine(OF(p, p+5, p+4));

                                // obji.AppendLine(OF(p+1, p+2, p+6));
                                // obji.AppendLine(OF(p+1, p+6, p+5));

                                // obji.AppendLine(OF(p+2, p+3, p+7));
                                // obji.AppendLine(OF(p+2, p+7, p+6));

                                // obji.AppendLine(OF(p+3, p, p+4));
                                // obji.AppendLine(OF(p+3, p+4, p+7));
                            }
                            break;
                            case OrganTypes.Shoot:
                            {
                                var halfRadiusY = new Vector3(0f, scale.Y * 0.5f, 0f);
                                writer.WriteU8(2); //WRITE NUMBER OF TRIANGLES in this surface
                                var p = IrradiancePoints.Count;
                                IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX - halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX - halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX + halfRadiusY, orientation));
                                IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX + halfRadiusY, orientation));

                                writer.WriteU32(p);
                                writer.WriteU32(p + 1);
                                writer.WriteU32(p + 2);
                                writer.WriteU32(p);
                                writer.WriteU32(p + 2);
                                writer.WriteU32(p + 3);

                                // obji.AppendLine(OF(p, p+1, p+2));
                                // obji.AppendLine(OF(p, p+2, p+3));
                            }
                            break;
                            default: throw new NotImplementedException();
                        }
                    }
                }
            }

            writer.Write((uint)IrradiancePoints.Count);
            for(int i = 0; i < IrradiancePoints.Count; ++i)
            {
                var p = IrradiancePoints[i];
                writer.Write(p.X);
                writer.Write(p.Y);
                writer.Write(p.Z);
                //objWriter.WriteLine($"v {p.X} {p.Y} {p.Z}");
            }

            //objWriter.WriteLine(obji.ToString());

            SW.Start();
            var request = new HttpRequestMessage() {
                Method = HttpMethod.Post,
                Content = new ByteArrayContent(meshStream.GetBuffer(), 0, (int)meshStream.Length)
            };
            request.Headers.Add("Ti", (AgroWorld.InitialTime + TimeSpan.FromHours(timestep / (double)AgroWorld.TotalHours)).ToString("o", CultureInfo.InvariantCulture));

            var result = Client.SendAsync(request).Result;
            using var responseStream = result.Content.ReadAsStreamAsync().Result;
            using var reader = new BinaryReader(responseStream);
            for(int i = 0; i < offsetCounter; ++i)
                Irradiances.Add(reader.ReadSingle());
            SW.Stop();
        }
    }

    public static float GetIrradiance(IFormation formation, int agentIndex) => Singleton.IrradianceFormationOffsets.TryGetValue(formation, out var offset) ? Singleton.Irradiances[offset + agentIndex] : 0f;
    static string OF(int a, int b, int c) => $"f {a+1} {b+1} {c+1}";

    public readonly System.Diagnostics.Stopwatch SW = new();

    ~IrradianceClient()
    {
        Client.Dispose();
    }

    public static readonly IrradianceClient Singleton = new();
}