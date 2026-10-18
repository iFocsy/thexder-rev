using System.Collections.Concurrent;

namespace Thexder.Core.Assets
{
    /// <summary>The three art libraries; each holds the same resources at a different scale.</summary>
    internal enum ArtResolution
    {
        /// <summary>THEX640.DLL: 8-pixel cells.</summary>
        Low = 0,

        /// <summary>THEX800.DLL: 10-pixel cells ("Medium").</summary>
        Medium = 1,

        /// <summary>THEX1024.DLL: 12-pixel cells ("High").</summary>
        High = 2,
    }

    /// <summary>One of THEX640/800/1024.DLL opened for reading, with a cache of decoded bitmaps.</summary>
    internal sealed class ResourceLibrary : IDisposable
    {
        private readonly PeResourceFile file;
        private readonly ConcurrentDictionary<string, IndexedImage> cache = new(StringComparer.OrdinalIgnoreCase);

        private ResourceLibrary(ArtResolution resolution, PeResourceFile file)
        {
            Resolution = resolution;
            this.file = file;
            CellSize = resolution switch
            {
                ArtResolution.Low => 8,
                ArtResolution.Medium => 10,
                _ => 12,
            };
        }

        public ArtResolution Resolution { get; }

        /// <summary>Size of one map cell in pixels at this resolution.</summary>
        public int CellSize { get; }

        /// <summary>Pixels per 640-scale pixel: 1, 1.25 or 1.5.</summary>
        public double Scale => CellSize / 8.0;

        public static string FileName(ArtResolution resolution) => resolution switch
        {
            ArtResolution.Low => "THEX640.DLL",
            ArtResolution.Medium => "THEX800.DLL",
            _ => "THEX1024.DLL",
        };

        public static ResourceLibrary Open(string dataDirectory, ArtResolution resolution) =>
            new(resolution, PeResourceFile.Open(Path.Combine(dataDirectory, FileName(resolution))));

        public static bool Exists(string dataDirectory, ArtResolution resolution) =>
            File.Exists(Path.Combine(dataDirectory, FileName(resolution)));

        public bool HasBitmap(string name) => file.Contains(PeResourceFile.RtBitmap, name);

        public bool HasBitmap(int id) => file.Contains(PeResourceFile.RtBitmap, id);

        public IndexedImage Bitmap(string name) =>
            cache.GetOrAdd(name, n => IndexedImage.FromDib(file.Read(PeResourceFile.RtBitmap, n)));

        public IndexedImage Bitmap(int id) =>
            cache.GetOrAdd("#" + id, _ => IndexedImage.FromDib(file.Read(PeResourceFile.RtBitmap, id)));

        public IEnumerable<string> BitmapNames => file.Names(PeResourceFile.RtBitmap);

        public Dictionary<int, string> Strings() => file.ReadStringTable();

        /// <summary>Drops decoded bitmaps (backgrounds are large; a level only needs its own).</summary>
        public void Evict(int id) => cache.TryRemove("#" + id, out _);

        public void Dispose() => file.Dispose();
    }
}
