using Microsoft.Maui.Graphics;

namespace Shiny.VoiceIntelligence.Maui;

/// <summary>
/// The segmented input-level meter drawn during recording by <see cref="VoiceEnrollmentView"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is scaled in dB, not linearly.</b> Speech RMS measured on an iPhone built-in mic with voice
/// processing off sits around 0.014–0.033, and the "too quiet" gate is 0.004 — all of which is squashed into
/// the bottom 3% of a linear 0..1 bar, where nothing appears to move. A −60…0 dBFS scale puts normal speech
/// near the middle and the gate at about a fifth, which is the only way the meter says anything useful.
/// </para>
/// <para>
/// <b>The threshold tick is the point of the whole control.</b> A bare level bar tells you there is sound; a
/// bar with <see cref="VoiceEnrollmentOptions.MinSpeechLevel"/> marked on it tells you whether you are loud
/// enough to be <i>accepted</i> — which is the actual failure everyone hits. Segments below the tick are
/// amber (audible but too quiet to keep), above it green, and the top of the range red as clipping nears.
/// </para>
/// </remarks>
class VuMeterDrawable : IDrawable
{
    /// <summary>Current input level, linear RMS in [0, 1], as reported by the recorder.</summary>
    public float Level { get; set; }

    /// <summary>The "too quiet" gate, linear RMS — drawn as a tick.</summary>
    public float Threshold { get; set; } = 0.004f;

    /// <summary>Recent maximum, so a brief peak stays visible long enough to see.</summary>
    public float Peak { get; set; }

    /// <summary>Segments light only while recording; otherwise the track is drawn empty.</summary>
    public bool IsActive { get; set; }

    const int Segments = 24;
    const float FloorDb = -60f;
    const float RedFrom = 0.86f;

    static readonly Color TrackColor = Color.FromArgb("#1F000000");
    static readonly Color QuietColor = Color.FromArgb("#D08A20");
    static readonly Color GoodColor = Color.FromArgb("#2E7D32");
    static readonly Color HotColor = Color.FromArgb("#C62828");
    static readonly Color TickColor = Color.FromArgb("#8A000000");

    public void Draw(ICanvas canvas, RectF rect)
    {
        var lit = this.IsActive ? Position(this.Level) : 0f;
        var peak = this.IsActive ? Position(this.Peak) : 0f;
        var gate = Position(this.Threshold);

        const float gap = 2f;
        var segmentWidth = (rect.Width - ((Segments - 1) * gap)) / Segments;

        for (var i = 0; i < Segments; i++)
        {
            // The fraction this segment represents — a segment lights once the level reaches its top edge.
            var frac = (i + 1) / (float)Segments;
            var x = rect.X + (i * (segmentWidth + gap));

            canvas.FillColor = frac <= lit ? ColorFor(frac, gate) : TrackColor;
            canvas.FillRoundedRectangle(x, rect.Y, segmentWidth, rect.Height, 2f);

            // Peak hold: outline the segment the loudest recent level reached.
            if (peak > lit && frac <= peak && frac > peak - (1f / Segments))
            {
                canvas.StrokeColor = ColorFor(frac, gate);
                canvas.StrokeSize = 1.5f;
                canvas.DrawRoundedRectangle(x, rect.Y, segmentWidth, rect.Height, 2f);
            }
        }

        // The gate. Anything left of this is rejected as too quiet however much it moves.
        var tickX = rect.X + (gate * rect.Width);
        canvas.StrokeColor = TickColor;
        canvas.StrokeSize = 1.5f;
        canvas.DrawLine(tickX, rect.Y - 2f, tickX, rect.Y + rect.Height + 2f);
    }

    static Color ColorFor(float frac, float gate) =>
        frac >= RedFrom ? HotColor :
        frac < gate ? QuietColor :
        GoodColor;

    /// <summary>Linear RMS → 0..1 across the <see cref="FloorDb"/>…0 dBFS range.</summary>
    static float Position(float rms)
    {
        if (rms <= 0f)
            return 0f;

        var db = 20f * MathF.Log10(rms);
        return Math.Clamp((db - FloorDb) / -FloorDb, 0f, 1f);
    }
}
