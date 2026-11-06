namespace Shiny.FaceIntelligence;

/// <summary>A successfully stored shot from one guided run, retaining its original instruction index.
/// The image is the upright analysis JPEG, not the person's display thumbnail.</summary>
public sealed class FaceEnrollmentCapture
{
    readonly byte[] imageData;

    public FaceEnrollmentCapture(int stepIndex, string personId, byte[] imageData, int imageWidth, int imageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stepIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(personId);
        ArgumentNullException.ThrowIfNull(imageData);
        this.StepIndex = stepIndex;
        this.PersonId = personId;
        this.imageData = imageData.ToArray();
        this.ImageWidth = imageWidth;
        this.ImageHeight = imageHeight;
    }

    public int StepIndex { get; }
    public string PersonId { get; }
    public int ImageWidth { get; }
    public int ImageHeight { get; }
    /// <summary>Returns an independent copy so later camera buffers/callers cannot mutate this result.</summary>
    public byte[] ImageData => this.imageData.ToArray();
}
