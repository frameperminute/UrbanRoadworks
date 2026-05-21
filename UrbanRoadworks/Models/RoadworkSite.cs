using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace UrbanRoadworks.Models
{
    [Table("roadwork_sites")]
    public class RoadworkSite
    {
        [Column("id")] public int Id { get; set; }
        [Column("name")] public string? Name { get; set; }
        [Column("status")] public string? Status { get; set; }
        [Column("start_date")] public DateTime? StartDate { get; set; }
        [Column("end_date")] public DateTime? EndDate { get; set; }
        [Column("geometry")] public Geometry? Geometry { get; set; }
    }
}
