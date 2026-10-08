// GPS receiver read straight from Chrome (Web Serial), for a saved copy of
// the map opened from a file with no PAT behind it. The live map doesn't
// need this - the app reads the receiver itself (GpsService) and serves the
// fixes at /gps.
//
// Inlined into the map page after its main script (MapService.BuildMapHtml).
// pollGps there takes a fresh fix from window.directGps ahead of PAT's GPS
// and Windows location. Tested 2026-10-08 on K5JSG's receiver (COM16, 4800
// baud): Chrome offers Web Serial to a page opened from a file.
(function () {
  if (location.protocol === 'http:' || !('serial' in navigator)) return;

  // Same speeds, same order as PAT (GpsService.AutoBaudRates).
  var BaudRates = [4800, 9600, 38400, 115200, 19200, 57600];
  var FreshMs = 5000;

  var latest = null; // { seq, lat, lon, accuracy, time }
  var seq = 0;
  var connected = false;
  var connecting = false;

  window.directGps = {
    // The newest fix if it's recent enough to trust, else null.
    latest: function () { return latest && Date.now() - latest.time < FreshMs ? latest : null; },
    connected: function () { return connected; }
  };

  var button = document.createElement('button');
  button.className = 'direct-gps-button';
  button.textContent = 'Connect GPS receiver';
  button.title = 'Read a GPS receiver plugged into this PC (a COM port) directly - pick its port in the list Chrome shows';
  document.body.appendChild(button);

  function showButton(text) {
    button.textContent = text || 'Connect GPS receiver';
    button.hidden = false;
  }

  function checksumOk(line) {
    var m = line.match(/^\$([^*]+)\*([0-9A-F]{2})\s*$/i);
    if (!m) return false;
    var sum = 0;
    for (var i = 0; i < m[1].length; i++) sum ^= m[1].charCodeAt(i);
    return sum === parseInt(m[2], 16);
  }

  function toDegrees(value, hemi) {
    var dot = value.indexOf('.');
    if (dot < 3) return NaN;
    var d = parseFloat(value.slice(0, dot - 2)) + parseFloat(value.slice(dot - 2)) / 60;
    return (hemi === 'S' || hemi === 'W') ? -d : d;
  }

  // GGA carries the fix quality and HDOP; the accuracy circle uses HDOP x 5 m,
  // a rough rule of thumb for a consumer receiver.
  function handleSentence(line) {
    var f = line.split('*')[0].split(',');
    if (f[0].slice(3) !== 'GGA') return;
    if (parseInt(f[6] || '0', 10) === 0) return; // no fix yet
    var lat = toDegrees(f[2], f[3]), lon = toDegrees(f[4], f[5]);
    if (isNaN(lat) || isNaN(lon)) return;
    var hdop = parseFloat(f[8]);
    latest = { seq: ++seq, lat: lat, lon: lon, accuracy: isNaN(hdop) ? 10 : Math.max(3, hdop * 5), time: Date.now() };
  }

  // Reads one baud rate. Resolves true once a checksummed NMEA sentence has
  // arrived (and keeps reading until the port goes away), false if nothing
  // valid arrived within ms.
  async function listen(port, baud, ms) {
    await port.open({ baudRate: baud });
    // DTR/RTS off - some receivers misbehave with them on (as in GpsService).
    try { await port.setSignals({ dataTerminalReady: false, requestToSend: false }); } catch (e) { /* not supported - fine */ }
    var reader = port.readable.getReader();
    var decoder = new TextDecoder('ascii'), buffer = '', found = false;
    var timer = setTimeout(function () { if (!found) reader.cancel(); }, ms);
    try {
      while (true) {
        var chunk = await reader.read();
        if (chunk.done) break;
        buffer += decoder.decode(chunk.value, { stream: true });
        var lines = buffer.split(/\r?\n/);
        buffer = lines.pop();
        for (var i = 0; i < lines.length; i++) {
          if (!checksumOk(lines[i])) continue;
          if (!found) {
            found = true;
            clearTimeout(timer);
            connected = true;
            button.hidden = true;
          }
          handleSentence(lines[i]);
        }
      }
    } catch (e) {
      // cancelled (wrong speed) or the receiver was unplugged
    } finally {
      try { reader.releaseLock(); } catch (e) { /* already released */ }
      try { await port.close(); } catch (e) { /* already closed */ }
    }
    return found;
  }

  async function connect(port) {
    if (connecting) return;
    connecting = true;
    button.hidden = true;
    try {
      for (var i = 0; i < BaudRates.length; i++) {
        if (await listen(port, BaudRates[i], 2500)) {
          // listen only returns once reading stops: unplugged or closed.
          connected = false;
          showButton('GPS receiver disconnected - reconnect');
          return;
        }
      }
      showButton('No GPS data on that port - try another');
    } catch (e) {
      // Usually the port is already open in another program - e.g. PAT,
      // whose GPS this map then uses instead (see pollGps).
      connected = false;
      showButton('GPS port busy (is PAT using it?) - retry');
    } finally {
      connecting = false;
    }
  }

  button.addEventListener('click', async function () {
    try {
      var ports = await navigator.serial.getPorts();
      var port = ports.length ? ports[0] : await navigator.serial.requestPort();
      connect(port);
    } catch (e) {
      showButton(); // closed the port chooser without picking one
    }
  });

  // A port allowed before is opened again without asking, if Chrome kept the
  // permission for this file.
  navigator.serial.getPorts().then(function (ports) {
    if (ports.length) connect(ports[0]);
  });
  navigator.serial.addEventListener('connect', function (e) {
    if (!connected) connect(e.target);
  });
})();
