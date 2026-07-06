namespace Sample.Infrastructure;

/// <summary>
/// Helpers for reading bundled <c>Resources/Raw</c> assets (ONNX models) into memory. Bundled assets
/// aren't real file paths on iOS/Android, so models are loaded as bytes via
/// <see cref="FileSystem.OpenAppPackageFileAsync"/>. A missing asset throws
/// <see cref="FileNotFoundException"/>, which the feature pages surface as a "model missing" message.
/// </summary>
public static class BundledAssets
{
    public static byte[] LoadBundledModel(string assetFileName)
    {
        using var stream = FileSystem.OpenAppPackageFileAsync(assetFileName).GetAwaiter().GetResult();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
