using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FocusBloom.Desktop.Presentation;

public sealed class CrossfadeLoop : ISampleProvider, IDisposable
{
    private readonly string _path;
    private readonly WaveFormat _format;
    private AudioFileReader? _reader;
    private float[] _head;
    private readonly long _length;
    private long _position;

    public CrossfadeLoop(string path)
    {
        _path = path;
        using var metadata = new AudioFileReader(path);
        _format = metadata.WaveFormat;
        _length = metadata.Length / sizeof(float);
        if (_length == 0) throw new ArgumentException("音频文件为空。");
        var channels = _format.Channels;
        var count = (int)Math.Min(_length / 4, _format.SampleRate * channels / 4);
        count -= count % channels;
        _head = new float[count];
    }

    private void OpenReader()
    {
        _reader = new AudioFileReader(_path);
        var read = 0;
        while (read < _head.Length)
        {
            var next = _reader.Read(_head, read, _head.Length - read);
            if (next == 0) break;
            read += next;
        }
        if (read != _head.Length) Array.Resize(ref _head, read);
        _reader.Position = 0;
    }

    public WaveFormat WaveFormat => _format;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_reader is null) OpenReader();
        var written = 0;
        while (written < count)
        {
            var read = _reader!.Read(buffer, offset + written, count - written);
            if (read == 0)
            {
                _reader.Position = _head.Length * sizeof(float);
                _position = _head.Length;
                continue;
            }
            for (var index = 0; index < read; index++)
            {
                var fadeIndex = _position + index - (_length - _head.Length);
                if (fadeIndex < 0 || fadeIndex >= _head.Length) continue;
                var blend = (float)fadeIndex / _head.Length;
                buffer[offset + written + index] = buffer[offset + written + index] * (1 - blend) + _head[fadeIndex] * blend;
            }
            _position += read;
            written += read;
        }
        return written;
    }

    public void Dispose() => _reader?.Dispose();
}

public sealed class FocusAudio : IDisposable
{
    private WaveOutEvent? _output;
    private CrossfadeLoop? _loop;
    private VolumeSampleProvider? _volume;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public void Load(string path, float volume)
    {
        Dispose();
        _loop = new CrossfadeLoop(path);
        _volume = new VolumeSampleProvider(_loop) { Volume = volume };
        _output = new WaveOutEvent();
        _output.PlaybackStopped += (_, args) => PlaybackStopped?.Invoke(this, args);
        _output.Init(_volume);
    }
    public void Play() => _output?.Play();
    public void Pause() => _output?.Pause();
    public void SetVolume(float volume) { if (_volume is not null) _volume.Volume = volume; }
    public void Dispose()
    {
        _output?.Dispose(); _output = null;
        _loop?.Dispose(); _loop = null; _volume = null;
    }
}
