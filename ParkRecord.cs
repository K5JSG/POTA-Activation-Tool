namespace PotaActivationTool
{
    public class ParkRecord
    {
        public string Reference { get; set; } = "";
        public string Name { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Grid { get; set; } = "";
        public double? ElevationFeet { get; set; }
        public string County { get; set; } = "";
        public string Fers { get; set; } = "";
        public string Kff { get; set; } = "";

        // Comma-joined SOTA summit reference(s) whose published coordinate
        // falls within this park's real boundary polygon - see
        // FerLookupService.ComputeSotaMatches. Same "candidate list, not a
        // guarantee" caveat as Fers: this only means the two programs' areas
        // overlap on a map, not that a SOTA activation from here is valid -
        // that still requires being within the summit's own Activation Zone.
        public string Sota { get; set; } = "";
        public string State { get; set; } = "";
        public bool Completed { get; set; }

        // User-set (checkbox in the grid, not derived from any data source -
        // see Form1's _boatAccessOnlyRefs/BoatAccessOnlyFileName): true for a
        // park you've noted is only reachable by boat. Persists across
        // reloads by Reference, independent of which state's list it's in.
        public bool BoatAccessOnly { get; set; }
        public bool OutOfState { get; set; }
        public bool MultiState { get; set; }
        public bool Exclude { get; set; }

        // True for a national trail shown in this state's list because its real
        // route crosses the state, even though POTA's own single reported
        // coordinate for it (Latitude/Longitude/County/State below) is
        // elsewhere. Deliberately separate from OutOfState - see
        // FerLookupService.ComputeTrailFers and buttonLoadAdif_Click's
        // OutOfState cleanup, which must not remove these rows.
        public bool AnchorOutOfState { get; set; }
    }
}