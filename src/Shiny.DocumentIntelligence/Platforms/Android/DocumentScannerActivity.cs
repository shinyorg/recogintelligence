using Android.App;
using Android.Content;
using Android.OS;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Transparent proxy activity that launches the ML Kit scanner's <see cref="IntentSender"/> and routes the
/// result back to <see cref="DocumentScanner"/>. Using a dedicated activity avoids the AndroidX
/// <c>ActivityResultLauncher</c> "register before RESUMED" constraint that a lazily-invoked service hits.
/// </summary>
[Activity(Theme = "@android:style/Theme.Translucent.NoTitleBar", ExcludeFromRecents = true)]
class DocumentScannerActivity : Activity
{
    const int RequestCode = 0x5CA4; // "SCAN"

    internal static IntentSender? PendingIntentSender;
    internal static TaskCompletionSource<Intent?>? Pending;

    bool launched;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var sender = PendingIntentSender;
        PendingIntentSender = null;
        if (sender is null)
        {
            this.Finish();
            return;
        }

        try
        {
            this.launched = true;
            this.StartIntentSenderForResult(sender, RequestCode, null, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Pending?.TrySetException(ex);
            Pending = null;
            this.Finish();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == RequestCode)
        {
            // null data signals cancel/failure to the awaiting ScanAsync.
            Pending?.TrySetResult(resultCode == Result.Ok ? data : null);
            Pending = null;
            this.Finish();
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // Safety net: if we navigated away without a result, unblock the caller.
        if (this.launched)
            Pending?.TrySetResult(null);
    }
}
