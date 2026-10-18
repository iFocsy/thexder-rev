using NAudio.CoreAudioApi;
using NAudio.Midi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog;
using Thexder.Core.Audio;

namespace Thexder.Services
{
    /// <summary>
    /// Connects the platform-independent <see cref="SoundMixer"/> and <see cref="MidiSequencer"/> to the Windows audio
    /// devices: WASAPI shared mode for sound (wave out as a fallback) and the first MIDI output (normally the Microsoft
    /// GS Wavetable Synth) for music.
    /// </summary>
    internal sealed class AudioService : IDisposable
    {
        private readonly IWavePlayer? output;
        private readonly MidiOut? midi;

        public AudioService()
        {
            Mixer = new SoundMixer();
            Music = new MidiSequencer(Send);

            var provider = new MixerProvider(Mixer);
            try
            {
                // Resample to the device's own mix rate here: letting WASAPI convert 22050 Hz itself (AUTOCONVERTPCM)
                // produced dropouts and a wrong pitch, heard as a buzz (measured with a loopback capture).
                var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                int deviceRate = device.AudioClient.MixFormat.SampleRate;
                ISampleProvider source = deviceRate == SoundMixer.OutputRate ? provider : new WdlResamplingSampleProvider(provider, deviceRate);
                var wasapi = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
                wasapi.Init(source);
                wasapi.Play();
                output = wasapi;
                Log.Information("Sound: WASAPI shared on {Device}, game mix {Rate} Hz resampled to {DeviceRate} Hz", device.FriendlyName, SoundMixer.OutputRate, deviceRate);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WASAPI output failed, trying wave out");
                try
                {
                    var waveOut = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 };
                    waveOut.Init(provider);
                    waveOut.Play();
                    output = waveOut;
                }
                catch (Exception ex2)
                {
                    Log.Error(ex2, "No sound output device; the game will be silent");
                }
            }

            try
            {
                if (MidiOut.NumberOfDevices > 0)
                {
                    midi = new MidiOut(0);
                    Log.Information("Music on MIDI device {Device}", MidiOut.DeviceInfo(0).ProductName);
                }
                else
                {
                    Log.Warning("No MIDI output device; music is disabled");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not open the MIDI output device");
            }
        }

        public SoundMixer Mixer { get; }

        public MidiSequencer Music { get; }

        public bool HasMusicDevice => midi != null;

        public void Dispose()
        {
            Music.Dispose();
            output?.Stop();
            output?.Dispose();
            midi?.Dispose();
        }

        private void Send(int message)
        {
            try
            {
                midi?.Send(message);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "MIDI send failed");
            }
        }

        private sealed class MixerProvider : ISampleProvider
        {
            private readonly SoundMixer mixer;

            public MixerProvider(SoundMixer mixer)
            {
                this.mixer = mixer;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SoundMixer.OutputRate, SoundMixer.OutputChannels);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count) => mixer.Read(buffer, offset, count);
        }
    }
}
