using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Object = Java.Lang.Object;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Tracks the foreground <see cref="Activity"/> so the scanner can launch without the consumer wiring
/// anything up. Bootstrapped by <see cref="StartupContentProvider"/> at process start (the same auto-init
/// trick MAUI Essentials uses), so it captures activities from the very first one.
/// </summary>
static class AndroidPlatform
{
    static Activity? current;

    public static Activity? CurrentActivity => current;

    internal static void Register(Application application) =>
        application.RegisterActivityLifecycleCallbacks(new Callbacks());

    sealed class Callbacks : Object, Application.IActivityLifecycleCallbacks
    {
        public void OnActivityResumed(Activity activity) => current = activity;
        public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) => current = activity;
        public void OnActivityStarted(Activity activity) => current = activity;

        public void OnActivityPaused(Activity activity)
        {
            if (ReferenceEquals(current, activity))
                current = null;
        }

        public void OnActivityStopped(Activity activity) { }
        public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
        public void OnActivityDestroyed(Activity activity) { }
    }
}

/// <summary>
/// Zero-config initializer: a <see cref="ContentProvider"/> is instantiated by Android before any activity,
/// so registering lifecycle callbacks here guarantees <see cref="AndroidPlatform.CurrentActivity"/> is populated.
/// </summary>
[ContentProvider([Authority], Exported = false, MultiProcess = true)]
class StartupContentProvider : ContentProvider
{
    // ${applicationId} is substituted by the manifest merger, keeping the authority unique per app
    // (ContentProvider authorities must be globally unique across the device).
    const string Authority = "${applicationId}.shiny.documentintelligence.startup";

    public override bool OnCreate()
    {
        if (this.Context?.ApplicationContext is Application application)
            AndroidPlatform.Register(application);
        return true;
    }

    public override ICursor? Query(Android.Net.Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;
    public override string? GetType(Android.Net.Uri uri) => null;
    public override Android.Net.Uri? Insert(Android.Net.Uri uri, Android.Content.ContentValues? values) => null;
    public override int Delete(Android.Net.Uri uri, string? selection, string[]? selectionArgs) => 0;
    public override int Update(Android.Net.Uri uri, Android.Content.ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}
