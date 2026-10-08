using System.Reflection;

namespace PotaActivationTool
{
    // The program's logo (source artwork: Logo.png in the project folder),
    // built into the exe: the multi-size logo.ico for every window's title
    // bar/taskbar icon, and a 256px PNG for the logo shown in the main
    // window. logo.ico is also the exe's own file icon (ApplicationIcon in
    // the .csproj) and the installer's (SetupIconFile in the .iss).
    internal static class AppLogo
    {
        private static readonly Lazy<Icon?> LazyIcon = new(() =>
        {
            using var stream = Open("AppLogo/logo.ico");
            return stream == null ? null : new Icon(stream);
        });

        private static readonly Lazy<Image?> LazyImage = new(() =>
        {
            using var stream = Open("AppLogo/logo-256.png");
            if (stream == null) return null;
            // Copied out of the stream: a GDI+ Image must keep its source
            // stream open for its whole life otherwise.
            using var loaded = Image.FromStream(stream);
            return new Bitmap(loaded);
        });

        public static Icon? Icon => LazyIcon.Value;
        public static Image? Image => LazyImage.Value;

        private static Stream? Open(string name) =>
            Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
    }
}
