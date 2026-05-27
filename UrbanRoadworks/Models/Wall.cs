using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace UrbanRoadworks.Models
{
    [Table("walls")]
    public class Wall
    {
        [Column("id")] public int Id { get; set; }
        [Column("site_id")] public int? SiteId { get; set; }
        [Column("thickness")] public double Thickness { get; set; }
        [Column("material")] public string? Material { get; set; }
        [Column("geometry")] public Geometry? Geometry { get; set; }
    }
}
