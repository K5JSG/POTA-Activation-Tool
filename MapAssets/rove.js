// Rove planner for the park map: pick parks (Add to Rove in a park's popup,
// or by reference/name in the panel) and other stops (an address, or drag
// the route line to where you want to go), put them in driving order, and
// see the road route with miles, drive time and turn-by-turn directions for
// each leg. Plans are saved by the app (RoveService, via the map server's
// /rove routes) and picked from the Plan dropdown to load them again;
// From pota.app loads the plans saved on your pota.app account. Print
// Directions makes a printable page (or PDF) of the whole trip.
//
// Inlined into the map page after its main script (MapService.BuildMapHtml),
// so it uses that script's globals: map, parkData, youMarker, escapeHtml,
// formatDistance and buildPopupHtml.
(function () {
  // Saving plans and road routes need the app's map server; a Save Map copy
  // opened from disk only gets an unsaved plan with straight-line legs.
  var roveServer = location.protocol === 'http:';
  // In a saved copy (Layers > Save a copy of this map): the app's plans and
  // their cached road routes, baked in by MapServer's /save-map - see
  // RoveService.BuildEmbedJson. Changes made in a saved copy last only while
  // it's open.
  var embedded = !roveServer && window.embeddedRove ? window.embeddedRove : null;
  // A saved copy can still ask the app for legs it doesn't have, while PAT is
  // running on this PC - the same address and key it uses for the GPS
  // (embeddedGps, see MapServer.AllowSavedCopy).
  var appRouteUrl = !roveServer && window.embeddedGps && embeddedGps.url
    ? embeddedGps.url.replace('/gps?', '/rove/route?') : null;

  var panelEl = document.getElementById('rovePanel');
  var toggleEl = document.getElementById('roveToggle');
  var planSelectEl = document.getElementById('rovePlanSelect');
  var planNameEl = document.getElementById('rovePlanName');
  var noteEl = document.getElementById('roveNote');
  var addInputEl = document.getElementById('roveAddInput');
  var addResultsEl = document.getElementById('roveAddResults');
  var startGpsEl = document.getElementById('roveStartGps');
  var summaryEl = document.getElementById('roveSummary');
  var stopsEl = document.getElementById('roveStops');
  var potaLoadEl = document.getElementById('rovePotaLoad');
  var potaSelectEl = document.getElementById('rovePotaSelect');

  var parkByRef = {};
  parkData.forEach(function (p) { parkByRef[p.reference.toUpperCase()] = p; });

  var plan = null;     // { name, startAtGps, stops: [{ reference, name, lat, lon }] }
  var planId = null;
  var planList = [];   // saved plans: { id, name, updated, stopCount }

  // The current route: routePoints are [lat, lon] for each point driven
  // through (your location first when the route starts there), legs[i] is
  // the road route from point i to i+1, or null for a straight line.
  var routePoints = [];
  var legs = [];
  var routeState = 'none'; // none | pending | done | offline | standalone
  var routeFromGps = false;
  var routeSeq = 0;
  var routeTimer = null;
  var openLegs = {};
  var saveTimer = null;

  // ---- Small helpers

  function storeGet(key) {
    try { return localStorage.getItem('rove.' + key); } catch (e) { return null; }
  }
  function storeSet(key, value) {
    try {
      if (value === null) localStorage.removeItem('rove.' + key);
      else localStorage.setItem('rove.' + key, value);
    } catch (e) { /* storage blocked - just not remembered */ }
  }

  var noteTimer = null;
  function showNote(text, sticky) {
    clearTimeout(noteTimer);
    noteEl.textContent = text;
    noteEl.hidden = !text;
    if (text && !sticky) noteTimer = setTimeout(function () { noteEl.hidden = true; }, 8000);
  }

  function formatMiles(meters) { return (meters / 1609.344).toFixed(1) + ' mi'; }

  function formatDuration(seconds) {
    var minutes = Math.round(seconds / 60);
    if (minutes < 60) return minutes + ' min';
    return Math.floor(minutes / 60) + ' h ' + (minutes % 60) + ' min';
  }

  function stopLabel(s) { return s.reference ? s.reference + ' ' + s.name : s.name; }

  function stopKey(s) {
    return s.reference ? s.reference.toUpperCase() : s.lat.toFixed(5) + ',' + s.lon.toFixed(5);
  }

  function indexOfRef(ref) {
    if (!plan || !ref) return -1;
    ref = ref.toUpperCase();
    for (var i = 0; i < plan.stops.length; i++) {
      if (plan.stops[i].reference.toUpperCase() === ref) return i;
    }
    return -1;
  }

  function newPlanId() {
    return Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
  }

  function todayName() {
    return 'Rove ' + new Date().toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
  }

  // Two clicks to do something that can't be undone, instead of a browser
  // confirm() box: the first click changes the button to ask, the second
  // (within a few seconds) does it.
  function confirmButton(button, question, action) {
    var label = button.textContent, timer = null;
    button.addEventListener('click', function () {
      if (timer === null) {
        button.textContent = question;
        timer = setTimeout(function () { button.textContent = label; timer = null; }, 4000);
        return;
      }
      clearTimeout(timer);
      timer = null;
      button.textContent = label;
      action();
    });
  }

  // ---- Saving and loading plans

  function api(method, path, body) {
    return fetch(path, {
      method: method,
      cache: 'no-store',
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined
    }).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.status === 204 ? null : r.json();
    });
  }

  function refreshPlanList() {
    if (!roveServer) {
      if (embedded) {
        planList = embedded.plans.map(function (p) {
          return { id: p.id, name: p.name, updated: p.updated, stopCount: p.stops.length, potaRoveId: p.potaRoveId };
        });
      }
      renderPlanSelect();
      return Promise.resolve();
    }
    return api('GET', '/rove/plans').then(function (list) {
      planList = list || [];
      renderPlanSelect();
    }).catch(function () {
      showNote("Couldn't reach the app to list saved plans - is it still running?", true);
    });
  }

  function renderPlanSelect() {
    planSelectEl.innerHTML = '';
    var entries = planList.slice();
    if (plan && !entries.some(function (p) { return p.id === planId; })) {
      entries.unshift({ id: planId, name: plan.name, stopCount: plan.stops.length });
    }
    if (entries.length === 0) addOption('', '(no saved plans yet)');
    else if (!plan) addOption('', '(pick a saved plan to load it)');
    entries.forEach(function (p) {
      var count = p.id === planId && plan ? plan.stops.length : p.stopCount;
      addOption(p.id, p.name + ' (' + count + (count === 1 ? ' stop)' : ' stops)'));
    });
    addOption('__new', '+ New plan');
    planSelectEl.value = plan ? planId : '';

    function addOption(value, text) {
      var option = document.createElement('option');
      option.value = value;
      option.textContent = text;
      planSelectEl.appendChild(option);
    }
  }

  function loadPlan(id, fit) {
    if (!roveServer) {
      var saved = embedded && embedded.plans.filter(function (p) { return p.id === id; })[0];
      if (saved) {
        setPlan(id, JSON.parse(JSON.stringify(saved))); // a copy, so edits don't change the baked-in one
        if (fit) fitToStops();
      }
      return Promise.resolve();
    }
    return api('GET', '/rove/plans/' + id).then(function (loaded) {
      setPlan(id, loaded);
      if (fit) fitToStops();
    }).catch(function () {
      showNote("Couldn't load that plan.");
      renderPlanSelect();
    });
  }

  function setPlan(id, newPlan) {
    plan = newPlan;
    planId = id;
    if (plan) {
      plan.stops = plan.stops || [];
      plan.stops.forEach(function (s) { s.reference = s.reference || ''; });
    }
    storeSet('planId', plan && roveServer ? id : null);
    openLegs = {};
    renderAll();
    requestRoute();
  }

  function createPlan(name, stops) {
    setPlan(newPlanId(), { name: name || todayName(), startAtGps: false, stops: stops || [] });
    savePlanNow();
  }

  function ensurePlan() {
    if (!plan) createPlan();
  }

  function scheduleSave() {
    if (!roveServer || !plan) return;
    clearTimeout(saveTimer);
    saveTimer = setTimeout(savePlanNow, 400);
  }

  function savePlanNow() {
    if (!roveServer || !plan) return;
    clearTimeout(saveTimer);
    var id = planId;
    api('PUT', '/rove/plans/' + id, plan).then(function (saved) {
      var entry = planList.filter(function (p) { return p.id === id; })[0];
      if (!entry) {
        entry = { id: id };
        planList.unshift(entry);
      }
      entry.name = saved.name;
      entry.updated = saved.updated;
      entry.stopCount = saved.stops.length;
      entry.potaRoveId = saved.potaRoveId;
      renderPlanSelect();
    }).catch(function () {
      showNote("Couldn't save the plan - is the app still running?", true);
    });
  }

  // Every change to the stops goes through here.
  function planChanged() {
    openLegs = {};
    renderAll();
    scheduleSave();
    requestRoute();
  }

  // ---- Editing the stops

  // Adds at the end, or before stop `index` when given.
  function addStop(stop, index) {
    ensurePlan();
    var key = stopKey(stop);
    for (var i = 0; i < plan.stops.length; i++) {
      if (stopKey(plan.stops[i]) === key) {
        showNote(stopLabel(stop) + ' is already stop ' + (i + 1) + '.');
        return false;
      }
    }
    if (typeof index === 'number' && index >= 0 && index < plan.stops.length) plan.stops.splice(index, 0, stop);
    else plan.stops.push(stop);
    planChanged();
    return true;
  }

  function stopFromPark(p) {
    return { reference: p.reference, name: p.name, lat: p.lat, lon: p.lon };
  }

  function removeStop(index) {
    plan.stops.splice(index, 1);
    planChanged();
  }

  // Moves stop `from` to sit just before what is now stop `to`
  // (to = stops.length moves it to the end).
  function moveStop(from, to) {
    if (to > from) to--;
    if (to === from) return;
    var stop = plan.stops.splice(from, 1)[0];
    plan.stops.splice(to, 0, stop);
    planChanged();
  }

  // A park outside the loaded state is looked up on pota.app, so it needs
  // signal. Returns a promise, so several references are added in order.
  function addByRef(ref) {
    ref = ref.toUpperCase();
    var p = parkByRef[ref];
    if (p) { addStop(stopFromPark(p)); return Promise.resolve(); }
    return fetch('https://api.pota.app/park/' + encodeURIComponent(ref)).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.json();
    }).then(function (d) {
      if (!d || d.latitude === null || d.latitude === undefined) {
        showNote(ref + ' was not found on pota.app.');
        return;
      }
      var name = d.name || '';
      if (d.parktypeDesc && name.toLowerCase().indexOf(d.parktypeDesc.toLowerCase()) < 0) name += ' ' + d.parktypeDesc;
      addStop({ reference: d.reference || ref, name: name, lat: d.latitude, lon: d.longitude });
    }).catch(function () {
      showNote("Couldn't look up " + ref + ' - it is not in the state loaded on this map, and pota.app could not be reached.');
    });
  }

  var RefPattern = /\b[A-Z0-9]{1,4}-\d{4,6}\b/gi;

  function findParksByName(text) {
    var words = text.toLowerCase().split(/\s+/).filter(function (w) { return w; });
    if (words.length === 0) return [];
    return parkData.filter(function (p) {
      var name = p.name.toLowerCase();
      return words.every(function (w) { return name.indexOf(w) >= 0; });
    });
  }

  function addFromInput() {
    var text = addInputEl.value.trim();
    if (!text) return;
    addResultsEl.innerHTML = '';
    RefPattern.lastIndex = 0;
    var refs = text.match(RefPattern);
    if (refs) {
      addInputEl.value = '';
      refs.reduce(function (chain, ref) { return chain.then(function () { return addByRef(ref); }); }, Promise.resolve());
      return;
    }
    var matches = findParksByName(text);
    if (matches.length === 1) {
      addStop(stopFromPark(matches[0]));
      addInputEl.value = '';
    } else if (matches.length === 0) {
      searchAddress(text);
    } else {
      showResults(matches, text);
    }
  }

  function clearAddBox() {
    addInputEl.value = '';
    addResultsEl.innerHTML = '';
  }

  function addResultRow(text, onClick, className) {
    var row = document.createElement('div');
    row.textContent = text;
    if (className) row.className = className;
    if (onClick) row.addEventListener('click', onClick);
    addResultsEl.appendChild(row);
  }

  // Parks whose names match, then a row to search for the text as an
  // address instead.
  function showResults(matches, text) {
    addResultsEl.innerHTML = '';
    matches.slice(0, 6).forEach(function (p) {
      addResultRow(p.reference + ' ' + p.name, function () { addStop(stopFromPark(p)); clearAddBox(); });
    });
    if (matches.length > 6) addResultRow((matches.length - 6) + ' more parks - keep typing to narrow it down.', null, 'sidebar-note');
    if (text) addResultRow('\u{1F50D} Search for the address "' + text + '"', function () { searchAddress(text); });
  }

  // ---- Addresses (OpenStreetMap's Nominatim service, the same one the
  // map's "What's here?" uses - needs signal).

  // "123 Main Street, Albion" rather than Nominatim's full
  // street-to-country display name.
  function shortAddress(result) {
    var a = result.address || {};
    var street = [a.house_number, a.road].filter(function (x) { return x; }).join(' ');
    var first = result.name || street || '';
    var place = a.city || a.town || a.village || a.hamlet || a.county || '';
    if (first) return [first, place].filter(function (x) { return x; }).join(', ');
    return (result.display_name || '').split(', ').slice(0, 3).join(', ');
  }

  function searchAddress(text) {
    addResultsEl.innerHTML = '';
    addResultRow('Searching for "' + text + '"...', null, 'sidebar-note');
    // Results near what the map shows are listed first.
    var b = map.getBounds();
    var url = 'https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=6&countrycodes=us,ca' +
      '&viewbox=' + [b.getWest(), b.getNorth(), b.getEast(), b.getSouth()].map(function (n) { return n.toFixed(4); }).join(',') +
      '&q=' + encodeURIComponent(text);
    fetch(url).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.json();
    }).then(function (results) {
      addResultsEl.innerHTML = '';
      if (!results.length) { addResultRow('No address found for "' + text + '".', null, 'sidebar-note'); return; }
      results.forEach(function (r) {
        var name = shortAddress(r);
        addResultRow('\u{1F4CD} ' + r.display_name, function () {
          addStop({ reference: '', name: name, lat: +(+r.lat).toFixed(6), lon: +(+r.lon).toFixed(6) });
          clearAddBox();
          programmaticMove = true;
          map.panTo([+r.lat, +r.lon]);
        });
      });
    }).catch(function () {
      addResultsEl.innerHTML = '';
      addResultRow("Couldn't search for addresses - no signal?", null, 'sidebar-note');
    });
  }

  function placeholderName(latlng) {
    return 'Stop at ' + latlng.lat.toFixed(5) + ', ' + latlng.lng.toFixed(5);
  }

  // Names a dropped or dragged stop after the nearest address when there's
  // signal; otherwise it keeps its coordinates as its name.
  function nameStopFromAddress(stop) {
    fetch('https://nominatim.openstreetmap.org/reverse?format=jsonv2&addressdetails=1&zoom=18&lat=' + stop.lat + '&lon=' + stop.lon)
      .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
      .then(function (r) {
        var name = r && !r.error ? shortAddress(r) : '';
        if (!name || !plan || plan.stops.indexOf(stop) < 0) return;
        stop.name = name;
        drawStops();
        renderStops();
        scheduleSave();
      })
      .catch(function () { /* keeps the coordinates as its name */ });
  }

  function addPlaceStop(latlng, index) {
    var stop = { reference: '', name: placeholderName(latlng), lat: +latlng.lat.toFixed(6), lon: +latlng.lng.toFixed(6) };
    if (addStop(stop, index)) nameStopFromAddress(stop);
  }

  // Shortest trip by straight-line distance: nearest neighbor, then 2-opt
  // (reverse any stretch of the trip that makes it shorter) until nothing
  // improves. The trip starts at your location when "Start from my
  // location" is on and there's a fix, otherwise stop 1 stays first.
  function orderByDistance() {
    if (!plan || plan.stops.length < 2) return;
    var start = plan.startAtGps && window.youMarker ? youMarker.getLatLng() : null;
    var remaining = plan.stops.slice();
    var order = [];
    var current;
    if (start) {
      current = start;
    } else {
      order.push(remaining.shift());
      current = L.latLng(order[0].lat, order[0].lon);
    }
    while (remaining.length) {
      var best = 0, bestDistance = Infinity;
      for (var i = 0; i < remaining.length; i++) {
        var d = current.distanceTo([remaining[i].lat, remaining[i].lon]);
        if (d < bestDistance) { bestDistance = d; best = i; }
      }
      var next = remaining.splice(best, 1)[0];
      order.push(next);
      current = L.latLng(next.lat, next.lon);
    }

    var pts = (start ? [start] : []).concat(order.map(function (s) { return L.latLng(s.lat, s.lon); }));
    var items = (start ? [null] : []).concat(order);
    function dist(a, b) { return pts[a].distanceTo(pts[b]); }
    var improved = true, rounds = 0;
    while (improved && rounds++ < 50) {
      improved = false;
      for (var a = 1; a < pts.length - 1; a++) {
        for (var b = a + 1; b < pts.length; b++) {
          // Reversing a..b swaps edges (a-1, a) + (b, b+1) for (a-1, b) + (a, b+1).
          var before = dist(a - 1, a) + (b + 1 < pts.length ? dist(b, b + 1) : 0);
          var after = dist(a - 1, b) + (b + 1 < pts.length ? dist(a, b + 1) : 0);
          if (after < before - 1) {
            reverse(pts, a, b);
            reverse(items, a, b);
            improved = true;
          }
        }
      }
    }
    plan.stops = items.filter(function (s) { return s; });
    planChanged();

    function reverse(list, i, j) {
      while (i < j) { var t = list[i]; list[i] = list[j]; list[j] = t; i++; j--; }
    }
  }

  // ---- Popup button (buildPopupHtml in the main script calls this)

  window.rovePopupButtonHtml = function (ref) {
    var i = indexOfRef(ref);
    return '<div><button class="rove-popup-btn" data-rove-ref="' + escapeHtml(ref) + '">' +
      (i >= 0 ? 'Remove from Rove (stop ' + (i + 1) + ')' : 'Add to Rove') + '</button></div>';
  };

  // Capture phase: Leaflet stops clicks inside a popup from bubbling.
  document.addEventListener('click', function (e) {
    var remove = e.target.closest ? e.target.closest('.rove-remove-stop') : null;
    if (remove) {
      var index = +remove.getAttribute('data-index');
      map.closePopup();
      if (plan && index >= 0 && index < plan.stops.length) removeStop(index);
      return;
    }
    var button = e.target.closest ? e.target.closest('.rove-popup-btn') : null;
    if (!button) return;
    var ref = button.getAttribute('data-rove-ref');
    var i = indexOfRef(ref);
    if (i >= 0) {
      removeStop(i);
      button.textContent = 'Add to Rove';
    } else {
      var p = parkByRef[ref.toUpperCase()];
      var stop = p ? stopFromPark(p) : (plan && plan.stops.filter(function (s) { return s.reference === ref; })[0]);
      if (!stop) return;
      addStop(stop);
      button.textContent = 'Remove from Rove (stop ' + plan.stops.length + ')';
      openPanel();
    }
  }, true);

  // ---- Drawing on the map: the route line under the park dots, numbered
  // stop markers above them.

  map.createPane('rovePane').style.zIndex = 390;
  // The park dots' canvas (overlayPane, 400) takes every mouse event over
  // it, so the line drawn under it in rovePane can't be grabbed. An
  // invisible, slightly wider copy of each leg sits above it to catch the
  // drag instead; the visible route stays under the dots.
  map.createPane('roveHitPane').style.zIndex = 450;
  var routeLayer = L.layerGroup().addTo(map);
  var stopLayer = L.layerGroup().addTo(map);
  var stopMarkers = [];

  function buildStopPopupHtml(s, index) {
    var html = '<div class="pota-popup" style="min-width:200px;">';
    if (s.reference) {
      html += '<div style="margin-bottom:4px;"><a href="https://pota.app/#/park/' + encodeURIComponent(s.reference) +
        '" target="_blank" rel="noopener">' + escapeHtml(s.reference) + ' - ' + escapeHtml(s.name) + '</a></div>';
      html += '<div>Not in the state loaded on this map.</div>';
      html += window.rovePopupButtonHtml(s.reference);
    } else {
      html += '<div style="font-weight:bold;margin-bottom:4px;">' + escapeHtml(s.name) + '</div>';
      html += '<div style="opacity:0.7;font-size:11px;">' + s.lat.toFixed(5) + ', ' + s.lon.toFixed(5) + ' - drag the marker to move it</div>';
      html += '<div><button class="rove-popup-btn rove-remove-stop" data-index="' + index + '">Remove stop ' + (index + 1) + '</button></div>';
    }
    return html + '</div>';
  }

  function drawStops() {
    stopLayer.clearLayers();
    stopMarkers = [];
    if (!plan) return;
    plan.stops.forEach(function (s, i) {
      // Parks stay where POTA puts them; an address or dropped stop can be
      // dragged somewhere else.
      var movable = !s.reference;
      var marker = L.marker([s.lat, s.lon], {
        icon: L.divIcon({ className: 'rove-stop-icon' + (movable ? ' rove-stop-place' : ''), html: '<div>' + (i + 1) + '</div>', iconSize: [22, 22], iconAnchor: [11, 11] }),
        title: stopLabel(s) + (movable ? ' (drag to move)' : ''),
        draggable: movable,
        zIndexOffset: 1000
      });
      marker.bindPopup(function () {
        var p = s.reference ? parkByRef[s.reference.toUpperCase()] : null;
        return p ? buildPopupHtml(p) : buildStopPopupHtml(s, i);
      });
      if (movable) {
        marker.on('dragend', function () {
          var ll = marker.getLatLng();
          s.lat = +ll.lat.toFixed(6);
          s.lon = +ll.lng.toFixed(6);
          s.name = placeholderName(ll);
          planChanged();
          nameStopFromAddress(s);
        });
      }
      marker.addTo(stopLayer);
      stopMarkers.push(marker);
    });
  }

  function drawRoute() {
    routeLayer.clearLayers();
    for (var i = 0; i < routePoints.length - 1; i++) {
      var leg = legs[i];
      var line;
      var path = leg ? leg.path : [routePoints[i], routePoints[i + 1]];
      if (leg) {
        L.polyline(path, { pane: 'rovePane', color: '#ffffff', weight: 8, opacity: 0.8, interactive: false }).addTo(routeLayer);
        L.polyline(path, { pane: 'rovePane', color: '#00796b', weight: 5, opacity: 0.9, interactive: false }).addTo(routeLayer);
      } else {
        L.polyline(path, { pane: 'rovePane', color: '#00796b', weight: 3, opacity: 0.8, dashArray: '8,8', interactive: false }).addTo(routeLayer);
      }
      line = L.polyline(path, { pane: 'roveHitPane', color: '#00796b', weight: 12, opacity: 0, className: 'rove-route-line' });
      wireRouteDrag(line, i);
      line.bindTooltip('Drag to add a stop here', { sticky: true, opacity: 0.85 });
      line.addTo(routeLayer);
    }
  }

  // ---- Drag the route line to add a stop, Google Maps-style: press on the
  // line, drag to where the route should go, let go - a new stop goes there,
  // between the two stops that leg joins. The map itself mustn't start
  // panning on that press, so its dragging is off while the mouse is over
  // the line.
  var routeDrag = null;
  var routeHover = false;

  function wireRouteDrag(line, legIndex) {
    line.on('mouseover', function () { routeHover = true; map.dragging.disable(); });
    line.on('mouseout', function () {
      routeHover = false;
      if (!routeDrag) map.dragging.enable();
    });
    line.on('mousedown', function (e) {
      if (e.originalEvent.button !== 0) return;
      L.DomEvent.preventDefault(e.originalEvent);
      map.dragging.disable();
      routeDrag = { legIndex: legIndex, start: e.containerPoint, moved: false, latlng: e.latlng, ghost: null, dot: null };
    });
  }

  map.on('mousemove', function (e) {
    if (!routeDrag) return;
    var d = routeDrag;
    if (!d.moved && e.containerPoint.distanceTo(d.start) < 6) return; // a click, not a drag
    d.moved = true;
    d.latlng = e.latlng;
    var a = routePoints[d.legIndex], b = routePoints[d.legIndex + 1];
    if (!d.ghost) {
      d.ghost = L.polyline([a, e.latlng, b], { color: '#00796b', weight: 3, dashArray: '6,6', interactive: false }).addTo(map);
      d.dot = L.circleMarker(e.latlng, { radius: 7, weight: 3, color: '#00796b', fillColor: '#ffffff', fillOpacity: 1, interactive: false }).addTo(map);
      map.eachLayer(function (layer) { if (layer.closeTooltip) layer.closeTooltip(); });
    } else {
      d.ghost.setLatLngs([a, e.latlng, b]);
      d.dot.setLatLng(e.latlng);
    }
  });

  document.addEventListener('mouseup', function () {
    if (!routeDrag) return;
    var d = routeDrag;
    routeDrag = null;
    if (!routeHover) map.dragging.enable();
    if (d.ghost) { map.removeLayer(d.ghost); map.removeLayer(d.dot); }
    if (!d.moved) return;
    // Leg i runs from route point i to i+1; when the route starts at your
    // location, point 0 is you, so stops sit one place further along.
    addPlaceStop(d.latlng, d.legIndex + 1 - (routeFromGps ? 1 : 0));
  });

  function fitToStops() {
    if (!plan || plan.stops.length === 0) return;
    var bounds = L.latLngBounds(plan.stops.map(function (s) { return [s.lat, s.lon]; }));
    programmaticMove = true;
    map.fitBounds(bounds, { padding: [60, 60], maxZoom: 13 });
  }

  // ---- Routing

  function requestRoute() {
    clearTimeout(routeTimer);
    routeTimer = setTimeout(fetchRoute, 300);
  }

  var routeGpsStart = null;

  function fetchRoute() {
    routePoints = [];
    routeFromGps = false;
    routeGpsStart = null;
    if (plan && plan.startAtGps && window.youMarker && plan.stops.length > 0) {
      routeGpsStart = youMarker.getLatLng();
      routePoints.push([routeGpsStart.lat, routeGpsStart.lng]);
      routeFromGps = true;
    }
    if (plan) plan.stops.forEach(function (s) { routePoints.push([s.lat, s.lon]); });
    legs = [];
    for (var i = 0; i < routePoints.length - 1; i++) legs.push(null);

    var seq = ++routeSeq;
    var routeUrl = '/rove/route?';
    if (legs.length === 0) routeState = 'none';
    else if (!roveServer) {
      routeState = 'standalone';
      // Legs baked into this saved copy, keyed like the app's route cache.
      if (embedded) {
        for (var k = 0; k < legs.length; k++) legs[k] = embedded.legs[legKey(routePoints[k], routePoints[k + 1])] || null;
      }
      // Any still missing: ask the app if it's running, else OSRM directly
      // (fillFromOsrm).
      if (legs.some(function (l) { return !l; })) {
        routeState = 'pending';
        routeUrl = appRouteUrl ? appRouteUrl + '&' : null;
      }
    }
    else routeState = 'pending';
    drawRoute();
    renderStops();
    if (routeState !== 'pending') return;

    var bakedLegs = legs.slice();
    if (!routeUrl) { fillFromOsrm(seq); return; }
    var coords = routePoints.map(function (p) { return p[1].toFixed(5) + ',' + p[0].toFixed(5); }).join(';');
    fetch(routeUrl + 'c=' + encodeURIComponent(coords), { cache: 'no-store' }).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.json();
    }).then(function (data) {
      if (seq !== routeSeq) return; // the stops changed while this was on its way
      legs = (data.legs || []).map(function (l, i) { return l || bakedLegs[i] || null; });
      routeState = data.online ? 'done' : 'offline';
      drawRoute();
      renderStops();
    }).catch(function () {
      if (seq !== routeSeq) return;
      if (!roveServer) { fillFromOsrm(seq); return; } // a saved copy whose app isn't running
      routeState = 'offline';
      renderStops();
    });
  }

  // ---- A saved copy with no PAT running asks OSRM itself for the legs it
  // doesn't have (e.g. from where you are now to the first stop) - OSRM
  // allows that (Access-Control-Allow-Origin: *). One leg at a time, so a
  // stop OSRM can't reach only leaves that one leg straight. Same leg format
  // and wording as RoveService.ConvertLeg/DescribeStep, which do this for
  // the live map. Legs found are kept for the rest of the session.
  var OsrmUrl = 'https://router.project-osrm.org/route/v1/driving/';

  function fillFromOsrm(seq) {
    var missing = [];
    legs.forEach(function (l, i) { if (!l) missing.push(i); });
    var gotAny = false, failed = false;
    missing.reduce(function (chain, i) {
      return chain.then(function () {
        if (seq !== routeSeq) return;
        return osrmLeg(routePoints[i], routePoints[i + 1]).then(function (leg) {
          if (seq !== routeSeq) return;
          if (leg) {
            legs[i] = leg;
            gotAny = true;
            if (embedded) embedded.legs[legKey(routePoints[i], routePoints[i + 1])] = leg;
          } else {
            failed = true;
          }
        });
      });
    }, Promise.resolve()).then(function () {
      if (seq !== routeSeq) return;
      routeState = gotAny || !failed ? 'done' : 'offline';
      drawRoute();
      renderStops();
    });
  }

  // a, b: [lat, lon]. Resolves to a leg, or null (no signal / no road route).
  function osrmLeg(a, b) {
    var url = OsrmUrl + a[1].toFixed(5) + ',' + a[0].toFixed(5) + ';' + b[1].toFixed(5) + ',' + b[0].toFixed(5) +
      '?overview=false&steps=true&geometries=geojson';
    return fetch(url).then(function (r) { return r.ok ? r.json() : null; }).then(function (data) {
      if (!data || data.code !== 'Ok' || !data.routes || !data.routes[0] || data.routes[0].legs.length !== 1) return null;
      return convertOsrmLeg(data.routes[0].legs[0]);
    }).catch(function () { return null; });
  }

  function round5(v) { return Math.round(v * 1e5) / 1e5; }

  function convertOsrmLeg(leg) {
    var path = [], steps = [], last = null;
    leg.steps.forEach(function (step) {
      step.geometry.coordinates.forEach(function (c) {
        var lat = round5(c[1]), lon = round5(c[0]);
        if (last && last[0] === lat && last[1] === lon) return;
        last = [lat, lon];
        path.push(last);
      });
      var text = describeOsrmStep(step);
      if (text) steps.push({ text: text, distance: Math.round(step.distance) });
    });
    return { distance: Math.round(leg.distance), duration: Math.round(leg.duration), path: path, steps: steps };
  }

  var CompassNames = ['north', 'northeast', 'east', 'southeast', 'south', 'southwest', 'west', 'northwest'];

  function osrmRoadName(step) {
    var name = step.name || '', ref = (step.ref || '').replace(/;/g, ' / ');
    if (name && ref && name.indexOf(ref) < 0) return name + ' (' + ref + ')';
    return name || ref;
  }

  function ordinal(n) {
    var t = n % 100, u = n % 10;
    return n + (t >= 11 && t <= 13 ? 'th' : u === 1 ? 'st' : u === 2 ? 'nd' : u === 3 ? 'rd' : 'th');
  }

  function describeOsrmStep(step) {
    var m = step.maneuver || {}, type = m.type || '', mod = m.modifier || '';
    var road = osrmRoadName(step), onto = road ? ' onto ' + road : '';
    var turn = mod === 'uturn' ? 'Make a U-turn' : mod === 'straight' ? 'Go straight' : !mod ? 'Turn' : 'Turn ' + mod;
    var side = mod.indexOf('left') >= 0 ? ' on the left' : mod.indexOf('right') >= 0 ? ' on the right' : '';
    switch (type) {
      case 'depart': return 'Head ' + CompassNames[Math.round((m.bearing_after || 0) / 45) % 8] + (road ? ' on ' + road : '');
      case 'arrive': return 'Arrive' + (mod === 'left' || mod === 'right' ? ' (on the ' + mod + ')' : '');
      case 'turn':
        if (mod === 'straight') return road ? 'Continue straight onto ' + road : null;
        return turn + onto;
      case 'end of road': return turn + ' at the end of the road' + onto;
      case 'new name':
      case 'continue':
        if (!mod || mod === 'straight') return road ? 'Continue onto ' + road : null;
        return (mod === 'uturn' ? 'Make a U-turn' : 'Continue ' + mod) + onto;
      case 'merge': return 'Merge' + (mod && mod !== 'straight' ? ' ' + mod : '') + onto;
      case 'on ramp': return 'Take the ramp' + side + onto;
      case 'off ramp': return 'Take the exit' + side + onto;
      case 'fork': return 'Keep ' + (mod.indexOf('left') >= 0 ? 'left' : mod.indexOf('right') >= 0 ? 'right' : 'straight') + ' at the fork' + onto;
      case 'roundabout':
      case 'rotary':
        return 'At the ' + (type === 'rotary' ? 'traffic circle' : 'roundabout') + (m.exit ? ', take the ' + ordinal(m.exit) + ' exit' : '') + onto;
      case 'roundabout turn': return 'At the roundabout, ' + turn.toLowerCase() + onto;
      case 'exit roundabout':
      case 'exit rotary':
      case 'notification':
        return null;
      default: return road ? 'Continue onto ' + road : null;
    }
  }

  // "lon,lat;lon,lat" rounded to 5 decimals with trailing zeros dropped -
  // the same key RoveService uses for its route cache (FormatPoint).
  function legKey(a, b) {
    function n(v) { return String(Math.round(v * 1e5) / 1e5); }
    return n(a[1]) + ',' + n(a[0]) + ';' + n(b[1]) + ',' + n(b[0]);
  }

  // While starting from your location: route again once a fix first
  // arrives, and again after you've moved a couple of km from where the
  // current route starts (not on every fix - each new start point is a
  // new request to the routing server).
  setInterval(function () {
    if (!plan || !plan.startAtGps || plan.stops.length === 0) return;
    var here = window.youMarker ? youMarker.getLatLng() : null;
    if (!here) return;
    if (!routeFromGps || (routeGpsStart && here.distanceTo(routeGpsStart) > 2000)) requestRoute();
  }, 5000);

  // ---- The panel

  function renderAll() {
    planNameEl.value = plan ? plan.name : '';
    planNameEl.disabled = !plan;
    startGpsEl.checked = !!(plan && plan.startAtGps);
    renderPlanSelect();
    drawStops();
    renderStops();
  }

  function legStraightMeters(i) {
    return L.latLng(routePoints[i]).distanceTo(routePoints[i + 1]);
  }

  function renderSummary() {
    if (!plan || plan.stops.length === 0) {
      summaryEl.textContent = plan ? 'No stops yet - add parks above.' : '';
      return;
    }
    var stops = plan.stops.length + (plan.stops.length === 1 ? ' stop' : ' stops');
    if (legs.length === 0) { summaryEl.textContent = stops; return; }
    var meters = 0, seconds = 0, straight = 0;
    legs.forEach(function (leg, i) {
      if (leg) { meters += leg.distance; seconds += leg.duration; }
      else { meters += legStraightMeters(i); straight++; }
    });
    var text = stops + ' · ' + formatMiles(meters);
    if (straight === 0) text += ' · ' + formatDuration(seconds) + ' driving';
    summaryEl.textContent = text;

    var extra = '';
    if (routeState === 'pending') extra = 'Finding the driving route...';
    else if (routeState === 'standalone' && straight > 0) extra = straight + (straight === 1 ? ' leg is' : ' legs are') + ' a straight line - this saved copy only has the road routes looked up before it was saved.';
    else if (straight > 0 && routeState === 'offline') extra = straight + (straight === 1 ? ' leg is' : ' legs are') + ' a straight line - no signal to look up the road route, and it has not been saved from before.';
    else if (straight > 0) extra = straight + (straight === 1 ? ' leg is' : ' legs are') + ' a straight line - no road route found (a boat-access park?).';
    if (extra) {
      var note = document.createElement('div');
      note.className = 'sidebar-note';
      note.style.fontWeight = 'normal';
      note.textContent = extra;
      summaryEl.appendChild(note);
    }
  }

  function addLegRow(legIndex, prefix, arriveLabel) {
    var leg = legs[legIndex];
    var row = document.createElement('li');
    row.className = 'rove-leg';
    if (leg) {
      row.textContent = prefix + formatMiles(leg.distance) + ' · ' + formatDuration(leg.duration) + (openLegs[legIndex] ? ' ▾ hide directions' : ' ▸ directions');
      row.title = 'Show or hide turn-by-turn directions';
      row.addEventListener('click', function () {
        openLegs[legIndex] = !openLegs[legIndex];
        renderStops();
      });
    } else if (routeState === 'pending') {
      row.textContent = prefix + '...';
    } else {
      row.textContent = prefix + formatMiles(legStraightMeters(legIndex)) + ' straight line';
      row.style.cursor = 'default';
    }
    stopsEl.appendChild(row);

    if (leg && openLegs[legIndex]) {
      var list = document.createElement('ol');
      list.className = 'rove-steps';
      leg.steps.forEach(function (step) {
        var item = document.createElement('li');
        var text = step.text;
        if (text.indexOf('Arrive') === 0 && arriveLabel) text = text.replace('Arrive', 'Arrive at ' + arriveLabel);
        item.textContent = text + ' ';
        if (step.distance > 0) {
          var d = document.createElement('span');
          d.textContent = formatDistance(step.distance);
          item.appendChild(d);
        }
        list.appendChild(item);
      });
      var holder = document.createElement('li');
      holder.style.listStyle = 'none';
      holder.appendChild(list);
      stopsEl.appendChild(holder);
    }
  }

  var dragIndex = -1;

  function renderStops() {
    renderSummary();
    stopsEl.innerHTML = '';
    if (!plan) return;
    var offset = routeFromGps ? 1 : 0;
    if (routeFromGps && legs.length > 0) addLegRow(0, 'From your location: ', stopLabel(plan.stops[0]));

    plan.stops.forEach(function (s, i) {
      var li = document.createElement('li');
      li.className = 'rove-stop';
      li.draggable = true;
      li.dataset.index = i;
      li.title = 'Drag to reorder';

      var num = document.createElement('span');
      num.className = 'rove-num';
      num.textContent = i + 1;

      var name = document.createElement('span');
      name.className = 'rove-name';
      if (s.reference) {
        var ref = document.createElement('b');
        ref.textContent = s.reference;
        name.appendChild(ref);
        name.appendChild(document.createTextNode(' ' + s.name));
      } else {
        name.textContent = s.name;
      }
      name.title = 'Show on the map';
      name.addEventListener('click', function () {
        programmaticMove = true;
        map.setView([s.lat, s.lon], Math.max(map.getZoom(), 13));
        if (stopMarkers[i]) stopMarkers[i].openPopup();
      });

      var remove = document.createElement('button');
      remove.textContent = '✕';
      remove.title = 'Remove from the plan';
      remove.addEventListener('click', function () { removeStop(i); });

      li.appendChild(num);
      li.appendChild(name);
      li.appendChild(remove);
      stopsEl.appendChild(li);

      if (i < plan.stops.length - 1 && legs.length > i + offset) {
        addLegRow(i + offset, '↓ ', stopLabel(plan.stops[i + 1]));
      }
    });
  }

  // Drag and drop to reorder: dropping on a stop puts the dragged one just
  // before it; dropping below the last stop moves it to the end.
  stopsEl.addEventListener('dragstart', function (e) {
    var li = e.target.closest && e.target.closest('.rove-stop');
    if (!li) return;
    dragIndex = +li.dataset.index;
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', String(dragIndex));
  });
  stopsEl.addEventListener('dragover', function (e) {
    if (dragIndex < 0) return;
    e.preventDefault();
    clearDropMarks();
    var li = dropTargetStop(e.target);
    if (li) li.classList.add('drop-before');
  });
  stopsEl.addEventListener('dragleave', clearDropMarks);
  stopsEl.addEventListener('drop', function (e) {
    if (dragIndex < 0) return;
    e.preventDefault();
    clearDropMarks();
    var li = dropTargetStop(e.target);
    var to = li ? +li.dataset.index : plan.stops.length;
    var from = dragIndex;
    dragIndex = -1;
    moveStop(from, to);
  });
  stopsEl.addEventListener('dragend', function () { dragIndex = -1; clearDropMarks(); });
  // The stop a drop lands before: the stop row itself, or for a leg or
  // directions row between two stops, the stop after it.
  function dropTargetStop(target) {
    var row = target.closest ? target.closest('#roveStops > li') : null;
    while (row && !row.classList.contains('rove-stop')) row = row.nextElementSibling;
    return row;
  }
  function clearDropMarks() {
    Array.prototype.forEach.call(stopsEl.querySelectorAll('.drop-before'), function (el) { el.classList.remove('drop-before'); });
  }

  function openPanel() {
    panelEl.hidden = false;
    toggleEl.hidden = true;
    storeSet('panelOpen', '1');
  }

  toggleEl.addEventListener('click', openPanel);
  document.getElementById('roveClose').addEventListener('click', function () {
    panelEl.hidden = true;
    toggleEl.hidden = false;
    storeSet('panelOpen', null);
  });

  planSelectEl.addEventListener('change', function () {
    var value = planSelectEl.value;
    if (value === '__new') createPlan();
    else if (value && value !== planId) loadPlan(value, true);
  });

  planNameEl.addEventListener('change', function () {
    if (!plan) return;
    plan.name = planNameEl.value.trim() || todayName();
    planNameEl.value = plan.name;
    renderPlanSelect();
    scheduleSave();
  });

  document.getElementById('roveNew').addEventListener('click', function () { createPlan(); });

  confirmButton(document.getElementById('roveDelete'), 'Delete? Click again', function () {
    if (!plan) return;
    var id = planId;
    planList = planList.filter(function (p) { return p.id !== id; });
    setPlan(null, null);
    if (roveServer) {
      api('DELETE', '/rove/plans/' + id).catch(function () { showNote("Couldn't delete the plan - is the app still running?"); });
    }
  });

  confirmButton(document.getElementById('roveClear'), 'Remove all? Click again', function () {
    if (!plan || plan.stops.length === 0) return;
    plan.stops = [];
    planChanged();
  });

  document.getElementById('roveOrder').addEventListener('click', orderByDistance);

  startGpsEl.addEventListener('change', function () {
    ensurePlan();
    plan.startAtGps = startGpsEl.checked;
    if (plan.startAtGps && !window.youMarker) {
      showNote('No location yet - the route starts at stop 1 until there is one. Pick a GPS source in the app.');
    }
    scheduleSave();
    requestRoute();
  });

  document.getElementById('roveAddBtn').addEventListener('click', addFromInput);
  addInputEl.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { e.preventDefault(); addFromInput(); }
  });
  // Suggestions while typing a park name.
  addInputEl.addEventListener('input', function () {
    var text = addInputEl.value.trim();
    RefPattern.lastIndex = 0;
    if (text.length < 3 || RefPattern.test(text)) { addResultsEl.innerHTML = ''; return; }
    showResults(findParksByName(text), text);
  });

  // ---- Rove plans saved on pota.app (signed in with POTA Login in the
  // app's menu bar - see PotaAccountService). A loaded plan becomes a plan
  // here like any other, so it works with no signal afterwards; loading the
  // same pota.app plan again updates that copy instead of adding another.

  function potaError(r) {
    return r.json().catch(function () { return {}; }).then(function (body) {
      if (body.error === 'signin') throw new Error("Sign in to pota.app first: click POTA Login in the app's menu bar, then try again.");
      if (body.error === 'noaccount') throw new Error("This login isn't linked to a POTA account. In the app, click your POTA name in the menu bar > Sign Out, then POTA Login and sign in the same way you do on pota.app (e.g. email and password rather than Google).");
      if (body.error === 'offline') throw new Error("Couldn't reach pota.app - no signal?");
      throw new Error("Couldn't load from pota.app.");
    });
  }

  function potaGet(path) {
    return fetch(path, { cache: 'no-store' }).then(function (r) { return r.ok ? r.json() : potaError(r); });
  }

  potaLoadEl.addEventListener('click', function () {
    if (!roveServer) { showNote('Loading from pota.app needs the app - use Show Map in the app. Plans already loaded from pota.app are in the Plan list.'); return; }
    potaLoadEl.disabled = true;
    showNote('Getting your rove plans from pota.app...', true);
    potaGet('/rove/pota/plans').then(function (list) {
      potaSelectEl.innerHTML = '';
      var first = document.createElement('option');
      first.value = '';
      first.textContent = list.length ? '(pick a pota.app plan)' : '(no rove plans on pota.app)';
      potaSelectEl.appendChild(first);
      list.forEach(function (p) {
        var option = document.createElement('option');
        option.value = p.id;
        option.textContent = p.name + ' (' + p.stopCount + (p.stopCount === 1 ? ' stop)' : ' stops)');
        potaSelectEl.appendChild(option);
      });
      potaSelectEl.hidden = false;
      showNote(list.length ? 'Pick a plan from the pota.app list to load it.' : '');
      if (list.length) potaSelectEl.focus();
    }).catch(function (e) {
      showNote(e.message, true);
    }).then(function () { potaLoadEl.disabled = false; });
  });

  potaSelectEl.addEventListener('change', function () {
    var roveId = +potaSelectEl.value;
    if (!roveId) return;
    showNote('Loading from pota.app...', true);
    potaGet('/rove/pota/plans/' + roveId).then(function (loaded) {
      if (!loaded.stops.length) { showNote('That pota.app plan has no parks in it yet.'); return; }
      var existing = planList.filter(function (p) { return p.potaRoveId === roveId; })[0];
      var keepGps = existing && plan && existing.id === planId && plan.startAtGps;
      setPlan(existing ? existing.id : newPlanId(), {
        name: loaded.name, startAtGps: !!keepGps, potaRoveId: roveId, stops: loaded.stops
      });
      savePlanNow();
      fitToStops();
      showNote((existing ? 'Updated ' : 'Loaded ') + '"' + loaded.name + '" from pota.app (' + loaded.stops.length +
        (loaded.stops.length === 1 ? ' stop).' : ' stops).'));
    }).catch(function (e) {
      showNote(e.message, true);
    }).then(function () { potaSelectEl.value = ''; });
  });

  // ---- Print Directions: the whole trip on a printable page in a new tab
  // - an outline drawing of the route, the stops, then each leg's
  // turn-by-turn directions. Its toolbar picks With directions or Stops
  // only (remembered for next time), then opens the browser's print window,
  // where "Save as PDF" makes a PDF of it.

  // ---- The printout's map: a picture of the street map (or satellite,
  // when that's the layer showing) covering the whole route, with the route
  // and numbered stops drawn on it. Built from map tiles fetched for it,
  // not a capture of the screen, so it always shows the whole trip whatever
  // the map is zoomed to. Both tile servers allow this (they send
  // Access-Control-Allow-Origin: *). Resolves to a PNG data URL, or null
  // with no signal - the printout then falls back to buildRouteSvg.
  var PrintMapWidth = 1080, PrintMapHeight = 640, PrintMapPad = 36;

  function routeLines() {
    var lines = [];
    for (var i = 0; i < routePoints.length - 1; i++) {
      lines.push({ path: legs[i] ? legs[i].path : [routePoints[i], routePoints[i + 1]], road: !!legs[i] });
    }
    return lines;
  }

  function renderPrintMap() {
    var satellite = currentBaseLayer === satelliteLayer;
    var tileLayer = satellite ? satelliteLayer : streetLayer;
    var attribution = satellite || /arcgisonline/.test(tileLayer._url) // Esri street map in a Save Map copy
      ? (satellite ? 'Imagery © Esri' : 'Map © Esri')
      : '© OpenStreetMap contributors';
    var W = PrintMapWidth, H = PrintMapHeight;

    var lines = routeLines();
    var all = [];
    lines.forEach(function (l) { all = all.concat(l.path); });
    plan.stops.forEach(function (s) { all.push([s.lat, s.lon]); });
    var bounds = L.latLngBounds(all);

    // Closest zoom where the whole trip fits.
    var zoom = 16;
    for (; zoom > 3; zoom--) {
      var size = map.project(bounds.getSouthEast(), zoom).subtract(map.project(bounds.getNorthWest(), zoom));
      if (size.x <= W - 2 * PrintMapPad && size.y <= H - 2 * PrintMapPad) break;
    }
    var center = map.project(bounds.getCenter(), zoom);
    var origin = center.subtract([W / 2, H / 2]).round();
    function px(latlng) { return map.project(L.latLng(latlng), zoom).subtract(origin); }

    var canvas = document.createElement('canvas');
    canvas.width = W;
    canvas.height = H;
    var ctx = canvas.getContext('2d');
    ctx.fillStyle = '#e8e8e8';
    ctx.fillRect(0, 0, W, H);

    var tiles = [];
    var max = Math.pow(2, zoom);
    for (var tx = Math.floor(origin.x / 256); tx <= Math.floor((origin.x + W - 1) / 256); tx++) {
      for (var ty = Math.floor(origin.y / 256); ty <= Math.floor((origin.y + H - 1) / 256); ty++) {
        if (ty < 0 || ty >= max) continue;
        tiles.push({ x: tx, y: ty });
      }
    }

    var loaded = 0;
    var jobs = tiles.map(function (t) {
      return new Promise(function (resolve) {
        var img = new Image();
        img.crossOrigin = 'anonymous';
        var timer = setTimeout(function () { img.src = ''; resolve(); }, 10000);
        img.onload = function () {
          clearTimeout(timer);
          ctx.drawImage(img, t.x * 256 - origin.x, t.y * 256 - origin.y);
          loaded++;
          resolve();
        };
        img.onerror = function () { clearTimeout(timer); resolve(); };
        img.src = L.Util.template(tileLayer._url, L.extend({}, tileLayer.options, {
          s: 'abc'.charAt(Math.abs(t.x + t.y) % 3), x: ((t.x % max) + max) % max, y: t.y, z: zoom, r: ''
        }));
      });
    });

    return Promise.all(jobs).then(function () {
      if (loaded === 0) return null; // no signal - use the outline drawing instead

      // The route: a white edge under the line so it shows on any map.
      ctx.lineJoin = 'round';
      ctx.lineCap = 'round';
      lines.forEach(function (l) {
        [['rgba(255,255,255,0.9)', 8], ['#00796b', 5]].forEach(function (style) {
          ctx.beginPath();
          l.path.forEach(function (p, k) { var c = px(p); if (k === 0) ctx.moveTo(c.x, c.y); else ctx.lineTo(c.x, c.y); });
          ctx.strokeStyle = style[0];
          ctx.lineWidth = style[1];
          ctx.setLineDash(l.road ? [] : [10, 8]);
          ctx.stroke();
        });
      });
      ctx.setLineDash([]);

      if (routeFromGps) {
        var g = px(routePoints[0]);
        ctx.beginPath();
        ctx.arc(g.x, g.y, 7, 0, Math.PI * 2);
        ctx.fillStyle = '#1a73e8';
        ctx.fill();
        ctx.lineWidth = 2.5;
        ctx.strokeStyle = '#fff';
        ctx.stroke();
      }

      plan.stops.forEach(function (s, i) {
        var c = px([s.lat, s.lon]);
        ctx.beginPath();
        ctx.arc(c.x, c.y, 12, 0, Math.PI * 2);
        ctx.fillStyle = s.reference ? '#00796b' : '#5c6bc0';
        ctx.fill();
        ctx.lineWidth = 2.5;
        ctx.strokeStyle = '#fff';
        ctx.stroke();
        ctx.fillStyle = '#fff';
        ctx.font = 'bold 13px Segoe UI, Arial, sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(String(i + 1), c.x, c.y + 0.5);
      });

      ctx.font = '11px Segoe UI, Arial, sans-serif';
      var tw = ctx.measureText(attribution).width;
      ctx.fillStyle = 'rgba(255,255,255,0.8)';
      ctx.fillRect(W - tw - 10, H - 18, tw + 10, 18);
      ctx.fillStyle = '#333';
      ctx.textAlign = 'right';
      ctx.textBaseline = 'middle';
      ctx.fillText(attribution, W - 5, H - 9);

      try { return canvas.toDataURL('image/png'); } catch (e) { return null; }
    });
  }

  // The route as a plain SVG outline, for when the map picture can't be
  // made (no signal for the tiles).
  function buildRouteSvg() {
    var W = 720, H = 420, pad = 28;
    var lines = routeLines();
    var pts = [];
    lines.forEach(function (l) { pts = pts.concat(l.path); });
    plan.stops.forEach(function (s) { pts.push([s.lat, s.lon]); });
    function mercY(lat) { return Math.log(Math.tan(Math.PI / 4 + lat * Math.PI / 360)); }
    var minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    pts.forEach(function (p) {
      var x = p[1] * Math.PI / 180, y = mercY(p[0]);
      minX = Math.min(minX, x); maxX = Math.max(maxX, x);
      minY = Math.min(minY, y); maxY = Math.max(maxY, y);
    });
    var scale = Math.min((W - 2 * pad) / Math.max(maxX - minX, 1e-6), (H - 2 * pad) / Math.max(maxY - minY, 1e-6));
    var offX = (W - (maxX - minX) * scale) / 2, offY = (H - (maxY - minY) * scale) / 2;
    function xy(p) {
      return [(p[1] * Math.PI / 180 - minX) * scale + offX, (maxY - mercY(p[0])) * scale + offY];
    }
    function pt(p) { var c = xy(p); return c[0].toFixed(1) + ',' + c[1].toFixed(1); }

    var svg = '<svg viewBox="0 0 ' + W + ' ' + H + '" xmlns="http://www.w3.org/2000/svg">';
    lines.forEach(function (l) {
      var step = Math.max(1, Math.floor(l.path.length / 400)); // plenty of detail at this size
      var d = [];
      for (var k = 0; k < l.path.length; k += step) d.push(pt(l.path[k]));
      d.push(pt(l.path[l.path.length - 1]));
      svg += '<polyline points="' + d.join(' ') + '" fill="none" stroke="#00796b" stroke-width="3" stroke-linejoin="round"' +
        (l.road ? '' : ' stroke-dasharray="7,6"') + '/>';
    });
    if (routeFromGps) {
      var g = xy(routePoints[0]);
      svg += '<circle cx="' + g[0].toFixed(1) + '" cy="' + g[1].toFixed(1) + '" r="6" fill="#1a73e8" stroke="#fff" stroke-width="2"/>';
    }
    plan.stops.forEach(function (s, i) {
      var c = xy([s.lat, s.lon]);
      svg += '<circle cx="' + c[0].toFixed(1) + '" cy="' + c[1].toFixed(1) + '" r="10" fill="' + (s.reference ? '#00796b' : '#5c6bc0') +
        '" stroke="#fff" stroke-width="2"/><text x="' + c[0].toFixed(1) + '" y="' + (c[1] + 4).toFixed(1) +
        '" font-size="11" font-weight="bold" text-anchor="middle" fill="#fff" font-family="Segoe UI, Arial">' + (i + 1) + '</text>';
    });
    return svg + '</svg>';
  }

  function buildDirectionsHtml(mapImage) {
    var e = escapeHtml;
    var offset = routeFromGps ? 1 : 0;
    var meters = 0, seconds = 0, straight = 0;
    legs.forEach(function (leg, i) {
      if (leg) { meters += leg.distance; seconds += leg.duration; } else { meters += legStraightMeters(i); straight++; }
    });

    var html = '<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>' + e(plan.name) + ' - Directions</title><style>' +
      'body{font:11pt "Segoe UI",Arial,sans-serif;margin:24px;color:#111;background:#fff}' +
      'h1{font-size:18pt;margin:0 0 2px}.sub{color:#444;margin-bottom:12px}' +
      'svg,.map{display:block;width:100%;max-width:7.5in;height:auto;margin:0 auto 10px;border:1px solid #bbb;border-radius:4px}' +
      '.mapnote{text-align:center;color:#666;font-size:9pt;margin:-6px 0 10px}' +
      'table{border-collapse:collapse;width:100%;margin:6px 0 14px}' +
      'th,td{border:1px solid #bbb;padding:3px 6px;text-align:left;font-size:10pt;vertical-align:top}th{background:#eef5f4}' +
      'h2{font-size:12.5pt;margin:16px 0 4px;padding-bottom:2px;border-bottom:2px solid #00796b;break-after:avoid}' +
      '.leginfo{color:#333;margin:2px 0 4px}ol{margin:2px 0 0;padding-left:26px}li{margin:2px 0;break-inside:avoid}.d{color:#666}' +
      '.note{color:#666;font-size:9pt;margin-top:18px}' +
      '.toolbar{position:sticky;top:0;background:#f3f3f3;margin:-24px -24px 16px;padding:10px 24px;border-bottom:1px solid #ccc;font-size:10pt}' +
      '.toolbar button{font:inherit;font-weight:bold;padding:5px 14px;margin-left:14px;margin-right:10px;cursor:pointer}' +
      '.toolbar label{margin-right:12px;cursor:pointer}body.stops-only .dirs{display:none}' +
      '@media print{.toolbar{display:none}body{margin:0}}' +
      '</style></head><body>' +
      '<div class="toolbar">' +
      '<label><input type="radio" name="mode" value="dirs" id="modeDirs"> With directions</label>' +
      '<label><input type="radio" name="mode" value="stops" id="modeStops"> Stops only</label>' +
      '<button id="printBtn">Print or Save as PDF</button>To make a PDF, pick &quot;Save as PDF&quot; as the printer.</div>';

    html += '<h1>' + e(plan.name) + '</h1><div class="sub">' + plan.stops.length + ' stops &middot; ' + formatMiles(meters) +
      (straight === 0 ? ' &middot; about ' + formatDuration(seconds) + ' driving' : '') +
      ' &middot; printed ' + e(new Date().toLocaleString()) + '</div>';
    html += mapImage
      ? '<img class="map" src="' + mapImage + '" alt="Route map">'
      : buildRouteSvg() + '<div class="mapnote">Street map not available (no signal?) - showing the route outline only.</div>';

    html += '<table><tr><th>#</th><th>Stop</th><th>Grid</th><th>Lat, Long</th><th>To the next stop</th></tr>';
    plan.stops.forEach(function (s, i) {
      var leg = legs[i + offset];
      var next = i === plan.stops.length - 1 ? 'end'
        : leg ? formatMiles(leg.distance) + ', ' + formatDuration(leg.duration)
        : formatMiles(legStraightMeters(i + offset)) + ' straight line';
      html += '<tr><td>' + (i + 1) + '</td><td>' + (s.reference ? '<b>' + e(s.reference) + '</b> ' : '') + e(s.name) + '</td>' +
        '<td>' + e(toGridSquare(s.lat, s.lon)) + '</td><td>' + s.lat.toFixed(5) + ', ' + s.lon.toFixed(5) + '</td><td>' + e(next) + '</td></tr>';
    });
    html += '</table>';

    html += '<div class="dirs">';
    for (var k = 0; k < legs.length; k++) {
      var from = k < offset ? 'Your location' : (k - offset + 1) + '. ' + stopLabel(plan.stops[k - offset]);
      var toStop = plan.stops[k + 1 - offset];
      var leg = legs[k];
      html += '<h2>' + e(from) + ' &rarr; ' + e((k + 2 - offset) + '. ' + stopLabel(toStop)) + '</h2>';
      if (!leg) {
        html += '<div class="leginfo">No road directions for this leg (no signal when the route was looked up, or no road route found) - ' +
          formatMiles(legStraightMeters(k)) + ' in a straight line.</div>';
        continue;
      }
      html += '<div class="leginfo">' + formatMiles(leg.distance) + ' &middot; about ' + formatDuration(leg.duration) + '</div><ol>';
      leg.steps.forEach(function (step) {
        var text = step.text.indexOf('Arrive') === 0 ? step.text.replace('Arrive', 'Arrive at ' + stopLabel(toStop)) : step.text;
        html += '<li>' + e(text) + (step.distance > 0 ? ' <span class="d">' + formatDistance(step.distance) + '</span>' : '') + '</li>';
      });
      html += '</ol>';
    }
    html += '</div>';

    html += '<div class="note">Directions are a guide only - check road signs and conditions. ' +
      'Routes by OSRM using OpenStreetMap data &copy; OpenStreetMap contributors.</div>';
    return html + '</body></html>';
  }

  document.getElementById('rovePrint').addEventListener('click', function () {
    if (!plan || plan.stops.length < 2) { showNote('Add at least two stops first.'); return; }
    if (routeState === 'pending') { showNote('Still finding the driving route - try again in a moment.'); return; }
    // Opened straight away (a tab opened later, after the map picture is
    // ready, would be stopped by the pop-up blocker), filled in once it is.
    var w = window.open('', '_blank');
    if (!w) { showNote('The browser blocked the new tab - allow pop-ups for this page and try again.'); return; }
    w.document.write('<!DOCTYPE html><title>Preparing directions...</title><p style="font:12pt Segoe UI,Arial,sans-serif;margin:24px">Preparing the map and directions...</p>');
    w.document.close();
    renderPrintMap().catch(function () { return null; }).then(function (mapImage) {
      if (w.closed) return;
      w.document.open();
      w.document.write(buildDirectionsHtml(mapImage));
      w.document.close();
      wirePrintPage(w);
    });
  });

  function wirePrintPage(w) {
    var d = w.document;
    function setMode(stopsOnly) {
      d.body.classList.toggle('stops-only', stopsOnly);
      d.title = plan.name + (stopsOnly ? ' - Stops' : ' - Directions'); // also the PDF's default file name
      d.getElementById(stopsOnly ? 'modeStops' : 'modeDirs').checked = true;
    }
    setMode(storeGet('printMode') === 'stops');
    d.getElementById('modeDirs').addEventListener('change', function () { setMode(false); storeSet('printMode', null); });
    d.getElementById('modeStops').addEventListener('change', function () { setMode(true); storeSet('printMode', 'stops'); });
    d.getElementById('printBtn').addEventListener('click', function () { w.print(); });
    w.focus();
  }

  // ---- Start up: reopen the panel and plan from last time

  // Layers panel > Save a copy of this map: a download from the app's map
  // server (/save-map), told which plan is open so the copy opens on it.
  var saveMapSection = document.getElementById('saveMapSection');
  var saveMapLink = document.getElementById('saveMapLink');
  if (roveServer && saveMapSection && saveMapLink) {
    saveMapSection.hidden = false;
    saveMapLink.addEventListener('click', function () {
      saveMapLink.href = '/save-map?plan=' + encodeURIComponent(plan && planId ? planId : '');
    });
  }

  if (storeGet('panelOpen')) openPanel();
  if (!roveServer) {
    potaLoadEl.disabled = true;
    potaLoadEl.title = 'Needs the app - use Show Map in the app';
    showNote(embedded
      ? 'This is a saved copy of the map, with the plans from when it was saved. Changes here are not saved back to the app.'
      : 'This is a saved copy of the map - plans are not saved here. Use Show Map in the app to save and load plans.', true);
  }
  renderAll();
  refreshPlanList().then(function () {
    var lastId = embedded ? embedded.selected : storeGet('planId');
    if (lastId && planList.some(function (p) { return p.id === lastId; })) {
      loadPlan(lastId, !!embedded);
      if (embedded) openPanel();
    }
  });
})();
