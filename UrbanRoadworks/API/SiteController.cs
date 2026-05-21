using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.IO;
using UrbanRoadworks.Data;
using UrbanRoadworks.Models;
using UrbanRoadworks.Models.DTOs;

namespace UrbanRoadworks.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class SiteController(ApplicationDbContext context) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;

        // all construction sites (polygons)
        // Optional parameter: ?status=active
        [HttpGet("sites")]
        public IActionResult GetSites([FromQuery] string? status = null)
        {
            var query = _context.RoadworkSites.AsQueryable();

            if (!string.IsNullOrEmpty(status))
                query = query.Where(s => s.Status == status);

            var wktWriter = new WKTWriter();
            var result = query.Select(s => new
            {
                s.Id,
                s.Name,
                s.Status,
                s.StartDate,
                s.EndDate,
                Geometry = wktWriter.Write(s.Geometry)
            }).ToList();

            return Ok(result);
        }

        [HttpPost("sites")]
        public IActionResult CreateSite([FromBody] RoadworkSiteDto dto)
        {
            var reader = new WKTReader();
            var site = new RoadworkSite
            {
                Name = dto.Name,
                Status = dto.Status,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                Geometry = dto.Geometry != null
                    ? (NetTopologySuite.Geometries.Polygon)reader.Read(dto.Geometry)
                    : null
            };
            _context.RoadworkSites.Add(site);
            _context.SaveChanges();
            return Ok(new { site.Id, site.Name, site.Status });
        }

        // updates construction site status
        [HttpPut("sites/{id}")]
        public IActionResult UpdateSite(int id, [FromBody] RoadworkSiteDto dto)
        {
            var site = _context.RoadworkSites.Find(id);
            if (site == null) return NotFound();

            site.Name = dto.Name;
            site.Status = dto.Status;
            site.StartDate = dto.StartDate.HasValue
                ? DateTime.SpecifyKind(dto.StartDate.Value, DateTimeKind.Utc) : null;
            site.EndDate = dto.EndDate.HasValue
                ? DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc) : null;

            if (dto.Status == "completed")
            {
                var assets = _context.RoadworkAssets
                    .Where(a => a.SiteId == id)
                    .ToList();
                _context.RoadworkAssets.RemoveRange(assets);
            }

            _context.SaveChanges();
            return Ok(new { site.Id, site.Name, site.Status });
        }

        // deletes construction site
        [HttpDelete("sites/{id}")]
        public IActionResult DeleteSite(int id)
        {
            var site = _context.RoadworkSites.Find(id);
            if (site == null) return NotFound();
            _context.RoadworkSites.Remove(site);
            _context.SaveChanges();
            return Ok();
        }
    }
}
