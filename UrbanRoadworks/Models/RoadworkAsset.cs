using System.ComponentModel.DataAnnotations.Schema;
using NetTopologySuite.Geometries;

namespace UrbanRoadworks.Models
{
    [Table("roadwork_assets")]
    public class RoadworkAsset
    {
        [Column("id")] public int Id { get; set; }
        [Column("asset_type")] public string? AssetType { get; set; }
        [Column("site_id")] public int? SiteId { get; set; }
        [Column("geometry")] public Geometry? Geometry { get; set; }
    }
}
