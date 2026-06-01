using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.IO;
using NetTopologySuite.Geometries;
using UrbanRoadworks.Data;
using UrbanRoadworks.Models;
using UrbanRoadworks.Models.DTOs;

namespace UrbanRoadworks.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class AssetController(ApplicationDbContext context) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;

        // Returns all roadwork assets (traffic lights, signs, etc.) as WKT.
        // Optional filter: ?assetType=temporary_traffic_light
        [HttpGet("assets")]
        public IActionResult GetAssets([FromQuery] string? assetType = null)
        {
            // Filters by assetType if provided, otherwise returns all assets
            var query = _context.RoadworkAssets.AsQueryable();

            if (!string.IsNullOrEmpty(assetType))
                query = query.Where(a => a.AssetType == assetType);

            var wktWriter = new WKTWriter();
            var result = query.Select(a => new
            {
                a.Id,
                a.AssetType,
                a.SiteId,
                Geometry = wktWriter.Write(a.Geometry)
            }).ToList();

            return Ok(result);
        }

        // Creates a new roadwork asset from a WKT point geometry
        [HttpPost("assets")]
        public IActionResult CreateAsset([FromBody] RoadworkAssetDto dto)
        {
            var reader = new WKTReader();
            var asset = new RoadworkAsset
            {
                AssetType = dto.AssetType,
                SiteId = dto.SiteId,
                Geometry = dto.Geometry != null
                    ? (Point)reader.Read(dto.Geometry)
                    : null
            };
            _context.RoadworkAssets.Add(asset);
            _context.SaveChanges();
            return Ok(new { asset.Id, asset.AssetType, asset.SiteId });
        }

        // Updates asset type and associated construction site; optionally updates geometry
        [HttpPut("assets/{id}")]
        public IActionResult UpdateAsset(int id, [FromBody] RoadworkAssetDto dto)
        {
            var asset = _context.RoadworkAssets.Find(id);
            if (asset == null) return NotFound();
            asset.AssetType = dto.AssetType;
            asset.SiteId = dto.SiteId;

            if (!string.IsNullOrEmpty(dto.Geometry))
            {
                var reader = new WKTReader();
                asset.Geometry = (Point)reader.Read(dto.Geometry);
            }

            _context.SaveChanges();
            return Ok(new { asset.Id, asset.AssetType, asset.SiteId });
        }

        // Permanently removes the asset from the database
        [HttpDelete("assets/{id}")]
        public IActionResult DeleteAsset(int id)
        {
            var asset = _context.RoadworkAssets.Find(id);
            if (asset == null) return NotFound();
            _context.RoadworkAssets.Remove(asset);
            _context.SaveChanges();
            return Ok();
        }
    }
}
