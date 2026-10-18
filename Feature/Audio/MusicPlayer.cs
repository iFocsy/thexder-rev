using Serilog;
using Thexder.Core.Audio;

namespace Thexder.Feature.Audio
{
    /// <summary>
    /// The level music: one General MIDI track per level in MDS\W##L##GM.MID, looping from the start whenever a level is
    /// (re)loaded.
    /// </summary>
    internal sealed class MusicPlayer
    {
        private readonly MidiSequencer? sequencer;
        private readonly string dataDirectory;
        private string? current;

        public MusicPlayer(MidiSequencer? sequencer, string dataDirectory)
        {
            this.sequencer = sequencer;
            this.dataDirectory = dataDirectory;
        }

        /// <summary>Options > Music.</summary>
        public bool Enabled { get; set; } = true;

        public void PlayLevel(int world, int level)
        {
            Stop();
            string mds = Path.Combine(dataDirectory, "MDS");
            string? path = new[] { $"W{world:00}L{level:00}GM.MID", $"W{world:00}L{level:00}.MID" }
                .Select(name => Path.Combine(mds, name))
                .FirstOrDefault(File.Exists);
            current = path;
            if (path != null && Enabled)
            {
                Start(path);
            }
        }

        /// <summary>Resumes the current track from the start (after a movie or when music is switched on).</summary>
        public void Resume()
        {
            if (current != null && Enabled && sequencer is { IsPlaying: false })
            {
                Start(current);
            }
        }

        public void Stop() => sequencer?.Stop();

        public void SetVolume(float volume)
        {
            if (sequencer != null)
            {
                sequencer.Volume = volume;
            }
        }

        private void Start(string path)
        {
            try
            {
                sequencer?.Play(MidiSong.Load(path), loop: true);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
            {
                Log.Warning(ex, "Could not play {Path}", path);
            }
        }
    }
}
