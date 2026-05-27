using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Npgsql;
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
            LineString? line = dto.Geometry != null ? (LineString)reader.Read(dto.Geometry) : null;

            int? autoFromSite = null;
            int? autoToSite = null;

            if (line != null)
            {
                line.SRID = 4326;
                // Calcolo automatico: trova il sito che interseca il punto di inizio e quello di fine
                autoFromSite = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(line.StartPoint))?.Id;

                autoToSite = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(line.EndPoint))?.Id;
            }

            var canal = new Canal
            {
                Name = dto.Name,
                Status = dto.Status ?? "planned",
                FromSite = autoFromSite, // Assegnato in automatico
                ToSite = autoToSite,     // Assegnato in automatico
                Geometry = line
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

            if (dto.Geometry != null)
            {
                var reader = new WKTReader();
                var line = (LineString)reader.Read(dto.Geometry);
                line.SRID = 4326;
                canal.Geometry = line;

                // Ricalcolo automatico dei siti in base alla nuova geometria
                canal.FromSite = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(line.StartPoint))?.Id;

                canal.ToSite = _context.RoadworkSites
                    .FirstOrDefault(s => s.Geometry != null && s.Geometry.Intersects(line.EndPoint))?.Id;
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
        // GET /api/canal/topology
        // Restituisce le coppie di canali connessi (entro 0.5 m) — utile per debug
        [HttpGet("topology")]
        public async Task<IActionResult> GetTopology()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = @"
                    SELECT
                        a.id                                            AS canal_a,
                        b.id                                            AS canal_b,
                        a.from_site,
                        a.to_site,
                        b.from_site                                     AS b_from_site,
                        b.to_site                                       AS b_to_site,
                        ROUND(CAST(
                            ST_Distance(
                                ST_Transform(a.geometry, 3857),
                                ST_Transform(b.geometry, 3857)
                            ) AS numeric
                        ), 3)                                           AS distance_m
                    FROM canals a
                    JOIN canals b ON a.id < b.id
                    WHERE ST_DWithin(
                        ST_Transform(a.geometry, 3857),
                        ST_Transform(b.geometry, 3857),
                        0.5
                    )
                    ORDER BY distance_m";

            var edges = new List<object>();

            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                edges.Add(new
                {
                    CanalA = reader.GetInt32(0),
                    CanalB = reader.GetInt32(1),
                    FromSiteA = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                    ToSiteA = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                    FromSiteB = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                    ToSiteB = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                    DistanceM = reader.GetDouble(6)
                });
            }

            return Ok(new
            {
                TotalConnectedPairs = edges.Count,
                Edges = edges
            });
        }
    }
}