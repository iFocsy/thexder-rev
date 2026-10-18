using System.Security.Cryptography;
using Thexder.Utils;

namespace Thexder.Services
{
    /// <summary>
    /// The game files in assets\: checked against the list of files the game needs (size and SHA-256) and restored from
    /// the CD image in iso\ when some are missing or damaged.
    /// </summary>
    internal static class AssetStore
    {
        public sealed record Entry(string Path, long Size, string Sha256);

        public static IReadOnlyList<Entry> Manifest { get; } = LoadManifest();

        /// <summary>The files that are missing or do not match the list.</summary>
        public static List<Entry> FindDamaged(string directory) =>
            Manifest.AsParallel().Where(e => !IsIntact(directory, e)).OrderBy(e => e.Path).ToList();

        public static string? FindImage(string isoDirectory) =>
            Directory.Exists(isoDirectory)
                ? Directory.EnumerateFiles(isoDirectory, "*.iso").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                : null;

        /// <summary>Copies the files from the image; returns those that could not be restored.</summary>
        public static List<Entry> Restore(string image, string directory, IReadOnlyList<Entry> files, IProgress<double>? progress = null)
        {
            var failed = new List<Entry>();
            long total = Math.Max(1, files.Sum(f => f.Size)), done = 0;
            using var iso = new IsoImage(image);
            foreach (var file in files)
            {
                string target = Target(directory, file);
                var location = iso.Find(file.Path);
                if (location == null || location.Value.Size != file.Size)
                {
                    failed.Add(file);
                }
                else
                {
                    iso.CopyTo(location.Value, target);
                    if (!IsIntact(directory, file))
                    {
                        failed.Add(file);
                        File.Delete(target);
                    }
                }
                done += file.Size;
                progress?.Report(done / (double)total);
            }
            return failed;
        }

        private static bool IsIntact(string directory, Entry entry)
        {
            var info = new FileInfo(Target(directory, entry));
            if (!info.Exists || info.Length != entry.Size)
            {
                return false;
            }
            using var stream = info.OpenRead();
            return Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);
        }

        private static string Target(string directory, Entry entry) =>
            Path.Combine(directory, entry.Path.Replace('/', Path.DirectorySeparatorChar));

        private static List<Entry> LoadManifest()
        {
            using var stream = typeof(AssetStore).Assembly.GetManifestResourceStream("Thexder.assets.txt")
                ?? throw new InvalidOperationException("The asset list is missing from the build.");
            using var reader = new StreamReader(stream);
            var list = new List<Entry>();
            while (reader.ReadLine() is { } line)
            {
                string[] parts = line.Split(' ', 3);
                if (parts.Length == 3)
                {
                    list.Add(new Entry(parts[2], long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[0]));
                }
            }
            return list;
        }
    }
}
