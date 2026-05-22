using System.Diagnostics;

namespace FacadeJsonImport;

public class CellModel
{
    public int X { get; set; }
    //public int Y { get; set; }
    public string Value { get; set; }
    public string Color { get; set; }
    public string Note { get; set; }
}

public enum Species : byte { Unknown = 0, Geranium_Macrorrhizum = 1, Geranium_Cantagrigiense = 2, Bergenia_Cordifolia = 4, Heuchera = 8, Campanulla = 16, Carex_Morrowii_Variegata = 32, Fragaria = 64, Empty = 255 }
public enum Condition : byte { Normal = 0, Weak = 1, VeryWeak = 2, Dead = 3, Burried = 4, Blooming = 8 }
public enum Annotations : byte { None = 0, LampAnchor, Railing, Sensor }

public readonly struct Slot
{
    static readonly Species[] LettersMap = [Species.Campanulla, Species.Bergenia_Cordifolia, Species.Carex_Morrowii_Variegata, 0, 0, Species.Fragaria, Species.Geranium_Cantagrigiense, Species.Heuchera, 0, 0, 0, 0, Species.Geranium_Macrorrhizum];

    public readonly Species Plant;
    public readonly Condition Condition;
    public readonly Annotations Notes;
    public Slot(CellModel input, int y)
    {
        if (input.Value == "| |")
        {
            Debug.Assert(input.Color == "#9900ff");
            Plant = Species.Empty;
            Condition = Condition.Normal;
            Notes = y < 20 ? Annotations.LampAnchor : Annotations.Railing;
        }
        else
        {
            Debug.Assert(input != null && input.Value.Length == 1);
            Plant = input.Value[0] switch
            {
                '-' => Species.Empty,
                _ => GetSpecies(input.Value[0])
            };
            Condition = input.Color switch
            {
                "#efefef" => Condition.Weak,
                "#cccccc" => Condition.Dead,
                "#ffff00" => Condition.Blooming,
                "#bf9000" => Condition.Burried,
                "#d9ead3" => Condition.VeryWeak,
                _ => Condition.Normal
            };
            Notes = input.Color switch
            {
                "#6fa8dc" => Annotations.Sensor,
                "#9900ff" => Annotations.Railing,
                _ => Annotations.None
            };
        }
    }

    internal static Species GetSpecies(char lowerLetter) => LettersMap[lowerLetter - 'A'];
}

public class Tray
{
    public readonly Slot[] Slots;
    public readonly byte[] Segments;
    public readonly int FirstX;
    public readonly int LastX;


    // public Tray(int slots) => Slots = new Slot[slots];
    public Tray(List<Slot> slots, List<byte> segments, int firstX, int lastX)
    {
        Slots = [.. slots];
        Segments = [..segments];
        FirstX = firstX;
        LastX = lastX;
    }
}

public class Row
{
    readonly Tray[] Trays;
    public Row(IList<CellModel> input, int y)
    {
        var result = new List<Tray>();
        var buffer = new List<Slot>();
        var segments = new List<byte>(){0};
        var prevX = -1;
        var startX = -1;

        for(int i = 0; i < input.Count; ++i)
        {
            if (input[i].X - prevX > 1 && buffer.Count > 0)
            {
                segments.Add((byte)buffer.Count);
                result.Add(new(buffer, segments, startX, input[i].X));
                buffer.Clear();
                segments.Clear();
                segments.Add(0);
            }

            switch(input[i].Value)
            {
                case "|":
                {

                    if (buffer.Count > 0)
                        segments.Add((byte)buffer.Count);
                    break;
                }
                case "||": case "|||": case "🭽": case "🭰": case "🭼": case " ": case "":
                {
                    if (buffer.Count > 0)
                    {
                        segments.Add((byte)buffer.Count);
                        result.Add(new(buffer, segments, startX, input[i].X));
                        buffer.Clear();
                        segments.Clear();
                        segments.Add(0);
                    }
                    break;
                }
                case "...":
                {
                    if (startX < 0) startX = input[i].X;
                    break;
                }
                case "-": case "A": case "B": case "C": case "F": case "G": case "H": case "M": case "| |":
                {
                    if (startX < 0) startX = input[i].X;
                    buffer.Add(new(input[i], y));
                    break;
                }
                //else ignore
            }
            prevX = input[i].X;
        }

        Trays = [..result];
    }
}