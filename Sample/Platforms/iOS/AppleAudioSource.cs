using System.Runtime.InteropServices;
using AVFoundation;
using Microsoft.Extensions.Logging;

namespace Sample.Features.Voice.Audio;

/// <summary>
/// iOS microphone capture via <see cref="AVAudioEngine"/>. Taps the input node in its native float32
/// format and streams the raw samples; <see cref="VoiceRecorder"/> resamples to 16 kHz. Vendored/adapted
/// from Shiny.Audio.
/// </summary>
public class AppleAudioSource(ILogger<AppleAudioSource> logger) : IAudioSource
{
    AVAudioEngine? audioEngine;
    PipeStream? pipe;

    public int SampleRate { get; private set; } = 16000;

    public Task<Stream> StartCaptureAsync(CancellationToken cancellationToken = default)
    {
        this.audioEngine = new AVAudioEngine();
        var inputNode = this.audioEngine.InputNode;
        var inputFormat = inputNode.GetBusOutputFormat(0);
        this.SampleRate = (int)inputFormat.SampleRate;

        var stream = new PipeStream();
        this.pipe = stream;

        // Tap in the node's native format (float32). Take the first channel's raw bytes (mono-ize).
        inputNode.InstallTapOnBus(0, 4096, inputFormat, (buffer, when) =>
        {
            var audioBuffer = buffer.AudioBufferList[0];
            if (audioBuffer.Data != IntPtr.Zero && audioBuffer.DataByteSize > 0)
            {
                var data = new byte[audioBuffer.DataByteSize];
                Marshal.Copy(audioBuffer.Data, data, 0, data.Length);
                try
                {
                    stream.Write(data, 0, data.Length);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        });

        var audioSession = AVAudioSession.SharedInstance();

        // No AllowBluetooth: a paired BT headset drags the route onto 8 kHz HFP, so the captured bandwidth
        // would silently depend on what happens to be connected — fatal when comparing a recording made
        // today against a voiceprint enrolled last week.
        audioSession.SetCategory(AVAudioSessionCategory.Record, AVAudioSessionCategoryOptions.DefaultToSpeaker, out _);

        // Measurement, NOT VoiceChat. VoiceChat turns on Apple's voice processing — AGC, noise suppression,
        // echo cancellation — which is adaptive, non-linear, and specifically designed to normalise away
        // speaker and channel characteristics. That is exactly the information a speaker embedding encodes,
        // and because it adapts per session two recordings of the same person come out differently.
        // Measurement disables that chain and gives the rawest mic signal iOS will hand over.
        audioSession.SetMode(AVAudioSessionMode.Measurement.GetConstant()!, out _);
        audioSession.SetActive(true, out _);

        this.audioEngine.Prepare();
        this.audioEngine.StartAndReturnError(out var error);
        if (error != null)
            throw new InvalidOperationException($"Failed to start audio engine: {error.LocalizedDescription}");

        var route = audioSession.CurrentRoute?.Inputs?.FirstOrDefault();
        var msg = $"[Audio] capture started {this.SampleRate} Hz, {inputFormat.ChannelCount} ch, " +
                  $"interleaved={inputFormat.Interleaved}, route={route?.PortType ?? "?"} ({route?.PortName ?? "?"})";
        logger.LogDebug(msg);
        Console.WriteLine(msg);   // console.out is what `maui devflow logs` surfaces
        return Task.FromResult<Stream>(stream);
    }

    public Task StopCaptureAsync()
    {
        if (this.audioEngine != null)
        {
            if (this.audioEngine.Running)
            {
                this.audioEngine.Stop();
                this.audioEngine.InputNode.RemoveTapOnBus(0);
            }

            var session = AVAudioSession.SharedInstance();
            session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _);
        }

        this.pipe?.Dispose();
        this.pipe = null;
        logger.LogDebug("Apple audio capture stopped");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await this.StopCaptureAsync();
        GC.SuppressFinalize(this);
    }
}
