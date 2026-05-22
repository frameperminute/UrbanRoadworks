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
    public class CanalController(ApplicationDbContext context) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;

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

        [HttpPost("canals")]
        public IActionResult CreateCanal([FromBody] CanalDto dto)
        {
            var reader = new WKTReader();
            var canal = new Canal
            {
                Name = dto.Name,
                Status = dto.Status ?? "planned",
                FromSite = dto.FromSite,
                ToSite = dto.ToSite,
                Geometry = dto.Geometry != null
                    ? (LineString)reader.Read(dto.Geometry)
                    : null
            };
            _context.Canals.Add(canal);
            _context.SaveChanges();
            return Ok(new { canal.Id, canal.Name, canal.Status });
        }

        [HttpPut("canals/{id}")]
        public IActionResult UpdateCanal(int id, [FromBody] CanalDto dto)
        {
            var canal = _context.Canals.Find(id);
            if (canal == null) return NotFound();
            if (dto.Geometry != null && canal.Status != "planned")
                return BadRequest(new { error = "Geometry can only be modified for planned canals." });
            canal.Name = dto.Name;
            canal.Status = dto.Status;
            canal.FromSite = dto.FromSite;
            canal.ToSite = dto.ToSite;

            if (dto.Geometry != null)
            {
                var reader = new WKTReader();
                canal.Geometry = (LineString)reader.Read(dto.Geometry);
            }

            _context.SaveChanges();
            return Ok(new { canal.Id, canal.Name, canal.Status });
        }

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