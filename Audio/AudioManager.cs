using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace Dust;

internal enum AudioCue
{
    Caught,
    Confirm,
    Select,
    Move,
    Type,
    Collect,
    MazeClear,
    ShopVoice
}

/// <summary>
/// Loads the supplied PCM WAV effects from assembly resources. Samples are
/// rescaled when master volume changes, keeping the published game standalone
/// without changing the machine's global mixer volume.
/// </summary>
internal sealed class AudioManager : IDisposable
{
    private const long CaughtPriorityMilliseconds = 500;

    private readonly Dictionary<AudioCue, SoundAsset> _sounds = [];
    private readonly MenuMusicPlayer _menuMusic;
    private readonly object _sync = new();
    private readonly string _musicAlias = $"DustMusic{Environment.ProcessId}";
    private int _volume = -1;
    private long _caughtPriorityUntil;
    private long _nextMusicStatusCheck;
    private string? _musicPath;
    private int _musicPreparedVolume = -1;
    private int _musicBuildSerial;
    private bool _musicOpen;
    private bool _musicRequested;
    private bool _disposed;

    public AudioManager(int volume)
    {
        _menuMusic = new MenuMusicPlayer(volume);
        Load(AudioCue.Caught, "caught.wav", 1f);
        Load(AudioCue.Confirm, "confirm.wav", 1f);
        Load(AudioCue.Select, "select.wav", 0.55f);
        Load(AudioCue.Move, "move.wav", 1f);
        Load(AudioCue.Type, "type.wav", 0.72f);
        Load(AudioCue.Collect, "collect.wav", 0.9f);
        Load(AudioCue.MazeClear, "mazeclear.wav", 1f);
        Load(AudioCue.ShopVoice, "shopVoice.wav", 0.68f);
        Volume = volume;
    }

    public int Volume
    {
        get => _volume;
        set
        {
            lock (_sync)
            {
                if (_disposed) return;

                var normalized = Math.Clamp(value, 0, 100);
                if (_volume == normalized) return;
                _volume = normalized;
                var gain = MathF.Pow(normalized / 100f, 1.45f);
                foreach (var sound in _sounds.Values) sound.SetGain(gain);
                _menuMusic.Volume = normalized;
                if (_musicPreparedVolume != normalized) DisposePreparedMusic();
            }
        }
    }

    public void Play(AudioCue cue)
    {
        if (cue is AudioCue.Type or AudioCue.ShopVoice)
        {
            QueueCharacterVoice(cue);
            return;
        }

        lock (_sync)
        {
            if (_disposed || _volume <= 0 || !_sounds.TryGetValue(cue, out var sound)) return;

            var now = Environment.TickCount64;
            // Printing, collection, and completion cues confirm state changes,
            // so they must still sound if a warning was issued in the same half-second.
            if (cue is not (AudioCue.Caught or AudioCue.Type or AudioCue.Collect or AudioCue.MazeClear or AudioCue.ShopVoice) &&
                now < _caughtPriorityUntil) return;
            if (cue == AudioCue.Caught) _caughtPriorityUntil = now + CaughtPriorityMilliseconds;

            sound.Play();
        }
    }

