namespace Thexder.Feature.Procedural
{
    /// <summary>How big a generated map is.</summary>
    internal enum MapSize
    {
        Small,
        Standard,
        Large,
        Huge,
        Giant,
    }

    internal static class MapSizes
    {
        public static readonly MapSize[] All = { MapSize.Small, MapSize.Standard, MapSize.Large, MapSize.Huge, MapSize.Giant };

        /// <summary>Width and height in cells.</summary>
        public static (int Width, int Height) Of(MapSize size) => size switch
        {
            MapSize.Small => (200, 68),
            MapSize.Large => (520, 102),
            MapSize.Huge => (960, 136),
            MapSize.Giant => (1600, 204),
            _ => (320, 68),
        };

        public static string Name(MapSize size) => size switch
        {
            MapSize.Small => "Small",
            MapSize.Large => "Large",
            MapSize.Huge => "Huge",
            MapSize.Giant => "Giant",
            _ => "Standard",
        };

        public static string Describe(MapSize size)
        {
            var (w, h) = Of(size);
            double screens = w * h / (60.0 * 34.0);
            return $"{w} × {h} cells, about {screens:0} screens";
        }
    }

    /// <summary>One level to generate.</summary>
    internal sealed record GenRequest(ulong Seed, int World, int Level, MapSize Size, int Difficulty, int Stage = 1, string Title = "")
    {
        public bool Boss => Level == 5;
    }
}
