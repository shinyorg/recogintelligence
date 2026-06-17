using System.Runtime.Versioning;
using AppKit;
using CoreGraphics;
using CoreImage;
using Foundation;
using ImageIO;
using Vision;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// macOS (AppKit) scanner. AppKit has no first-party document camera (VisionKit's is iOS/Mac Catalyst
/// only), so the user picks image file(s) and we deskew each with Vision's
/// <see cref="VNDetectDocumentSegmentationRequest"/> + Core Image perspective correction.
/// </summary>
public class DocumentScanner : IDocumentScanner
{
    public bool IsSupported => OperatingSystem.IsMacOSVersionAtLeast(12);

    public Task<DocumentScanResult> ScanAsync(DocumentScanRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (!this.IsSupported)
            throw new PlatformNotSupportedException("Vision document segmentation requires macOS 12 or later.");

        request ??= new DocumentScanRequest();
        var tcs = new TaskCompletionSource<DocumentScanResult>();

        // NSOpenPanel and Vision work must run on the main thread.
        NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            try
            {
                using var panel = new NSOpenPanel
                {
                    CanChooseFiles = true,
                    CanChooseDirectories = false,
                    AllowsMultipleSelection = true,
                    AllowedFileTypes = ["png", "jpg", "jpeg", "tiff", "heic"],
                    Title = "Choose document photo(s) to scan"
                };

                if (panel.RunModal() != (nint)1 || panel.Urls.Length == 0)
                {
                    tcs.TrySetResult(DocumentScanResult.Cancelled);
                    return;
                }

                var pages = new List<DocumentScannedPage>(panel.Urls.Length);
                foreach (var url in panel.Urls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = ScanOne(url);
                    if (page is not null)
                        pages.Add(page);
                }
                tcs.TrySetResult(DocumentScanResult.Completed(pages));
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetResult(DocumentScanResult.Cancelled);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    [SupportedOSPlatform("macos12.0")]
    static DocumentScannedPage? ScanOne(NSUrl url)
    {
        using var source = CGImageSource.FromUrl(url);
        using var cgImage = source?.CreateImage(0, null);
        if (cgImage is null)
            return null;

        using var corrected = Deskew(cgImage);
        var bytes = EncodePng(corrected ?? cgImage);
        return bytes is null ? null : new DocumentScannedPage(bytes, "image/png");
    }

    /// <summary>Find the document quad and perspective-correct it. Returns null when no document is detected.</summary>
    [SupportedOSPlatform("macos12.0")]
    static CGImage? Deskew(CGImage cg)
    {
        using var request = new VNDetectDocumentSegmentationRequest();
        using var handler = new VNImageRequestHandler(cg, new NSDictionary());
        if (!handler.Perform([request], out _))
            return null;

        var observation = request.GetResults<VNRectangleObservation>()?.FirstOrDefault();
        if (observation is null)
            return null;

        using var ci = CIImage.FromCGImage(cg);
        nfloat w = ci.Extent.Width, h = ci.Extent.Height;
        CIVector V(CGPoint p) => new(p.X * w, p.Y * h); // Vision is normalized + bottom-left, same as CIImage — no Y-flip.

        // Drive the filter via Core Image keys (KVC) to stay independent of binding property naming.
        using var filter = CIFilter.FromName("CIPerspectiveCorrection")!;
        filter.SetValueForKey(ci, new NSString("inputImage"));
        filter.SetValueForKey(V(observation.TopLeft), new NSString("inputTopLeft"));
        filter.SetValueForKey(V(observation.TopRight), new NSString("inputTopRight"));
        filter.SetValueForKey(V(observation.BottomLeft), new NSString("inputBottomLeft"));
        filter.SetValueForKey(V(observation.BottomRight), new NSString("inputBottomRight"));

        if (filter.ValueForKey(new NSString("outputImage")) is not CIImage output)
            return null;

        using var context = new CIContext();
        return context.CreateCGImage(output, output.Extent);
    }

    static byte[]? EncodePng(CGImage cg)
    {
        using var rep = new NSBitmapImageRep(cg);
        using var png = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary());
        return png?.ToArray();
    }
}
