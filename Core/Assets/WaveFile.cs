using System.Buffers.Binary;

namespace Thexder.Core.Assets
{
    /// <summary>A PCM sound from the WAVE folder (the CD's are all 8-bit mono 22050 Hz).</summary>
    internal sealed class WaveFile
    {
        private WaveFile(string name, int sampleRate, int channels, int bitsPerSample, byte[] data)
        {
            Name = name;
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = bitsPerSample;
            Data = data;
        }

        public string Name { get; }

        public int SampleRate { get; }

        public int Channels { get; }

        public int BitsPerSample { get; }

        public byte[] Data { get; }

        public int FrameCount => Data.Length / (Channels * BitsPerSample / 8);

        public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);

        public static WaveFile Load(string path) => Parse(Path.GetFileNameWithoutExtension(path).ToUpperInvariant(), File.ReadAllBytes(path));

        public static WaveFile Parse(string name, byte[] file)
        {
            var span = file.AsSpan();
            if (span.Length < 12 || !span[..4].SequenceEqual("RIFF"u8) || !span.Slice(8, 4).SequenceEqual("WAVE"u8))
            {
                throw new InvalidDataException($"{name} is not a RIFF WAVE file.");
            }

            int sampleRate = 0, channels = 0, bits = 0;
            byte[]? data = null;
            int pos = 12;
            // Many CD sounds have an odd-sized data chunk without the pad byte, so whatever follows the data is
            // misaligned: stop once format and data are known, and at the first impossible chunk size.
            while (pos + 8 <= span.Length && (data == null || sampleRate == 0))
            {
                var id = span.Slice(pos, 4);
                int size = BinaryPrimitives.ReadInt32LittleEndian(span[(pos + 4)..]);
                int body = pos + 8;
                if (size < 0)
                {
                    break;
                }
                size = Math.Min(size, span.Length - body);
                if (id.SequenceEqual("fmt "u8) && size >= 16)
                {
                    int format = BinaryPrimitives.ReadUInt16LittleEndian(span[body..]);
                    if (format != 1)
                    {
                        throw new InvalidDataException($"{name} is not PCM (format {format}).");
                    }
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(span[(body + 2)..]);
                    sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span[(body + 4)..]);
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(span[(body + 14)..]);
                }
                else if (id.SequenceEqual("data"u8))
                {
                    data = span.Slice(body, size).ToArray();
                }
                pos = body + size + (size & 1);
            }

            if (data == null || sampleRate == 0 || (bits != 8 && bits != 16) || channels is < 1 or > 2)
            {
                throw new InvalidDataException($"{name} has no usable PCM data.");
            }
            return new WaveFile(name, sampleRate, channels, bits, data);
        }

        /// <summary>The sound as mono floats in [-1, 1] at its own sample rate.</summary>
        public float[] ToMonoFloat()
        {
            int frames = FrameCount;
            var samples = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int c = 0; c < Channels; c++)
                {
                    int index = i * Channels + c;
                    sum += BitsPerSample == 8
                        ? (Data[index] - 128) / 128f
                        : BinaryPrimitives.ReadInt16LittleEndian(Data.AsSpan(index * 2)) / 32768f;
                }
                samples[i] = sum / Channels;
            }
            return samples;
        }
    }
}
