using Android.Content;
using Android.Runtime;
using Google.MLKit.Vision.Documentscanner;

namespace Shiny.DocumentIntelligence;

/// <summary>Android scanner backed by ML Kit's <c>GmsDocumentScanning</c> modal flow.</summary>
public class DocumentScanner : IDocumentScanner
{
    public bool IsSupported => true;

    public async Task<DocumentScanResult> ScanAsync(DocumentScanRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new DocumentScanRequest();
        var activity = AndroidPlatform.CurrentActivity
            ?? throw new InvalidOperationException("No current Activity is available to launch the scanner.");

        var wantsPdf = request.Formats.HasFlag(DocumentScanFormats.Pdf);
        var formats = wantsPdf
            ? new[] { GmsDocumentScannerOptions.ResultFormatJpeg, GmsDocumentScannerOptions.ResultFormatPdf }
            : new[] { GmsDocumentScannerOptions.ResultFormatJpeg };

        var options = new GmsDocumentScannerOptions.Builder()
            .SetGalleryImportAllowed(request.AllowGalleryImport)
            .SetPageLimit(Math.Max(1, request.PageLimit))
            .SetResultFormats(formats[0], formats.Skip(1).ToArray())
            .SetScannerMode(GmsDocumentScannerOptions.ScannerModeFull)
            .Build();

        var scanner = GmsDocumentScanning.GetClient(options);

        // GetStartScanIntent -> IntentSender (async via Play Services Task).
        var senderObj = await scanner.GetStartScanIntent(activity).AsAsync().ConfigureAwait(false);
        var intentSender = senderObj?.JavaCast<IntentSender>()
            ?? throw new InvalidOperationException("ML Kit did not return a scan IntentSender.");

        cancellationToken.ThrowIfCancellationRequested();

        // Launch via the proxy activity and await its result.
        var pending = new TaskCompletionSource<Intent?>();
        DocumentScannerActivity.PendingIntentSender = intentSender;
        DocumentScannerActivity.Pending = pending;
        using (cancellationToken.Register(() => pending.TrySetResult(null)))
        {
            activity.StartActivity(new Intent(activity, typeof(DocumentScannerActivity)));
            var data = await pending.Task.ConfigureAwait(false);
            if (data is null)
                return DocumentScanResult.Cancelled;

            return ReadResult(GmsDocumentScanningResult.FromActivityResultIntent(data), wantsPdf);
        }
    }

    static DocumentScanResult ReadResult(GmsDocumentScanningResult? result, bool wantsPdf)
    {
        if (result is null)
            return DocumentScanResult.Cancelled;

        var resolver = Android.App.Application.Context.ContentResolver!;
        var pages = new List<DocumentScannedPage>();
        if (result.Pages is { } resultPages)
        {
            foreach (var page in resultPages)
            {
                var bytes = ReadUri(resolver, page.ImageUri);
                if (bytes is not null)
                    pages.Add(new DocumentScannedPage(bytes, "image/jpeg"));
            }
        }

        byte[]? pdf = null;
        if (wantsPdf && result.GetPdf()?.Uri is { } pdfUri)
            pdf = ReadUri(resolver, pdfUri);

        return DocumentScanResult.Completed(pages, pdf);
    }

    static byte[]? ReadUri(ContentResolver resolver, Android.Net.Uri? uri)
    {
        if (uri is null)
            return null;
        using var stream = resolver.OpenInputStream(uri);
        if (stream is null)
            return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
