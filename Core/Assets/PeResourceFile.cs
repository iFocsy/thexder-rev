using System.Buffers.Binary;
using System.Text;

namespace Thexder.Core.Assets
{
    /// <summary>
    /// Reads resources out of a Win32 PE file (THEXDER.EXE or the THEX640/800/1024.DLL art libraries) without loading it
    /// as a module.
    /// </summary>
    internal sealed class PeResourceFile : IDisposable
    {
        public const int RtBitmap = 2;
        public const int RtString = 6;

        private readonly FileStream stream;
        private readonly List<Section> sections = new();

        // Type -> (name or id) -> (file offset, size); the first language entry is used.
        private readonly Dictionary<ResourceKey, Dictionary<ResourceKey, (long Offset, int Size)>> directory = new();

        private PeResourceFile(string path)
        {
            Path = path;
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        }

        public string Path { get; }

        /// <summary>Preferred load address.</summary>
        public uint ImageBase { get; private set; }

        /// <summary>Bytes past a section's raw data read as zero.</summary>
        public byte[] ReadVirtual(uint address, int count)
        {
            uint rva = address - ImageBase;
            foreach (var section in sections)
            {
                if (rva >= section.VirtualAddress && rva + count <= section.VirtualAddress + section.Size)
                {
                    byte[] data = new byte[count];
                    long available = Math.Max(0, Math.Min(count, (long)section.RawSize - (rva - section.VirtualAddress)));
                    if (available > 0)
                    {
                        byte[] raw = ReadAt(section.RawOffset + (rva - section.VirtualAddress), (int)available);
                        raw.CopyTo(data, 0);
                    }
                    return data;
                }
            }
            throw new InvalidDataException($"Address 0x{address:x} is not inside one section of {System.IO.Path.GetFileName(Path)}.");
        }

