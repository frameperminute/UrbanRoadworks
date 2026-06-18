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
    public class CanalController(ApplicationDbContext context, IConfiguration configuration) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")!;

        // Returns all cable canals as WKT linestrings. Optional filter: ?status=planned
        [HttpGet("canals")]
        public IActionResult GetCanals([FromQuery] string? status = null)
        {
            var query = _context.Canals.AsQueryable();
            if (!string.IsNullOrEmpty(status))
                query = query.Where(c => c.Status == status);

            var wktWriter = new WKTWriter();
            var result = query.Select(c => new
            {
                c.Id,
                c.Name,
                c.Status,
                c.FromSite,
                c.ToSite,
                Geometry = wktWriter.Write(c.Geometry)
            }).ToList();

            return Ok(result);
        }

        // Creates a new canal from a WKT linestring.
        // FromSite and ToSite are automatically resolved by checking which construction site
        // intersects the start/end point of the drawn line.
        [HttpPost("canals")]
        public IActionResult CreateCanal([FromBody] CanalDto dto)
        {
            var reader = new WKTReader();
            LineString? line = dto.Geometry != null ? (LineString)reader.Read(dto.Geometry) : null;

            int? autoFromSite = null;
            int? autoToSite = null;

            if (line != null)
            {
                line.SRID = 4326;

                autoFromSite = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(line.StartPoint))?.Id;

                autoToSite = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(line.EndPoint))?.Id;
            }

            var canal = new Canal
            {
                Name = dto.Name,
                Status = dto.Status ?? "planned",
                FromSite = autoFromSite,
                ToSite = autoToSite,
                Geometry = line
            };

            _context.Canals.Add(canal);
            _context.SaveChanges();
            return Ok(new { canal.Id, canal.Name, canal.Status });
        }

        // Updates canal name and status. If geometry changes (planned only),
        // FromSite/ToSite are recalculated automatically from the new start/end points.
        [HttpPut("canals/{id}")]
        public IActionResult UpdateCanal(int id, [FromBody] CanalDto dto)
        {
            var canal = _context.Canals.Find(id);
            if (canal == null) return NotFound();
            if (dto.Geometry != null && canal.Status != "planned")
                return BadRequest(new { error = "Geometry can only be modified for planned canals." });

            canal.Name = dto.Name;
            canal.Status = dto.Status;

            if (dto.Geometry != null)
            {
                var reader = new WKTReader();
                var line = (LineString)reader.Read(dto.Geometry);
                line.SRID = 4326;
                canal.Geometry = line;

                canal.FromSite = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(line.StartPoint))?.Id;

                canal.ToSite = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(line.EndPoint))?.Id;
            }

            _context.SaveChanges();
            return Ok(new { canal.Id, canal.Name, canal.Status });
        }

        // Permanently removes the canal from the database
        [HttpDelete("canals/{id}")]
        public IActionResult DeleteCanal(int id)
        {
            var canal = _context.Canals.Find(id);
            if (canal == null) return NotFound();
            _context.Canals.Remove(canal);
            _context.SaveChanges();
            return Ok();
        }

    }
}