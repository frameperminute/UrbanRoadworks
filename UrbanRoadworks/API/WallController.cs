using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using UrbanRoadworks.Data;
using UrbanRoadworks.Models;
using UrbanRoadworks.Models.DTOs;

namespace UrbanRoadworks.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class WallController(ApplicationDbContext context) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;

        // Returns all walls as WKT linestrings
        [HttpGet("walls")]
        public IActionResult GetWalls([FromQuery] int? siteId = null)
        {
            var query = _context.Walls.AsQueryable();
            if (siteId.HasValue)
                query = query.Where(w => w.SiteId == siteId);

            var wktWriter = new WKTWriter();
            var result = query.Select(w => new
            {
                w.Id,
                w.Name,
                w.SiteId,
                w.Thickness,
                w.Material,
                Geometry = wktWriter.Write(w.Geometry)
            }).ToList();

            return Ok(result);
        }

        // Creates a new wall from a WKT linestring with thickness (cm) and material
        [HttpPost("walls")]
        public IActionResult CreateWall([FromBody] WallDto dto)
        {
            var reader = new WKTReader();
            var geom = dto.Geometry != null ? reader.Read(dto.Geometry) : null;

            int? autoSiteId = null;
            if (geom != null)
            {
                geom.SRID = 4326;

                autoSiteId = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(geom))?.Id;
            }

            var wall = new Wall
            {
                Name = dto.Name,
                SiteId = autoSiteId,
                Thickness = dto.Thickness,
                Material = dto.Material,
                Geometry = (LineString?)geom
            };

            _context.Walls.Add(wall);
            _context.SaveChanges();
            return Ok(new { wall.Id, wall.SiteId, wall.Thickness, wall.Material });
        }

        // Updates wall name, thickness, material, and optionally geometry
        [HttpPut("walls/{id}")]
        public IActionResult UpdateWall(int id, [FromBody] WallDto dto)
        {
            var wall = _context.Walls.Find(id);
            if (wall == null) return NotFound();

            wall.Name = dto.Name;
            wall.Thickness = dto.Thickness;
            wall.Material = dto.Material;

            if (dto.Geometry != null)
            {
                var reader = new WKTReader();
                var geom = reader.Read(dto.Geometry);
                geom.SRID = 4326;
                wall.Geometry = (LineString)geom;

                wall.SiteId = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(geom))?.Id;
            }

            _context.SaveChanges();
            return Ok(new { wall.Id, wall.SiteId, wall.Thickness, wall.Material });
        }

        // Permanently removes the wall from the database
        [HttpDelete("walls/{id}")]
        public IActionResult DeleteWall(int id)
        {
            var wall = _context.Walls.Find(id);
            if (wall == null) return NotFound();
            _context.Walls.Remove(wall);
            _context.SaveChanges();
            return Ok();
        }
    }
}
