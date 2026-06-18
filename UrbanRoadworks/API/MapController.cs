using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace UrbanRoadworks.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class MapController(IConfiguration configuration) : ControllerBase
    {
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")!;

        // Returns all road network segments (grey base layer) with WKT geometry in EPSG:4326.
        [HttpGet("roads")]
        public async Task<IActionResult> GetRoads()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // Selects all rows from road_network, converting geometry from 3857 -> 4326
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

        // Returns only the road segments that intersect active or planned construction sites.
        // It cuts the geometry to show only the overlapping area.
        [HttpGet("affected-network-roads")]
        public async Task<IActionResult> GetAffectedNetworkRoads()
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                // SQL: DISTINCT ON (rn.id) avoids duplicate roads when a segment crosses multiple sites;
                //      ST_Intersection clips the geometry to the exact overlap;
                //      ORDER BY status ensures 'active' is preferred over 'planned' when deduplicating
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