namespace Sample.Features.Voice;

/// <summary>
/// Kaldi-compatible 80-bin log-Mel filterbank matching the WeSpeaker / sherpa-onnx feature pipeline the
/// bundled speaker model (<c>wespeaker_en_voxceleb_CAM++_LM.onnx</c>) was trained with. Verified
/// bit-exact (cosine 1.000000) against sherpa-onnx's own extractor on 2–4 s clips.
///
/// Config (do not "tidy" these — each was pinned by matching the reference extractor):
/// 25 ms / 10 ms frames, dither = 0, remove-DC, preemphasis 0.97, <b>povey</b> window, power spectrum,
/// mel = 1127·ln(1+f/700) over [20, <b>7600</b>] Hz (NOT nyquist), n_fft = 512, natural log floored at
/// FLT_EPSILON, <b>snip_edges = false</b> (rounded frame count, frames centred and reflection-padded at
/// the edges), and <b>no</b> cepstral mean normalization. Input samples are float [-1, 1] and are scaled
/// to the int16 range because the model's <c>normalize_samples=0</c> metadata says so.
/// </summary>
public static class KaldiFbank
{
    const int SampleRate = 16000;
    const int FrameLength = 400;   // 25 ms
    const int FrameShift  = 160;   // 10 ms
    const int NFft        = 512;   // next pow2 of 400
    const int NumBins     = 80;
    const int NumFftBins  = NFft / 2 + 1; // 257
    const float LowFreq   = 20f;
    const float HighFreq  = 7600f; // WeSpeaker/sherpa config (nyquist-400), NOT 8000
    const float Preemph   = 0.97f;
    const float Eps       = 1.1920929e-7f; // FLT_EPSILON

    static readonly float[] Window = BuildPoveyWindow();
    static readonly float[][] MelWeights = BuildMelBanks(); // [80][257]

    static float[] BuildPoveyWindow()
    {
        var w = new float[FrameLength];
        for (var i = 0; i < FrameLength; i++)
        {
            var hann = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FrameLength - 1));
            w[i] = (float)Math.Pow(hann, 0.85); // povey = hann^0.85
        }
        return w;
    }

    static float MelScale(float f) => 1127f * MathF.Log(1f + f / 700f);

    static float[][] BuildMelBanks()
    {
        var melLow = MelScale(LowFreq);
        var melHigh = MelScale(HighFreq);
        var delta = (melHigh - melLow) / (NumBins + 1);
        var banks = new float[NumBins][];
        for (var m = 0; m < NumBins; m++)
        {
            banks[m] = new float[NumFftBins];
            var left = melLow + m * delta;
            var center = melLow + (m + 1) * delta;
            var right = melLow + (m + 2) * delta;
            for (var k = 0; k < NumFftBins; k++)
            {
                var freq = (float)k * SampleRate / NFft;
                var mel = MelScale(freq);
                var w = 0f;
                if (mel >= left && mel <= center) w = (mel - left) / (center - left);
                else if (mel > center && mel <= right) w = (right - mel) / (right - center);
                if (w > 0f) banks[m][k] = w;
            }
        }
        return banks;
    }

    /// <summary>Compute fbank features as [numFrames][80] from mono [-1,1] samples at 16 kHz.</summary>
    public static float[][] Compute(ReadOnlySpan<float> samplesMinus1To1)
    {
        var n = samplesMinus1To1.Length;
        if (n < 1) return [];

        // Scale to int16 range (model metadata: normalize_samples=0).
        var s = new float[n];
        for (var i = 0; i < n; i++) s[i] = samplesMinus1To1[i] * 32768f;

        // snip_edges=false: rounded frame count; frames are centred and reflection-padded at the edges.
        var numFrames = (n + FrameShift / 2) / FrameShift;
        if (numFrames < 1) return [];
        var feats = new float[numFrames][];

        var re = new float[NFft];
        var im = new float[NFft];
        var power = new float[NumFftBins];
        var frame = new float[FrameLength];
        for (var t = 0; t < numFrames; t++)
        {
            // snip_edges=false first sample = t*shift + shift/2 - frameLength/2 (may be negative / past end).
            var start = t * FrameShift + FrameShift / 2 - FrameLength / 2;
            Array.Clear(re, 0, NFft);
            Array.Clear(im, 0, NFft);
            for (var i = 0; i < FrameLength; i++) frame[i] = s[Reflect(start + i, n)];

            // remove DC offset
            var mean = 0f;
            for (var i = 0; i < FrameLength; i++) mean += frame[i];
            mean /= FrameLength;
            for (var i = 0; i < FrameLength; i++) frame[i] -= mean;

            // preemphasis (i from end down to 1, then i=0)
            for (var i = FrameLength - 1; i > 0; i--) frame[i] -= Preemph * frame[i - 1];
            frame[0] -= Preemph * frame[0];

            // povey window
            for (var i = 0; i < FrameLength; i++) re[i] = frame[i] * Window[i];

            Fft(re, im);

            // power spectrum -> mel -> log
            var feat = new float[NumBins];
            for (var k = 0; k < NumFftBins; k++) power[k] = re[k] * re[k] + im[k] * im[k];
            for (var m = 0; m < NumBins; m++)
            {
                var wts = MelWeights[m];
                var e = 0f;
                for (var k = 0; k < NumFftBins; k++) e += wts[k] * power[k];
                if (e < Eps) e = Eps;
                feat[m] = MathF.Log(e);
            }
            feats[t] = feat;
        }

        // NOTE: WeSpeaker/sherpa feed the raw fbank to this model — NO cepstral mean normalization.
        return feats;
    }

    // Reflect an out-of-range sample index back into [0, n) (kaldi snip_edges=false edge handling).
    static int Reflect(int s, int n)
    {
        while (s < 0 || s >= n)
        {
            if (s < 0) s = -s - 1;   // mirror around -0.5
            else s = 2 * n - 1 - s;  // mirror around n-0.5
        }
        return s;
    }

    // Iterative radix-2 Cooley-Tukey FFT; length must be a power of two.
    static void Fft(float[] re, float[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2.0 * Math.PI / len;
            var wRe = (float)Math.Cos(ang);
            var wIm = (float)Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                float curRe = 1f, curIm = 0f;
                for (var k = 0; k < len / 2; k++)
                {
                    var uRe = re[i + k];
                    var uIm = im[i + k];
                    var vRe = re[i + k + len / 2] * curRe - im[i + k + len / 2] * curIm;
                    var vIm = re[i + k + len / 2] * curIm + im[i + k + len / 2] * curRe;
                    re[i + k] = uRe + vRe;
                    im[i + k] = uIm + vIm;
                    re[i + k + len / 2] = uRe - vRe;
                    im[i + k + len / 2] = uIm - vIm;
                    var nRe = curRe * wRe - curIm * wIm;
                    var nIm = curRe * wIm + curIm * wRe;
                    curRe = nRe;
                    curIm = nIm;
                }
            }
        }
    }
}
