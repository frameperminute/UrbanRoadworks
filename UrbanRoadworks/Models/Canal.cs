using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace UrbanRoadworks.Models
{
    [Table("canals")]
    public class Canal
    {
        [Column("id")] public int Id { get; set; }
        [Column("name")] public string? Name { get; set; }
        [Column("from_site")] public int? FromSite { get; set; }
        [Column("to_site")] public int? ToSite { get; set; }
        [Column("status")] public string? Status { get; set; }
        [Column("geometry")] public Geometry? Geometry { get; set; }
    }
}
