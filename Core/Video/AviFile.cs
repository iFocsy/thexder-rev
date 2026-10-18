using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Thexder.Core.Video
{
    /// <summary>Audio stream format taken from the stream's WAVEFORMATEX ('strf').</summary>
    internal sealed class AviAudioFormat
    {
        public ushort FormatTag { get; init; }
        public ushort Channels { get; init; }
        public uint SamplesPerSecond { get; init; }
        public uint AverageBytesPerSecond { get; init; }
        public ushort BlockAlign { get; init; }
        public ushort BitsPerSample { get; init; }

        /// <summary>Codec-specific bytes following the 18-byte WAVEFORMATEX (cbSize bytes), usually empty for PCM.</summary>
        public byte[] Extra { get; init; } = Array.Empty<byte>();

        public bool IsPcm => FormatTag == 1;
    }

    /// <summary>
    /// Minimal RIFF AVI 1.0 reader: parses the header list, builds an index of the first video stream's frames and the
    /// first audio stream's chunks (from idx1 when present, otherwise by scanning the movi list(s), including LIST 'rec '
    /// groups), and reads chunk payloads on demand.
    /// </summary>
    internal sealed class AviFile : IDisposable
    {
        private const uint FccRiff = 0x46464952;
        private const uint FccList = 0x5453494C;
        private const uint FccAvi  = 0x20495641; // "AVI "
        private const uint FccAvix = 0x58495641;
        private const uint FccHdrl = 0x6C726468;
        private const uint FccAvih = 0x68697661;
        private const uint FccStrl = 0x6C727473;
        private const uint FccStrh = 0x68727473;
        private const uint FccStrf = 0x66727473;
        private const uint FccMovi = 0x69766F6D;
        private const uint FccIdx1 = 0x31786469;
        private const uint FccVids = 0x73646976;
        private const uint FccAuds = 0x73647561;

        private const uint IndexFlagList     = 0x01;
        private const uint IndexFlagKeyFrame = 0x10;

        private readonly struct ChunkRef
        {
            public ChunkRef(long dataOffset, int size, bool keyFrame)
            {
                DataOffset = dataOffset;
                Size = size;
                KeyFrame = keyFrame;
            }

            public long DataOffset { get; }
            public int Size { get; }
            public bool KeyFrame { get; }
        }

        private sealed class StreamInfo
        {
            public uint Type;
            public uint Handler;
            public uint Scale;
            public uint Rate;
            public uint Start;
            public uint Length;
            public byte[] Format = Array.Empty<byte>();
        }

        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        private readonly object _sync = new();
        private readonly long _fileLength;
        private readonly List<StreamInfo> _streams = new();
        private readonly List<(long Start, long End)> _moviLists = new();
        private readonly List<ChunkRef> _videoFrames = new();
        private readonly List<ChunkRef> _audioChunks = new();
        private long[] _audioChunkStarts = Array.Empty<long>();
        private readonly int _videoStream = -1;
        private readonly int _audioStream = -1;
        private bool _disposed;

        public AviFile(string path)
            : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024), leaveOpen: false)
        {
        }

        public AviFile(Stream stream, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanSeek || !stream.CanRead)
                throw new ArgumentException("The AVI stream must be readable and seekable.", nameof(stream));

            _stream = stream;
            _leaveOpen = leaveOpen;
            _fileLength = stream.Length;

            try
            {
                long idx1Offset = -1;
                int idx1Size = 0;
                ParseTopLevel(ref idx1Offset, ref idx1Size);

                for (int i = 0; i < _streams.Count; i++)
                {
                    if (_videoStream < 0 && _streams[i].Type == FccVids)
                        _videoStream = i;
                    else if (_audioStream < 0 && _streams[i].Type == FccAuds)
                        _audioStream = i;
                }

                if (_videoStream < 0)
                    throw new InvalidDataException("AVI file has no video stream.");

                StreamInfo video = _streams[_videoStream];
                if (video.Format.Length < 40)
                    throw new InvalidDataException("AVI video stream has no BITMAPINFOHEADER.");

                BitmapInfoHeader = video.Format;
                Width = BinaryPrimitives.ReadInt32LittleEndian(video.Format.AsSpan(4));
                int height = BinaryPrimitives.ReadInt32LittleEndian(video.Format.AsSpan(8));
                Height = Math.Abs(height);
                uint compression = BinaryPrimitives.ReadUInt32LittleEndian(video.Format.AsSpan(16));
                FourCC = FourCCToString(compression != 0 ? compression : video.Handler);
                VideoRate = video.Rate != 0 ? video.Rate : 15;
                VideoScale = video.Rate != 0 && video.Scale != 0 ? video.Scale : 1;
                VideoStart = video.Start;

                if (_audioStream >= 0)
                {
                    StreamInfo audio = _streams[_audioStream];
                    AudioFormat = ParseWaveFormat(audio.Format);
                    AudioStart = audio.Start;
                }

                if (idx1Offset < 0 || !TryLoadIdx1(idx1Offset, idx1Size))
                {
                    _videoFrames.Clear();
                    _audioChunks.Clear();
                    foreach ((long start, long end) in _moviLists)
                        ScanList(start, end, depth: 0);
                }

                _audioChunkStarts = new long[_audioChunks.Count];
                long total = 0;
                for (int i = 0; i < _audioChunks.Count; i++)
                {
                    _audioChunkStarts[i] = total;
                    total += _audioChunks[i].Size;
                }

                AudioLength = total;
            }
            catch
            {
                if (!leaveOpen)
                    stream.Dispose();
                throw;
            }
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Video codec FourCC, e.g. "IV32".</summary>
        public string FourCC { get; }

        /// <summary>Raw BITMAPINFOHEADER (plus any trailing codec data) of the video stream.</summary>
        public byte[] BitmapInfoHeader { get; }

        /// <summary>Video stream rate/scale from 'strh'; frames per second = rate / scale.</summary>
        public uint VideoRate { get; }

        public uint VideoScale { get; }

        /// <summary>Start of the video stream in frames ('strh' dwStart), normally 0.</summary>
        public uint VideoStart { get; }

        public double FramesPerSecond => (double)VideoRate / VideoScale;

        /// <summary>Number of video frames (one per video chunk, including empty/dropped ones).</summary>
        public int FrameCount => _videoFrames.Count;

        /// <summary>Movie duration derived from the frame count and frame rate.</summary>
        public TimeSpan Duration => GetFrameTime(FrameCount);

        public bool HasAudio => AudioFormat is not null;

        /// <summary>Audio format of the first audio stream, or null when there is none.</summary>
        public AviAudioFormat? AudioFormat { get; }

        /// <summary>Start of the audio stream in blocks ('strh' dwStart), normally 0.</summary>
        public uint AudioStart { get; }

        /// <summary>Total size in bytes of the concatenated audio chunks.</summary>
        public long AudioLength { get; }

        /// <summary>Presentation time of a video frame.</summary>
        public TimeSpan GetFrameTime(int index)
        {
            double seconds = (VideoStart + (double)index) * VideoScale / VideoRate;
            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>Size of a compressed frame in bytes; 0 means a dropped frame (repeat the previous picture).</summary>
        public int GetVideoFrameSize(int index)
        {
            return GetFrame(index).Size;
        }

        /// <summary>True when the index (or, without idx1, nothing) marked the frame as a key frame.</summary>
        public bool IsKeyFrame(int index)
        {
            return GetFrame(index).KeyFrame;
        }

        /// <summary>Reads a compressed video frame.</summary>
        public byte[] ReadVideoFrame(int index)
        {
            ChunkRef frame = GetFrame(index);
            if (frame.Size == 0)
                return Array.Empty<byte>();

            byte[] data = new byte[frame.Size];
            int read = ReadAt(frame.DataOffset, data);
            return read == data.Length ? data : data.AsSpan(0, read).ToArray();
        }

        /// <summary>
        /// Reads a compressed video frame into <paramref name="destination"/> (which must hold <see
        /// cref="GetVideoFrameSize"/> bytes) and returns the number of bytes read.
        /// </summary>
        public int ReadVideoFrame(int index, Span<byte> destination)
        {
            ChunkRef frame = GetFrame(index);
            if (destination.Length < frame.Size)
                throw new ArgumentException("Destination is smaller than the frame.", nameof(destination));

            return frame.Size == 0 ? 0 : ReadAt(frame.DataOffset, destination.Slice(0, frame.Size));
        }

        /// <summary>
        /// Reads audio bytes from the concatenated audio stream starting at byte <paramref name="position"/>.
        /// </summary>
        public int ReadAudio(long position, Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (position < 0)
                throw new ArgumentOutOfRangeException(nameof(position));
            if (position >= AudioLength || destination.IsEmpty)
                return 0;

            int chunk = Array.BinarySearch(_audioChunkStarts, position);
            if (chunk < 0)
                chunk = ~chunk - 1;

            int copied = 0;
            while (copied < destination.Length && chunk < _audioChunks.Count)
            {
                ChunkRef c = _audioChunks[chunk];
                long inChunk = position + copied - _audioChunkStarts[chunk];
                int count = (int)Math.Min(c.Size - inChunk, destination.Length - copied);
                if (count > 0)
                {
                    int read = ReadAt(c.DataOffset + inChunk, destination.Slice(copied, count));
                    copied += read;
                    if (read < count)
                        break;
                }

                chunk++;
            }

            return copied;
        }

        /// <summary>Reads the whole audio stream (all audio chunks concatenated).</summary>
        public byte[] ReadAllAudio()
        {
            byte[] data = new byte[AudioLength];
            int read = ReadAudio(0, data);
            return read == data.Length ? data : data.AsSpan(0, read).ToArray();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (!_leaveOpen)
                _stream.Dispose();
        }

        private ChunkRef GetFrame(int index)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if ((uint)index >= (uint)_videoFrames.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _videoFrames[index];
        }

        private int ReadAt(long offset, Span<byte> destination)
        {
            lock (_sync)
            {
                _stream.Position = offset;
                int total = 0;
                while (total < destination.Length)
                {
                    int n = _stream.Read(destination.Slice(total));
                    if (n <= 0)
                        break;
                    total += n;
                }

                return total;
            }
        }

        private bool TryReadHeader(long position, long end, out uint fourCC, out uint size)
        {
            fourCC = 0;
            size = 0;
            if (position + 8 > end)
                return false;

            Span<byte> header = stackalloc byte[8];
            if (ReadAt(position, header) != 8)
                return false;

            fourCC = BinaryPrimitives.ReadUInt32LittleEndian(header);
            size = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4));
            return true;
        }

        private uint ReadFourCC(long position)
        {
            Span<byte> buffer = stackalloc byte[4];
            return ReadAt(position, buffer) == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(buffer) : 0;
        }

        /// <summary>
        /// Offset of the chunk that follows a chunk at <paramref name="position"/> (payloads are word aligned).
        /// </summary>
        private static long NextChunk(long position, uint size)
        {
            return position + 8 + size + (size & 1);
        }

        private void ParseTopLevel(ref long idx1Offset, ref int idx1Size)
        {
            if (!TryReadHeader(0, _fileLength, out uint riff, out uint riffSize) || riff != FccRiff || ReadFourCC(8) != FccAvi)
                throw new InvalidDataException("Not a RIFF AVI file.");

            long position = 0;
            bool first = true;
            while (TryReadHeader(position, _fileLength, out uint fourCC, out uint size) && fourCC == FccRiff)
            {
                uint form = ReadFourCC(position + 8);
                long end = Math.Min(position + 8 + size, _fileLength);
                if (first || form == FccAvix)
                    ParseRiffBody(position + 12, end, ref idx1Offset, ref idx1Size, first);

                first = false;
                position = NextChunk(position, size);
            }
        }

        private void ParseRiffBody(long position, long end, ref long idx1Offset, ref int idx1Size, bool mainRiff)
        {
            while (TryReadHeader(position, end, out uint fourCC, out uint size))
            {
                long chunkEnd = Math.Min(position + 8 + size, end);
                if (fourCC == FccList && position + 12 <= end)
                {
                    uint listType = ReadFourCC(position + 8);
                    if (listType == FccHdrl && mainRiff)
                        ParseHeaderList(position + 12, chunkEnd);
                    else if (listType == FccMovi)
                        _moviLists.Add((position + 8, chunkEnd)); // Offset of the 'movi' tag (idx1 base)
                }
                else if (fourCC == FccIdx1 && mainRiff)
                {
                    idx1Offset = position + 8;
                    idx1Size = (int)Math.Min(size, chunkEnd - idx1Offset);
                }

                position = NextChunk(position, size);
            }
        }

        private void ParseHeaderList(long position, long end)
        {
            while (TryReadHeader(position, end, out uint fourCC, out uint size))
            {
                long chunkEnd = Math.Min(position + 8 + size, end);
                if (fourCC == FccList && ReadFourCC(position + 8) == FccStrl)
                    ParseStreamList(position + 12, chunkEnd);

                position = NextChunk(position, size);
            }
        }

        private void ParseStreamList(long position, long end)
        {
            var info = new StreamInfo();
            while (TryReadHeader(position, end, out uint fourCC, out uint size))
            {
                int available = (int)Math.Max(0, Math.Min(size, end - position - 8));
                if (fourCC == FccStrh && available >= 36)
                {
                    byte[] strh = new byte[available];
                    ReadAt(position + 8, strh);
                    info.Type = BinaryPrimitives.ReadUInt32LittleEndian(strh);
                    info.Handler = BinaryPrimitives.ReadUInt32LittleEndian(strh.AsSpan(4));
                    info.Scale = BinaryPrimitives.ReadUInt32LittleEndian(strh.AsSpan(20));
                    info.Rate = BinaryPrimitives.ReadUInt32LittleEndian(strh.AsSpan(24));
                    info.Start = BinaryPrimitives.ReadUInt32LittleEndian(strh.AsSpan(28));
                    info.Length = BinaryPrimitives.ReadUInt32LittleEndian(strh.AsSpan(32));
                }
                else if (fourCC == FccStrf)
                {
                    info.Format = new byte[available];
                    ReadAt(position + 8, info.Format);
                }

                position = NextChunk(position, size);
            }

            _streams.Add(info);
        }

        private static AviAudioFormat ParseWaveFormat(byte[] format)
        {
            if (format.Length < 14)
                throw new InvalidDataException("AVI audio stream has no WAVEFORMATEX.");

            ReadOnlySpan<byte> f = format;
            byte[] extra = Array.Empty<byte>();
            if (f.Length >= 18)
            {
                int cbSize = BinaryPrimitives.ReadUInt16LittleEndian(f.Slice(16));
                cbSize = Math.Min(cbSize, f.Length - 18);
                extra = f.Slice(18, cbSize).ToArray();
            }

            return new AviAudioFormat
            {
                FormatTag = BinaryPrimitives.ReadUInt16LittleEndian(f),
                Channels = BinaryPrimitives.ReadUInt16LittleEndian(f.Slice(2)),
                SamplesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian(f.Slice(4)),
                AverageBytesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian(f.Slice(8)),
                BlockAlign = BinaryPrimitives.ReadUInt16LittleEndian(f.Slice(12)),
                BitsPerSample = f.Length >= 16 ? BinaryPrimitives.ReadUInt16LittleEndian(f.Slice(14)) : (ushort)8,
                Extra = extra,
            };
        }

        /// <summary>Classifies a data chunk id "##xx" as belonging to the video or audio stream.</summary>
        private int Classify(uint chunkId)
        {
            int d0 = (int)(chunkId & 0xFF) - '0';
            int d1 = (int)((chunkId >> 8) & 0xFF) - '0';
            if ((uint)d0 > 9 || (uint)d1 > 9)
                return -1;

            int streamNumber = d0 * 10 + d1;
            if (streamNumber == _videoStream)
            {
                bool palette = (chunkId >> 16) == ('p' | ('c' << 8));
                return palette ? -1 : 0;
            }

            return streamNumber == _audioStream ? 1 : -1;
        }

        private void AddChunk(uint chunkId, long dataOffset, uint size, bool keyFrame)
        {
            int kind = Classify(chunkId);
            if (kind < 0)
                return;

            int clamped = (int)Math.Max(0, Math.Min(size, _fileLength - dataOffset));
            if (kind == 0)
                _videoFrames.Add(new ChunkRef(dataOffset, clamped, keyFrame));
            else if (clamped > 0)
                _audioChunks.Add(new ChunkRef(dataOffset, clamped, keyFrame: true));
        }

        private void ScanList(long position, long end, int depth)
        {
            // 'position' points at a list type tag ("movi" or "rec "); chunks follow it.
            position += 4;
            while (TryReadHeader(position, end, out uint fourCC, out uint size))
            {
                if (fourCC == FccList)
                {
                    if (depth < 4 && position + 12 <= end)
                        ScanList(position + 8, Math.Min(position + 8 + size, end), depth + 1);
                }
                else
                {
                    // Without an index every frame is a potential key frame; the codec header decides.
                    AddChunk(fourCC, position + 8, size, keyFrame: false);
                }

                position = NextChunk(position, size);
            }
        }

        private bool TryLoadIdx1(long offset, int size)
        {
            if (_moviLists.Count == 0 || size < 16)
                return false;

            byte[] index = new byte[size - size % 16];
            if (ReadAt(offset, index) != index.Length)
                return false;

            // Idx1 offsets are normally relative to the 'movi' tag, but some writers use absolute file offsets.
            long moviTag = _moviLists[0].Start;
            long baseOffset = -1;
            for (int i = 0; i < index.Length; i += 16)
            {
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i));
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i + 4));
                uint chunkOffset = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i + 8));
                if ((flags & IndexFlagList) != 0 || id == FccList || Classify(id) < 0)
                    continue;

                if (ReadFourCC(moviTag + chunkOffset) == id)
                    baseOffset = moviTag;
                else if (ReadFourCC(chunkOffset) == id)
                    baseOffset = 0;

                break;
            }

            if (baseOffset < 0)
                return false;

            for (int i = 0; i < index.Length; i += 16)
            {
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i));
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i + 4));
                uint chunkOffset = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i + 8));
                uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i + 12));
                if ((flags & IndexFlagList) != 0 || id == FccList)
                    continue;

                long dataOffset = baseOffset + chunkOffset + 8;
                if (dataOffset > _fileLength)
                    return false;

                AddChunk(id, dataOffset, chunkSize, (flags & IndexFlagKeyFrame) != 0);
            }

            return _videoFrames.Count > 0;
        }

        private static string FourCCToString(uint fourCC)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, fourCC);
            return Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
        }
    }
}
