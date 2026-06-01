// --- GLOBAL STATE ---
// Shared data arrays populated by loadAllData(), used across all modules
let allSites = [];
let allAssets = [];
let allAffectedRoads = [];
let allCanals = [];
let allWalls = [];

let drawWall = null;
let drawSite = null;
let drawAsset = null;
let drawCanal = null;

let translateInteraction = null;
let modifySiteInteraction = null;
let modifyCanalInteraction = null;
let modifyWallInteraction = null;
let dragBoxInteraction = null;

// --- VECTOR SOURCES & LAYERS ---
// One source/layer pair per feature type; zIndex controls draw order
const cableNodesSource = new ol.source.Vector();
const sitesSource = new ol.source.Vector();
const assetsSource = new ol.source.Vector();
const affectedRoadsSource = new ol.source.Vector();
const networkSource = new ol.source.Vector();
const canalsSource = new ol.source.Vector();
const queryHighlightSource = new ol.source.Vector();
const wallsSource = new ol.source.Vector();
const routeMarkersSource = new ol.source.Vector();
let cablePlanHighlightSource = new ol.source.Vector();

const sitesLayer = new ol.layer.Vector({ source: sitesSource, style: siteStyle, zIndex: 1 });

const assetsLayer = new ol.layer.Vector({ source: assetsSource, style: assetStyle, zIndex: 3 });

const cablePlanHighlightLayer = new ol.layer.Vector({
    source: cablePlanHighlightSource,
    style: new ol.style.Style({ stroke: new ol.style.Stroke({ color: '#e8af34', width: 6 }) }),
    zIndex: 9
});

const routeMarkersLayer = new ol.layer.Vector({
    source: routeMarkersSource,
    zIndex: 10,
    style: function (feature) {
        const type = feature.get('pointType');
        return new ol.style.Style({
            image: new ol.style.Circle({
                radius: 10,
                fill: new ol.style.Fill({ color: type === 'from' ? '#4f98a3' : '#ff6b6b' }),
                stroke: new ol.style.Stroke({ color: '#fff', width: 2 })
            }),
            text: new ol.style.Text({
                text: type === 'from' ? 'A' : 'B',
                fill: new ol.style.Fill({ color: '#fff' }),
                font: 'bold 11px sans-serif'
            })
        });
    }
});

const cableNodesLayer = new ol.layer.Vector({
    source: cableNodesSource,
    style: function (feature) {
        const nodeIndex = feature.get('nodeIndex');
        return new ol.style.Style({
            image: new ol.style.Circle({
                radius: 9,
                fill: new ol.style.Fill({ color: 'rgba(232,175,52,0.9)' }),
                stroke: new ol.style.Stroke({ color: '#fff', width: 2 })
            }),
            text: new ol.style.Text({
                text: 'N' + nodeIndex,
                fill: new ol.style.Fill({ color: '#1a1a2e' }),
                font: 'bold 9px sans-serif',
                offsetY: 1
            })
        });
    },
    zIndex: 11
});

const wallsLayer = new ol.layer.Vector({
    source: wallsSource,
    style: function (feature) {
        const thickness = feature.get('thickness') || 10;
        const lineWidth = Math.max(2, Math.min(10, thickness / 3));
        const material = feature.get('material');
        const colors = {
            concrete: '#b0bec5',
            brick: '#ef9a9a',
            drywall: '#fff9c4',
            stone: '#a1887f'
        };
        return new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: colors[material] || '#e0e0e0',
                width: lineWidth
            })
        });
    },
    zIndex: 5
});

const queryHighlightLayer = new ol.layer.Vector({
    source: queryHighlightSource,
    style: function (feature) {
        const geomType = feature.getGeometry().getType();
        if (geomType === 'Point') {
            return new ol.style.Style({
                image: new ol.style.Circle({
                    radius: 12,
                    fill: new ol.style.Fill({ color: 'rgba(232,175,52,0.5)' }),
                    stroke: new ol.style.Stroke({ color: '#e8af34', width: 3 })
                })
            });
        }
        if (geomType === 'LineString') {
            return new ol.style.Style({
                stroke: new ol.style.Stroke({ color: '#e8af34', width: 6 })
            });
        }
        return new ol.style.Style({
            fill: new ol.style.Fill({ color: 'rgba(232,175,52,0.25)' }),
            stroke: new ol.style.Stroke({ color: '#e8af34', width: 3 })
        });
    },
    zIndex: 8
});

const canalsLayer = new ol.layer.Vector({
    source: canalsSource,
    style: function (feature) {
        const status = feature.get('status');
        const colors = {
            active: { color: '#4caf7d', width: 3 },
            planned: { color: '#5ab4f7', width: 3 },
            completed: { color: '#1a80e5', width: 3 }
        };
        const c = colors[status] || colors.planned;
        return new ol.style.Style({
            stroke: new ol.style.Stroke({ color: c.color, width: c.width })
        });
    },
    zIndex: 2
});

const networkLayer = new ol.layer.Vector({
    source: networkSource,
    style: new ol.style.Style({
        stroke: new ol.style.Stroke({ color: '#555', width: 1 })
    }),
    zIndex: 0
});

const affectedRoadsLayer = new ol.layer.Vector({
    source: affectedRoadsSource,
    style: function (feature) {
        const status = feature.get('siteStatus');
        return new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: status === 'active' ? '#ff4444' : '#fdab43',
                width: 4,
                lineDash: [8, 4]
            })
        });
    },
    zIndex: 4
});

// --- STYLE FUNCTIONS ---
// Returns an OpenLayers Style based on feature properties (status, type, selection state)
function siteStyle(feature) {
    const status = feature.get('status');
    const colors = {
        active: { fill: 'rgba(255,107,107,0.25)', stroke: '#ff4444' },
        planned: { fill: 'rgba(150,150,150,0.2)', stroke: '#fdab43' },
        completed: { fill: 'rgba(109,170,69,0.2)', stroke: '#6daa45' }
    };
    const c = colors[status] || colors.planned;
    return new ol.style.Style({
        fill: new ol.style.Fill({ color: c.fill }),
        stroke: new ol.style.Stroke({ color: c.stroke, width: 2 })
    });
}

function assetStyle(feature) {
    const type = feature.get('assetType');
    const colors = {
        temporary_traffic_light: '#ff4444',
        warning_sign: '#fdab43',
        site_entrance: '#6daa45',
        detour_sign: '#a86fdf'
    };
    const color = colors[type] || '#aaa';
    return new ol.style.Style({
        image: new ol.style.Circle({
            radius: 8,
            fill: new ol.style.Fill({ color }),
            stroke: new ol.style.Stroke({ color: '#fff', width: 1.5 })
        })
    });
}

// --- MAP INITIALIZATION ---
// OpenLayers map centered on the project area with OSM base layer
const map = new ol.Map({
    target: 'map',
    layers: [
        new ol.layer.Tile({ source: new ol.source.OSM() }),
        networkLayer,
        canalsLayer,
        wallsLayer,
        sitesLayer,
        assetsLayer,
        affectedRoadsLayer,
        queryHighlightLayer,
        routeMarkersLayer,
        cablePlanHighlightLayer,
        cableNodesLayer
    ],
    view: new ol.View({
        center: ol.proj.fromLonLat([13.0680, 43.1360]),
        zoom: 15
    })
});