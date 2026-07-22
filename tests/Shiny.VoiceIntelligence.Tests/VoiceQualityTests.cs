using Xunit;

namespace Shiny.VoiceIntelligence.Tests;

/// <summary>
/// <see cref="VoiceQuality"/> against synthesized signals — no mic, no model. These pin the behavior the
/// enrollment gates depend on: that silence isn't counted as speech, that level is measured over the
/// speech and not over the pauses, and that clipping is seen.
/// </summary>
public class VoiceQualityTests
{
    const int Rate = 16000;

    /// <summary>Tone at <paramref name="amplitude"/> for <paramref name="toneSeconds"/>, then silence.</summary>
    static float[] Tone(float amplitude, float toneSeconds, float silenceSeconds = 0f, float noise = 0f)
    {
        var total = (int)((toneSeconds + silenceSeconds) * Rate);
        var toneSamples = (int)(toneSeconds * Rate);
        var buffer = new float[total];
        var rng = new Random(42);

        for (var i = 0; i < total; i++)
        {
            var s = i < toneSamples ? amplitude * MathF.Sin(2 * MathF.PI * 220f * i / Rate) : 0f;
            if (noise > 0)
                s += (float)((rng.NextDouble() - 0.5) * 2 * noise);
            buffer[i] = s;
        }
        return buffer;
    }

    [Fact]
    public void Empty_Buffer_Measures_Nothing()
    {
        var m = VoiceQuality.Measure([], Rate);

        Assert.Equal(0f, m.Seconds);
        Assert.Equal(0f, m.SpeechSeconds);
    }

    [Fact]
    public void Silence_Has_No_Speech()
    {
        var m = VoiceQuality.Measure(new float[Rate * 3], Rate);

        Assert.Equal(3f, m.Seconds, 2);
        Assert.Equal(0f, m.SpeechSeconds, 2);
        Assert.Equal(0f, m.SpeechRms, 4);
    }

    [Fact]
    public void Speech_Duration_Excludes_The_Silent_Tail()
    {
        var m = VoiceQuality.Measure(Tone(0.1f, toneSeconds: 3f, silenceSeconds: 2f), Rate);

        Assert.Equal(5f, m.Seconds, 2);
        Assert.Equal(3f, m.SpeechSeconds, 1);
    }

    [Fact]
    public void Level_Is_Measured_Over_The_Speech_Not_The_Pauses()
    {
        // Half tone, half silence: whole-buffer RMS is dragged down by the pause, speech RMS is not.
        // A gate on the former would reject someone who simply paused before starting.
        var m = VoiceQuality.Measure(Tone(0.1f, toneSeconds: 2f, silenceSeconds: 2f), Rate);

        Assert.Equal(0.0707f, m.SpeechRms, 3);   // RMS of a 0.1-amplitude sine = 0.1/sqrt(2)
        Assert.True(m.Rms < m.SpeechRms * 0.8f, $"whole-buffer RMS {m.Rms} should sit well below speech RMS {m.SpeechRms}");
    }

    [Fact]
    public void Clipping_Is_Detected()
    {
        var clean = VoiceQuality.Measure(Tone(0.5f, 2f), Rate);
        var loud = VoiceQuality.Measure(Tone(2f, 2f).Select(s => Math.Clamp(s, -1f, 1f)).ToArray(), Rate);

        Assert.Equal(0f, clean.ClippedFraction, 4);
        Assert.True(loud.ClippedFraction > 0.01f, $"expected clipping, got {loud.ClippedFraction}");
        Assert.Equal(1f, loud.Peak, 3);
    }

    [Fact]
    public void Snr_Falls_As_Noise_Rises()
    {
        var quietRoom = VoiceQuality.Measure(Tone(0.1f, 2f, 1f, noise: 0.0005f), Rate);
        var noisyRoom = VoiceQuality.Measure(Tone(0.1f, 2f, 1f, noise: 0.05f), Rate);

        Assert.True(quietRoom.SnrDb > 30, $"quiet room SNR was only {quietRoom.SnrDb} dB");
        Assert.True(noisyRoom.SnrDb < quietRoom.SnrDb - 15, $"noisy {noisyRoom.SnrDb} dB vs quiet {quietRoom.SnrDb} dB");
    }
}
