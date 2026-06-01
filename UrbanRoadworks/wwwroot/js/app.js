const popup = document.getElementById('popup');
map.on('click', async function (evt) {
    if (modes.canalSelect && modes.canalSelect.active) {
        const feature = map.forEachFeatureAtPixel(evt.pixel, f => f, { layerFilter: l => l === canalsLayer });
        if (feature && feature.get('id')) {
            const id = feature.get('id');
            if (selectedCanalIds.has(id)) selectedCanalIds.delete(id);
            else selectedCanalIds.add(id);
            refreshCanalSelectionHighlight();
        }
        return;
    }

    if (pickMode) {
        const coord = ol.proj.toLonLat(evt.coordinate);
        routeMarkersSource.getFeatures().filter(f => f.get('pointType') === pickMode).forEach(f => routeMarkersSource.removeFeature(f));
        const marker = new ol.Feature({ geometry: new ol.geom.Point(evt.coordinate) });
        marker.set('pointType', pickMode);
        routeMarkersSource.addFeature(marker);
        if (pickMode === 'from') {
            routeFrom = coord;
            document.getElementById('btn-pick-from').style.background = '#2a3a5e';
            document.getElementById('btn-pick-to').disabled = false;
        } else {
            routeTo = coord;
            document.getElementById('btn-pick-to').style.background = '#2a3a5e';
        }
        pickMode = null;
        map.getTargetElement().style.cursor = '';
        if (routeFrom && routeTo) document.getElementById('btn-route').style.display = 'block';
        return;
    }

    if (modes.queryNearest.active) {
        const coord = ol.proj.toLonLat(evt.coordinate);
        const nRaw = parseInt(document.getElementById('nearest-n').value) || 3;
        const n = Math.max(1, Math.min(nRaw, 50));
        deactivateMode('queryNearest');
        try {
            const r = await fetch(`/api/site/nearest-by-road?lon=${coord[0]}&lat=${coord[1]}&n=${n}`);
            const sites = await r.json();
            queryHighlightSource.clear();
            const fmt = new ol.format.WKT();
            const clickMarker = new ol.Feature({ geometry: new ol.geom.Point(evt.coordinate) });
            clickMarker.set('clickMarker', true);
            queryHighlightSource.addFeature(clickMarker);

            const actual = sites.length;
            sites.forEach((s, i) => {
                const geoKey = Object.keys(s).find(k => k.toLowerCase() === 'geometry' || k.toLowerCase() === 'wkt');
                const geoValue = geoKey ? s[geoKey] : null;
                if (!geoValue) return;
                const f = fmt.readFeature(geoValue, { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' });
                const { geometry, Geometry, ...propsWithoutGeom } = s;
                f.setProperties(propsWithoutGeom);
                queryHighlightSource.addFeature(f);

                const centroid = ol.extent.getCenter(f.getGeometry().getExtent());
                const line = new ol.Feature({ geometry: new ol.geom.LineString([evt.coordinate, centroid]) });
                line.set('connectLine', true);
                line.set('rank', i + 1);
                queryHighlightSource.addFeature(line);
            });

            const info = document.getElementById('query-info');
            info.style.display = 'block';
            const heading = actual < n ? `<b>${actual} nearest sites</b> (max available)<br>` : `<b>${actual} nearest sites</b><br>`;
            info.innerHTML = heading + sites.map((s, i) => {
                const dist = (s.roadDistanceMeters / 1000).toFixed(2);
                return `<div><i>${i + 1}.</i> <b>${s.name || s.id}</b> — ${dist} km road</div>`;
            }).join('');
            document.getElementById('btn-clear-query').style.display = 'block';
        } catch (e) { console.error('Nearest sites error:', e); }
        return;
    }

    const feature = map.forEachFeatureAtPixel(evt.pixel, f => f);
    if (feature) {
        const props = feature.getProperties();
        let html = '';
        if (modes.editSite.active && props.id && props.status && !props.assetType) { openEditPanel(props); return; }
        if (modes.editAsset.active && props.id && props.assetType) { openAssetEditPanel(props); return; }
        if (modes.editCanal.active && props.id && props.status && !props.assetType && !props.siteStatus) { openCanalEditPanel(props); return; }
        if (modes.editWall.active && props.id && props.thickness !== undefined) { openWallEditPanel(props); return; }

        if (props.thickness !== undefined) {
            const labels = { concrete: '🔘 Concrete', brick: '🧱 Brick', drywall: '📋 Drywall', stone: '🪨 Stone' };
            const matLabel = labels[props.material] || props.material;
            const title = props.name || 'Unnamed Wall';
            html = `<h4>${title}</h4><p style="margin-top:6px; color:#ccc;">Material: <b style="color:#fff;">${matLabel}</b></p><p style="color:#ccc;">Thickness: <b style="color:#fff;">${props.thickness} cm</b></p>`;
        } else if (props.name) {
            const badgeClass = 'badge-' + (props.status || 'planned');
            html = `<h4>${props.name}</h4><span class="badge ${badgeClass}">${props.status || ''}</span>
                    ${props.startDate ? `<p style="margin-top:6px">Start: ${props.startDate}</p>` : ''}
                    ${props.endDate ? `<p>End: ${props.endDate}</p>` : ''}`;
        } else if (props.assetType) {
            const labels = { temporary_traffic_light: '🚦 Temporary traffic light', warning_sign: '⚠️ Warning sign', site_entrance: '🚧 Site entrance', detour_sign: '↪️ Detour sign' };
            html = `<h4>${labels[props.assetType] || props.assetType}</h4>`;
        }
        if (html) {
            popup.innerHTML = `<button class="popup-close" onclick="document.getElementById('popup').style.display='none'">✕</button>` + html;
            popup.style.display = 'block';
            popup.style.left = (evt.pixel[0] + 12) + 'px';
            popup.style.top = (evt.pixel[1] - 10) + 'px';
        }
        return;
    }
    popup.style.display = 'none';
});

map.on('pointermove', function (evt) {
    const anyActive = Object.values(modes).some(m => m.active);
    if (pickMode || anyActive) return;
    map.getTargetElement().style.cursor = map.hasFeatureAtPixel(evt.pixel) ? 'pointer' : '';
});

function openAssetEditPanel(props) {
    document.getElementById('asset-panel-title').textContent = 'Edit asset';
    document.getElementById('asset-id').value = props.id || '';
    document.getElementById('asset-type').value = props.assetType || 'warning_sign';
    document.getElementById('asset-geometry').value = '';
    document.getElementById('asset-btn-delete').style.display = 'block';
    populateSiteDropdown(props.siteId);
    document.getElementById('asset-panel').style.display = 'block';
    document.getElementById('overlay').style.display = 'block';
}

function openCanalEditPanel(props) {
    document.getElementById('canal-panel-title').textContent = 'Edit canal';
    document.getElementById('canal-id').value = props.id || '';
    document.getElementById('canal-name').value = props.name || '';
    document.getElementById('canal-status').value = props.status || 'planned';
    document.getElementById('canal-geometry').value = '';
    document.getElementById('canal-btn-delete').style.display = 'block';
    document.getElementById('canal-panel').style.display = 'block';
    document.getElementById('overlay').style.display = 'block';
}

function openWallEditPanel(props) {
    document.getElementById('wall-panel-title').textContent = 'Edit wall';
    document.getElementById('wall-id').value = props.id || '';
    document.getElementById('wall-name').value = props.name || '';
    document.getElementById('wall-thickness').value = props.thickness || 20;
    document.getElementById('wall-material').value = props.material || 'concrete';
    document.getElementById('wall-geometry').value = '';
    document.getElementById('wall-btn-delete').style.display = 'block';
    document.getElementById('wall-panel').style.display = 'block';
    document.getElementById('overlay').style.display = 'block';
}

function openEditPanel(props) {
    document.getElementById('panel-title').textContent = 'Edit site';
    document.getElementById('edit-id').value = props.id || '';
    document.getElementById('edit-name').value = props.name || '';
    document.getElementById('edit-status').value = props.status || 'active';
    document.getElementById('edit-start').value = props.startDate ? props.startDate.substring(0, 10) : '';
    document.getElementById('edit-end').value = props.endDate ? props.endDate.substring(0, 10) : '';
    document.getElementById('edit-geometry').value = '';
    document.getElementById('btn-delete').style.display = 'block';
    document.getElementById('edit-panel').style.display = 'block';
    document.getElementById('overlay').style.display = 'block';
}

function populateSiteDropdown(selectedId = null) {
    const select = document.getElementById('asset-site-id');
    select.innerHTML = '<option value="">— none —</option>';
    const availableSites = allSites.filter(s => s.status === 'active' || s.status === 'planned');
    availableSites.forEach(s => {
        const opt = document.createElement('option');
        opt.value = s.id;
        const statusLabel = { active: 'active', planned: 'planned' };
        opt.textContent = `${s.name || 'No name'} (${statusLabel[s.status]})`;
        if (selectedId && s.id === parseInt(selectedId)) opt.selected = true;
        select.appendChild(opt);
    });
}

function populateInspectorPanel() {
    const startSelect = document.getElementById('inspector-start');
    const listDiv = document.getElementById('inspector-sites-list');
    if (!startSelect || !listDiv) return;
    const activeSites = allSites.filter(s => s.status === 'active' || s.status === 'planned');
    startSelect.innerHTML = '<option value="">— select —</option>';
    activeSites.forEach(s => {
        const opt = document.createElement('option');
        opt.value = s.id;
        opt.textContent = s.name || 'No name';
        startSelect.appendChild(opt);
    });
    listDiv.innerHTML = '';
    activeSites.forEach(s => {
        const label = document.createElement('label');
        label.style.cssText = 'display:flex;align-items:center;gap:8px;font-size:0.78rem;cursor:pointer;padding:3px 0;';
        label.innerHTML = `<input type="checkbox" value="${s.id}" checked style="accent-color:#a86fdf;width:13px;height:13px;" /><span style="color:#ccc;">${s.name || 'No name'}</span>`;
        listDiv.appendChild(label);
    });
}

function isChecked(id) { return document.getElementById(id)?.checked ?? true; }

function applyAreaFilter(extent) {
    queryHighlightSource.clear();
    let nSites = 0, nAssets = 0, nCanals = 0;
    sitesSource.getFeaturesInExtent(extent).forEach(f => { queryHighlightSource.addFeature(f.clone()); nSites++; });
    assetsSource.getFeaturesInExtent(extent).forEach(f => { queryHighlightSource.addFeature(f.clone()); nAssets++; });
    canalsSource.getFeatures().forEach(f => {
        if (f.getGeometry().intersectsExtent(extent)) { queryHighlightSource.addFeature(f.clone()); nCanals++; }
    });
    const info = document.getElementById('query-info');
    info.style.display = 'block';
    info.innerHTML = `<b>Selection:</b><br>Sites: <b>${nSites}</b> &nbsp;|&nbsp;Assets: <b>${nAssets}</b> &nbsp;|&nbsp;Canals: <b>${nCanals}</b>`;
    document.getElementById('btn-clear-query').style.display = 'block';
    deactivateMode('queryArea');
}

function clearQueryArea() {
    queryHighlightSource.clear();
    document.getElementById('query-info').style.display = 'none';
    document.getElementById('btn-clear-query').style.display = 'none';
}

function applyFilters() {
    const activeSites = allSites.filter(s => {
        if (s.status === 'active' && !isChecked('filter-sites-active')) return false;
        if (s.status === 'planned' && !isChecked('filter-sites-planned')) return false;
        if (s.status === 'completed' && !isChecked('filter-sites-completed')) return false;
        return true;
    });
    const activeRoads = allAffectedRoads.filter(r => {
        if (r.siteStatus === 'active' && !isChecked('filter-roads-closed')) return false;
        if (r.siteStatus === 'planned' && !isChecked('filter-roads-reduced')) return false;
        return true;
    });
    const activeAssets = allAssets.filter(a => isChecked('filter-asset-' + a.assetType));
    const activeCanals = allCanals.filter(c => {
        if (c.status === 'planned' && !isChecked('filter-canals-planned')) return false; 
        if (c.status === 'active' && !isChecked('filter-canals-active')) return false;
        if (c.status === 'completed' && !isChecked('filter-canals-completed')) return false;
        return true;
    });
    const activeWalls = allWalls.filter(() => isChecked('filter-walls-visible'));

    sitesSource.clear(); assetsSource.clear(); affectedRoadsSource.clear(); canalsSource.clear(); wallsSource.clear();
    sitesSource.addFeatures(parseFeatures(activeSites));
    assetsSource.addFeatures(parseFeatures(activeAssets));
    affectedRoadsSource.addFeatures(parseFeatures(activeRoads));
    canalsSource.addFeatures(parseFeatures(activeCanals));
    wallsSource.addFeatures(parseFeatures(activeWalls));

    document.getElementById('cnt-sites').textContent = activeSites.length;
    document.getElementById('cnt-roads').textContent = activeRoads.length;
    document.getElementById('cnt-assets').textContent = activeAssets.length;
    document.getElementById('cnt-canals').textContent = activeCanals.length;
}

const modes = {
    canalSelect: { active: false, btnId: 'btn-canal-select', onText: '🖱️ Selecting… click to stop', offText: '🖱️ Select canals', onStyle: { background: '#1a80e5', color: '#fff', border: '1px solid #1a80e5' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #1a80e5' }, cursor: 'crosshair', onActivate: null, onDeactivate: null },
    editWall: { active: false, btnId: 'btn-edit-wall', onText: '✏️ Wall ON — click a wall', offText: '✏️ Edit wall', onStyle: { background: '#78909c', color: '#fff', border: '1px solid #78909c' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #78909c' }, cursor: 'crosshair', onActivate: null, onDeactivate: null },
    modifyWall: {
        active: false, btnId: 'btn-modify-wall', onText: '↕️ Wall ON — drag a vertex', offText: '↕️ Reshape wall', onStyle: { background: '#78909c', color: '#fff', border: '1px solid #78909c' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #78909c' }, cursor: 'crosshair',
        onActivate: function () {
            modifyWallInteraction = new ol.interaction.Modify({ source: wallsSource });
            modifyWallInteraction.on('modifyend', async function (evt) {
                for (const feature of evt.features.getArray()) {
                    const id = feature.get('id'); if (!id) continue;
                    const wkt = new ol.format.WKT().writeGeometry(feature.getGeometry(), { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' });
                    await fetch(`/api/wall/walls/${id}`, {
                        method: 'PUT', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ name: feature.get('name'), thickness: feature.get('thickness') ?? 20, material: feature.get('material') ?? 'concrete', geometry: wkt })
                    });
                }
                await loadAllData();
            });
            map.addInteraction(modifyWallInteraction);
        },
        onDeactivate: function () { if (modifyWallInteraction) { map.removeInteraction(modifyWallInteraction); modifyWallInteraction = null; } }
    },
    modifySite: {
        active: false, btnId: 'btn-modify-site', onText: '↕️ Site ON — drag a vertex', offText: '↕️ Reshape site', onStyle: { background: '#fdab43', color: '#1a1a2e', border: '1px solid #fdab43' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #fdab43' }, cursor: 'crosshair',
        onActivate: function () {
            modifySiteInteraction = new ol.interaction.Modify({ source: sitesSource });
            modifySiteInteraction.on('modifyend', async function (evt) {
                for (const feature of evt.features.getArray()) {
                    const id = feature.get('id'); if (!id) continue;
                    const wkt = new ol.format.WKT().writeGeometry(feature.getGeometry(), { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' });
                    await fetch(`/api/site/sites/${id}`, {
                        method: 'PUT', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ name: feature.get('name'), status: feature.get('status'), startDate: feature.get('startDate') ?? null, endDate: feature.get('endDate') ?? null, geometry: wkt })
                    });
                }
                await loadAllData();
            });
            map.addInteraction(modifySiteInteraction);
        },
        onDeactivate: function () { if (modifySiteInteraction) { map.removeInteraction(modifySiteInteraction); modifySiteInteraction = null; } }
    },
    modifyCanal: {
        active: false, btnId: 'btn-modify-canal', onText: '↕️ Canal ON — drag a vertex', offText: '↕️ Reshape canal', onStyle: { background: '#0066cc', color: '#fff', border: '1px solid #0066cc' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #0066cc' }, cursor: 'crosshair',
        onActivate: function () {
            modifyCanalInteraction = new ol.interaction.Modify({ source: canalsSource });
            modifyCanalInteraction.on('modifyend', async function (evt) {
                for (const feature of evt.features.getArray()) {
                    const id = feature.get('id'); if (!id) continue;
                    const wkt = new ol.format.WKT().writeGeometry(feature.getGeometry(), { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' });
                    await fetch(`/api/canal/canals/${id}`, {
                        method: 'PUT', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ name: feature.get('name'), status: feature.get('status'), geometry: wkt })
                    });
                }
                await loadAllData();
            });
            map.addInteraction(modifyCanalInteraction);
        },
        onDeactivate: function () { if (modifyCanalInteraction) { map.removeInteraction(modifyCanalInteraction); modifyCanalInteraction = null; } }
    },
    editSite: { active: false, btnId: 'btn-edit', onText: '✏️ Edit ON — click on a site', offText: '✏️ Edit state', onStyle: { background: '#4f98a3', color: '#fff', border: '1px solid #4f98a3' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #4f98a3' }, cursor: 'crosshair', onActivate: null, onDeactivate: null },
    editAsset: { active: false, btnId: 'btn-edit-asset', onText: '✏️ Asset ON — click on a point', offText: '✏️ Edit asset', onStyle: { background: '#e8af34', color: '#1a1a2e', border: '1px solid #e8af34' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #e8af34' }, cursor: 'crosshair', onActivate: null, onDeactivate: null },
    moveAsset: {
        active: false, btnId: 'btn-move-asset', onText: '↕️ Move ON — drag an asset', offText: '↕️ Move asset', onStyle: { background: '#4f98a3', color: '#fff', border: '1px solid #4f98a3' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #4f98a3' }, cursor: 'grab',
        onActivate: function () {
            translateInteraction = new ol.interaction.Translate({ layers: [assetsLayer] });
            translateInteraction.on('translateend', async function (evt) {
                for (const feature of evt.features.getArray()) {
                    const id = feature.get('id'); if (!id) continue;
                    const coord = ol.proj.toLonLat(feature.getGeometry().getCoordinates());
                    await fetch(`/api/asset/assets/${id}`, {
                        method: 'PUT', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ assetType: feature.get('assetType'), siteId: feature.get('siteId') ?? null, geometry: `POINT(${coord[0]}  ${coord[1]})` })
                    });
                }
                await loadAllData();
            });
            map.addInteraction(translateInteraction);
        },
        onDeactivate: function () { if (translateInteraction) { map.removeInteraction(translateInteraction); translateInteraction = null; } }
    },
    editCanal: { active: false, btnId: 'btn-edit-canal', onText: '✏️ Canal ON — click a line', offText: '✏️ Edit canal', onStyle: { background: '#0066cc', color: '#fff', border: '1px solid #0066cc' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #0066cc' }, cursor: 'crosshair', onActivate: null, onDeactivate: null },
    queryArea: {
        active: false, btnId: 'btn-query-area', onText: '🔲 Drag to select area...', offText: '🔲 Query area', onStyle: { background: '#dd6974', color: '#fff', border: '1px solid #dd6974' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #dd6974' }, cursor: 'crosshair',
        onActivate: function () {
            dragBoxInteraction = new ol.interaction.DragBox({ condition: ol.events.condition.noModifierKeys });
            dragBoxInteraction.on('boxend', function () { applyAreaFilter(dragBoxInteraction.getGeometry().getExtent()); });
            map.addInteraction(dragBoxInteraction);
        },
        onDeactivate: function () { if (dragBoxInteraction) { map.removeInteraction(dragBoxInteraction); dragBoxInteraction = null; } }
    },
    queryNearest: { active: false, btnId: 'btn-query-nearest', onText: '📍 Click a point on the map...', offText: '📍 N nearest sites', onStyle: { background: '#dd6974', color: '#fff', border: '1px solid #dd6974' }, offStyle: { background: '#2a3a5e', color: '#eee', border: '1px solid #dd6974' }, cursor: 'crosshair', onActivate: null, onDeactivate: null },
};

function activateMode(key) {
    Object.keys(modes).forEach(k => { if (k !== key && modes[k].active) deactivateMode(k); });
    const mode = modes[key];
    if (mode.active) { deactivateMode(key); return; }
    mode.active = true;
    const btn = document.getElementById(mode.btnId);
    if (btn) { btn.textContent = mode.onText; Object.assign(btn.style, mode.onStyle); }
    map.getTargetElement().style.cursor = mode.cursor;
    if (mode.onActivate) mode.onActivate();
}

function deactivateMode(key) {
    const mode = modes[key];
    if (!mode.active) return;
    mode.active = false;
    const btn = document.getElementById(mode.btnId);
    if (btn) { btn.textContent = mode.offText; Object.assign(btn.style, mode.offStyle); }
    map.getTargetElement().style.cursor = '';
    if (mode.onDeactivate) mode.onDeactivate();
}

function toggleEditMode() { activateMode('editSite'); }
function toggleAssetEditMode() { activateMode('editAsset'); }
function toggleMoveAssetMode() { activateMode('moveAsset'); }
function toggleCanalEditMode() { activateMode('editCanal'); }
function toggleQueryAreaMode() { activateMode('queryArea'); }
function toggleNearestMode() { activateMode('queryNearest'); }
function toggleModifySiteMode() { activateMode('modifySite'); }
function toggleModifyCanalMode() { activateMode('modifyCanal'); }
function toggleWallEditMode() { activateMode('editWall'); }
function toggleModifyWallMode() { activateMode('modifyWall'); }
function toggleCanalSelectMode() { activateMode('canalSelect'); }

function resetButtons() {
    Object.keys(modes).forEach(k => deactivateMode(k));
    map.getTargetElement().style.cursor = '';
}

loadAllData();