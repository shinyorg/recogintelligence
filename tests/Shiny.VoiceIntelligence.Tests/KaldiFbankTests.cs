using Shiny.VoiceIntelligence.Onnx;
using Xunit;

namespace Shiny.VoiceIntelligence.Tests;

/// <summary>
/// Guards the fbank front end that feature-input speaker models (WeSpeaker/sherpa CAM++) depend on. These
/// need no ONNX model — they pin the feature contract itself, which is where a silent regression would
/// otherwise only show up as "recognition stopped matching".
/// </summary>
public class KaldiFbankTests
{
    static float[] Tone(int samples, float hz = 220f)
    {
        var x = new float[samples];
        for (var i = 0; i < samples; i++)
            x[i] = 0.25f * MathF.Sin(2 * MathF.PI * hz * i / 16000f);
        return x;
    }

    [Fact]
    public void Produces_80_Bins_Per_Frame()
    {
        var feats = KaldiFbank.Compute(Tone(16000));
        Assert.NotEmpty(feats);
        Assert.All(feats, f => Assert.Equal(KaldiFbank.NumBins, f.Length));
    }

    [Fact]
    public void Frame_Count_Follows_Kaldi_SnipEdges_False()
    {
        // snip_edges=false => round(n / shift), i.e. (n + shift/2) / shift with a 160-sample shift.
        foreach (var seconds in new[] { 1, 2, 5 })
        {
            var n = 16000 * seconds;
            var expected = (n + 80) / 160;
            Assert.Equal(expected, KaldiFbank.Compute(Tone(n)).Length);
        }
    }

    [Fact]
    public void Is_Deterministic()
    {
        var audio = Tone(16000 * 2);
        var a = KaldiFbank.Compute(audio);
        var b = KaldiFbank.Compute(audio);

        Assert.Equal(a.Length, b.Length);
        for (var t = 0; t < a.Length; t++)
            Assert.Equal(a[t], b[t]);
    }

    [Fact]
    public void Louder_Audio_Raises_Log_Energy()
    {
        // Features are raw log-mel with NO cepstral mean normalization — a deliberate WeSpeaker choice that
        // was measured (adding CMN made same-speaker distances worse). If someone adds CMN, level dependence
        // disappears and this fails, which is the point.
        var quiet = KaldiFbank.Compute(Tone(16000, 220f));
        var loud = KaldiFbank.Compute(Tone(16000, 220f).Select(v => v * 4f).ToArray());

        var quietMean = quiet.SelectMany(f => f).Average();
        var loudMean = loud.SelectMany(f => f).Average();
        Assert.True(loudMean > quietMean, $"expected louder audio to raise log energy ({loudMean} vs {quietMean})");
    }

    [Fact]
    public void Empty_Audio_Yields_No_Frames()
        => Assert.Empty(KaldiFbank.Compute([]));
}
