using CoreFoundation;
using Foundation;
using UIKit;
using VisionKit;

namespace Shiny.DocumentIntelligence;

/// <summary>iOS / Mac Catalyst scanner backed by VisionKit's <see cref="VNDocumentCameraViewController"/>.</summary>
public class DocumentScanner : IDocumentScanner
{
    // VisionKit needs a camera; the native +isSupported isn't surfaced by the binding, so gate on OS
    // version (iOS/Mac Catalyst 13+). A device without a usable camera surfaces via DidFail at scan time.
    public bool IsSupported =>
        OperatingSystem.IsIOSVersionAtLeast(13) || OperatingSystem.IsMacCatalystVersionAtLeast(13);

    public Task<DocumentScanResult> ScanAsync(DocumentScanRequest? request = null, CancellationToken cancellationToken = default)
    {
        if (!this.IsSupported)
            throw new PlatformNotSupportedException("VisionKit document scanning is not available on this device.");

        var tcs = new TaskCompletionSource<DocumentScanResult>();

        // Must touch UIKit on the main thread.
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            var presenter = ApplePlatform.GetTopViewController();
            if (presenter is null)
            {
                tcs.TrySetException(new InvalidOperationException("No presenting UIViewController is available."));
                return;
            }

            var controller = new VNDocumentCameraViewController();
            // The delegate is captured by the closure below so it stays alive until the scan completes.
            var del = new ScanDelegate(tcs);
            controller.Delegate = del;

            cancellationToken.Register(() => DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                if (controller.PresentingViewController is not null)
                    controller.DismissViewController(true, () => tcs.TrySetResult(DocumentScanResult.Cancelled));
            }));

            presenter.PresentViewController(controller, true, null);
        });

        return tcs.Task;
    }

    sealed class ScanDelegate(TaskCompletionSource<DocumentScanResult> tcs) : VNDocumentCameraViewControllerDelegate
    {
        public override void DidFinish(VNDocumentCameraViewController controller, VNDocumentCameraScan scan)
        {
            var pages = new List<DocumentScannedPage>((int)scan.PageCount);
            for (nuint i = 0; i < scan.PageCount; i++)
            {
                using var image = scan.GetImage(i);
                using var png = image.AsPNG();
                if (png is not null)
                    pages.Add(new DocumentScannedPage(png.ToArray(), "image/png"));
            }
            controller.DismissViewController(true, null);
            tcs.TrySetResult(DocumentScanResult.Completed(pages));
        }

        public override void DidCancel(VNDocumentCameraViewController controller)
        {
            controller.DismissViewController(true, null);
            tcs.TrySetResult(DocumentScanResult.Cancelled);
        }

        public override void DidFail(VNDocumentCameraViewController controller, NSError error)
        {
            controller.DismissViewController(true, null);
            tcs.TrySetException(new InvalidOperationException($"Document scan failed: {error.LocalizedDescription}"));
        }
    }
}
