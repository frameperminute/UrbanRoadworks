using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.IO;
using Npgsql;
using NetTopologySuite.Geometries;
using UrbanRoadworks.Data;
using UrbanRoadworks.Models;
using UrbanRoadworks.Models.DTOs;

namespace UrbanRoadworks.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class SiteController(ApplicationDbContext context, IConfiguration configuration) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")!;

        // Returns all construction sites as WKT polygons. Optional filter: ?status=active
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

        // Creates a new construction site from a WKT polygon
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
                    ? (Polygon)reader.Read(dto.Geometry)
                    : null
            };
            _context.RoadworkSites.Add(site);
            _context.SaveChanges();
            return Ok(new { site.Id, site.Name, site.Status });
        }

        // Updates site metadata and optionally its geometry (only allowed for planned sites).
        // When status is set to 'completed', all linked assets are automatically removed.
        [HttpPut("sites/{id}")]
        public IActionResult UpdateSite(int id, [FromBody] RoadworkSiteDto dto)
        {
            var site = _context.RoadworkSites.Find(id);
            if (site == null) return NotFound();
            if (dto.Geometry != null && site.Status != "planned")
                return BadRequest(new { error = "Geometry can only be modified for planned sites." });
            site.Name = dto.Name;
            site.Status = dto.Status;
            site.StartDate = dto.StartDate.HasValue
                ? DateTime.SpecifyKind(dto.StartDate.Value, DateTimeKind.Utc) : null;
            site.EndDate = dto.EndDate.HasValue
                ? DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc) : null;

            if (dto.Geometry != null)
            {
                var reader = new WKTReader();
                site.Geometry = (Polygon)reader.Read(dto.Geometry);
            }

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

        // Permanently removes the site from the database
        [HttpDelete("sites/{id}")]
        public IActionResult DeleteSite(int id)
        {
            var site = _context.RoadworkSites.Find(id);
            if (site == null) return NotFound();
            _context.RoadworkSites.Remove(site);
            _context.SaveChanges();
            return Ok();
        }

        // Returns the N nearest construction sites to a clicked point, ranked by road distance.
        [HttpGet("nearest-by-road")]
        public async Task<IActionResult> GetNearestByRoad([FromQuery] double lon, [FromQuery] double lat, [FromQuery] int n = 3)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // SQL: finds the road network vertex nearest to the clicked point (EPSG:4326 -> 3857)
            // using the <-> k-Nearest Neighbors operator for efficient index-based search
            await using var cmdSource = new NpgsqlCommand(@"
                SELECT id FROM road_network_noded_vertices
                ORDER BY the_geom <-> ST_Transform(ST_Point(@lon, @lat, 4326), 3857)
                LIMIT 1", conn);
            cmdSource.Parameters.AddWithValue("lon", lon);
            cmdSource.Parameters.AddWithValue("lat", lat);
            var sourceNode = (long)(await cmdSource.ExecuteScalarAsync() ?? 0L);
            if (sourceNode == 0) return Ok(Array.Empty<object>());

            // SQL: for each non-completed site, finds the nearest road vertex to its centroid
            // using CROSS JOIN LATERAL to apply the nearest-neighbour search per row
            await using var cmdSitesAndNodes = new NpgsqlCommand(@"
                SELECT rs.id, v.id 
                FROM roadwork_sites rs
                CROSS JOIN LATERAL (
                    SELECT id FROM road_network_noded_vertices
                    ORDER BY the_geom <-> ST_Transform(ST_Centroid(rs.geometry), 3857)
                    LIMIT 1
                ) v
                WHERE rs.geometry IS NOT NULL AND rs.status != 'completed'", conn);

            var targetNodes = new Dictionary<long, int>();
            await using (var rdr = await cmdSitesAndNodes.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                {
                    var siteId = rdr.GetInt32(0);
                    var node = rdr.GetInt64(1);
                    if (node != 0 && node != sourceNode)
                        targetNodes.TryAdd(node, siteId);
                }
            }

            if (targetNodes.Count == 0) return Ok(Array.Empty<object>());

            var targets = string.Join(",", targetNodes.Keys);

            // SQL: runs pgr_dijkstraCost from the source node to all site nodes in one call,
            // returns the N cheapest paths ordered by accumulated cost
            await using var cmdDijk = new NpgsqlCommand($@"
                SELECT end_vid, agg_cost
                FROM pgr_dijkstraCost(
                    'SELECT id, source, target,
                            CASE WHEN oneway_reversed THEN -1 ELSE cost END AS cost,
                            CASE WHEN oneway THEN -1 ELSE reverse_cost END AS reverse_cost
                     FROM road_network_noded',
                    {sourceNode}, ARRAY[{targets}], directed := true
                )
                ORDER BY agg_cost
                LIMIT {n}", conn);

            var wktWriter = new WKTWriter();
            var results = new List<object>();

            await using (var rdr = await cmdDijk.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                {
                    var endNode = rdr.GetInt64(0);
                    var cost = rdr.GetDouble(1);
                    if (!targetNodes.TryGetValue(endNode, out var siteId)) continue;

                    var site = _context.RoadworkSites.Find(siteId);
                    if (site == null) continue;

                    results.Add(new
                    {
                        site.Id,
                        site.Name,
                        site.Status,
                        site.StartDate,
                        site.EndDate,
                        RoadDistanceMeters = (int)cost,
                        Geometry = wktWriter.Write(site.Geometry) 
                    });
                }
            }
            return Ok(results);
        }
    }
}
