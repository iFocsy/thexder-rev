using System.Buffers.Binary;
using System.Text;
using Serilog;
using Thexder.Core.Assets;

namespace Thexder.Core.Audio
{
    /// <summary>
    /// The sound table (name, minimum repeat interval, rewind flag, speech flag) is read from THEXDER.EXE.
    /// </summary>
    internal sealed class SoundBoard
    {
        public const int NoSound = 0xffff;
        public const uint TableAddress = 0x499ae0;
        public const int RecordSize = 0x35;
        public const int SoundCount = 211;

        // Per-channel minimum intervals: channel 0 none, 1 = 1 s, 2 = 2 s.
        private static readonly int[] ChannelIntervals = { 0, 1000, 2000 };

        private readonly SoundMixer mixer;
        private readonly Entry[] entries;
        private readonly long[] channelLast = new long[ChannelIntervals.Length];
        private readonly Func<long> clock;

        public SoundBoard(SoundMixer mixer, IReadOnlyList<SoundRecord> records, Func<string, WaveFile?> loadWave, Func<long>? clock = null)
        {
            this.mixer = mixer;
            this.clock = clock ?? (() => Environment.TickCount64);
            entries = new Entry[records.Count];
            for (int i = 0; i < records.Count; i++)
            {
                var wave = loadWave(records[i].Name);
                entries[i] = new Entry(records[i], wave?.ToMonoFloat(), wave?.SampleRate ?? 22050);
            }
            long now = this.clock();
            Array.Fill(channelLast, now);
        }

        /// <summary>Options > Sound.</summary>
        public bool SoundEnabled { get; set; } = true;

        /// <summary>Options > Speech (announcer and taunts).</summary>
        public bool SpeechEnabled { get; set; } = true;

        public IReadOnlyList<SoundRecord> Records => entries.Select(e => e.Record).ToArray();

        /// <summary>Reads the sound table from THEXDER.EXE.</summary>
        public static IReadOnlyList<SoundRecord> ReadTable(PeResourceFile exe)
        {
            byte[] data = exe.ReadVirtual(TableAddress, SoundCount * RecordSize);
            var records = new SoundRecord[SoundCount];
            for (int i = 0; i < SoundCount; i++)
            {
                var r = data.AsSpan(i * RecordSize, RecordSize);
                int nameLength = r[..9].IndexOf((byte)0);
                string name = Encoding.ASCII.GetString(r[..(nameLength < 0 ? 9 : nameLength)]);
                records[i] = new SoundRecord(
                    i,
                    name,
                    BinaryPrimitives.ReadInt32LittleEndian(r[0x09..]),
                    BinaryPrimitives.ReadInt32LittleEndian(r[0x11..]) != 0,
                    BinaryPrimitives.ReadInt32LittleEndian(r[0x15..]) != 0);
            }
            return records;
        }

        public void Play(int id, bool loop = false)
        {
            if (!Gate(id, out var entry) || !IntervalElapsed(entry))
            {
                return;
            }
            Start(entry, loop);
        }

        public void PlayChannel(int id, int channel, bool loop = false)
        {
            if (!Gate(id, out var entry))
            {
                return;
            }
            long now = clock();
            if (ChannelIntervals[channel] != 0 && now - channelLast[channel] < ChannelIntervals[channel])
            {
                return;
            }
            channelLast[channel] = now;
            if (!loop && channel == 1)
            {
                PlayAt(id, 0, -600);
                return;
            }
            Start(entry, loop);
        }

        public void PlayAt(int id, int pan, int volume, bool loop = false)
        {
            if (!Gate(id, out var entry) || !IntervalElapsed(entry))
            {
                return;
            }
            entry.Pan = pan;
            entry.Volume = volume;
            Start(entry, loop);
        }

        public void PlayPositional(int id, int sourceX, int sourceY, int playerX, int playerY)
        {
            int dx = sourceX - playerX;
            int pan = Math.Clamp(dx * 45, -10000, 10000);
            int volume = Math.Clamp(-45 * (Math.Abs(sourceY - playerY) + Math.Abs(dx)), -2000, -300);
            PlayAt(id, pan, volume);
        }

        public void Stop(int id)
        {
            if (id >= 0 && id < entries.Length && entries[id].Voice != 0)
            {
                mixer.Stop(entries[id].Voice);
                entries[id].Voice = 0;
            }
        }

        public void StopAll()
        {
            mixer.StopAll();
            foreach (var entry in entries)
            {
                entry.Voice = 0;
            }
        }

        private bool Gate(int id, out Entry entry)
        {
            entry = null!;
            if (!SoundEnabled || id < 0 || id >= entries.Length)
            {
                return false;
            }
            entry = entries[id];
            return entry.Samples != null && (!entry.Record.IsSpeech || SpeechEnabled);
        }

        private bool IntervalElapsed(Entry entry)
        {
            if (entry.Record.IntervalMs == 0)
            {
                return true;
            }
            long now = clock();
            if (now - entry.LastPlayed < entry.Record.IntervalMs)
            {
                return false;
            }
            entry.LastPlayed = now;
            return true;
        }

        private void Start(Entry entry, bool loop)
        {
            if (entry.Voice != 0 && mixer.IsPlaying(entry.Voice))
            {
                if (!entry.Record.Restart && !loop)
                {
                    return;
                }
                mixer.Stop(entry.Voice);
            }
            // DirectSound volume is in 1/100 dB; pan attenuates the opposite side.
            float gain = MathF.Pow(10f, entry.Volume / 2000f);
            float left = gain * (entry.Pan > 0 ? MathF.Pow(10f, -entry.Pan / 2000f) : 1f);
            float right = gain * (entry.Pan < 0 ? MathF.Pow(10f, entry.Pan / 2000f) : 1f);
            entry.Voice = mixer.Play(entry.Samples!, entry.SampleRate, left, right, entry.Record.IsSpeech ? 1 : 0, loop);
        }

        private sealed class Entry
        {
            public Entry(SoundRecord record, float[]? samples, int sampleRate)
            {
                Record = record;
                Samples = samples;
                SampleRate = sampleRate;
                if (samples == null)
                {
                    Log.Warning("Sound {Name} (id {Id}) is missing", record.Name, record.Id);
                }
            }

            public SoundRecord Record { get; }

            public float[]? Samples { get; }

            public int SampleRate { get; }

            public int Voice { get; set; }

            public int Pan { get; set; }

            public int Volume { get; set; }

            public long LastPlayed { get; set; } = long.MinValue / 2;
        }
    }

    internal sealed record SoundRecord(int Id, string Name, int IntervalMs, bool Restart, bool IsSpeech);
}
