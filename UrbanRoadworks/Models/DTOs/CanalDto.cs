namespace UrbanRoadworks.Models.DTOs
{
    public class CanalDto
    {
        public string? Name { get; set; }
        public int? FromSite { get; set; }
        public int? ToSite { get; set; }
        public string? Status { get; set; }
        public string? Geometry { get; set; }  // WKT LINESTRING
    }
}
