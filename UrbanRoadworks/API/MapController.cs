using Microsoft.AspNetCore.Mvc;
using Npgsql;
using UrbanRoadworks.Data;

namespace UrbanRoadworks.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class MapController(ApplicationDbContext context, IConfiguration configuration) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")!;

        // all roads of road_network (grey base layer)
        [HttpGet("roads")]
        public async Task<IActionResult> GetRoads()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"SELECT id, osm_id, name, highway,
                           ST_AsText(ST_Transform(geom, 4326)) AS geometry
                    FROM road_network";

                await using var cmd = new NpgsqlCommand(sql, conn);
                var results = new List<object>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        id = reader.GetInt32(0),
                        osmId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                        name = reader.IsDBNull(2) ? null : reader.GetString(2),
                        highway = reader.IsDBNull(3) ? null : reader.GetString(3),
                        geometry = reader.GetString(4)
                    });
                }
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // only road_network roads that intersect active/planned construction sites
        [HttpGet("affected-network-roads")]
        public async Task<IActionResult> GetAffectedNetworkRoads()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"
                    SELECT DISTINCT ON (rn.id)
                        rn.id,
                        rn.name,
                        rs.status AS site_status,
                        ST_AsText(
                            ST_Transform(
                                ST_Intersection(rn.geom, ST_Transform(rs.geometry, 3857)),
                                4326
                            )
                        ) AS geometry
                    FROM road_network rn
                    JOIN roadwork_sites rs
                        ON ST_Intersects(rn.geom, ST_Transform(rs.geometry, 3857))
                    WHERE rs.status IN ('active', 'planned')
                      AND NOT ST_IsEmpty(ST_Intersection(rn.geom, ST_Transform(rs.geometry, 3857)))
                    ORDER BY rn.id, rs.status";

                await using var cmd = new NpgsqlCommand(sql, conn);
                var results = new List<object>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        id = reader.GetInt32(0),
                        name = reader.IsDBNull(1) ? null : reader.GetString(1),
                        siteStatus = reader.IsDBNull(2) ? null : reader.GetString(2),
                        geometry = reader.GetString(3)
                    });
                }
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}