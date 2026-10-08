using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PotaActivationTool
{
    // The GPS dropdown's choice in Form1, remembered between runs: Off, the
    // COM port a GPS receiver is on (its own fix is used directly - see
    // GpsService), or Windows / Browser Location (the map page's own browser
    // Geolocation API, which on Windows comes from the Windows location
    // service - on a PC without GPS hardware that's Wi-Fi/cell-tower lookups
    // and can be miles off, hence the COM port option).
    public class GpsSettings
    {
        public string Port { get; set; } = "";

        public bool UseWindowsLocation { get; set; }

        // A COM-port receiver is selected (what GpsService reads).
        [JsonIgnore]
        public bool Enabled => Port.Length > 0 && !UseWindowsLocation;

        private const string FileName = "GpsSettings.json";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static GpsSettings Load(string folder)
        {
            try
            {
                string path = Path.Combine(folder, FileName);
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<GpsSettings>(File.ReadAllText(path), JsonOptions) ?? new GpsSettings();
            }
            catch
            {
                // A corrupt/unreadable settings file just falls back to defaults.
            }
            return new GpsSettings();
        }

        public void Save(string folder)
        {
            try
            {
                File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, JsonOptions));
            }
            catch
            {
                // Best-effort - losing the saved choice isn't worth an error dialog.
            }
        }
    }

    // Reads NMEA 0183 sentences (GGA/RMC) from a GPS receiver on a serial
    // port on a background thread, keeping the latest fix for the map's
    // local server to hand out (see MapServer's /gps endpoint).
    // Survives the receiver being unplugged and plugged back in - common
    // with USB pucks in the field - by just retrying the port every few
    // seconds until it opens again.
    public sealed class GpsService : IDisposable
    {
        // Tried in this order when no fixed rate is given. 4800 is the NMEA 0183
        // standard (and what most USB pucks like the BU-353 use); 9600 is
        // what most modern u-blox-based receivers default to.
        public static readonly int[] AutoBaudRates = { 4800, 9600, 38400, 115200, 19200, 57600 };

        // How long to listen at one baud rate for a valid, checksummed
        // sentence before auto-detect moves on to the next rate.
        private static readonly TimeSpan AutoBaudListenTime = TimeSpan.FromSeconds(3);

        // A fix older than this is reported as lost rather than current.
        private static readonly TimeSpan FixStaleAfter = TimeSpan.FromSeconds(10);

        // Rough conversion from HDOP to an expected horizontal error in
        // meters - HDOP times a typical consumer-receiver range error.
        // Only used for the accuracy circle/readout, never for any
        // in-park decision.
        private const double MetersPerHdop = 5.0;

        private readonly object _lock = new();
        private Thread? _thread;
        private volatile bool _stopRequested;
        private SerialPort? _port;

        private string _portName = "";
        private string _status = "";
        private int _activeBaud;
        private double _lat, _lon, _accuracy;
        private int _satellites;
        private DateTime _fixTimeUtc = DateTime.MinValue;
        private long _fixSequence;
        private double _lastHdop = double.NaN;

        public void Start(string portName, int baudRate)
        {
            Stop();
            _stopRequested = false;
            lock (_lock)
            {
                _portName = portName;
                _status = "Opening " + portName + "...";
                _activeBaud = 0;
                _fixTimeUtc = DateTime.MinValue;
                _lastHdop = double.NaN;
            }
            _thread = new Thread(() => Run(portName, baudRate)) { IsBackground = true, Name = "GPS serial reader" };
            _thread.Start();
        }

        public void Stop()
        {
            _stopRequested = true;
            try { _port?.Close(); } catch { /* unblocks a pending ReadLine */ }
            _thread?.Join(3000);
            _thread = null;
            lock (_lock)
            {
                _status = "";
                _fixTimeUtc = DateTime.MinValue;
            }
        }

        public void Dispose() => Stop();

        // The current position if the receiver has a fresh fix, else null.
        public (double Lat, double Lon)? GetCurrentFix()
        {
            lock (_lock)
            {
                bool hasFix = _fixTimeUtc != DateTime.MinValue && DateTime.UtcNow - _fixTimeUtc < FixStaleAfter;
                return hasFix ? (_lat, _lon) : null;
            }
        }

        // Snapshot for the map's /gps endpoint.
        public object GetStatus()
        {
            lock (_lock)
            {
                bool hasFix = _fixTimeUtc != DateTime.MinValue && DateTime.UtcNow - _fixTimeUtc < FixStaleAfter;
                return new
                {
                    port = _portName,
                    baud = _activeBaud,
                    status = hasFix ? "" : _status,
                    fix = hasFix
                        ? new
                        {
                            seq = _fixSequence,
                            lat = _lat,
                            lon = _lon,
                            accuracy = _accuracy,
                            satellites = _satellites,
                            ageMs = (long)(DateTime.UtcNow - _fixTimeUtc).TotalMilliseconds
                        }
                        : null
                };
            }
        }

        private void SetStatus(string status)
        {
            lock (_lock) _status = status;
        }

        private void Run(string portName, int baudRate)
        {
            int[] rates = baudRate > 0 ? new[] { baudRate } : AutoBaudRates;
            int rateIndex = 0;

            while (!_stopRequested)
            {
                int baud = rates[rateIndex];
                bool gotValidSentence = false;
                try
                {
                    using var port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
                    {
                        Encoding = Encoding.ASCII,
                        NewLine = "\n",
                        ReadTimeout = 1000,
                        // Deliberately left de-asserted: on a ham station
                        // PC, other COM ports are often radio CAT/keying
                        // interfaces that use DTR or RTS for PTT or CW, so
                        // picking the wrong port from the menu must never
                        // key a transmitter. GPS receivers don't need them.
                        DtrEnable = false,
                        RtsEnable = false
                    };
                    port.Open();
                    _port = port;
                    SetStatus(rates.Length > 1
                        ? $"Listening on {portName} at {baud} baud..."
                        : $"Waiting for data on {portName}...");

                    DateTime lastValidUtc = DateTime.UtcNow;
                    while (!_stopRequested)
                    {
                        string line;
                        try
                        {
                            line = port.ReadLine();
                        }
                        catch (TimeoutException)
                        {
                            line = "";
                        }

                        if (TryHandleSentence(line.Trim()))
                        {
                            if (!gotValidSentence)
                            {
                                gotValidSentence = true;
                                lock (_lock)
                                {
                                    _activeBaud = baud;
                                    if (_fixTimeUtc == DateTime.MinValue) _status = $"Connected to {portName} ({baud} baud), waiting for a satellite fix...";
                                }
                            }
                            lastValidUtc = DateTime.UtcNow;
                        }
                        else if (DateTime.UtcNow - lastValidUtc > AutoBaudListenTime)
                        {
                            // Nothing usable at this rate. In auto mode, move
                            // on to the next rate; with a fixed rate, reopen
                            // and keep waiting (a receiver can take a while
                            // to start talking after power-up).
                            if (!gotValidSentence) break;
                            SetStatus($"No data from {portName} - check the receiver.");
                            gotValidSentence = false;
                            lastValidUtc = DateTime.UtcNow;
                        }
                    }
                }
                // Deliberately catches everything: an unhandled exception on
                // this background thread would take the whole app down, and
                // closing the port from Stop() mid-ReadLine can surface as
                // several different exception types.
                catch (Exception ex)
                {
                    if (_stopRequested) break;
                    SetStatus(ex is UnauthorizedAccessException
                        ? $"{portName} is in use by another program."
                        : $"{portName} not available - is the GPS plugged in?");
                    _port = null;
                    SleepUnlessStopped(3000);
                    continue;
                }
                finally
                {
                    _port = null;
                }

                if (!gotValidSentence)
                {
                    rateIndex = (rateIndex + 1) % rates.Length;
                    if (rateIndex == 0) SetStatus($"No GPS data recognized on {portName} - check the port and baud rate.");
                }
            }
        }

        private void SleepUnlessStopped(int milliseconds)
        {
            for (int waited = 0; waited < milliseconds && !_stopRequested; waited += 100)
                Thread.Sleep(100);
        }

        // Returns true for any well-formed, correctly checksummed NMEA
        // sentence (whether or not it carries a position) - that's what
        // baud-rate auto-detection keys off.
        private bool TryHandleSentence(string sentence)
        {
            if (!TryValidateChecksum(sentence, out string body)) return false;

            string[] f = body.Split(',');
            if (f[0].Length < 5) return true;
            string type = f[0].Substring(f[0].Length - 3);

            if (type == "GGA" && f.Length > 8)
            {
                // Fix quality 0 = no fix.
                if (!int.TryParse(f[6], out int quality) || quality == 0) return true;
                if (!TryParseCoordinate(f[2], f[3], out double lat) || !TryParseCoordinate(f[4], f[5], out double lon)) return true;
                int.TryParse(f[7], out int sats);
                double hdop = double.TryParse(f[8], NumberStyles.Float, CultureInfo.InvariantCulture, out double h) ? h : double.NaN;
                lock (_lock) _lastHdop = hdop;
                RecordFix(lat, lon, hdop, sats);
            }
            else if (type == "RMC" && f.Length > 6)
            {
                // Status A = valid, V = warning/no fix. RMC is only used for
                // receivers that don't send GGA - when both arrive, GGA's
                // HDOP/satellite count make it the better source.
                if (f[2] != "A") return true;
                if (!double.IsNaN(_lastHdop)) return true;
                if (!TryParseCoordinate(f[3], f[4], out double lat) || !TryParseCoordinate(f[5], f[6], out double lon)) return true;
                RecordFix(lat, lon, double.NaN, 0);
            }
            return true;
        }

        private void RecordFix(double lat, double lon, double hdop, int satellites)
        {
            lock (_lock)
            {
                _lat = lat;
                _lon = lon;
                _accuracy = double.IsNaN(hdop) || hdop <= 0 ? 10 : hdop * MetersPerHdop;
                _satellites = satellites;
                _fixTimeUtc = DateTime.UtcNow;
                _fixSequence++;
                _status = "";
            }
        }

        private static bool TryValidateChecksum(string sentence, out string body)
        {
            body = "";
            if (sentence.Length < 6 || sentence[0] != '$') return false;
            int star = sentence.LastIndexOf('*');
            if (star < 0 || star + 3 > sentence.Length) return false;

            body = sentence.Substring(1, star - 1);
            int checksum = 0;
            foreach (char c in body) checksum ^= c;
            return int.TryParse(sentence.AsSpan(star + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int expected)
                && expected == checksum;
        }

        // NMEA coordinates are (d)ddmm.mmmm plus a hemisphere letter.
        private static bool TryParseCoordinate(string value, string hemisphere, out double degrees)
        {
            degrees = 0;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw)) return false;
            double whole = Math.Floor(raw / 100);
            degrees = whole + (raw - whole * 100) / 60;
            if (hemisphere == "S" || hemisphere == "W") degrees = -degrees;
            else if (hemisphere != "N" && hemisphere != "E") return false;
            return true;
        }
    }
}
