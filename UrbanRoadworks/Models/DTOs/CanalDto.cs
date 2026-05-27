namespace UrbanRoadworks.Models.DTOs
{
    public class CanalDto
    {
        public string? Name { get; set; }
        public string? Status { get; set; }
        public string? Geometry { get; set; }  // WKT LINESTRING
    }
}
