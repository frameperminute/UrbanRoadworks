using Microsoft.AspNetCore.Mvc;
using Npgsql;
using UrbanRoadworks.Models.DTOs;

namespace UrbanRoadworks.API;

[Route("api/[controller]")]
[ApiController]
public class CableCalculatorController(IConfiguration configuration) : ControllerBase
{
    private readonly string _conn = configuration.GetConnectionString("DefaultConnection")!;

    private static readonly Dictionary<string, double> DrillingRate =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "concrete", 2.0 }, { "brick", 1.5 }, { "stone", 2.5 },
            { "drywall",  0.5 }, { "wood",  0.3 }, { "default", 1.0 }
        };

    [HttpGet("calculate")]
    public async Task<IActionResult> Calculate([FromQuery] string canalIds)
    {
        if (string.IsNullOrWhiteSpace(canalIds))
            return BadRequest("Provide at least one canalId.");

        var ids = canalIds.Split(',')
                          .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                          .Where(n => n > 0).Distinct().ToArray();

        if (ids.Length == 0) return BadRequest("No valid canal IDs.");

        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();

        // 1. Canal length — ordered by user selection
        const string canalSql = @"
            SELECT id, ST_Length(geometry::geography) AS length_m
            FROM canals WHERE id = ANY(@ids)
            ORDER BY array_position(@ids, id)";

        var canals = new List<(int Id, double LengthM)>();
        await using (var cmd = new NpgsqlCommand(canalSql, conn))
        {
            cmd.Parameters.AddWithValue("ids", ids);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                canals.Add((r.GetInt32(0), r.GetDouble(1)));
        }

        if (canals.Count == 0) return NotFound("No canals found.");

        // 2. Walls for each canal
        const string wallSql = @"
            SELECT
                w.id,
                COALESCE(w.thickness, 20),
                COALESCE(w.material, 'default'),
                COALESCE(
                    ST_NumGeometries(
                        ST_CollectionExtract(
                            ST_Intersection(w.geometry, c.geometry), 1
                        )
                    ), 1
                ) AS crossing_count
            FROM walls w
            JOIN canals c ON ST_Intersects(w.geometry, c.geometry)
            WHERE c.id = @canalId";

        // 3. UTP nodes with full orientation:
        //    - For canals after the first: orient so that ST_StartPoint is closest
        //      to ST_EndPoint of the previous canal (same as before).
        //    - For the first canal: orient so that ST_EndPoint is closest to
        //      ST_StartPoint of the next canal — guaranteeing the first node
        //      is placed 100 m from the FREE end of the first canal, not the junction.
        const string nodesSql = @"
            WITH canal_lengths AS (
                SELECT id,
                       ST_Length(geometry::geography) AS len,
                       array_position(@ids, id) AS pos,
                       ST_Transform(geometry, 3857) AS geom3857
                FROM canals
                WHERE id = ANY(@ids)
            ),
            oriented AS (
                SELECT
                    curr.id,
                    curr.len,
                    curr.pos,
                    CASE
                        -- First canal: orient so its END is close to next canal's nearest endpoint
                        WHEN prev.id IS NULL AND nxt.id IS NOT NULL THEN
                            CASE
                                WHEN ST_Distance(ST_EndPoint(curr.geom3857),
                                                 ST_StartPoint(nxt.geom3857))
                                   <= ST_Distance(ST_EndPoint(curr.geom3857),
                                                  ST_EndPoint(nxt.geom3857))
                                  OR ST_Distance(ST_EndPoint(curr.geom3857),
                                                 ST_StartPoint(nxt.geom3857))
                                   <= ST_Distance(ST_StartPoint(curr.geom3857),
                                                  ST_StartPoint(nxt.geom3857))
                                THEN curr.geom3857            -- EndPoint already faces next
                                ELSE ST_Reverse(curr.geom3857) -- flip so EndPoint faces next
                            END
                        -- Single canal: no orientation needed
                        WHEN prev.id IS NULL THEN curr.geom3857
                        -- Subsequent canals: orient so StartPoint continues from prev EndPoint
                        WHEN ST_Distance(ST_EndPoint(prev.geom3857),
                                         ST_StartPoint(curr.geom3857))
                           <= ST_Distance(ST_EndPoint(prev.geom3857),
                                          ST_EndPoint(curr.geom3857))
                        THEN curr.geom3857
                        ELSE ST_Reverse(curr.geom3857)
                    END AS oriented_geom
                FROM canal_lengths curr
                LEFT JOIN canal_lengths prev ON prev.pos = curr.pos - 1
                LEFT JOIN canal_lengths nxt  ON nxt.pos  = curr.pos + 1
            ),
            cumulative AS (
                SELECT id, len, pos, oriented_geom,
                    SUM(len) OVER (
                        ORDER BY pos
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                    ) AS cum_end,
                    COALESCE(SUM(len) OVER (
                        ORDER BY pos
                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
                    ), 0) AS cum_start
                FROM oriented
            ),
            total AS (SELECT SUM(len) AS total_len FROM canal_lengths),
            node_distances AS (
                SELECT generate_series(100, (total_len - 1)::int, 100) AS dist_m
                FROM total
            )
            SELECT
                nd.dist_m,
                c.id AS canal_id,
                ST_X(ST_Transform(
                    ST_LineInterpolatePoint(
                        c.oriented_geom,
                        (nd.dist_m - c.cum_start) / c.len
                    ), 4326)) AS lon,
                ST_Y(ST_Transform(
                    ST_LineInterpolatePoint(
                        c.oriented_geom,
                        (nd.dist_m - c.cum_start) / c.len
                    ), 4326)) AS lat
            FROM node_distances nd
            JOIN cumulative c
              ON nd.dist_m > c.cum_start AND nd.dist_m <= c.cum_end
            ORDER BY nd.dist_m";

        var plan = new CablePlanDto();

        // Recover nodes
        await using (var cmd = new NpgsqlCommand(nodesSql, conn))
        {
            cmd.Parameters.AddWithValue("ids", ids);
            await using var r = await cmd.ExecuteReaderAsync();
            int idx = 1;
            while (await r.ReadAsync())
                plan.NodePoints.Add(new NodePointDto
                {
                    NodeIndex = idx++,
                    CanalId = r.GetInt32(1),
                    Lon = r.GetDouble(2),
                    Lat = r.GetDouble(3)
                });
        }

        // Recover walls and build route
        foreach (var (id, lengthM) in canals)
        {
            var walls = new List<WallIntersectionDto>();
            int wallTimeMin = 0;

            await using (var cmd = new NpgsqlCommand(wallSql, conn))
            {
                cmd.Parameters.AddWithValue("canalId", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    double thick = r.GetDouble(1);
                    string mat = r.GetString(2);
                    int crossings = r.GetInt32(3);
                    double rate = DrillingRate.GetValueOrDefault(mat, DrillingRate["default"]);
                    int drillMin = (int)Math.Ceiling(thick * rate) * crossings;
                    wallTimeMin += drillMin;
                    walls.Add(new WallIntersectionDto
                    {
                        WallId = r.GetInt32(0),
                        ThicknessCm = thick,
                        Material = mat,
                        CrossingCount = crossings,
                        DrillingTimeMin = drillMin
                    });
                }
            }

            plan.Route.Add(new CanalSegmentDto
            {
                CanalId = id,
                LengthM = Math.Round(lengthM, 1),
                Walls = walls
            });

            plan.TotalCableMeters += lengthM;
            plan.TotalWorkTimeMin += wallTimeMin;
        }

        plan.NodesNeeded = plan.NodePoints.Count;
        plan.UtpSegmentsCount = plan.NodesNeeded + 1;
        plan.TotalCableMeters = Math.Round(plan.TotalCableMeters, 1);

        return Ok(plan);
    }
}