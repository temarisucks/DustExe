using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;

namespace Dust;

/// <summary>
/// Decodes the embedded Ogg Opus menu track once, away from the UI thread,
/// then feeds the PCM from memory to a looping WinMM output. This keeps menu
/// playback independent from machine-installed codecs and avoids temp files.
/// </summary>
internal sealed class MenuMusicPlayer : IDisposable
{
    private const int SampleRate = 48_000;
    private const float MusicTrim = .62f;

    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task<DecodedTrack>? _decodeTask;
    private DecodedTrack? _track;
    private LoopingPcmProvider? _provider;
    private WaveOutEvent? _output;
    private int _volume;
    private bool _requested;
    private bool _disposed;

    public MenuMusicPlayer(int volume) => _volume = Math.Clamp(volume, 0, 100);

    public int Volume
    {
        set
        {
            lock (_sync)
            {
                if (_disposed) return;
                _volume = Math.Clamp(value, 0, 100);
                if (_output is not null) _output.Volume = OutputGain(_volume);

                if (!_requested || _track is null) return;
                if (_volume <= 0)
                {
                    if (_output?.PlaybackState == PlaybackState.Playing) _output.Pause();
                    return;
                }

                EnsureOutputCore();
                if (_output?.PlaybackState != PlaybackState.Playing) _output?.Play();
            }
        }
    }

    public async Task<bool> PrepareAsync(CancellationToken cancellationToken = default)
    {
        Task<DecodedTrack> decodeTask;
        lock (_sync)
        {
            if (_disposed) return false;
            if (_track is not null) return true;
            decodeTask = _decodeTask ??= Task.Run(
                () => DecodeEmbeddedTrack(_lifetime.Token), _lifetime.Token);
        }

        try
        {
            var decoded = await decodeTask.WaitAsync(cancellationToken);
            lock (_sync)
            {
                if (_disposed) return false;
                _track ??= decoded;
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            if (decodeTask.IsCanceled)
            {
                lock (_sync)
                    if (ReferenceEquals(_decodeTask, decodeTask)) _decodeTask = null;
            }
            return false;
        }
        catch
        {
            lock (_sync)
                if (ReferenceEquals(_decodeTask, decodeTask)) _decodeTask = null;
            return false;
        }
    }

    public void Play()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _requested = true;
            if (_track is null || _volume <= 0) return;

            EnsureOutputCore();
            if (_output?.PlaybackState != PlaybackState.Playing) _output?.Play();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _requested = false;
            DisposeOutputCore();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _requested = false;
            _lifetime.Cancel();
            DisposeOutputCore();
            _track = null;
            _decodeTask = null;
        }
        _lifetime.Dispose();
    }

    private void EnsureOutputCore()
    {
        if (_output is not null || _track is null || _track.Pcm.Length == 0) return;
        WaveOutEvent? output = null;
        try
        {
            _provider = new LoopingPcmProvider(_track.Pcm,
                new WaveFormat(SampleRate, 16, _track.Channels));
            output = new WaveOutEvent
            {
                DesiredLatency = 150,
                NumberOfBuffers = 3,
                Volume = OutputGain(_volume)
            };
            output.Init(_provider);
            _output = output;
        }
        catch
        {
            output?.Dispose();
            _provider = null;
            _output = null;
        }
    }

    private void DisposeOutputCore()
    {
        if (_output is not null)
        {
            try { _output.Stop(); }
            catch { }
            _output.Dispose();
            _output = null;
        }
        _provider = null;
    }

    private static float OutputGain(int volume) =>
        MathF.Pow(Math.Clamp(volume, 0, 100) / 100f, 1.45f) * MusicTrim;

    private static DecodedTrack DecodeEmbeddedTrack(CancellationToken cancellationToken)
    {
        var assembly = typeof(MenuMusicPlayer).Assembly;
        using var source = assembly.GetManifestResourceStream("Dust.Audio.track.ogg")
            ?? throw new FileNotFoundException("The embedded track.ogg resource was not found.");

        var (channels, preSkip) = ReadOpusHeader(source);
        source.Position = 0;

        // Dust ships as a single managed executable, so force the deterministic
        // managed codec instead of probing for an optional native Opus library.
        OpusCodecFactory.AttemptToUseNativeLibrary = false;
        using var decoder = OpusCodecFactory.CreateDecoder(SampleRate, channels);
        var ogg = new OpusOggReadStream(decoder, source);
        try
        {
            if (!ogg.HasNextPacket)
                throw new InvalidDataException(ogg.LastError ?? "The Ogg Opus stream has no audio packets.");

            var expectedValues = ogg.GranuleCount > preSkip
                ? (ogg.GranuleCount - preSkip) * channels
                : 0;
            var expectedBytes = expectedValues > 0 && expectedValues <= int.MaxValue / 2
                ? (int)expectedValues * sizeof(short)
                : 0;
            using var pcm = expectedBytes > 0 ? new MemoryStream(expectedBytes) : new MemoryStream();
            var skipValues = preSkip * channels;
            var remainingValues = expectedValues > 0 ? expectedValues : long.MaxValue;

            while (ogg.HasNextPacket && remainingValues > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var packet = ogg.DecodeNextPacket();
                if (packet is null)
                    throw new InvalidDataException(ogg.LastError ?? "The Ogg Opus decoder stopped unexpectedly.");

                var start = Math.Min(skipValues, packet.Length);
                skipValues -= start;
                var valueCount = packet.Length - start;
                if (remainingValues != long.MaxValue)
                    valueCount = (int)Math.Min(valueCount, remainingValues);
                if (valueCount <= 0) continue;

                var bytes = new byte[valueCount * sizeof(short)];
                Buffer.BlockCopy(packet, start * sizeof(short), bytes, 0, bytes.Length);
                pcm.Write(bytes, 0, bytes.Length);
                if (remainingValues != long.MaxValue) remainingValues -= valueCount;
            }

            if (pcm.Length == 0) throw new InvalidDataException("The menu track decoded to no PCM audio.");
            return new DecodedTrack(pcm.ToArray(), channels);
        }
        finally
        {
            ogg.Close();
        }
    }

    private static (int Channels, int PreSkip) ReadOpusHeader(Stream source)
    {
        if (!source.CanSeek) throw new InvalidDataException("The embedded menu track is not seekable.");
        var probe = new byte[Math.Min(4096, checked((int)source.Length))];
        var read = source.Read(probe, 0, probe.Length);
        ReadOnlySpan<byte> signature = "OpusHead"u8;
        var header = probe.AsSpan(0, read).IndexOf(signature);
        if (header < 0 || header + 12 > read)
            throw new InvalidDataException("The menu track is not an Ogg Opus stream.");

        var channels = probe[header + 9];
        if (channels is < 1 or > 2)
            throw new InvalidDataException("Only mono or stereo menu tracks are supported.");
        var preSkip = probe[header + 10] | probe[header + 11] << 8;
        return (channels, preSkip);
    }

    private sealed record DecodedTrack(byte[] Pcm, int Channels);

    private sealed class LoopingPcmProvider(byte[] pcm, WaveFormat waveFormat) : IWaveProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = waveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            if (pcm.Length == 0) return 0;
            var written = 0;
            while (written < count)
            {
                var available = pcm.Length - _position;
                var copy = Math.Min(available, count - written);
                Buffer.BlockCopy(pcm, _position, buffer, offset + written, copy);
                written += copy;
                _position += copy;
                if (_position >= pcm.Length) _position = 0;
            }
            return written;
        }
    }
}
