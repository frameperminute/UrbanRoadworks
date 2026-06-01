// Activates polygon draw interaction; on completion opens the site edit panel
function startDrawSite() {
    resetButtons();
    const btn = document.getElementById('btn-add');
    btn.textContent = '🖊 Draw the perimeter...';
    btn.style.background = '#e8af34';

    const drawSource = new ol.source.Vector();
    drawSite = new ol.interaction.Draw({ source: drawSource, type: 'Polygon' });

    drawSite.on('drawend', function (evt) {
        const wkt = new ol.format.WKT().writeFeature(evt.feature, {
            dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857'
        });
        document.getElementById('edit-geometry').value = wkt;
        map.removeInteraction(drawSite);
        drawSite = null;
        btn.textContent = '+ New construction site';
        btn.style.background = '#4f98a3';

        document.getElementById('panel-title').textContent = 'New construction site';
        document.getElementById('edit-id').value = '';
        document.getElementById('edit-name').value = '';
        document.getElementById('edit-status').value = 'active';
        document.getElementById('edit-start').value = '';
        document.getElementById('edit-end').value = '';
        document.getElementById('btn-delete').style.display = 'none';
        document.getElementById('edit-panel').style.display = 'block';
        document.getElementById('overlay').style.display = 'block';
    });
    map.addInteraction(drawSite);
}

// Activates point draw interaction; on completion opens the asset edit panel
function startDrawAsset() {
    resetButtons();
    map.getTargetElement().style.cursor = 'crosshair';
    const btn = document.querySelector('[onclick="startDrawAsset()"]');
    btn.textContent = '📍 Click on the map...';
    btn.style.background = '#4f98a3';

    const drawSource = new ol.source.Vector();
    drawAsset = new ol.interaction.Draw({ source: drawSource, type: 'Point' });

    drawAsset.on('drawend', function (evt) {
        const wkt = new ol.format.WKT().writeFeature(evt.feature, {
            dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857'
        });
        document.getElementById('asset-geometry').value = wkt;
        document.getElementById('asset-id').value = '';
        document.getElementById('asset-type').value = 'temporary_traffic_light';
        document.getElementById('asset-btn-delete').style.display = 'none';
        document.getElementById('asset-panel-title').textContent = 'New asset';

        populateSiteDropdown();
        document.getElementById('asset-panel').style.display = 'block';
        document.getElementById('overlay').style.display = 'block';

        map.removeInteraction(drawAsset);
        drawAsset = null;
        map.getTargetElement().style.cursor = '';
        btn.textContent = '+ New asset';
        btn.style.background = '#e8af34';
    });
    map.addInteraction(drawAsset);
}

// Snaps the start/end of a new canal to the nearest existing canal endpoint
// within SNAP_TOLERANCE_M meters (EPSG:3857 units)
function snapCanalEndpoints(coords) {
    const SNAP_TOLERANCE_M = 1.0;
    let start = [...coords[0]];
    let end = [...coords[coords.length - 1]];
    let bestStartDist = SNAP_TOLERANCE_M;
    let bestEndDist = SNAP_TOLERANCE_M;

    canalsSource.getFeatures().forEach(function (feat) {
        const pts = feat.getGeometry().getCoordinates();
        [pts[0], pts[pts.length - 1]].forEach(function (pt) {
            const dStart = Math.hypot(pt[0] - start[0], pt[1] - start[1]);
            const dEnd = Math.hypot(pt[0] - end[0], pt[1] - end[1]);
            if (dStart < bestStartDist) { bestStartDist = dStart; start = [...pt]; }
            if (dEnd < bestEndDist) { bestEndDist = dEnd; end = [...pt]; }
        });
    });
    const snapped = [...coords];
    snapped[0] = start;
    snapped[snapped.length - 1] = end;
    return snapped;
}

// Activates LineString draw interaction for canals; snaps endpoints on completion
function startDrawCanal() {
    resetButtons();
    const btn = document.querySelector('[onclick="startDrawCanal()"]');
    btn.textContent = '🖊 Draw the canal...';
    btn.style.background = '#0066cc';

    const drawSource = new ol.source.Vector();
    drawCanal = new ol.interaction.Draw({ source: drawSource, type: 'LineString' });

    drawCanal.on('drawend', function (evt) {
        const geom = evt.feature.getGeometry();
        const snappedCoords = snapCanalEndpoints(geom.getCoordinates());
        geom.setCoordinates(snappedCoords);
        const wkt = new ol.format.WKT().writeFeature(evt.feature, {
            dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857'
        });
        document.getElementById('canal-geometry').value = wkt;
        document.getElementById('canal-id').value = '';
        document.getElementById('canal-name').value = '';
        document.getElementById('canal-status').value = 'planned';
        document.getElementById('canal-btn-delete').style.display = 'none';
        document.getElementById('canal-panel-title').textContent = 'New canal';
        document.getElementById('canal-panel').style.display = 'block';
        document.getElementById('overlay').style.display = 'block';

        map.removeInteraction(drawCanal);
        drawCanal = null;
        btn.textContent = '+ New canal';
        btn.style.background = '#0d3d6b';
    });
    map.addInteraction(drawCanal);
}

