// Map tab page, hosted by WebView2 in the statistics window. The window sends everything to draw
// (init → route → progress); this page only renders and reports problems back.
import { Map, Marker, LngLatBounds } from './maplibre-gl.mjs';

const host = window.chrome.webview;
let map = null;
let loaded = false;
let pending = [];
let markers = [];
let tilesErrorReported = false;
let routeBounds = null;   // last fitted route, re-fitted when the map resizes
let userMoved = false;    // until the user pans or zooms

const post = message => host.postMessage(message);

function webglSupported() {
  try {
    const canvas = document.createElement('canvas');
    return !!(canvas.getContext('webgl2') || canvas.getContext('webgl'));
  } catch {
    return false;
  }
}

function init(message) {
  for (const [name, value] of Object.entries(message.colors)) {
    document.documentElement.style.setProperty(`--${name}`, value);
  }

  if (!webglSupported()) {
    post({ type: 'error', code: 'webgl' });
    return;
  }

  map = new Map({
    container: 'map',
    style: message.style,
    center: [20, 48],
    zoom: 3,
    attributionControl: { compact: true, customAttribution: message.attribution },
    dragRotate: false,
    pitchWithRotate: false,
    touchPitch: false,
  });

  map.on('load', () => {
    const colors = message.colors;
    map.addSource('route', { type: 'geojson', data: emptyCollection() });
    map.addSource('walked', { type: 'geojson', data: emptyCollection() });
    map.addLayer({
      id: 'route-road', type: 'line', source: 'route', filter: ['!', ['get', 'straight']],
      layout: { 'line-cap': 'round', 'line-join': 'round' },
      paint: { 'line-color': colors.muted, 'line-width': 4, 'line-opacity': 0.8 },
    });
    map.addLayer({
      id: 'route-straight', type: 'line', source: 'route', filter: ['get', 'straight'],
      layout: { 'line-cap': 'round' },
      paint: { 'line-color': colors.muted, 'line-width': 3, 'line-dasharray': [1, 2], 'line-opacity': 0.8 },
    });
    map.addLayer({
      id: 'walked', type: 'line', source: 'walked',
      layout: { 'line-cap': 'round', 'line-join': 'round' },
      paint: { 'line-color': colors.accent, 'line-width': 5 },
    });
    loaded = true;
    pending.forEach(handle);
    pending = [];
    post({ type: 'loaded' });
  });

  // The panel above the map can grow (hints), which shrinks the map: keep the whole route in view until the user moves.
  map.on('resize', () => {
    if (routeBounds && !userMoved) {
      map.fitBounds(routeBounds, { padding: 70, maxZoom: 12, duration: 0 });
    }
  });
  map.on('movestart', e => {
    if (e.originalEvent) {
      userMoved = true;
    }
  });

  map.on('error', e => {
    // A missing tile is normal while offline; report once so the window can say so.
    if (!loaded) {
      post({ type: 'error', code: 'style', message: String(e.error?.message ?? e.error ?? '') });
    } else if (!tilesErrorReported) {
      tilesErrorReported = true;
      post({ type: 'error', code: 'tiles', message: String(e.error?.message ?? e.error ?? '') });
    }
  });
}

const emptyCollection = () => ({ type: 'FeatureCollection', features: [] });

const lineFeature = (coordinates, properties = {}) => ({
  type: 'Feature', properties, geometry: { type: 'LineString', coordinates },
});

function element(className, label) {
  const div = document.createElement('div');
  div.className = className;
  if (label) {
    const span = document.createElement('span');
    span.textContent = label; // text only: labels never become markup
    div.appendChild(span);
  }
  return div;
}

function clearMarkers(kind) {
  markers.filter(m => m.kind === kind).forEach(m => m.marker.remove());
  markers = markers.filter(m => m.kind !== kind);
}

function showRoute(message) {
  map.getSource('route').setData({
    type: 'FeatureCollection',
    features: message.legs.map(leg => lineFeature(leg.line, { straight: leg.straight })),
  });
  clearMarkers('place');
  for (const place of message.places) {
    const marker = new Marker({ element: element(place.auto ? 'place auto' : 'place', place.name) }).setLngLat([place.lon, place.lat]).addTo(map);
    markers.push({ kind: 'place', marker });
  }

  const points = message.legs.flatMap(leg => leg.line);
  userMoved = false;
  routeBounds = null;
  if (points.length === 0 && message.places.length > 0) {
    map.flyTo({ center: [message.places[0].lon, message.places[0].lat], zoom: 8 });
  } else if (points.length > 0) {
    routeBounds = points.reduce((b, p) => b.extend(p), new LngLatBounds(points[0], points[0]));
    map.fitBounds(routeBounds, { padding: 70, maxZoom: 12, duration: 600 });
  }
}

function showProgress(message) {
  map.getSource('walked').setData(message.walked.length > 1 ? { type: 'FeatureCollection', features: [lineFeature(message.walked)] } : emptyCollection());
  clearMarkers('period');
  // Selected last, so it is drawn on top.
  for (const m of [...message.markers].sort((a, b) => a.selected - b.selected)) {
    const marker = new Marker({ element: element(m.selected ? 'period selected' : 'period', m.label) })
      .setLngLat([m.lon, m.lat]).addTo(map);
    markers.push({ kind: 'period', marker });
  }
}

function handle(message) {
  if (message.type === 'init') {
    init(message);
    return;
  }

  if (!loaded) {
    pending = pending.filter(p => p.type !== message.type); // only the latest of each kind matters
    pending.push(message);
    return;
  }

  if (message.type === 'route') {
    showRoute(message);
  } else if (message.type === 'progress') {
    showProgress(message);
  }
}

host.addEventListener('message', e => handle(e.data));
post({ type: 'ready' });
