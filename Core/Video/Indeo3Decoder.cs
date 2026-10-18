using System;
using System.Buffers.Binary;
using System.Text.Json;

namespace Thexder.Core.Video
{
    /// <summary>Decoder for Intel Indeo Video 3 (FourCC "IV31"/"IV32").</summary>
    internal sealed class Indeo3Decoder
    {
        // The 16-byte "OS header" checksum is frame number ^ word 2 ^ size ^ 'FRMH'.
        private const uint FrameHeaderTag = ('F' << 24) | ('R' << 16) | ('M' << 8) | 'H';
        private const int OsHeaderSize = 16;
        private const int BitstreamVersion = 32;

        // Bitstream header layout (offsets relative to its start, i.e. frame offset 16).
        private const int AltQuantOffset = 32; // 16 nibble pairs: primary/secondary codebook for modes 1 and 4
        private const int PlaneDataMinOffset = 48;
        private const int NullFrameDataSize = 16; // A frame that is just the header: repeat the last picture

        private const int FlagEightBitPixels = 1 << 1;
        private const int FlagKeyFrame = 1 << 2;
        private const int FlagHalfPelMvY = 1 << 4;
        private const int FlagHalfPelMvX = 1 << 5;
        // Bit 8 marks droppable (non-reference) frames; decoding does not need it.
        private const int BufferSelectShift = 9;

        // Vertical splits of wide cells first cut "strips" of this width (in 4x4 blocks).
        private const int LumaStripWidth = 40;
        private const int ChromaStripWidth = 10;
        private const int MaxTreeDepth = 20;
        private const int MaxMotionVectors = 256;

        // Two-bit codes of the cell tree.
        private const int TreeHSplit = 0;
        private const int TreeVSplit = 1;
        private const int TreeIntraOrNull = 2; // MC tree: cell is INTRA; VQ tree: no residual (plain copy)
        private const int TreeInterOrData = 3; // MC tree: motion vector follows; VQ tree: cell data follows

        // Escape codes inside cell data (values below 248 are VQ codes).
        private const int EscSkipBlockAndNext = 0xF9; // Like 0xFA, then one more block treated the same
        private const int EscSkipBlock = 0xFA; // INTRA: leave block untouched; INTER: copy reference block
        private const int EscBlockRun = 0xFB; // Next byte: run length (bits 0-4) and skip flag (bit 5)
        private const int EscCopyRestAndNext = 0xFC; // Like 0xFD, then copy the next block as well
        private const int EscCopyToLine4 = 0xFD; // Copy prediction for all remaining lines of the block
        private const int EscCopyToLine3 = 0xFE; // Copy prediction up to line 3
        private const int EscCopyToLine2 = 0xFF; // Copy prediction up to line 2
        private const int FirstEscapeCode = 0xF8;

        private const uint Mask7Bit32 = 0x7F7F7F7F;
        private const ulong Mask7Bit64 = 0x7F7F7F7F7F7F7F7FUL;

        /// <summary>How the 4-line blocks of a cell are coded (derived from the cell's coding mode).</summary>
        private enum BlockMode
        {
            /// <summary>Modes 0 and 1: 4x4 blocks, every pixel row coded.</summary>
            Rows4x4,

            /// <summary>Modes 3 and 4 (INTRA only): 4x8 blocks, odd rows coded, even rows interpolated.</summary>
            Rows4x8,

            /// <summary>Mode 10 INTRA: 8x8 blocks coded at half resolution in both directions.</summary>
            Intra8x8,

            /// <summary>Mode 10 INTER: copied 8x8 blocks refined by horizontally doubled deltas on row pairs.</summary>
            Inter8x8,

            /// <summary>Mode 11 (INTER only): copied 4x8 blocks refined by deltas on row pairs.</summary>
            Inter4x8,
        }

        private static readonly byte[][] Requant = BuildRequantTables();
        private static readonly VqCodebook[] Codebooks = LoadCodebooks();

        private readonly Plane[] _planes;
        private int _displayBuffer = -1;

        public Indeo3Decoder(int width, int height)
        {
            if (width < 16 || height < 16 || (width & 3) != 0 || (height & 3) != 0)
                throw new ArgumentException($"Unsupported Indeo 3 picture size {width}x{height}.");

            Width = width;
            Height = height;
            _planes = new[]
            {
                new Plane(width, height, LumaStripWidth),
                new Plane(Align4(width >> 2), Align4(height >> 2), ChromaStripWidth), // U (Cb)
                new Plane(Align4(width >> 2), Align4(height >> 2), ChromaStripWidth), // V (Cr)
            };
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Width of the visible chroma planes.</summary>
        public int ChromaWidth => (Width + 3) >> 2;

        /// <summary>Height of the visible chroma planes.</summary>
        public int ChromaHeight => (Height + 3) >> 2;

        /// <summary>True once a picture has been decoded (before that the output is black).</summary>
        public bool HasPicture => _displayBuffer >= 0;

        /// <summary>Frame number from the last parsed frame header.</summary>
        public uint LastFrameNumber { get; private set; }

        /// <summary>True when the last decoded frame was an intra (key) frame.</summary>
        public bool LastFrameWasKeyFrame { get; private set; }

        /// <summary>Returns the decoder to its initial state (e.g. before seeking to a key frame).</summary>
        public void Reset()
        {
            foreach (Plane plane in _planes)
                plane.Clear();

            _displayBuffer = -1;
            LastFrameNumber = 0;
            LastFrameWasKeyFrame = false;
        }

        /// <summary>Decodes one compressed frame.</summary>
        public bool Decode(ReadOnlySpan<byte> frame)
        {
            if (frame.IsEmpty)
                return true;

            if (frame.Length < OsHeaderSize + 9)
                return false;

            uint frameNumber = BinaryPrimitives.ReadUInt32LittleEndian(frame);
            uint word2 = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(4));
            uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(8));
            uint osSize = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(12));
            if ((frameNumber ^ word2 ^ osSize ^ FrameHeaderTag) != checksum)
                return false;

