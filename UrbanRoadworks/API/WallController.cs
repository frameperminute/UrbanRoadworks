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
                w.SiteId,
                w.Thickness,
                w.Material,
                Geometry = wktWriter.Write(w.Geometry)
            }).ToList();

            return Ok(result);
        }

        [HttpPost("walls")]
        public IActionResult CreateWall([FromBody] WallDto dto)
        {
            var reader = new WKTReader();
            var wall = new Wall
            {
                SiteId = dto.SiteId,
                Thickness = dto.Thickness,
                Material = dto.Material,
                Geometry = dto.Geometry != null
                    ? (LineString)reader.Read(dto.Geometry)
                    : null
            };
            _context.Walls.Add(wall);
            _context.SaveChanges();
            return Ok(new { wall.Id, wall.SiteId, wall.Thickness, wall.Material });
        }

        [HttpPut("walls/{id}")]
        public IActionResult UpdateWall(int id, [FromBody] WallDto dto)
        {
            var wall = _context.Walls.Find(id);
            if (wall == null) return NotFound();

            wall.SiteId = dto.SiteId;
            wall.Thickness = dto.Thickness;
            wall.Material = dto.Material;

            if (dto.Geometry != null)
            {
                var reader = new WKTReader();
                wall.Geometry = (LineString)reader.Read(dto.Geometry);
            }

            _context.SaveChanges();
            return Ok(new { wall.Id, wall.SiteId, wall.Thickness, wall.Material });
        }

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
