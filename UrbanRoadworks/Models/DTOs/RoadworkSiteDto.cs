namespace UrbanRoadworks.Models.DTOs
{
    public class RoadworkSiteDto
    {
        public string? Name { get; set; }
        public string? Status { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Geometry { get; set; }
    }
}
