namespace Thexder.Core.Audio
{
    /// <summary>Mixes sound effects, speech and one streamed source (movie audio) into interleaved stereo floats.</summary>
    internal sealed class SoundMixer
    {
        /// <summary>
        /// Every sound and movie soundtrack of the game is 22050 Hz, so the mixer runs at that rate and the system
        /// resamples the result to the device (as DirectSound did).
        /// </summary>
        public const int OutputRate = 22050;
        public const int OutputChannels = 2;

        private readonly object gate = new();
        private readonly List<Voice> voices = new();
        private readonly Queue<float[]> streamBlocks = new();
        private float[]? streamBlock;
        private double streamPosition;
        private int streamRate = 22050;
        private long streamFramesPlayed;
        private int nextHandle = 1;

        public SoundMixer(int maxVoices = 16)
        {
            MaxVoices = maxVoices;
        }

        public int MaxVoices { get; }

        public float EffectsVolume { get; set; } = 1f;

        public float StreamVolume { get; set; } = 1f;

        /// <summary>Source frames of the streamed audio that have been mixed so far (movie clock).</summary>
        public long StreamFramesPlayed
        {
            get
            {
                lock (gate)
                {
                    return streamFramesPlayed;
                }
            }
        }

        /// <summary>Starts a sound.</summary>
        public int Play(float[] samples, int sampleRate, float volume = 1f, int priority = 0, bool loop = false) =>
            Play(samples, sampleRate, volume, volume, priority, loop);

        /// <summary>Starts a sound with separate left and right gains (DirectSound style panning).</summary>
        public int Play(float[] samples, int sampleRate, float leftGain, float rightGain, int priority, bool loop)
        {
            lock (gate)
            {
                if (voices.Count >= MaxVoices)
                {
                    int victim = -1;
                    for (int i = 0; i < voices.Count; i++)
                    {
                        if (voices[i].Priority <= priority && (victim < 0 || voices[i].Started < voices[victim].Started))
                        {
                            victim = i;
                        }
                    }
                    if (victim < 0)
                    {
                        return 0;
                    }
                    voices.RemoveAt(victim);
                }
                var voice = new Voice(nextHandle++, samples, (double)sampleRate / OutputRate, leftGain, rightGain, priority, loop, Environment.TickCount64);
                voices.Add(voice);
                return voice.Handle;
            }
        }

        public bool IsPlaying(int handle)
        {
            lock (gate)
            {
                return voices.Exists(v => v.Handle == handle);
            }
        }

        public void Stop(int handle)
        {
            lock (gate)
            {
                voices.RemoveAll(v => v.Handle == handle);
            }
        }

        public void StopAll()
        {
            lock (gate)
            {
                voices.Clear();
            }
        }

        /// <summary>Starts (or restarts) the streamed source; queue its audio with <see cref="QueueStream"/>.</summary>
        public void BeginStream(int sampleRate)
        {
            lock (gate)
            {
                streamBlocks.Clear();
                streamBlock = null;
                streamPosition = 0;
                streamRate = sampleRate;
                streamFramesPlayed = 0;
            }
        }

        public void QueueStream(float[] monoSamples)
        {
            lock (gate)
            {
                streamBlocks.Enqueue(monoSamples);
            }
        }

        public void EndStream()
        {
            lock (gate)
            {
                streamBlocks.Clear();
                streamBlock = null;
            }
        }

        /// <summary>Fills <paramref name="buffer"/> with interleaved stereo samples; always returns count.</summary>
        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            int frames = count / OutputChannels;
            lock (gate)
            {
                for (int v = voices.Count - 1; v >= 0; v--)
                {
                    if (!voices[v].Mix(buffer, offset, frames, EffectsVolume))
                    {
                        voices.RemoveAt(v);
                    }
                }
                MixStream(buffer, offset, frames);
            }

            // Soft clip so that many simultaneous explosions do not wrap around.
            for (int i = offset; i < offset + count; i++)
            {
                float s = buffer[i];
                buffer[i] = s > 1f ? 1f : s < -1f ? -1f : s;
            }
            return count;
        }

        private void MixStream(float[] buffer, int offset, int frames)
        {
            double step = (double)streamRate / OutputRate;
            for (int i = 0; i < frames; i++)
            {
                while (streamBlock == null || streamPosition >= streamBlock.Length)
                {
                    if (streamBlock != null)
                    {
                        streamPosition -= streamBlock.Length;
                    }
                    if (!streamBlocks.TryDequeue(out streamBlock))
                    {
                        streamBlock = null;
                        streamPosition = 0;
                        return;
                    }
                }
                int index = (int)streamPosition;
                float sample = streamBlock[index] * StreamVolume;
                buffer[offset + i * 2] += sample;
                buffer[offset + i * 2 + 1] += sample;
                double before = streamPosition;
                streamPosition += step;
                streamFramesPlayed += (long)streamPosition - (long)before;
            }
        }

        private sealed class Voice
        {
            private readonly float[] samples;
            private readonly double step;
            private readonly float left;
            private readonly float right;
            private readonly bool loop;
            private double position;

            public Voice(int handle, float[] samples, double step, float left, float right, int priority, bool loop, long started)
            {
                Handle = handle;
                this.samples = samples;
                this.step = step;
                this.left = left;
                this.right = right;
                Priority = priority;
                this.loop = loop;
                Started = started;
            }

            public int Handle { get; }

            public int Priority { get; }

            public long Started { get; }

            // Returns false once the sound has finished.
            public bool Mix(float[] buffer, int offset, int frames, float master)
            {
                float gainLeft = left * master;
                float gainRight = right * master;
                int length = samples.Length;
                if (length == 0)
                {
                    return false;
                }
                for (int i = 0; i < frames; i++)
                {
                    if (position >= length)
                    {
                        if (!loop)
                        {
                            return false;
                        }
                        position -= length;
                    }
                    // Linear interpolation: the CD's 22050 Hz sounds are upsampled to the output rate.
                    int index = (int)position;
                    float frac = (float)(position - index);
                    float a = samples[index];
                    float b = index + 1 < length ? samples[index + 1] : (loop ? samples[0] : a);
                    float sample = a + (b - a) * frac;
                    buffer[offset + i * 2] += sample * gainLeft;
                    buffer[offset + i * 2 + 1] += sample * gainRight;
                    position += step;
                }
                return position < length || loop;
            }
        }
    }
}
