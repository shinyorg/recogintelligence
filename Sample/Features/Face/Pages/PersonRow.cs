namespace Sample.Features.Face.Pages;

/// <summary>One row per enrolled name (shots collapsed into a single entry).</summary>
public record PersonRow(string Name, int Count, ImageSource? Thumb)
{
    public string CountText => this.Count == 1 ? "1 shot" : $"{this.Count} shots";
}