// Activates LineString draw interaction for walls
function startDrawWall() {
    resetButtons();
    const btn = document.querySelector('[onclick="startDrawWall()"]');
    btn.textContent = '🖊 Draw the wall...';
    btn.style.background = '#78909c';

    const drawSource = new ol.source.Vector();
    drawWall = new ol.interaction.Draw({ source: drawSource, type: 'LineString' });

    drawWall.on('drawend', function (evt) {
        const wkt = new ol.format.WKT().writeFeature(evt.feature, {
            dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857'
        });
        document.getElementById('wall-geometry').value = wkt;
        document.getElementById('wall-id').value = '';
        document.getElementById('wall-name').value = '';
        document.getElementById('wall-thickness').value = '20';
        document.getElementById('wall-material').value = 'concrete';
        document.getElementById('wall-btn-delete').style.display = 'none';
        document.getElementById('wall-panel-title').textContent = 'New wall';
        document.getElementById('wall-panel').style.display = 'block';
        document.getElementById('overlay').style.display = 'block';
        map.removeInteraction(drawWall);
        drawWall = null;
        btn.textContent = '+ New wall';
        btn.style.background = '#546e7a';
    });
    map.addInteraction(drawWall);
}

let routeFrom = null, routeTo = null, pickMode = null, routeLayer = null;

// Enters point-pick mode for route A -> B; type is 'from' or 'to'
function startPickPoint(type) {
    resetButtons();
    if (type === 'from') {
        routeMarkersSource.clear();
        if (routeLayer) { map.removeLayer(routeLayer); routeLayer = null; }
        document.getElementById('route-info').style.display = 'none';
        document.getElementById('btn-clear-route').style.display = 'none';
        document.getElementById('btn-route').style.display = 'none';
        routeFrom = null; routeTo = null;
        document.getElementById('btn-pick-to').disabled = true;
    }
    if (type === 'to') {
        routeMarkersSource.getFeatures()
            .filter(f => f.get('pointType') === 'to')
            .forEach(f => routeMarkersSource.removeFeature(f));
        routeTo = null;
    }
    pickMode = type;
    const btnFrom = document.getElementById('btn-pick-from');
    const btnTo = document.getElementById('btn-pick-to');
    btnFrom.style.background = type === 'from' ? '#4f98a3' : '#2a3a5e';
    btnTo.style.background = type === 'to' ? '#ff6b6b' : '#2a3a5e';
    map.getTargetElement().style.cursor = 'crosshair';
}

// Clears the A -> B route layer, markers, and UI state
function clearRoute() {
    pickMode = null; routeFrom = null; routeTo = null;
    routeMarkersSource.clear();
    if (routeLayer) { map.removeLayer(routeLayer); routeLayer = null; }
    document.getElementById('btn-route').style.display = 'none';
    document.getElementById('btn-clear-route').style.display = 'none';
    document.getElementById('route-info').style.display = 'none';
    document.getElementById('btn-pick-from').style.background = '#2a3a5e';
    document.getElementById('btn-pick-to').style.background = '#2a3a5e';
    document.getElementById('btn-pick-to').disabled = true;
    map.getTargetElement().style.cursor = '';
}

