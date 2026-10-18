using System.Diagnostics;
using Serilog;
using Thexder.Core.Audio;
using Thexder.Core.Video;

namespace Thexder.Feature.Video
{
    /// <summary>Plays the briefing movies (Indeo 3 AVIs).</summary>
    internal sealed class MoviePlayer : IDisposable
    {
        private readonly SoundMixer mixer;
        private AviFile? avi;
        private Indeo3Decoder? decoder;
        private byte[] frameBuffer = Array.Empty<byte>();
        private int nextFrame;
        private int audioRate;
        private readonly Stopwatch clock = new();

        public MoviePlayer(SoundMixer mixer)
        {
            this.mixer = mixer;
        }

        public bool IsPlaying => avi != null;

        public int Width => avi?.Width ?? 0;

        public int Height => avi?.Height ?? 0;

        /// <summary>The current picture as BGRA32 (Width * 4 bytes per row).</summary>
        public byte[] Pixels => frameBuffer;

        /// <summary>Raised once when the movie has played to its end.</summary>
        public event Action? Finished;

        public bool Start(string path)
        {
            Stop();
            try
            {
                avi = new AviFile(path);
                decoder = new Indeo3Decoder(avi.Width, avi.Height);
                frameBuffer = new byte[avi.Width * avi.Height * 4];
                nextFrame = 0;
                audioRate = 0;
                if (avi.AudioFormat is { IsPcm: true } format && format.BitsPerSample == 8 && format.Channels == 1)
                {
                    audioRate = (int)format.SamplesPerSecond;
                    byte[] pcm = avi.ReadAllAudio();
                    var samples = new float[pcm.Length];
                    for (int i = 0; i < pcm.Length; i++)
                    {
                        samples[i] = (pcm[i] - 128) / 128f;
                    }
                    mixer.BeginStream(audioRate);
                    mixer.QueueStream(samples);
                }
                clock.Restart();
                Log.Information("Movie {Path}: {Frames} frames at {Fps:0.##} fps", Path.GetFileName(path), avi.FrameCount, avi.FramesPerSecond);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                Log.Error(ex, "Could not play {Path}", path);
                Stop();
                return false;
            }
        }

        /// <summary>Decodes the frames that are due; returns true when the picture changed.</summary>
        public bool Update()
        {
            if (avi == null || decoder == null)
            {
                return false;
            }
            double now = audioRate > 0 ? (double)mixer.StreamFramesPlayed / audioRate : clock.Elapsed.TotalSeconds;
            if (audioRate > 0 && mixer.StreamFramesPlayed == 0)
            {
                now = Math.Min(now, clock.Elapsed.TotalSeconds);
            }
            bool changed = false;
            while (nextFrame < avi.FrameCount && avi.GetFrameTime(nextFrame).TotalSeconds <= now)
            {
                byte[] data = avi.ReadVideoFrame(nextFrame);
                if (data.Length > 0)
                {
                    decoder.Decode(data);
                }
                nextFrame++;
                changed = true;
            }
            if (changed && decoder.HasPicture)
            {
                decoder.ToBgra32(frameBuffer, avi.Width * 4);
            }
            if (nextFrame >= avi.FrameCount && now >= avi.Duration.TotalSeconds)
            {
                Stop();
                Finished?.Invoke();
            }
            return changed;
        }

        public void Stop()
        {
            mixer.EndStream();
            avi?.Dispose();
            avi = null;
            decoder = null;
        }

        public void Dispose() => Stop();
    }
}