            ReadOnlySpan<byte> bs = frame.Slice(OsHeaderSize);
            if (BinaryPrimitives.ReadUInt16LittleEndian(bs) != BitstreamVersion)
                return false;

            int flags = BinaryPrimitives.ReadUInt16LittleEndian(bs.Slice(2));
            long dataSize = ((long)BinaryPrimitives.ReadUInt32LittleEndian(bs.Slice(4)) + 7) >> 3;
            int codebookOffset = bs[8];
            LastFrameNumber = frameNumber;

            if (dataSize == NullFrameDataSize)
                return true;

            if (bs.Length < PlaneDataMinOffset)
                return false;

            dataSize = Math.Min(dataSize, bs.Length);
            int height = BinaryPrimitives.ReadUInt16LittleEndian(bs.Slice(12));
            int width = BinaryPrimitives.ReadUInt16LittleEndian(bs.Slice(14));
            if (width != Width || height != Height)
                return false;

            if ((flags & (FlagEightBitPixels | FlagHalfPelMvX | FlagHalfPelMvY)) != 0)
                return false; // Never produced by the Indeo 3.x encoders we know of

            // Plane data offsets come in Y, V, U order; their order in the buffer varies, so each plane's size is the
            // distance to the next plane start (or to the end of the data).
            Span<long> starts = stackalloc long[3];
            starts[0] = BinaryPrimitives.ReadUInt32LittleEndian(bs.Slice(16));
            starts[1] = BinaryPrimitives.ReadUInt32LittleEndian(bs.Slice(20));
            starts[2] = BinaryPrimitives.ReadUInt32LittleEndian(bs.Slice(24));
            Span<long> sizes = stackalloc long[3];
            for (int p = 0; p < 3; p++)
            {
                long end = dataSize;
                for (int q = 0; q < 3; q++)
                {
                    if (starts[q] > starts[p] && starts[q] < end)
                        end = starts[q];
                }

                if (starts[p] < PlaneDataMinOffset || starts[p] >= dataSize - 16 || end - starts[p] <= 0)
                    return false;

                sizes[p] = end - starts[p];
            }

            ReadOnlySpan<byte> altQuant = bs.Slice(AltQuantOffset, 16);
            int buffer = (flags >> BufferSelectShift) & 1;

            bool ok = DecodePlane(_planes[0], buffer, bs.Slice((int)starts[0], (int)sizes[0]), altQuant, codebookOffset)
                && DecodePlane(_planes[1], buffer, bs.Slice((int)starts[2], (int)sizes[2]), altQuant, codebookOffset)
                && DecodePlane(_planes[2], buffer, bs.Slice((int)starts[1], (int)sizes[1]), altQuant, codebookOffset);

