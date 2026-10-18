using System.Text;

namespace Thexder.Utils
{
    /// <summary>Reads files out of an ISO 9660 CD image (2048-byte sectors).</summary>
    internal sealed class IsoImage : IDisposable
    {
        private const int SectorSize = 2048;
        private readonly FileStream stream;
        private readonly (long Lba, long Size) root;

        public IsoImage(string path)
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sector = new byte[SectorSize];
            for (long lba = 16; ; lba++)
            {
                ReadSector(lba, sector);
                if (Encoding.ASCII.GetString(sector, 1, 5) != "CD001" || sector[0] == 255)
                {
                    throw new InvalidDataException("Not an ISO 9660 image.");
                }
                if (sector[0] == 1)
                {
                    root = (BitConverter.ToUInt32(sector, 156 + 2), BitConverter.ToUInt32(sector, 156 + 10));
                    break;
                }
            }
        }

        /// <summary>Where a file lies in the image, or null when it is not there.</summary>
        public (long Lba, long Size)? Find(string path)
        {
            var dir = root;
            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var entry = FindEntry(dir, parts[i], wantDirectory: i < parts.Length - 1);
                if (entry == null)
                {
                    return null;
                }
                dir = entry.Value;
            }
            return dir;
        }

        public void CopyTo((long Lba, long Size) file, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write);
            stream.Position = file.Lba * SectorSize;
            var buffer = new byte[1 << 20];
            for (long left = file.Size; left > 0;)
            {
                int n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (n <= 0)
                {
                    throw new EndOfStreamException("The image is truncated.");
                }
                output.Write(buffer, 0, n);
                left -= n;
            }
        }

        public void Dispose() => stream.Dispose();

        private (long Lba, long Size)? FindEntry((long Lba, long Size) dir, string name, bool wantDirectory)
        {
            var sector = new byte[SectorSize];
            for (long offset = 0; offset < dir.Size; offset += SectorSize)
            {
                ReadSector(dir.Lba + offset / SectorSize, sector);
                for (int p = 0; p < SectorSize && sector[p] != 0; p += sector[p])
                {
                    int nameLength = sector[p + 32];
                    string entryName = Encoding.ASCII.GetString(sector, p + 33, nameLength);
                    int version = entryName.IndexOf(';');
                    if (version >= 0)
                    {
                        entryName = entryName[..version];
                    }
                    bool isDirectory = (sector[p + 25] & 2) != 0;
                    if (isDirectory == wantDirectory && entryName.TrimEnd('.').Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return (BitConverter.ToUInt32(sector, p + 2), BitConverter.ToUInt32(sector, p + 10));
                    }
                }
            }
            return null;
        }

        private void ReadSector(long lba, byte[] buffer)
        {
            stream.Position = lba * SectorSize;
            stream.ReadExactly(buffer, 0, SectorSize);
        }
    }
}
