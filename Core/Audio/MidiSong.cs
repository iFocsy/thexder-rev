using System.Buffers.Binary;

namespace Thexder.Core.Audio
{
    /// <summary>
    /// A Standard MIDI File flattened into one time-ordered list of channel messages with absolute times in seconds
    /// (tempo changes already applied).
    /// </summary>
    internal sealed class MidiSong
    {
        private MidiSong(IReadOnlyList<MidiEvent> events, double length)
        {
            Events = events;
            Length = length;
        }

        public IReadOnlyList<MidiEvent> Events { get; }

        /// <summary>Time of the last event, in seconds.</summary>
        public double Length { get; }

        public static MidiSong Load(string path) => Parse(File.ReadAllBytes(path));

        public static MidiSong Parse(byte[] file)
        {
            var data = file.AsSpan();
            if (data.Length < 14 || !data[..4].SequenceEqual("MThd"u8))
            {
                throw new InvalidDataException("Not a Standard MIDI File.");
            }
            int trackCount = BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
            int division = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
            if ((division & 0x8000) != 0)
            {
                throw new InvalidDataException("SMPTE time division is not supported.");
            }

            // (tick, order, message) for channel messages; tempo changes kept separately.
            var raw = new List<(long Tick, int Order, int Message)>();
            var tempos = new List<(long Tick, int MicrosPerQuarter)>();
            int pos = 8 + BinaryPrimitives.ReadInt32BigEndian(data[4..]);
            int order = 0;
            for (int track = 0; track < trackCount && pos + 8 <= data.Length; track++)
            {
                if (!data.Slice(pos, 4).SequenceEqual("MTrk"u8))
                {
                    break;
                }
                int length = BinaryPrimitives.ReadInt32BigEndian(data[(pos + 4)..]);
                int end = Math.Min(data.Length, pos + 8 + length);
                int p = pos + 8;
                long tick = 0;
                int status = 0;
                while (p < end)
                {
                    tick += ReadVarLen(data, ref p);
                    if (p >= end)
                    {
                        break;
                    }
                    int b = data[p];
                    if (b >= 0x80)
                    {
                        p++;
                        if (b < 0xf0)
                        {
                            status = b;
                        }
                    }
                    else
                    {
                        b = status;
                    }

                    if (b == 0xff)
                    {
                        int type = data[p++];
                        int metaLength = (int)ReadVarLen(data, ref p);
                        if (type == 0x51 && metaLength == 3)
                        {
                            tempos.Add((tick, (data[p] << 16) | (data[p + 1] << 8) | data[p + 2]));
                        }
                        p += metaLength;
                    }
                    else if (b == 0xf0 || b == 0xf7)
                    {
                        p += (int)ReadVarLen(data, ref p);
                    }
                    else
                    {
                        int command = b & 0xf0;
                        int d1 = data[p++];
                        int d2 = command is 0xc0 or 0xd0 ? 0 : data[p++];
                        raw.Add((tick, order++, b | (d1 << 8) | (d2 << 16)));
                    }
                }
                pos += 8 + length;
            }

            raw.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Order.CompareTo(b.Order));
            tempos.Sort((a, b) => a.Tick.CompareTo(b.Tick));

            var events = new List<MidiEvent>(raw.Count);
            int tempoIndex = 0;
            long tempoTick = 0;
            double tempoSeconds = 0;
            double secondsPerTick = 500000 / 1e6 / division;
            foreach (var (tick, _, message) in raw)
            {
                while (tempoIndex < tempos.Count && tempos[tempoIndex].Tick <= tick)
                {
                    tempoSeconds += (tempos[tempoIndex].Tick - tempoTick) * secondsPerTick;
                    tempoTick = tempos[tempoIndex].Tick;
                    secondsPerTick = tempos[tempoIndex].MicrosPerQuarter / 1e6 / division;
                    tempoIndex++;
                }
                events.Add(new MidiEvent(tempoSeconds + (tick - tempoTick) * secondsPerTick, message));
            }
            return new MidiSong(events, events.Count > 0 ? events[^1].Time : 0);
        }

        private static long ReadVarLen(ReadOnlySpan<byte> data, ref int p)
        {
            long value = 0;
            for (int i = 0; i < 4 && p < data.Length; i++)
            {
                byte b = data[p++];
                value = (value << 7) | (uint)(b & 0x7f);
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }
            return value;
        }
    }

    /// <summary>A packed short MIDI message (status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16) at a time in seconds.</summary>
    internal readonly record struct MidiEvent(double Time, int Message);
}
