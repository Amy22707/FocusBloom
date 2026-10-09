using System.IO;
using FocusBloom.Desktop.Presentation;
using NAudio.Wave;
using Xunit;

namespace FocusBloom.Tests;

public sealed class FocusAudioTests
{
    [Theory]
    [InlineData("rain.mp3")]
    [InlineData("fire.wav")]
    [InlineData("white.wav")]
    [InlineData("pink.wav")]
    [InlineData("brown.wav")]
    public void OfflineSoundDecodesAndLoopsBeyondTheEnd(string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Focus", "Audio", filename);
        using var reader = new AudioFileReader(path);
        Assert.True(reader.TotalTime > TimeSpan.FromSeconds(1));
        using var loop = new CrossfadeLoop(path);
        var samples = new float[4096];
        var remaining = reader.Length / sizeof(float) + loop.WaveFormat.SampleRate;
        var audible = false;
        while (remaining > 0)
        {
            var read = loop.Read(samples, 0, samples.Length);
            Assert.Equal(samples.Length, read);
            Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
            audible |= samples.Any(sample => sample != 0);
            remaining -= read;
        }
        Assert.True(audible);
    }
}
