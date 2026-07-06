using Android;
using Android.Content.PM;
using Android.Media;
using Microsoft.Extensions.Logging;

namespace Sample.Features.Voice.Audio;

/// <summary>
/// Android microphone capture via <see cref="AudioRecord"/> configured at 16 kHz / 16-bit / mono. Converts
/// each PCM16 sample to float32 before streaming so the source matches the shared float32 contract.
/// Vendored/adapted from Shiny.Audio (permission is handled by <c>VoiceRecorder</c> via MAUI).
/// </summary>
public class AndroidAudioSource(ILogger<AndroidAudioSource> logger) : IAudioSource
{
    AudioRecord? audioRecord;
    CancellationTokenSource? recordingCts;
    PipeStream? pipe;

    public int SampleRate => 16000;

    public Task<System.IO.Stream> StartCaptureAsync(CancellationToken cancellationToken = default)
    {
        const int sampleRate = 16000;
        const ChannelIn channelConfig = ChannelIn.Mono;
        const Encoding audioFormat = Encoding.Pcm16bit;

        if (global::Android.App.Application.Context.CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
            throw new InvalidOperationException("RECORD_AUDIO permission has not been granted.");

        var bufferSize = AudioRecord.GetMinBufferSize(sampleRate, channelConfig, audioFormat);
        if (bufferSize <= 0)
            bufferSize = 4096;

        this.audioRecord = new AudioRecord(AudioSource.Mic, sampleRate, channelConfig, audioFormat, bufferSize);
        if (this.audioRecord.State != State.Initialized)
            throw new InvalidOperationException($"Failed to initialize AudioRecord (state={this.audioRecord.State}). The mic may be in use.");

        this.pipe = new PipeStream();
        this.recordingCts = new CancellationTokenSource();
        this.audioRecord.StartRecording();

        // Capture locals — StopCaptureAsync nulls the fields concurrently with this loop.
        var token = this.recordingCts.Token;
        var record = this.audioRecord;
        var sink = this.pipe;

        _ = Task.Run(() =>
        {
            var pcm = new short[bufferSize / 2];
            var floats = new byte[pcm.Length * sizeof(float)];
            while (!token.IsCancellationRequested)
            {
                var samplesRead = record.Read(pcm, 0, pcm.Length);
                if (samplesRead > 0)
                {
                    for (var i = 0; i < samplesRead; i++)
                    {
                        var f = pcm[i] / 32768f;
                        var bytes = BitConverter.GetBytes(f);
                        Buffer.BlockCopy(bytes, 0, floats, i * sizeof(float), sizeof(float));
                    }
                    try
                    {
                        sink.Write(floats, 0, samplesRead * sizeof(float));
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break; // pipe writer completed by Stop
                    }
                }
            }
        }, token);

        logger.LogDebug("Android audio capture started at {Rate} Hz", sampleRate);
        return Task.FromResult<System.IO.Stream>(this.pipe);
    }

    public Task StopCaptureAsync()
    {
        this.recordingCts?.Cancel();
        this.recordingCts?.Dispose();
        this.recordingCts = null;

        if (this.audioRecord != null)
        {
            if (this.audioRecord.RecordingState == RecordState.Recording)
                this.audioRecord.Stop();
            this.audioRecord.Release();
            this.audioRecord = null;
        }

        this.pipe?.Dispose();
        this.pipe = null;
        logger.LogDebug("Android audio capture stopped");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await this.StopCaptureAsync();
        GC.SuppressFinalize(this);
    }
}
