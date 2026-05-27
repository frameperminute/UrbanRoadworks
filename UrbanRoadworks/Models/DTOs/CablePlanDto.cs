namespace UrbanRoadworks.Models.DTOs;

public class CablePlanDto
{
    public double TotalCableMeters { get; set; }
    public int UtpSegmentsCount { get; set; }
    public int NodesNeeded { get; set; }
    public int TotalWorkTimeMin { get; set; }
    public List<CanalSegmentDto> Route { get; set; } = new();
    public List<NodePointDto> NodePoints { get; set; } = new();

}

public class CanalSegmentDto
{
    public int CanalId { get; set; }
    public double LengthM { get; set; }
    public int CableSegments { get; set; }
    public int IntermediateNodes { get; set; }
    public List<WallIntersectionDto> Walls { get; set; } = new();
}

public class WallIntersectionDto
{
    public int WallId { get; set; }
    public double ThicknessCm { get; set; }
    public string Material { get; set; } = string.Empty;
    public int DrillingTimeMin { get; set; }
}

public class NodePointDto
{
    public int NodeIndex { get; set; }
    public double Lon { get; set; }
    public double Lat { get; set; }
    public int CanalId { get; set; }
}
