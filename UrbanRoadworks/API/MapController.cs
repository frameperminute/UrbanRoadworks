using Microsoft.AspNetCore.Mvc;
using NetTopologySuite.IO;
using Npgsql;
using UrbanRoadworks.Data;
using UrbanRoadworks.Models;

namespace UrbanRoadworks.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class MapController(ApplicationDbContext context, IConfiguration configuration) : ControllerBase
    {
        private readonly ApplicationDbContext _context = context;
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")!;

        // all construction sites (polygons)
        // Optional parameter: ?status=active
        [HttpGet("sites")]
        public IActionResult GetSites([FromQuery] string? status = null)
        {
            var query = _context.RoadworkSites.AsQueryable();

            // Attributive Filter (Data Filters from the slide)
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

        // only road_network roads that intersect active/planned construction sites,
        // with the status of the construction site
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

        // asset points (traffic lights, signals)
        [HttpGet("assets")]
        public IActionResult GetAssets([FromQuery] string? assetType = null)
        {
            var query = _context.RoadworkAssets.AsQueryable();

            if (!string.IsNullOrEmpty(assetType))
                query = query.Where(a => a.AssetType == assetType);

            var wktWriter = new WKTWriter();
            var result = query.Select(a => new
            {
                a.Id,
                a.AssetType,
                a.SiteId,
                Geometry = wktWriter.Write(a.Geometry)
            }).ToList();

            return Ok(result);
        }

        // create new asset
        [HttpPost("assets")]
        public IActionResult CreateAsset([FromBody] RoadworkAssetDto dto)
        {
            var reader = new WKTReader();
            var asset = new RoadworkAsset
            {
                AssetType = dto.AssetType,
                SiteId = dto.SiteId,
                Geometry = dto.Geometry != null
                    ? (NetTopologySuite.Geometries.Point)reader.Read(dto.Geometry)
                    : null
            };
            _context.RoadworkAssets.Add(asset);
            _context.SaveChanges();
            return Ok(new { asset.Id, asset.AssetType, asset.SiteId });
        }

        // updates type and associated construction site
        [HttpPut("assets/{id}")]
        public IActionResult UpdateAsset(int id, [FromBody] RoadworkAssetDto dto)
        {
            var asset = _context.RoadworkAssets.Find(id);
            if (asset == null) return NotFound();
            asset.AssetType = dto.AssetType;
            asset.SiteId = dto.SiteId;

            if (!string.IsNullOrEmpty(dto.Geometry))
            {
                var reader = new WKTReader();
                asset.Geometry = (NetTopologySuite.Geometries.Point)reader.Read(dto.Geometry);
            }

            _context.SaveChanges();
            return Ok(new { asset.Id, asset.AssetType, asset.SiteId });
        }

        // delete asset
        [HttpDelete("assets/{id}")]
        public IActionResult DeleteAsset(int id)
        {
            var asset = _context.RoadworkAssets.Find(id);
            if (asset == null) return NotFound();
            _context.RoadworkAssets.Remove(asset);
            _context.SaveChanges();
            return Ok();
        }

        [HttpGet("route")]
        public async Task<IActionResult> GetRoute(
            [FromQuery] double fromLon, [FromQuery] double fromLat,
            [FromQuery] double toLon, [FromQuery] double toLat)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            const string nearestNodeSql = @"
                SELECT id FROM road_network_noded_vertices
                ORDER BY the_geom <-> ST_Transform(
                    ST_SetSRID(ST_MakePoint(@lon, @lat), 4326), 3857)
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

            // Retrieves blocked (active) and slowed (planned) segment IDs
            const string blockedSql = @"
                SELECT ARRAY(
                    SELECT DISTINCT r.id FROM road_network_noded r
                    JOIN roadwork_sites rs ON rs.status = 'active'
                    WHERE ST_Intersects(r.geom, ST_Transform(rs.geometry, 3857))
                        OR ST_Within(ST_StartPoint(r.geom), ST_Transform(rs.geometry, 3857))
                        OR ST_Within(ST_EndPoint(r.geom),   ST_Transform(rs.geometry, 3857))
                )";
            await using var cmdBlocked = new NpgsqlCommand(blockedSql, conn);
            var blockedArray = (long[])(await cmdBlocked.ExecuteScalarAsync() ?? Array.Empty<long>());

            const string slowedSql = @"
                SELECT ARRAY(
                    SELECT DISTINCT r.id FROM road_network_noded r
                    JOIN roadwork_sites rs ON rs.status = 'planned'
                    WHERE ST_Intersects(r.geom, ST_Transform(rs.geometry, 3857))
                        OR ST_Within(ST_StartPoint(r.geom), ST_Transform(rs.geometry, 3857))
                        OR ST_Within(ST_EndPoint(r.geom),   ST_Transform(rs.geometry, 3857))
                )";
            await using var cmdSlowed = new NpgsqlCommand(slowedSql, conn);
            var slowedArray = (long[])(await cmdSlowed.ExecuteScalarAsync() ?? Array.Empty<long>());

            // Blocked segments: excluded from the network with WHERE NOT IN
            // Slowed segments: cost x5 but passable
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
            catch (PostgresException ex) when (ex.SqlState == "XX000")
            {
                // pgr_dijkstra throws exception if no route is available
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
                FROM road_network_noded_vertices v
                ORDER BY v.the_geom <-> ST_Transform(
                    ST_Centroid((SELECT cs.geometry FROM roadwork_sites cs WHERE cs.id = @siteId)),
                    3857
                )
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

            // Nearest Neighbor: optimizes visiting order
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
                        SELECT MAX(agg_cost) FROM pgr_dijkstra(
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
                    catch
                    {
                        //not reachable node, skip
                    }
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
                    {
                        return Ok(new { segments = Array.Empty<object>(), orderedSiteIds = siteIds });
                    }

                    segments.AddRange(legSegments);
                }
                catch (PostgresException ex) when (ex.SqlState == "XX000")
                {
                    return Ok(new { segments = Array.Empty<object>(), orderedSiteIds = siteIds });
                }
            }

            return Ok(new { segments, orderedSiteIds = siteIds });
        }

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
                    ? (NetTopologySuite.Geometries.Polygon)reader.Read(dto.Geometry)
                    : null
            };
            _context.RoadworkSites.Add(site);
            _context.SaveChanges();
            return Ok(new { site.Id, site.Name, site.Status });
        }

        // updates construction site status
        [HttpPut("sites/{id}")]
        public IActionResult UpdateSite(int id, [FromBody] RoadworkSiteDto dto)
        {
            var site = _context.RoadworkSites.Find(id);
            if (site == null) return NotFound();

            site.Name = dto.Name;
            site.Status = dto.Status;
            site.StartDate = dto.StartDate.HasValue
                ? DateTime.SpecifyKind(dto.StartDate.Value, DateTimeKind.Utc) : null;
            site.EndDate = dto.EndDate.HasValue
                ? DateTime.SpecifyKind(dto.EndDate.Value, DateTimeKind.Utc) : null;

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

        // deletes construction site
        [HttpDelete("sites/{id}")]
        public IActionResult DeleteSite(int id)
        {
            var site = _context.RoadworkSites.Find(id);
            if (site == null) return NotFound();
            _context.RoadworkSites.Remove(site);
            _context.SaveChanges();
            return Ok();
        }

    }
}
