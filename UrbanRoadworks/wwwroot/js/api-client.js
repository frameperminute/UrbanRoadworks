const format = new ol.format.WKT();

function parseFeatures(items) {
    return items.map(item => {
        const geoKey = Object.keys(item).find(k => k.toLowerCase() === 'geometry');
        if (!geoKey || !item[geoKey]) return null;
        try {
            const feature = format.readFeature(item[geoKey], {
                dataProjection: 'EPSG:4326',
                featureProjection: 'EPSG:3857'
            });
            const props = { ...item };
            delete props[geoKey];
            feature.setProperties(props);
            return feature;
        } catch (e) {
            console.error('Error WKT:', e.message, item[geoKey]);
            return null;
        }
    }).filter(f => f !== null);
}

async function loadAllData() {
    try {
        const [sitesRes, affectedRes, assetsRes, canalsRes, wallsRes] = await Promise.all([
            fetch('/api/site/sites'),
            fetch('/api/map/affected-network-roads'),
            fetch('/api/asset/assets'),
            fetch('/api/canal/canals'),
            fetch('/api/wall/walls')
        ]);
        allSites = await sitesRes.json();
        allAffectedRoads = await affectedRes.json();
        allAssets = await assetsRes.json();
        allCanals = (await canalsRes.json()).map(c => ({ ...c, canalType: 'cable' }));
        allWalls = await wallsRes.json();

        const netRes = await fetch('/api/map/roads');
        const netData = await netRes.json();
        networkSource.clear();
        networkSource.addFeatures(parseFeatures(netData));
        applyFilters();
        populateInspectorPanel();
    } catch (err) {
        console.error('Errore loading data:', err);
        document.getElementById('cnt-sites').textContent = 'ERR';
        document.getElementById('cnt-roads').textContent = 'ERR';
        document.getElementById('cnt-assets').textContent = 'ERR';
        document.getElementById('cnt-canals').textContent = 'ERR';
    }
}

async function saveChanges() {
    const id = document.getElementById('edit-id').value;
    const geometry = document.getElementById('edit-geometry').value;
    const body = {
        name: document.getElementById('edit-name').value,
        status: document.getElementById('edit-status').value,
        startDate: document.getElementById('edit-start').value || null,
        endDate: document.getElementById('edit-end').value || null,
        geometry: geometry || null
    };
    const url = id ? `/api/site/sites/${id}` : '/api/site/sites';
    const method = id ? 'PUT' : 'POST';
    const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (res.ok) {
        const msg = document.createElement('div');
        msg.textContent = id ? '✅ Construction site updated' : '✅ Construction site created';
        msg.style.cssText = 'background:#1a3a20;color:#6daa45;padding:8px;border-radius:6px;font-size:0.82rem;text-align:center;';
        document.getElementById('sidebar').appendChild(msg);
        setTimeout(() => msg.remove(), 3000);
    }
    document.getElementById('edit-panel').style.display = 'none';
    document.getElementById('overlay').style.display = 'none';
    resetButtons();
    await loadAllData();
}

async function deleteSite() {
    const id = document.getElementById('edit-id').value;
    if (!id || !confirm('Delete this site?')) return;
    await fetch(`/api/site/sites/${id}`, { method: 'DELETE' });
    document.getElementById('edit-panel').style.display = 'none';
    document.getElementById('overlay').style.display = 'none';
    resetButtons();
    await loadAllData();
}

async function saveAsset() {
    const id = document.getElementById('asset-id').value;
    const body = {
        assetType: document.getElementById('asset-type').value,
        siteId: parseInt(document.getElementById('asset-site-id').value) || null,
        geometry: document.getElementById('asset-geometry').value || null
    };
    const url = id ? `/api/asset/assets/${id}` : '/api/asset/assets';
    const method = id ? 'PUT' : 'POST';
    await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    document.getElementById('asset-panel').style.display = 'none';
    document.getElementById('overlay').style.display = 'none';
    resetButtons();
    await loadAllData();
}

async function deleteAsset() {
    const id = document.getElementById('asset-id').value;
    if (!id || !confirm('Remove this asset?')) return;
    await fetch(`/api/asset/assets/${id}`, { method: 'DELETE' });
    document.getElementById('asset-panel').style.display = 'none';
    document.getElementById('overlay').style.display = 'none';
    resetButtons();
    await loadAllData();
}

async function saveCanal() {
    const id = document.getElementById('canal-id').value;
    const body = {
        name: document.getElementById('canal-name').value,
        status: document.getElementById('canal-status').value,
        geometry: document.getElementById('canal-geometry').value || null
    };
    const url = id ? `/api/canal/canals/${id}` : '/api/canal/canals';
    const method = id ? 'PUT' : 'POST';
    await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    document.getElementById('canal-panel').style.display = 'none';
    document.getElementById('overlay').style.display = 'none';
    resetButtons();
    map.getTargetElement().style.cursor = '';
    await loadAllData();
}

async function deleteCanal() {
    const id = document.getElementById('canal-id').value;
    if (!id || !confirm('Delete this canal?')) return;
    await fetch(`/api/canal/canals/${id}`, { method: 'DELETE' });
    document.getElementById('canal-panel').style.display = 'none';
    document.getElementById('overlay').style.display = 'none';
    resetButtons();
    await loadAllData();
}

async function saveWall() {
    const id = document.getElementById('wall-id').value;
    const body = {
        name: document.getElementById('wall-name').value,
        thickness: parseFloat(document.getElementById('wall-thickness').value) || 20,
        material: document.getElementById('wall-material').value,
        geometry: document.getElementById('wall-geometry').value || null
    };
    const url = id ? `/api/wall/walls/${id}` : '/api/wall/walls';
    const method = id ? 'PUT' : 'POST';
    await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    document.getElementById('wall-panel').style.display = 'none';
    document.getElementById('overlay').style.display = 'none';
    resetButtons();
    await loadAllData();
}

async function deleteWall() {
    const id = document.getElementById('wall-id').value;
    if (!id || !confirm('Delete this wall?')) return;
    await fetch(`/api/wall/walls/${id}`, { method: 'DELETE' });
    document.getElementById('wall-panel').style.display = 'none';
    document.getElementById('overlay').style.display = 'none';
    resetButtons();
    await loadAllData();
}