            // Even when the data turns out to be corrupt the target buffer has been (partly) overwritten, so it becomes
            // the displayed picture; the next key frame repairs it.
            _displayBuffer = buffer;
            LastFrameWasKeyFrame = (flags & FlagKeyFrame) != 0;
            return ok;
        }

        /// <summary>
        /// Copies one plane of the current picture (0 = Y, 1 = U/Cb, 2 = V/Cr) as 8-bit samples (the 7-bit codec samples
        /// shifted left by one).
        /// </summary>
        public void CopyPlane(int plane, Span<byte> destination, int stride)
        {
            if ((uint)plane > 2)
                throw new ArgumentOutOfRangeException(nameof(plane));

            int w = plane == 0 ? Width : ChromaWidth;
            int h = plane == 0 ? Height : ChromaHeight;
            if (stride < w || destination.Length < (h - 1) * stride + w)
                throw new ArgumentException("Destination is too small.", nameof(destination));

            Plane p = _planes[plane];
            for (int y = 0; y < h; y++)
            {
                Span<byte> row = destination.Slice(y * stride, w);
                if (_displayBuffer < 0)
                {
                    row.Fill(plane == 0 ? (byte)0 : (byte)128);
                    continue;
                }

                ReadOnlySpan<byte> src = p.Buffers[_displayBuffer].AsSpan(p.Offset(0, y), w);
                for (int x = 0; x < w; x++)
                    row[x] = (byte)((src[x] & 0x7F) << 1);
            }
        }

        /// <summary>
        /// Converts the current picture to 32-bit BGRA (alpha 255), top-down, using BT.601 studio-range coefficients and
        /// bilinear chroma upsampling (chroma samples sit at the centre of their 4x4 luma block).
        /// </summary>
        public void ToBgra32(Span<byte> destination, int stride)
        {
            if (stride < Width * 4 || destination.Length < (Height - 1) * stride + Width * 4)
                throw new ArgumentException("Destination is too small.", nameof(destination));

            if (_displayBuffer < 0)
            {
                for (int y = 0; y < Height; y++)
                {
                    Span<byte> row = destination.Slice(y * stride, Width * 4);
                    for (int x = 0; x < row.Length; x += 4)
                    {
                        row[x] = 0;
                        row[x + 1] = 0;
                        row[x + 2] = 0;
                        row[x + 3] = 255;
                    }
                }

                return;
            }

            YuvToRgb.Convert(_planes[0], _planes[1], _planes[2], _displayBuffer, Width, Height, ChromaWidth, ChromaHeight, destination, stride);
        }

        private static int Align4(int value) => (value + 3) & ~3;

        // ---- Plane / cell tree decoding ------------------------------------------------------------------

        private static bool DecodePlane(Plane plane, int buffer, ReadOnlySpan<byte> data, ReadOnlySpan<byte> altQuant, int codebookOffset)
        {
            // Plane data: u32 motion vector count, (dy, dx) signed byte pairs, then the cell tree with the cell data
            // bytes interleaved into it.
            if (data.Length < 4)
                return false;

            uint vectorCount = BinaryPrimitives.ReadUInt32LittleEndian(data);
            if (vectorCount > MaxMotionVectors || 4 + vectorCount * 2 > (uint)data.Length)
                return false;

            var job = new PlaneJob
            {
                Plane = plane,
                Current = plane.Buffers[buffer],
                Reference = plane.Buffers[buffer ^ 1],
                Vectors = data.Slice(4, (int)vectorCount * 2),
                VectorCount = (int)vectorCount,
                AltQuant = altQuant,
                CodebookOffset = codebookOffset,
                Reader = new TreeReader(data.Slice(4 + (int)vectorCount * 2)),
            };

            var root = new Cell
            {
                X = 0,
                Y = 0,
                Width = plane.Width >> 2,
                Height = plane.Height >> 2,
                InVqTree = false,
                Vector = -1,
            };

            return DecodeCellTree(ref job, root, MaxTreeDepth);
        }

        /// <summary>Decodes one cell of the binary tree.</summary>
        private static bool DecodeCellTree(ref PlaneJob job, Cell cell, int depth)
        {
            if (depth <= 0)
                return false;

            while (true)
            {
                if (!job.Reader.TryReadCode(out int code))
                    return false;

                switch (code)
                {
                    case TreeHSplit:
                    {
                        Cell top = cell;
                        top.Height = SplitSize(cell.Height);
                        cell.Y += top.Height;
                        cell.Height -= top.Height;
                        if (cell.Height <= 0 || !DecodeCellTree(ref job, top, depth - 1))
                            return false;
                        break;
                    }

                    case TreeVSplit:
                    {
                        Cell left = cell;
                        int strip = job.Plane.StripWidth;
                        if (cell.Width > strip)
                            left.Width = cell.Width <= strip * 2 ? strip : strip * 2;
                        else
                            left.Width = SplitSize(cell.Width);

                        cell.X += left.Width;
                        cell.Width -= left.Width;
                        if (cell.Width <= 0 || !DecodeCellTree(ref job, left, depth - 1))
                            return false;
                        break;
                    }

                    case TreeIntraOrNull:
                        if (!cell.InVqTree)
                        {
                            cell.Vector = -1;
                            cell.InVqTree = true;
                            break;
                        }

                        // "Null" VQ cell: 0 = copy from the reference, 1 = skip.
                        if (!job.Reader.TryReadCode(out int nullKind) || nullKind >= 2)
                            return false;
                        if (cell.Vector < 0 || !job.Plane.Contains(cell))
                            return false;
                        return CopyCell(ref job, cell);

                    default:
                        if (!cell.InVqTree)
                        {
                            if (!job.Reader.TryReadByte(out int vector) || vector >= job.VectorCount)
                                return false;
                            cell.Vector = vector;
                            cell.InVqTree = true;
                            break;
                        }

                        return job.Plane.Contains(cell) && DecodeCell(ref job, cell);
                }
            }
        }

        /// <summary>Size of the first part when splitting a cell dimension (in 4x4 blocks).</summary>
        private static int SplitSize(int size) => size > 2 ? ((size + 2) >> 2) << 1 : 1;

        private static bool TryGetMotionSource(ref PlaneJob job, in Cell cell, out int sourceOffset)
        {
            int dy = 0;
            int dx = 0;
            if (cell.Vector >= 0)
            {
                dy = (sbyte)job.Vectors[cell.Vector * 2];
                dx = (sbyte)job.Vectors[cell.Vector * 2 + 1];
            }

            Plane plane = job.Plane;
            int x = cell.X * 4 + dx;
            int y = cell.Y * 4 + dy;

            // Row -1 (the constant prediction row) is a legal source.
            if (y < -1 || x < 0 || y + cell.Height * 4 > plane.Height || x + cell.Width * 4 > plane.Width)
            {
                sourceOffset = 0;
                return false;
            }

            sourceOffset = plane.Offset(x, y);
            return true;
        }

        /// <summary>Copies the motion-compensated area of the reference picture into the cell.</summary>
        private static bool CopyCell(ref PlaneJob job, in Cell cell)
        {
            if (!TryGetMotionSource(ref job, cell, out int source))
                return false;

            Plane plane = job.Plane;
            int target = plane.Offset(cell.X * 4, cell.Y * 4);
            int width = cell.Width * 4;
            for (int row = 0; row < cell.Height * 4; row++)
            {
                job.Reference.AsSpan(source + row * plane.Pitch, width)
                    .CopyTo(job.Current.AsSpan(target + row * plane.Pitch, width));
            }

            return true;
        }

        /// <summary>Decodes a coded leaf cell: descriptor byte (mode, codebook) followed by block data.</summary>
        private static bool DecodeCell(ref PlaneJob job, in Cell cell)
        {
            if (!job.Reader.TryReadByte(out int descriptor))
                return false;

            int mode = descriptor >> 4;
            int vqIndex = descriptor & 0x0F;
            Plane plane = job.Plane;
            byte[] target = job.Current;
            int targetOffset = plane.Offset(cell.X * 4, cell.Y * 4);
            bool inter = cell.Vector >= 0;

            // Prediction source: INTRA cells predict from the row above them (for the top row of the plane that is the
            // constant row of 64s); INTER modes 0/1 add deltas to the motion-compensated reference; INTER modes 10/11
            // copy the reference first and then refine the copy in place.
            byte[]? source = null;
            int sourceOffset = 0;
            if (!inter)
            {
                source = target;
                sourceOffset = targetOffset - plane.Pitch;
            }
            else if (mode >= 10)
            {
                if (!CopyCell(ref job, cell))
                    return false;
            }
            else
            {
                if (!TryGetMotionSource(ref job, cell, out sourceOffset))
                    return false;
                source = job.Reference;
            }

            // Modes 1 and 4 alternate between two codebooks (secondary on even lines, primary on odd lines) chosen
            // through the frame's alternate-quantiser table.
            int primary;
            int secondary;
            if (mode == 1 || mode == 4)
            {
                int pair = job.AltQuant[vqIndex];
                primary = (pair >> 4) + job.CodebookOffset;
                secondary = (pair & 0x0F) + job.CodebookOffset;
            }
            else
            {
                vqIndex += job.CodebookOffset;
                primary = vqIndex;
                secondary = vqIndex;
            }

            if (primary >= Codebooks.Length || secondary >= Codebooks.Length)
                return false;

            // Coarser codebooks (index >= 8) first snap the prediction row onto their quantisation grid so that adding
            // the deltas cannot overflow 7 bits.
            if (vqIndex >= 8 && source is not null)
            {
                byte[] table = Requant[vqIndex & 7];
                for (int i = 0; i < cell.Width * 4; i++)
                    source[sourceOffset + i] = table[source[sourceOffset + i] & 0x7F];
            }

            BlockMode blockMode;
            switch (mode)
            {
                case 0:
                case 1:
                    blockMode = BlockMode.Rows4x4;
                    break;
                case 3:
                case 4:
                    if (inter)
                        return false;
                    blockMode = BlockMode.Rows4x8;
                    break;
                case 10:
                    blockMode = inter ? BlockMode.Inter8x8 : BlockMode.Intra8x8;
                    break;
                case 11:
                    if (!inter)
                        return false;
                    blockMode = BlockMode.Inter4x8;
                    break;
                default:
                    return false;
            }

            if (source is null)
            {
                source = target; // In-place modes read what they write
                sourceOffset = targetOffset;
            }

            var blocks = new BlockContext
            {
                Mode = blockMode,
                Inter = inter,
                AtPictureTop = cell.Y == 0,
                Pitch = plane.Pitch,
                Target = target,
                Source = source,
                EvenBook = Codebooks[secondary],
                OddBook = Codebooks[primary],
                SwapEven = secondary >= 16,
                SwapOdd = primary >= 16,
            };

            return DecodeBlocks(ref job.Reader, in cell, ref blocks, targetOffset, sourceOffset);
        }

        /// <summary>Walks the blocks of a cell in raster order.</summary>
        private static bool DecodeBlocks(ref TreeReader reader, in Cell cell, ref BlockContext b, int targetOrigin, int sourceOrigin)
        {
            int hZoom = b.Mode is BlockMode.Intra8x8 or BlockMode.Inter8x8 ? 1 : 0;
            int vZoom = b.Mode == BlockMode.Rows4x4 ? 0 : 1;
            if ((cell.Height & vZoom) != 0 || (cell.Width & hZoom) != 0)
                return false;

            bool isMode10or11 = b.Mode >= BlockMode.Intra8x8;
            int pitch = b.Pitch;
            int runBlocks = 0;
            b.SkipRun = false;

            for (int by = 0; by < cell.Height; by += 1 + vZoom)
            {
                bool firstBlockRow = by == 0;
                for (int bx = 0; bx < cell.Width; bx += 1 + hZoom)
                {
                    int t = targetOrigin + by * 4 * pitch + bx * 4;
                    int s = sourceOrigin + by * 4 * pitch + bx * 4;

                    if (runBlocks > 0)
                    {
                        RepeatBlock(ref b, t, s, firstBlockRow);
                        runBlocks--;
                        continue;
                    }

                    for (int line = 0; line < 4;)
                    {
                        int lines = 1;
                        bool topOfCell = firstBlockRow && line == 0;
                        bool evenLine = (line & 1) == 0;
                        VqCodebook book = !isMode10or11 && evenLine ? b.EvenBook : b.OddBook;

                        if (!reader.TryReadByte(out int code))
                            return false;

                        if (code < FirstEscapeCode)
                        {
                            int dyad1;
                            int dyad2;
                            if (code < book.DyadCount)
                            {
                                // Two-byte code: this byte is the right dyad, the next one the left.
                                if (!reader.TryReadByte(out dyad1) || dyad1 >= book.DyadCount || dyad1 >= FirstEscapeCode)
                                    return false;
                                dyad2 = code;
                            }
                            else
                            {
                                // One-byte "quad" code: both dyads packed as a two-digit number.
                                int quad = code - book.DyadCount;
                                dyad1 = quad / book.QuadBase;
                                dyad2 = quad % book.QuadBase;
                                if (evenLine ? b.SwapEven : b.SwapOdd)
                                    (dyad1, dyad2) = (dyad2, dyad1);
                            }

                            ApplyDeltas(ref b, book, dyad1, dyad2, t, s, topOfCell);
                        }
                        else
                        {
                            switch (code)
                            {
                                case EscCopyRestAndNext:
                                case EscCopyToLine4:
                                case EscCopyToLine3:
                                case EscCopyToLine2:
                                    if (code == EscCopyRestAndNext)
                                    {
                                        b.SkipRun = false;
                                        runBlocks = 1;
                                        code = EscCopyToLine4;
                                    }

                                    lines = 257 - code - line;
                                    if (lines <= 0)
                                        return false;
                                    CopyLines(ref b, t, s, lines, topOfCell);
                                    break;

                                case EscBlockRun:
                                {
                                    if (!reader.TryReadByte(out int run))
                                        return false;
                                    runBlocks = (run & 0x1F) - 1;
                                    if (run >= 64 || runBlocks < 0)
                                        return false;
                                    b.SkipRun = (run & 0x20) != 0;
                                    lines = 4 - line;
                                    if (isMode10or11 || b.Inter || !b.SkipRun)
                                        CopyLines(ref b, t, s, lines, topOfCell);
                                    break;
                                }

                                case EscSkipBlockAndNext:
                                case EscSkipBlock:
                                    if (code == EscSkipBlockAndNext)
                                    {
                                        b.SkipRun = true;
                                        runBlocks = 1;
                                    }

                                    if (line != 0)
                                        return false;
                                    lines = 4;
                                    if (b.Inter)
                                        CopyLines(ref b, t, s, lines, topOfCell);
                                    break;

                                default:
                                    return false;
                            }
                        }

                        line += lines;
                        t += pitch * (lines << vZoom);
                        s += pitch * (lines << vZoom);
                    }
                }
            }

            return true;
        }

        /// <summary>Applies the deltas of one VQ code to the current line of a block.</summary>
        private static void ApplyDeltas(ref BlockContext b, VqCodebook book, int dyad1, int dyad2, int t, int s, bool topOfCell)
        {
            byte[] dst = b.Target;
            byte[] src = b.Source;
            int pitch = b.Pitch;
            switch (b.Mode)
            {
                case BlockMode.Rows4x4:
                    AddPair(dst, t, src, s, book.Pairs[dyad1]);
                    AddPair(dst, t + 2, src, s + 2, book.Pairs[dyad2]);
                    break;

                case BlockMode.Rows4x8:
                {
                    // The coded row is the lower one of the pair; the upper row is the average of the row above and the
                    // coded row (or a copy at the very top of the picture).
                    int coded = t + pitch;
                    AddPair(dst, coded, src, s, book.Pairs[dyad1]);
                    AddPair(dst, coded + 2, src, s + 2, book.Pairs[dyad2]);
                    if (topOfCell && b.AtPictureTop)
                        Write32(dst, t, Read32(dst, coded));
                    else
                        Write32(dst, t, Average32(Read32(src, s), Read32(dst, coded)));
                    break;
                }

                case BlockMode.Intra8x8:
                {
                    // Same as Rows4x8 at half horizontal resolution.
                    int coded = t + pitch;
                    uint left = Read32(src, s);
                    uint right = Read32(src, s + 4);
                    if (topOfCell)
                    {
                        left = DoubleEvenPixels(left);
                        right = DoubleEvenPixels(right);
                    }

                    Write32(dst, coded, (left + book.Quads[dyad1]) & Mask7Bit32);
                    Write32(dst, coded + 4, (right + book.Quads[dyad2]) & Mask7Bit32);
                    if (topOfCell && b.AtPictureTop)
                        Write64(dst, t, Read64(dst, coded));
                    else
                        Write64(dst, t, Average64(Read64(src, s), Read64(dst, coded)));
                    break;
                }

                case BlockMode.Inter8x8:
                    for (int row = t; row <= t + pitch; row += pitch)
                    {
                        Write32(dst, row, (Read32(dst, row) + book.Quads[dyad1]) & Mask7Bit32);
                        Write32(dst, row + 4, (Read32(dst, row + 4) + book.Quads[dyad2]) & Mask7Bit32);
                    }

                    break;

                case BlockMode.Inter4x8:
                    for (int row = t; row <= t + pitch; row += pitch)
                    {
                        AddPair(dst, row, dst, row, book.Pairs[dyad1]);
                        AddPair(dst, row + 2, dst, row + 2, book.Pairs[dyad2]);
                    }

                    break;
            }
        }

        /// <summary>Copies the prediction for <paramref name="lines"/> lines (a "null delta").</summary>
        private static void CopyLines(ref BlockContext b, int t, int s, int lines, bool topOfCell)
        {
            switch (b.Mode)
            {
                case BlockMode.Rows4x4:
                    CopyRows4(b.Target, t, b.Source, s, b.Pitch, lines);
                    break;

                case BlockMode.Rows4x8:
                    CopyRows4(b.Target, t, b.Source, s, b.Pitch, lines * 2);
                    break;

                case BlockMode.Intra8x8:
                {
                    ulong pixels = Read64(b.Source, s);
                    if (topOfCell)
                    {
                        pixels = DoubleEvenPixels(pixels);
                        Fill64(b.Target, t + b.Pitch, b.Pitch, lines * 2 - 1, pixels);
                        Write64(b.Target, t, Average64(Read64(b.Source, s), Read64(b.Target, t + b.Pitch)));
                    }
                    else
                    {
                        Fill64(b.Target, t, b.Pitch, lines * 2, pixels);
                    }

                    break;
                }

                default:
                    break; // INTER modes 10/11 already hold the copied reference
            }
        }

        /// <summary>Fills a whole block that is covered by a pending block run.</summary>
        private static void RepeatBlock(ref BlockContext b, int t, int s, bool firstBlockRow)
        {
            switch (b.Mode)
            {
                case BlockMode.Rows4x4:
                case BlockMode.Rows4x8:
                    // INTRA blocks under a "skip" run keep the content already in the buffer.
                    if (b.Inter || !b.SkipRun)
                        CopyRows4(b.Target, t, b.Source, s, b.Pitch, b.Mode == BlockMode.Rows4x4 ? 4 : 8);
                    break;

                case BlockMode.Intra8x8:
                {
                    ulong pixels = Read64(b.Source, s);
                    if (firstBlockRow)
                    {
                        pixels = DoubleEvenPixels(pixels);
                        Fill64(b.Target, t + b.Pitch, b.Pitch, 7, pixels);
                        Write64(b.Target, t, Average64(Read64(b.Source, s), Read64(b.Target, t + b.Pitch)));
                    }
                    else
                    {
                        Fill64(b.Target, t, b.Pitch, 8, pixels);
                    }

                    break;
                }

                default:
                    break;
            }
        }

        // ---- Pixel helpers. ------------------------------------------------------------------------------

        private static void AddPair(byte[] dst, int t, byte[] src, int s, short delta)
        {
            int value = (src[s] | (src[s + 1] << 8)) + delta;
            dst[t] = (byte)(value & 0x7F);
            dst[t + 1] = (byte)((value >> 8) & 0x7F);
        }

        /// <summary>Copies 4-pixel rows one after another (so an INTRA copy replicates the row above).</summary>
        private static void CopyRows4(byte[] dst, int t, byte[] src, int s, int pitch, int rows)
        {
            for (int i = 0; i < rows; i++)
                Write32(dst, t + i * pitch, Read32(src, s + i * pitch));
        }

        private static void Fill64(byte[] dst, int t, int pitch, int rows, ulong value)
        {
            for (int i = 0; i < rows; i++)
                Write64(dst, t + i * pitch, value);
        }

        private static uint Average32(uint a, uint b) => ((a + b) >> 1) & Mask7Bit32;

        private static ulong Average64(ulong a, ulong b) => ((a + b) >> 1) & Mask7Bit64;

        /// <summary>ABCD -> AACC (horizontal 2:1 subsampling with replication).</summary>
        private static uint DoubleEvenPixels(uint value)
        {
            value &= 0x00FF00FF;
            return value | (value << 8);
        }

        private static ulong DoubleEvenPixels(ulong value)
        {
            value &= 0x00FF00FF00FF00FFUL;
            return value | (value << 8);
        }

        private static uint Read32(byte[] buffer, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));

        private static void Write32(byte[] buffer, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);

        private static ulong Read64(byte[] buffer, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset, 8));

        private static void Write64(byte[] buffer, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset, 8), value);

        /// <summary>
        /// Requantisation tables, one per quantiser step 2..9: they map a 7-bit sample onto the step's grid (with Intel's
        /// offsets) and keep the result below 128.
        /// </summary>
        private static byte[][] BuildRequantTables()
        {
            ReadOnlySpan<int> offsets = [1, 1, 2, -3, -3, 3, 4, 4];
            ReadOnlySpan<int> bias = [0, 1, 0, 4, 4, 1, 0, 1];
            var tables = new byte[8][];
            for (int i = 0; i < 8; i++)
            {
                int step = i + 2;
                tables[i] = new byte[128];
                for (int v = 0; v < 128; v++)
                    tables[i][v] = (byte)((v + offsets[i]) / step * step + bias[i]); // C-style truncating division
            }

            // Clamp the top entries that would leave the 7-bit range.
            tables[0][127] = 126;
            tables[1][119] = 118;
            tables[1][120] = 118;
            tables[2][126] = 124;
            tables[2][127] = 124;
            tables[6][124] = 120;
            tables[6][125] = 120;
            tables[6][126] = 120;
            tables[6][127] = 120;

            // Two irregular entries that match Intel's binary decoder.
            tables[1][7] = 10;
            tables[4][8] = 10;
            return tables;
        }

        /// <summary>One image plane with its two picture buffers.</summary>
        private sealed class Plane
        {
            public Plane(int width, int height, int stripWidth)
            {
                Width = width;
                Height = height;
                Pitch = width;
                StripWidth = stripWidth;
                Buffers = new byte[2][];
                for (int i = 0; i < 2; i++)
                    Buffers[i] = new byte[Pitch * (height + 1)];

                Clear();
            }

            public void Clear()
            {
                foreach (byte[] buffer in Buffers)
                {
                    // One extra row above the picture holds the INTRA prediction for the top cells; it starts at 64 (mid
                    // grey) and the picture starts black.
                    buffer.AsSpan(0, Pitch).Fill(0x40);
                    buffer.AsSpan(Pitch).Clear();
                }
            }

            public int Width { get; }

            public int Height { get; }

            public int Pitch { get; }

            public int StripWidth { get; }

            public byte[][] Buffers { get; }

            public int Offset(int x, int y) => (y + 1) * Pitch + x;

            public bool Contains(in Cell cell) => cell.X + cell.Width <= Width >> 2 && cell.Y + cell.Height <= Height >> 2;
        }

        /// <summary>A rectangle of the cell tree, in units of 4x4 blocks.</summary>
        private struct Cell
        {
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public bool InVqTree; // False while still in the motion-compensation tree
            public int Vector; // Motion vector index, -1 for INTRA
        }

        /// <summary>
        /// Reads the cell tree: two-bit codes are taken MSB-first from "code bytes", while cell data and motion vector
        /// indices are byte-aligned and stored directly after the code byte that is being consumed.
        /// </summary>
        private ref struct TreeReader
        {
            private readonly ReadOnlySpan<byte> _data;
            private int _position;
            private int _codeByte;
            private int _codeBits;

            public TreeReader(ReadOnlySpan<byte> data)
            {
                _data = data;
                _position = 0;
                _codeByte = 0;
                _codeBits = 0;
            }

            public bool TryReadCode(out int code)
            {
                if (_codeBits == 0)
                {
                    if (_position >= _data.Length)
                    {
                        code = 0;
                        return false;
                    }

                    _codeByte = _data[_position++];
                    _codeBits = 8;
                }

                _codeBits -= 2;
                code = (_codeByte >> _codeBits) & 3;
                return true;
            }

            public bool TryReadByte(out int value)
            {
                if (_position >= _data.Length)
                {
                    value = 0;
                    return false;
                }

                value = _data[_position++];
                return true;
            }
        }

        private ref struct PlaneJob
        {
            public Plane Plane;
            public byte[] Current;
            public byte[] Reference;
            public ReadOnlySpan<byte> Vectors;
            public int VectorCount;
            public ReadOnlySpan<byte> AltQuant;
            public int CodebookOffset;
            public TreeReader Reader;
        }

        private struct BlockContext
        {
            public BlockMode Mode;
            public bool Inter;
            public bool AtPictureTop;
            public bool SkipRun;
            public int Pitch;
            public byte[] Target;
            public byte[] Source;
            public VqCodebook EvenBook;
            public VqCodebook OddBook;
            public bool SwapEven;
            public bool SwapOdd;
        }

        /// <summary>A VQ codebook: a list of dyads (delta pairs for two horizontally adjacent pixels).</summary>
        private sealed class VqCodebook
        {
            public VqCodebook(ReadOnlySpan<sbyte> compact, int dyadCount, int quadBase)
            {
                DyadCount = dyadCount;
                QuadBase = quadBase;
                Pairs = new short[dyadCount];
                Quads = new uint[dyadCount];

                int n = 0;
                for (int i = 0; i < compact.Length; i += 3)
                {
                    int kind = compact[i];
                    int a = compact[i + 1];
                    int b = compact[i + 2];
                    Add(ref n, a, b);
                    if (kind >= 2)
                        Add(ref n, -a, -b);
                    if (kind == 4)
                    {
                        Add(ref n, b, a);
                        Add(ref n, -b, -a);
                    }
                }

                // Remaining entries stay zero (some tables are shorter than their code range).
            }

            public int DyadCount { get; }

            public int QuadBase { get; }

            /// <summary>Packed 16-bit delta: left pixel in the low byte.</summary>
            public short[] Pairs { get; }

            /// <summary>Packed 32-bit delta with each value doubled: (a, a, b, b).</summary>
            public uint[] Quads { get; }

            private void Add(ref int n, int a, int b)
            {
                Pairs[n] = (short)(b * 256 + a);
                Quads[n] = unchecked((uint)(b * (1 << 24) + b * (1 << 16) + a * (1 << 8) + a));
                n++;
            }
        }

        // ---- Colour conversion ---------------------------------------------------------------------------

        private static class YuvToRgb
        {
            private const int Shift = 16;
            private static readonly int[] LumaTable = new int[256];
            private static readonly int[] CrToR = new int[256];
            private static readonly int[] CrToG = new int[256];
            private static readonly int[] CbToG = new int[256];
            private static readonly int[] CbToB = new int[256];

            // Horizontal/vertical bilinear weights (in eighths) for the four luma positions inside a chroma sample:
            // sample k covers luma 4k..4k+3 and sits at 4k+1.5.
            private static readonly int[] NearWeight = [5, 7, 7, 5];
            private static readonly int[] NeighbourSide = [-1, -1, 1, 1];

            static YuvToRgb()
            {
                const double scale = 1 << Shift;
                for (int i = 0; i < 256; i++)
                {
                    LumaTable[i] = (int)Math.Round(1.164383 * (i - 16) * scale + scale / 2);
                    CrToR[i] = (int)Math.Round(1.596027 * (i - 128) * scale);
                    CrToG[i] = (int)Math.Round(-0.812968 * (i - 128) * scale);
                    CbToG[i] = (int)Math.Round(-0.391762 * (i - 128) * scale);
                    CbToB[i] = (int)Math.Round(2.017232 * (i - 128) * scale);
                }
            }

            public static void Convert(Plane yPlane, Plane uPlane, Plane vPlane, int buffer, int width, int height,
                int chromaWidth, int chromaHeight, Span<byte> destination, int stride)
            {
                byte[] yBuf = yPlane.Buffers[buffer];
                byte[] uBuf = uPlane.Buffers[buffer];
                byte[] vBuf = vPlane.Buffers[buffer];
                Span<int> uRow = chromaWidth <= 512 ? stackalloc int[chromaWidth] : new int[chromaWidth];
                Span<int> vRow = chromaWidth <= 512 ? stackalloc int[chromaWidth] : new int[chromaWidth];

                for (int y = 0; y < height; y++)
                {
                    // Vertical interpolation of the two nearest chroma rows (weights sum to 8).
                    int cy = y >> 2;
                    int nearW = NearWeight[y & 3];
                    int cy2 = Math.Clamp(cy + NeighbourSide[y & 3], 0, chromaHeight - 1);
                    int uNear = uPlane.Offset(0, cy);
                    int uFar = uPlane.Offset(0, cy2);
                    int vNear = vPlane.Offset(0, cy);
                    int vFar = vPlane.Offset(0, cy2);
                    for (int x = 0; x < chromaWidth; x++)
                    {
                        uRow[x] = ((uBuf[uNear + x] & 0x7F) * nearW + (uBuf[uFar + x] & 0x7F) * (8 - nearW)) << 1;
                        vRow[x] = ((vBuf[vNear + x] & 0x7F) * nearW + (vBuf[vFar + x] & 0x7F) * (8 - nearW)) << 1;
                    }

                    int yRow = yPlane.Offset(0, y);
                    Span<byte> outRow = destination.Slice(y * stride, width * 4);
                    for (int x = 0; x < width; x++)
                    {
                        int cx = x >> 2;
                        int w = NearWeight[x & 3];
                        int cx2 = Math.Clamp(cx + NeighbourSide[x & 3], 0, chromaWidth - 1);
                        int u = (uRow[cx] * w + uRow[cx2] * (8 - w) + 32) >> 6;
                        int v = (vRow[cx] * w + vRow[cx2] * (8 - w) + 32) >> 6;
                        int l = LumaTable[(yBuf[yRow + x] & 0x7F) << 1];

                        int o = x * 4;
                        outRow[o] = ClampToByte((l + CbToB[u]) >> Shift);
                        outRow[o + 1] = ClampToByte((l + CbToG[u] + CrToG[v]) >> Shift);
                        outRow[o + 2] = ClampToByte((l + CrToR[v]) >> Shift);
                        outRow[o + 3] = 255;
                    }
                }
            }

            private static byte ClampToByte(int value) => (byte)Math.Clamp(value, 0, 255);
        }

        // ---- Codebooks -----------------------------------------------------------------------------------

        /// <summary>The 24 VQ codebook slots, from the embedded indeo3-codebooks.json.</summary>
        private static VqCodebook[] LoadCodebooks()
        {
            using var stream = typeof(Indeo3Decoder).Assembly.GetManifestResourceStream("Thexder.indeo3-codebooks.json")
                ?? throw new InvalidOperationException("The Indeo 3 codebooks are missing from the build.");
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.GetProperty("codebooks").EnumerateArray()
                .Select(book => new VqCodebook(
                    book.GetProperty("deltas").EnumerateArray().Select(v => checked((sbyte)v.GetInt32())).ToArray(),
                    book.GetProperty("dyads").GetInt32(),
                    book.GetProperty("quadBase").GetInt32()))
                .ToArray();
        }
    }
}
