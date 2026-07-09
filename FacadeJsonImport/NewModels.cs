namespace FacadeJsonImport;

public class TrayModel
{
    public TrayModel() {}

    public int Panel { get; set; }
    public int Tray { get; set; }
    public int Section { get; set; }
    public int Row { get; set; }
    public string TrayId { get; set; }
    public string SectionId { get; set; }
    public PlantModel[] Plants { get; set; }
}

public class PlantModel
{
    public PlantModel() {}

    public string? Species { get; set; }
    public string? Key { get; set; }
    public string? Type { get; set; }
    public string? Note { get; set; }
    public string? Status  { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}