        public static PeResourceFile Open(string path)
        {
            var file = new PeResourceFile(path);
            try
            {
                file.ParseHeaders();
                return file;
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        public bool Contains(int type, string name) =>
            directory.TryGetValue(ResourceKey.Of(type), out var entries) && entries.ContainsKey(ResourceKey.Of(name));

        public bool Contains(int type, int id) =>
            directory.TryGetValue(ResourceKey.Of(type), out var entries) && entries.ContainsKey(ResourceKey.Of(id));

        public IEnumerable<string> Names(int type) =>
            directory.TryGetValue(ResourceKey.Of(type), out var entries)
                ? entries.Keys.Where(k => k.Name != null).Select(k => k.Name!)
                : Enumerable.Empty<string>();

        public IEnumerable<int> Ids(int type) =>
            directory.TryGetValue(ResourceKey.Of(type), out var entries)
                ? entries.Keys.Where(k => k.Name == null).Select(k => k.Id)
                : Enumerable.Empty<int>();

        public byte[] Read(int type, string name) => Read(type, ResourceKey.Of(name));

        public byte[] Read(int type, int id) => Read(type, ResourceKey.Of(id));

        /// <summary>Returns the strings of RT_STRING resources, keyed by string id.</summary>
        public Dictionary<int, string> ReadStringTable()
        {
            var result = new Dictionary<int, string>();
            foreach (int block in Ids(RtString))
            {
                byte[] data = Read(RtString, block);
                int pos = 0;
                for (int i = 0; i < 16 && pos + 2 <= data.Length; i++)
                {
                    int length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
                    pos += 2;
                    if (length > 0)
                    {
                        result[(block - 1) * 16 + i] = Encoding.Unicode.GetString(data, pos, length * 2);
                    }
                    pos += length * 2;
                }
            }
            return result;
        }

        public void Dispose() => stream.Dispose();

        private byte[] Read(int type, ResourceKey key)
        {
            if (!directory.TryGetValue(ResourceKey.Of(type), out var entries) || !entries.TryGetValue(key, out var location))
            {
                throw new KeyNotFoundException($"Resource {key} (type {type}) not found in {System.IO.Path.GetFileName(Path)}.");
            }
            byte[] data = new byte[location.Size];
            lock (stream)
            {
                stream.Position = location.Offset;
                stream.ReadExactly(data);
            }
            return data;
        }

        private void ParseHeaders()
        {
            byte[] dos = ReadAt(0, 64);
            if (dos[0] != 'M' || dos[1] != 'Z')
            {
                throw new InvalidDataException($"{Path} is not a Windows executable.");
            }
            int peOffset = BinaryPrimitives.ReadInt32LittleEndian(dos.AsSpan(0x3c));
            byte[] fileHeader = ReadAt(peOffset, 24);
            if (BinaryPrimitives.ReadUInt32LittleEndian(fileHeader) != 0x00004550)
            {
                throw new InvalidDataException($"{Path} has no PE header.");
            }
            int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader.AsSpan(6));
            int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader.AsSpan(20));
            byte[] optional = ReadAt(peOffset + 24, optionalSize);
            bool pe32Plus = BinaryPrimitives.ReadUInt16LittleEndian(optional) == 0x20b;
            ImageBase = pe32Plus ? (uint)BinaryPrimitives.ReadUInt64LittleEndian(optional.AsSpan(24)) : BinaryPrimitives.ReadUInt32LittleEndian(optional.AsSpan(28));
            int dataDirectories = pe32Plus ? 112 : 96;
            uint resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(optional.AsSpan(dataDirectories + 2 * 8));

            byte[] table = ReadAt(peOffset + 24 + optionalSize, sectionCount * 40);
            for (int i = 0; i < sectionCount; i++)
            {
                var span = table.AsSpan(i * 40);
                sections.Add(new Section(
                    BinaryPrimitives.ReadUInt32LittleEndian(span[12..]),
                    Math.Max(BinaryPrimitives.ReadUInt32LittleEndian(span[8..]), BinaryPrimitives.ReadUInt32LittleEndian(span[16..])),
                    BinaryPrimitives.ReadUInt32LittleEndian(span[20..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(span[16..])));
            }
            if (resourceRva == 0)
            {
                return;
            }

            long root = RvaToOffset(resourceRva);
            foreach (var (typeKey, typeOffset, typeIsDir) in ReadDirectory(root, root))
            {
                if (!typeIsDir)
                {
                    continue;
                }
                var entries = new Dictionary<ResourceKey, (long, int)>();
                foreach (var (nameKey, nameOffset, nameIsDir) in ReadDirectory(root, root + typeOffset))
                {
                    long leaf = root + nameOffset;
                    if (nameIsDir)
                    {
                        var languages = ReadDirectory(root, leaf);
                        if (languages.Count == 0)
                        {
                            continue;
                        }
                        leaf = root + languages[0].Offset;
                    }
                    byte[] dataEntry = ReadAt(leaf, 16);
                    uint rva = BinaryPrimitives.ReadUInt32LittleEndian(dataEntry);
                    int size = BinaryPrimitives.ReadInt32LittleEndian(dataEntry.AsSpan(4));
                    entries[nameKey] = (RvaToOffset(rva), size);
                }
                directory[typeKey] = entries;
            }
        }

        private List<(ResourceKey Key, uint Offset, bool IsDirectory)> ReadDirectory(long root, long offset)
        {
            byte[] header = ReadAt(offset, 16);
            int named = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12));
            int ids = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14));
            byte[] entries = ReadAt(offset + 16, (named + ids) * 8);
            var result = new List<(ResourceKey, uint, bool)>(named + ids);
            for (int i = 0; i < named + ids; i++)
            {
                uint nameField = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(i * 8));
                uint dataField = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(i * 8 + 4));
                ResourceKey key;
                if ((nameField & 0x80000000) != 0)
                {
                    long nameAt = root + (nameField & 0x7fffffff);
                    int length = BinaryPrimitives.ReadUInt16LittleEndian(ReadAt(nameAt, 2));
                    key = ResourceKey.Of(Encoding.Unicode.GetString(ReadAt(nameAt + 2, length * 2)));
                }
                else
                {
                    key = ResourceKey.Of((int)nameField);
                }
                result.Add((key, dataField & 0x7fffffff, (dataField & 0x80000000) != 0));
            }
            return result;
        }

        private long RvaToOffset(uint rva)
        {
            foreach (var section in sections)
            {
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.Size)
                {
                    return section.RawOffset + (rva - section.VirtualAddress);
                }
            }
            throw new InvalidDataException($"RVA 0x{rva:x} is outside every section of {Path}.");
        }

        private byte[] ReadAt(long offset, int count)
        {
            byte[] buffer = new byte[count];
            lock (stream)
            {
                stream.Position = offset;
                stream.ReadExactly(buffer);
            }
            return buffer;
        }

        private readonly record struct Section(uint VirtualAddress, uint Size, uint RawOffset, uint RawSize);

        // Resource names are matched case-insensitively, as FindResource does.
        private readonly record struct ResourceKey(string? Name, int Id)
        {
            public static ResourceKey Of(string name) => new(name.ToUpperInvariant(), 0);

            public static ResourceKey Of(int id) => new(null, id);

            public override string ToString() => Name ?? $"#{Id}";
        }
    }
}
