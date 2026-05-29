using Microsoft.AspNetCore.Mvc;
using Npgsql;
using UrbanRoadworks.Data;

namespace UrbanRoadworks.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class RouteController(IConfiguration configuration) : ControllerBase
    {
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")!;

        [HttpGet("route")]
        public async Task<IActionResult> GetRoute(
            [FromQuery] double fromLon, [FromQuery] double fromLat,
            [FromQuery] double toLon, [FromQuery] double toLat)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            const string nearestNodeSql = @"
                SELECT id FROM road_network_noded_vertices
                ORDER BY the_geom <-> ST_Transform(ST_Point(@lon, @lat, 4326), 3857)
                LIMIT 1";

            await using var cmdFrom = new NpgsqlCommand(nearestNodeSql, conn);
            cmdFrom.Parameters.AddWithValue("lon", fromLon);
            cmdFrom.Parameters.AddWithValue("lat", fromLat);
            var sourceNode = (long)(await cmdFrom.ExecuteScalarAsync() ?? 0L);

            await using var cmdTo = new NpgsqlCommand(nearestNodeSql, conn);
            cmdTo.Parameters.AddWithValue("lon", toLon);
            cmdTo.Parameters.AddWithValue("lat", toLat);
            var targetNode = (long)(await cmdTo.ExecuteScalarAsync() ?? 0L);

            if (sourceNode == 0 || targetNode == 0)
                return Ok(Array.Empty<object>());

            const string blockedSql = @"
                SELECT ARRAY(
                    SELECT DISTINCT r.id FROM road_network_noded r
                    JOIN roadwork_sites rs ON rs.status = 'active'
                    WHERE ST_Intersects(r.geom, ST_Transform(rs.geometry, 3857))
                )";
            await using var cmdBlocked = new NpgsqlCommand(blockedSql, conn);
            var blockedArray = (long[])(await cmdBlocked.ExecuteScalarAsync() ?? Array.Empty<long>());

            const string slowedSql = @"
                SELECT ARRAY(
                    SELECT DISTINCT r.id FROM road_network_noded r
                    JOIN roadwork_sites rs ON rs.status = 'planned'
                    WHERE ST_Intersects(r.geom, ST_Transform(rs.geometry, 3857))
                )";
            await using var cmdSlowed = new NpgsqlCommand(slowedSql, conn);
            var slowedArray = (long[])(await cmdSlowed.ExecuteScalarAsync() ?? Array.Empty<long>());

            var blockedIds = blockedArray.Length > 0 ? string.Join(",", blockedArray) : "0";
            var slowedIds = slowedArray.Length > 0 ? string.Join(",", slowedArray) : "0";

            var routeSql = $@"
                SELECT rn.id,
                       ST_AsText(ST_Transform(rn.geom, 4326)) AS geometry,
                       dij.agg_cost
                FROM pgr_dijkstra(
                    'SELECT r.id, r.source, r.target,
                        CASE
                            WHEN r.id IN ({slowedIds}) THEN r.cost * 5
                            WHEN r.oneway_reversed THEN -1
                            ELSE r.cost
                        END AS cost,
                        CASE WHEN r.oneway THEN -1 ELSE r.cost END AS reverse_cost
                     FROM road_network_noded r
                     WHERE r.id NOT IN ({blockedIds})',
                    @source, @target, true
                ) AS dij
                JOIN road_network_noded rn ON rn.id = dij.edge";

            try
            {
                await using var cmdRoute = new NpgsqlCommand(routeSql, conn);
                cmdRoute.Parameters.AddWithValue("source", sourceNode);
                cmdRoute.Parameters.AddWithValue("target", targetNode);

                var results = new List<object>();
                await using var reader = await cmdRoute.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new
                    {
                        osmId = reader.GetInt64(0),
                        geometry = reader.GetString(1),
                        aggCost = reader.GetDouble(2)
                    });
                }
                return Ok(results);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "XX000")
            {
                return Ok(Array.Empty<object>());
            }
        }

        [HttpPost("inspector-route")]
        public async Task<IActionResult> InspectorRoute([FromBody] List<int> siteIds)
        {
            if (siteIds == null || siteIds.Count < 2)
                return BadRequest("At least 2 sites are needed");

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            const string centroidNodeSql = @"
                SELECT v.id
                FROM road_network_noded_vertices v, roadwork_sites cs
                WHERE cs.id = @siteId
                ORDER BY v.the_geom <-> ST_Transform(ST_Centroid(cs.geometry), 3857)
                LIMIT 1";

            var nodeIds = new List<long>();
            foreach (var siteId in siteIds)
            {
                await using var cmd = new NpgsqlCommand(centroidNodeSql, conn);
                cmd.Parameters.AddWithValue("siteId", siteId);
                var nodeId = await cmd.ExecuteScalarAsync();
                if (nodeId != null) nodeIds.Add(Convert.ToInt64(nodeId));
            }

            if (nodeIds.Count < 2)
                return Ok(Array.Empty<object>());

            var optimizedNodes = new List<long> { nodeIds[0] };
            var optimizedSiteIds = new List<int> { siteIds[0] };
            var remaining = nodeIds.Skip(1).ToList();
            var remainingSites = siteIds.Skip(1).ToList();

            while (remaining.Count > 0)
            {
                long current = optimizedNodes.Last();
                int nearestIdx = 0;
                double minCost = double.MaxValue;

                for (int j = 0; j < remaining.Count; j++)
                {
                    const string costSql = @"
                        SELECT agg_cost FROM pgr_dijkstraCost(
                            'SELECT id, source, target,
                                CASE WHEN oneway_reversed THEN -1 ELSE cost END AS cost,
                                CASE WHEN oneway THEN -1 ELSE reverse_cost END AS reverse_cost
                             FROM road_network_noded',
                            @source, @target, true
                        )";
                    try
                    {
                        await using var cmdCost = new NpgsqlCommand(costSql, conn);
                        cmdCost.Parameters.AddWithValue("source", current);
                        cmdCost.Parameters.AddWithValue("target", remaining[j]);
                        var cost = await cmdCost.ExecuteScalarAsync();
                        if (cost != null && cost != DBNull.Value && (double)cost < minCost)
                        {
                            minCost = (double)cost;
                            nearestIdx = j;
                        }
                    }
                    catch { /* not reachable node, skip */ }
                }

                optimizedNodes.Add(remaining[nearestIdx]);
                optimizedSiteIds.Add(remainingSites[nearestIdx]);
                remaining.RemoveAt(nearestIdx);
                remainingSites.RemoveAt(nearestIdx);
            }

            nodeIds = optimizedNodes;
            siteIds = optimizedSiteIds;

            var segments = new List<object>();

            for (int i = 0; i < nodeIds.Count - 1; i++)
            {
                if (nodeIds[i] == nodeIds[i + 1]) continue;

                var segSql = @"
                    SELECT rn.id,
                           ST_AsText(ST_Transform(rn.geom, 4326)) AS geometry,
                           dij.agg_cost
                    FROM pgr_dijkstra(
                        'SELECT id, source, target,
                            CASE WHEN oneway_reversed THEN -1 ELSE cost END AS cost,
                            CASE WHEN oneway THEN -1 ELSE reverse_cost END AS reverse_cost
                         FROM road_network_noded',
                        @source, @target, true
                    ) AS dij
                    JOIN road_network_noded rn ON rn.id = dij.edge";

                try
                {
                    await using var cmdSeg = new NpgsqlCommand(segSql, conn);
                    cmdSeg.Parameters.AddWithValue("source", nodeIds[i]);
                    cmdSeg.Parameters.AddWithValue("target", nodeIds[i + 1]);

                    var legSegments = new List<object>();
                    await using var segReader = await cmdSeg.ExecuteReaderAsync();
                    while (await segReader.ReadAsync())
                    {
                        legSegments.Add(new
                        {
                            osmId = segReader.GetInt64(0),
                            geometry = segReader.GetString(1),
                            aggCost = segReader.GetDouble(2),
                            leg = i
                        });
                    }

                    if (legSegments.Count == 0)
                        return Ok(new { segments = Array.Empty<object>(), orderedSiteIds = siteIds });

                    segments.AddRange(legSegments);
                }
                catch (PostgresException ex) when (ex.SqlState == "XX000")
                {
                    return Ok(new { segments = Array.Empty<object>(), orderedSiteIds = siteIds });
                }
            }

            return Ok(new { segments, orderedSiteIds = siteIds });
        }
    }
}