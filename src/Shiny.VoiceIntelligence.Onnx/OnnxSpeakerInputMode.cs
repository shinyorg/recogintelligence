namespace Shiny.VoiceIntelligence.Onnx;

/// <summary>
/// How a speaker model's input tensor must be fed. <see cref="Auto"/> is right for every export seen so
/// far — it reads the model's own declared input rank — so only set this explicitly when a model's
/// metadata lies about its shape.
/// </summary>
public enum OnnxSpeakerInputMode
{
    /// <summary>Decide from the model's declared input rank: 3 = fbank features, 2 = raw waveform.</summary>
    Auto,

    /// <summary>Feed the raw mono waveform as <c>[1, samples]</c> (classic ECAPA/x-vector exports).</summary>
    Waveform,

    /// <summary>Feed 80-bin kaldi fbank as <c>[1, frames, 80]</c> (WeSpeaker / sherpa-onnx exports).</summary>
    Fbank80
}
