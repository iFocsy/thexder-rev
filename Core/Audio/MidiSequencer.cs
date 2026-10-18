using System.Diagnostics;

namespace Thexder.Core.Audio
{
    /// <summary>
    /// Plays a <see cref="MidiSong"/> in a loop on its own thread, sending short messages to a MIDI device through
    /// <paramref name="send"/>.
    /// </summary>
    internal sealed class MidiSequencer : IDisposable
    {
        private readonly Action<int> send;
        private readonly object gate = new();
        private readonly int[] channelVolume = Enumerable.Repeat(100, 16).ToArray();
        private Thread? thread;
        private volatile bool stopRequested;
        private float volume = 1f;

        public MidiSequencer(Action<int> send)
        {
            this.send = send;
        }

        public bool IsPlaying => thread != null;

        public float Volume
        {
            get => volume;
            set
            {
                volume = Math.Clamp(value, 0f, 1f);
                lock (gate)
                {
                    for (int channel = 0; channel < 16; channel++)
                    {
                        SendVolume(channel);
                    }
                }
            }
        }

        public void Play(MidiSong song, bool loop = true)
        {
            Stop();
            stopRequested = false;
            thread = new Thread(() => Run(song, loop)) { IsBackground = true, Name = "Thexder music", Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        public void Stop()
        {
            var running = thread;
            if (running == null)
            {
                return;
            }
            stopRequested = true;
            running.Join(1000);
            thread = null;
            AllNotesOff();
        }

        public void Dispose() => Stop();

        private void Run(MidiSong song, bool loop)
        {
            do
            {
                ResetControllers();
                var clock = Stopwatch.StartNew();
                foreach (var e in song.Events)
                {
                    while (!stopRequested)
                    {
                        double wait = e.Time - clock.Elapsed.TotalSeconds;
                        if (wait <= 0)
                        {
                            break;
                        }
                        Thread.Sleep(wait > 0.004 ? (int)Math.Min(wait * 1000 - 2, 50) : 0);
                    }
                    if (stopRequested)
                    {
                        return;
                    }
                    Dispatch(e.Message);
                }
                AllNotesOff();
            }
            while (loop && !stopRequested && song.Events.Count > 0);
            thread = null;
        }

        private void Dispatch(int message)
        {
            int status = message & 0xff;
            int channel = status & 0x0f;
            lock (gate)
            {
                if ((status & 0xf0) == 0xb0 && ((message >> 8) & 0x7f) == 7)
                {
                    channelVolume[channel] = (message >> 16) & 0x7f;
                    SendVolume(channel);
                    return;
                }
                send(message);
            }
        }

        private void SendVolume(int channel) =>
            send(0xb0 | channel | (7 << 8) | ((int)(channelVolume[channel] * volume) << 16));

        private void ResetControllers()
        {
            lock (gate)
            {
                for (int channel = 0; channel < 16; channel++)
                {
                    send(0xb0 | channel | (121 << 8));
                    channelVolume[channel] = 100;
                    SendVolume(channel);
                }
            }
        }

        private void AllNotesOff()
        {
            lock (gate)
            {
                for (int channel = 0; channel < 16; channel++)
                {
                    send(0xb0 | channel | (123 << 8));
                    send(0xb0 | channel | (120 << 8));
                }
            }
        }
    }
}