    private void QueueCharacterVoice(AudioCue cue)
    {
        if (Volatile.Read(ref _disposed) || Volatile.Read(ref _volume) <= 0) return;

        // Character audio can arrive more than forty times per second. Starting
        // a WinMM voice on the paint/timer thread makes the printer itself lag,
        // so only the already-loaded voice trigger is dispatched to the pool.
        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (manager, queuedCue) = state;
            SoundAsset? sound;
            lock (manager._sync)
            {
                if (manager._disposed || manager._volume <= 0 ||
                    !manager._sounds.TryGetValue(queuedCue, out sound)) return;
            }
            sound.Play();
        }, (this, cue), preferLocal: false);
    }

    /// <summary>
    /// Extracts and volume-trims the large music resource away from the UI
    /// thread. The prepared file is retained between runs until volume changes.
    /// </summary>
    public async Task<bool> PrepareMusicAsync(CancellationToken cancellationToken = default)
    {
        int volume;
        int buildSerial;
        lock (_sync)
        {
            if (_disposed) return false;
            volume = _volume;
            if (volume <= 0) return true;
            if (_musicOpen && _musicPreparedVolume == volume) return true;
            buildSerial = ++_musicBuildSerial;
        }

        string? preparedPath = null;
        try
        {
            preparedPath = await Task.Run(
                () => BuildMusicFile(volume, buildSerial, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Do not opt out of the captured WinForms context above: string-command
            // MCI aliases belong to the thread that opened them. Playback, polling,
            // and shutdown all run on this same UI thread.
            lock (_sync)
            {
                if (_disposed || volume != _volume || buildSerial != _musicBuildSerial)
                    return false;
                DisposePreparedMusic();
                var quotedPath = preparedPath.Replace("\"", "\"\"");
                if (SendMusicCommand($"open \"{quotedPath}\" type waveaudio alias {_musicAlias}") != 0)
                    return false;
                _musicPath = preparedPath;
                _musicOpen = true;
                _musicPreparedVolume = volume;
                _nextMusicStatusCheck = 0;
                preparedPath = null;
                if (_musicRequested) StartMusicFromBeginning();
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            DeleteMusicFile(preparedPath);
        }
    }

    public void PlayMusic()
    {
        _menuMusic.Stop();
        lock (_sync)
        {
            if (_disposed) return;
            _musicRequested = true;
            if (_volume <= 0 || !_musicOpen || _musicPreparedVolume != _volume) return;
            StartMusicFromBeginning();
        }
    }

    /// <summary>
    /// Restarts the MCI wave stream when it reaches the end. Polling is
    /// throttled so the normal 60 Hz game loop does not spam WinMM.
    /// </summary>
    public void UpdateMusic()
    {
        lock (_sync)
        {
            if (_disposed || !_musicRequested || !_musicOpen || _volume <= 0) return;
            var now = Environment.TickCount64;
            if (now < _nextMusicStatusCheck) return;
            _nextMusicStatusCheck = now + 250;
            var mode = QueryMusicCommand($"status {_musicAlias} mode");
            if (!string.Equals(mode, "playing", StringComparison.OrdinalIgnoreCase))
                StartMusicFromBeginning();
        }
    }

    public void StopMusic()
    {
        _menuMusic.Stop();
        lock (_sync)
        {
            _musicRequested = false;
            if (_musicOpen) SendMusicCommand($"stop {_musicAlias}");
        }
    }

    public Task<bool> PrepareMenuMusicAsync(CancellationToken cancellationToken = default) =>
        _menuMusic.PrepareAsync(cancellationToken);

    public void PlayMenuMusic()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _musicRequested = false;
            if (_musicOpen) SendMusicCommand($"stop {_musicAlias}");
        }
        _menuMusic.Play();
    }

    public void Dispose()
    {
        _menuMusic.Dispose();
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _musicRequested = false;
            _musicBuildSerial++;
            DisposePreparedMusic();
            foreach (var sound in _sounds.Values) sound.Dispose();
            _sounds.Clear();
        }
    }

    private static string BuildMusicFile(int volume, int buildSerial, CancellationToken cancellationToken)
    {
        var assembly = typeof(AudioManager).Assembly;
        using var source = assembly.GetManifestResourceStream("Dust.Audio.Re_Dust.wav")
            ?? throw new FileNotFoundException("The embedded Re_Dust.wav resource was not found.");
        CleanupStaleMusicFiles();
        var destinationPath = Path.Combine(
            Path.GetTempPath(), $"Dust-Re_Dust-{Environment.ProcessId}-{buildSerial}.wav");
        try
        {
            using var destination = new FileStream(
                destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read,
                bufferSize: 128 * 1024, FileOptions.SequentialScan);
            ScaleMusicWave(source, destination, MusicGain(volume), cancellationToken);
            return destinationPath;
        }
        catch
        {
            DeleteMusicFile(destinationPath);
            throw;
        }
    }

    private static float MusicGain(int volume) =>
        MathF.Pow(Math.Clamp(volume, 0, 100) / 100f, 1.45f) * .62f;

    private static void ScaleMusicWave(Stream source, Stream destination, float gain,
        CancellationToken cancellationToken)
    {
        var header = new byte[12];
        ReadExactly(source, header, cancellationToken);
        if (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
            throw new InvalidDataException("Music resource is not a RIFF WAVE file.");
        destination.Write(header);

        var pcm16 = false;
        var chunkHeader = new byte[8];
        var buffer = new byte[128 * 1024];
        while (source.Position < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadExactly(source, chunkHeader, cancellationToken);
            destination.Write(chunkHeader);
            var chunkId = Encoding.ASCII.GetString(chunkHeader, 0, 4);
            var chunkLength = BitConverter.ToUInt32(chunkHeader, 4);

            if (chunkId == "fmt ")
            {
                if (chunkLength > 4096) throw new InvalidDataException("Music format chunk is invalid.");
                var format = new byte[(int)chunkLength];
                ReadExactly(source, format, cancellationToken);
                destination.Write(format);
                pcm16 = format.Length >= 16 && BitConverter.ToUInt16(format, 0) == 1 &&
                        BitConverter.ToUInt16(format, 14) == 16;
            }
            else if (chunkId == "data")
            {
                if (!pcm16) throw new InvalidDataException("Music must be 16-bit PCM.");
                if ((chunkLength & 1) != 0)
                    throw new InvalidDataException("16-bit music data is not sample aligned.");
                var remaining = (long)chunkLength;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = (int)Math.Min(buffer.Length, remaining);
                    ReadExactly(source, buffer, count, cancellationToken);
                    for (var offset = 0; offset < count; offset += 2)
                    {
                        var sample = BitConverter.ToInt16(buffer, offset);
                        var scaled = (short)Math.Clamp(MathF.Round(sample * gain), short.MinValue, short.MaxValue);
                        buffer[offset] = (byte)(scaled & 0xff);
                        buffer[offset + 1] = (byte)((scaled >> 8) & 0xff);
                    }
                    destination.Write(buffer, 0, count);
                    remaining -= count;
                }
            }
            else
            {
                CopyExactly(source, destination, chunkLength, buffer, cancellationToken);
            }

            if ((chunkLength & 1) != 0)
            {
                var padding = source.ReadByte();
                if (padding < 0) throw new EndOfStreamException();
                destination.WriteByte((byte)padding);
            }
        }
    }

    private static void CopyExactly(Stream source, Stream destination, uint length, byte[] buffer,
        CancellationToken cancellationToken)
    {
        var remaining = (long)length;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count <= 0) throw new EndOfStreamException();
            destination.Write(buffer, 0, count);
            remaining -= count;
        }
    }

    private static void ReadExactly(Stream source, byte[] buffer, CancellationToken cancellationToken)
        => ReadExactly(source, buffer, buffer.Length, cancellationToken);

    private static void ReadExactly(Stream source, byte[] buffer, int length,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = source.Read(buffer, offset, length - offset);
            if (count <= 0) throw new EndOfStreamException();
            offset += count;
        }
    }

    private static void CleanupStaleMusicFiles()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(Path.GetTempPath(), "Dust-Re_Dust-*.wav"))
            {
                var stem = Path.GetFileNameWithoutExtension(path);
                const string prefix = "Dust-Re_Dust-";
                var processText = stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? stem[prefix.Length..].Split('-')[0]
                    : string.Empty;
                if (!int.TryParse(processText, out var processId) ||
                    processId == Environment.ProcessId) continue;
                try
                {
                    using var process = Process.GetProcessById(processId);
                    if (!process.HasExited) continue;
                }
                catch (ArgumentException)
                {
                    // No process owns the abandoned extraction.
                }
                try { File.Delete(path); }
                catch { }
            }
        }
        catch
        {
            // Temp-folder cleanup is optional.
        }
    }

    private void DisposePreparedMusic()
    {
        if (_musicOpen)
        {
            SendMusicCommand($"stop {_musicAlias}");
            SendMusicCommand($"close {_musicAlias}");
            _musicOpen = false;
        }
        _musicPreparedVolume = -1;
        DeleteMusicFile(_musicPath);
        _musicPath = null;
    }

    private void StartMusicFromBeginning()
    {
        if (!_musicOpen) return;
        if (SendMusicCommand($"play {_musicAlias} from 0") == 0)
            _nextMusicStatusCheck = Environment.TickCount64 + 250;
    }

    private static int SendMusicCommand(string command)
    {
        try { return mciSendString(command, null, 0, IntPtr.Zero); }
        catch { return -1; }
    }

    private static string QueryMusicCommand(string command)
    {
        try
        {
            var result = new StringBuilder(64);
            return mciSendString(command, result, result.Capacity, IntPtr.Zero) == 0
                ? result.ToString().Trim()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? returnValue,
        int returnLength, IntPtr callback);

    private static void DeleteMusicFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.Delete(path);
                if (!File.Exists(path)) return;
            }
            catch
            {
                // MCI can retain the file handle briefly after a successful close.
            }
            if (attempt < 7) Thread.Sleep(25);
        }
    }

    private void Load(AudioCue cue, string fileName, float trim)
    {
        try
        {
            var assembly = typeof(AudioManager).Assembly;
            using var stream = assembly.GetManifestResourceStream($"Dust.Audio.{fileName}");
            if (stream is null) return;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var voices = cue is AudioCue.Type or AudioCue.ShopVoice ? 12 : 1;
            _sounds[cue] = new SoundAsset(buffer.ToArray(), trim, voices);
        }
        catch
        {
            // A missing or unreadable optional effect should never prevent startup.
        }
    }

    private sealed class SoundAsset : IDisposable
    {
        private readonly byte[] _source;
        private readonly float _trim;
        private readonly int _voiceCount;
        private readonly object _voiceSync = new();
        private readonly List<MemoryStream> _streams = [];
        private readonly List<SoundPlayer> _players = [];
        private int _voiceCursor;

        public SoundAsset(byte[] source, float trim, int voiceCount)
        {
            _source = source;
            _trim = Math.Clamp(trim, 0f, 1f);
            _voiceCount = Math.Max(1, voiceCount);
        }

        public void SetGain(float gain)
        {
            lock (_voiceSync)
            {
                DisposePlayersCore();
                try
                {
                    var scaledWave = ScalePcm16Wave(_source, gain * _trim);
                    for (var index = 0; index < _voiceCount; index++)
                    {
                        var stream = new MemoryStream(scaledWave, writable: false);
                        var player = new SoundPlayer(stream) { LoadTimeout = 1000 };
                        player.Load();
                        _streams.Add(stream);
                        _players.Add(player);
                    }
                }
                catch
                {
                    DisposePlayersCore();
                }
            }
        }

        public void Play()
        {
            SoundPlayer? player;
            lock (_voiceSync)
            {
                if (_players.Count == 0) return;
                player = _players[_voiceCursor++ % _players.Count];
            }
            try
            {
                player.Play();
            }
            catch
            {
                // A concurrent volume rebuild or shutdown may retire this voice.
            }
        }

        public void Dispose()
        {
            lock (_voiceSync) DisposePlayersCore();
        }

        private void DisposePlayersCore()
        {
            foreach (var player in _players)
            {
                player.Stop();
                player.Dispose();
            }
            foreach (var stream in _streams) stream.Dispose();
            _players.Clear();
            _streams.Clear();
            _voiceCursor = 0;
        }
    }

    private static byte[] ScalePcm16Wave(byte[] source, float gain)
    {
        var output = (byte[])source.Clone();
        if (output.Length < 44 || Encoding.ASCII.GetString(output, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(output, 8, 4) != "WAVE")
            throw new InvalidDataException("Audio resource is not a RIFF WAVE file.");

        var pcm = false;
        var bitsPerSample = 0;
        var position = 12;
        while (position + 8 <= output.Length)
        {
            var chunkId = Encoding.ASCII.GetString(output, position, 4);
            var chunkSize = BitConverter.ToInt32(output, position + 4);
            var dataStart = position + 8;
            if (chunkSize < 0 || dataStart + (long)chunkSize > output.Length)
                throw new InvalidDataException("Audio resource contains a damaged chunk.");

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                pcm = BitConverter.ToInt16(output, dataStart) == 1;
                bitsPerSample = BitConverter.ToInt16(output, dataStart + 14);
            }
            else if (chunkId == "data")
            {
                if (!pcm || bitsPerSample != 16)
                    throw new InvalidDataException("Only 16-bit PCM effects are supported.");
                var end = dataStart + chunkSize - chunkSize % 2;
                for (var sampleOffset = dataStart; sampleOffset < end; sampleOffset += 2)
                {
                    var sample = BitConverter.ToInt16(output, sampleOffset);
                    var scaled = (short)Math.Clamp(MathF.Round(sample * gain), short.MinValue, short.MaxValue);
                    output[sampleOffset] = (byte)(scaled & 0xff);
                    output[sampleOffset + 1] = (byte)((scaled >> 8) & 0xff);
                }
                return output;
            }

            position = dataStart + chunkSize + (chunkSize & 1);
        }

        throw new InvalidDataException("Audio resource has no PCM data chunk.");
    }
}