// Calls the route API and renders the result as a green polyline on the map
async function calculateRouteAB() {
    if (!routeFrom || !routeTo) return;
    document.getElementById('btn-route').textContent = '⏳ Computing...';
    try {
        const segments = await fetchRouteAB(routeFrom[0], routeFrom[1], routeTo[0], routeTo[1]);
        if (!segments.length) {
            document.getElementById('route-info').style.display = 'block';
            document.getElementById('route-info').textContent = '⚠️ No route found';
            return;
        }
        if (routeLayer) map.removeLayer(routeLayer);
        const fmt = new ol.format.WKT();
        const features = segments
            .filter(s => s.geometry)
            .map(s => fmt.readFeature(s.geometry, { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' }));
        routeLayer = new ol.layer.Vector({
            source: new ol.source.Vector({ features }),
            style: new ol.style.Style({ stroke: new ol.style.Stroke({ color: '#00CC44', width: 5 }) }),
            zIndex: 9
        });
        map.addLayer(routeLayer);
        const totalMeters = features.reduce((sum, f) => sum + ol.sphere.getLength(f.getGeometry()), 0);
        document.getElementById('route-info').style.display = 'block';
        document.getElementById('route-info').textContent = `✅ Length: ${(totalMeters / 1000).toFixed(2)} kms`;
        document.getElementById('btn-clear-route').style.display = 'block';
    } finally {
        document.getElementById('btn-route').textContent = '🗺 Compute route';
    }
}

const legColors = ['#a86fdf', '#ff6b6b', '#fdab43', '#4f98a3', '#5591c7', '#6daa45'];
let inspectorLayer = null;

// Calls the inspector tour API and renders each leg in a different color (dashed)
async function calculateInspectorTour() {
    const startId = parseInt(document.getElementById('inspector-start').value);
    if (!startId) { alert('Select the construction site to start'); return; }
    const checked = [...document.querySelectorAll('#inspector-sites-list input:checked')]
        .map(cb => parseInt(cb.value)).filter(id => id !== startId);
    if (checked.length === 0) { alert('Select at least a site to visit'); return; }
    const allIds = [startId, ...checked];
    document.querySelector('[onclick="calculateInspectorTour()"]').textContent = '⏳ Computing...';

    try {
        const data = await fetchInspectorTour(allIds);
        const segments = data.segments ?? data;
        const orderedSiteIds = data.orderedSiteIds ?? allIds;

        if (!segments.length) {
            document.getElementById('tour-info').style.display = 'block';
            document.getElementById('tour-info').textContent = '⚠️ Tour not found';
            return;
        }
        if (inspectorLayer) map.removeLayer(inspectorLayer);
        const fmt = new ol.format.WKT();
        const features = segments.filter(s => s.geometry).map(s => {
            const f = fmt.readFeature(s.geometry, { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' });
            f.set('leg', s.leg);
            return f;
        });
        const totalMeters = features.reduce((sum, f) => sum + ol.sphere.getLength(f.getGeometry()), 0);
        inspectorLayer = new ol.layer.Vector({
            source: new ol.source.Vector({ features }),
            style: function (feature) {
                const leg = feature.get('leg') || 0;
                return new ol.style.Style({
                    stroke: new ol.style.Stroke({ color: legColors[leg % legColors.length], width: 5, lineDash: [10, 5] })
                });
            },
            zIndex: 9
        });
        map.addLayer(inspectorLayer);

        const info = document.getElementById('tour-info');
        info.style.display = 'block';
        const orderedNames = orderedSiteIds.map((id, idx) => {
            const site = allSites.find(s => s.id === id);
            const name = site ? site.name : `#${id}`;
            return `<div style="display:flex;gap:6px;align-items:center;">
                        <span style="background:#a86fdf;color:#fff;border-radius:50%;width:18px;height:18px;display:flex;align-items:center;justify-content:center;font-size:0.7rem;flex-shrink:0;">${idx + 1}</span>
                        <span>${name}</span>
                    </div>`;
        }).join('');
        info.innerHTML = `<div style="margin-bottom:6px;">✅ Tour: ${orderedSiteIds.length} sites — ${(totalMeters / 1000).toFixed(2)} kms</div>
                          <div style="display:flex;flex-direction:column;gap:4px;">${orderedNames}</div>`;
        document.getElementById('btn-clear-tour').style.display = 'block';
    } finally {
        document.querySelector('[onclick="calculateInspectorTour()"]').textContent = '🔍 Compute optimal tour';
    }
}

// Clears the inspector tour layer and resets the UI
function clearInspectorTour() {
    if (inspectorLayer) { map.removeLayer(inspectorLayer); inspectorLayer = null; }
    document.getElementById('tour-info').style.display = 'none';
    document.getElementById('btn-clear-tour').style.display = 'none';
}

const selectedCanalIds = new Set();

// Updates the canal selection highlight layer and the selected-count/meters UI
function refreshCanalSelectionHighlight() {
    cablePlanHighlightSource.clear();
    let totalM = 0;
    canalsSource.getFeatures().forEach(f => {
        if (selectedCanalIds.has(f.get('id'))) {
            cablePlanHighlightSource.addFeature(f.clone());
            totalM += ol.sphere.getLength(f.getGeometry());
        }
    });
    const info = document.getElementById('cable-selected-info');
    if (selectedCanalIds.size > 0) {
        info.style.display = 'block';
        document.getElementById('cable-sel-count').textContent = selectedCanalIds.size;
        document.getElementById('cable-sel-meters').textContent = Math.round(totalM);
    } else {
        info.style.display = 'none';
    }
}

// Calls the cable calculator API with selected canal IDs and renders the plan
async function calculateCablePlan() {
    if (selectedCanalIds.size === 0) { alert('Select at least one canal by clicking on the map.'); return; }
    const ids = Array.from(selectedCanalIds).join(',');
    try {
        const res = await fetch(`/api/cablecalculator/calculate?canalIds=${ids}`);
        if (!res.ok) throw new Error(await res.text());
        const plan = await res.json();
        renderCablePlan(plan);
        selectedCanalIds.clear();
        cablePlanHighlightSource.clear();
        document.getElementById('cable-selected-info').style.display = 'none';
        deactivateMode('canalSelect');
    } catch (e) { alert('Error: ' + e.message); }
}

// Renders the cable plan result: summary, canal segments, wall drill list,
// and node markers on the map. Wall thickness/time are multiplied by crossingCount.
function renderCablePlan(plan) {
    document.getElementById('cr-meters').textContent = plan.totalCableMeters.toFixed(1) + ' m';
    document.getElementById('cr-segments').textContent = plan.utpSegmentsCount;
    document.getElementById('cr-nodes').textContent = plan.nodesNeeded;
    document.getElementById('cr-time').textContent = formatCableTime(plan.totalWorkTimeMin);

    const rd = document.getElementById('cr-route-detail');
    rd.innerHTML = '';
    plan.route.forEach(seg => {
        const div = document.createElement('div');
        div.style.cssText = 'background:#0d1b2a;border:1px solid #2a3a5e;border-radius:5px;padding:5px 8px;font-size:0.75rem;color:#ccc';
        const nodes = plan.nodePoints.filter(n => n.canalId === seg.canalId).length;
        const canalName = allCanals.find(c => c.id === seg.canalId)?.name || `Canal ${seg.canalId}`;
        div.innerHTML = `<span style="color:#4f98a3;font-weight:600">Canal ${canalName}</span> ${seg.lengthM.toFixed(1)} m ${nodes > 0 ? `<span style="color:#e8af34">${nodes} node${nodes > 1 ? 's' : ''}</span>` : ''}`;
        rd.appendChild(div);
    });

    const wd = document.getElementById('cr-walls');
    wd.innerHTML = '';
    const planWalls = plan.route.flatMap(s => s.walls);
    if (planWalls.length === 0) {
        wd.innerHTML = '<div style="font-size:0.75rem;color:#888">No walls intersected.</div>';
    } else {
        planWalls.forEach(w => {
            const crossings = w.crossingCount ?? 1;
            const div = document.createElement('div');
            div.style.cssText = 'background:#0d1b2a;border:1px solid #2a3a5e;border-radius:5px;padding:5px 8px;font-size:0.75rem;color:#ccc';
            const wallName = allWalls.find(ww => ww.id === w.wallId)?.name || w.wallId;
            div.innerHTML = `Wall ${wallName} <span style="background:#546e7a;color:#fff;border-radius:3px;padding:1px 5px;font-size:0.7rem">${w.material}</span> ${w.thicknessCm * crossings} cm <span style="color:#fdab43">${w.drillingTimeMin} min</span>`;
            wd.appendChild(div);
        });
    }

    document.getElementById('cable-results').style.display = 'flex';
    cableNodesSource.clear();
    plan.nodePoints.forEach(node => {
        const feature = new ol.Feature({
            geometry: new ol.geom.Point(ol.proj.fromLonLat([node.lon, node.lat])),
            nodeIndex: node.nodeIndex,
            canalId: node.canalId
        });
        cableNodesSource.addFeature(feature);
    });
}

// Clears cable plan results, highlights, and node markers
function clearCablePlan() {
    selectedCanalIds.clear();
    cablePlanHighlightSource.clear();
    cableNodesSource.clear();
    document.getElementById('cable-selected-info').style.display = 'none';
    document.getElementById('cable-results').style.display = 'none';
}

// Formats minutes into "Xh Ymin" or "Y min" string for display
function formatCableTime(mins) {
    if (!mins) return '0 min';
    if (mins < 60) return mins + ' min';
    return Math.floor(mins / 60) + 'h ' + (mins % 60) + 'min';
}