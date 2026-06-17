using Android.Gms.Tasks;

namespace Shiny.DocumentIntelligence;

/// <summary>Awaits a Google Play Services <see cref="Android.Gms.Tasks.Task"/> via success/failure listeners.</summary>
static class GmsTasks
{
    public static Task<Java.Lang.Object?> AsAsync(this Android.Gms.Tasks.Task task)
    {
        var tcs = new TaskCompletionSource<Java.Lang.Object?>();
        task.AddOnSuccessListener(new SuccessListener(tcs));
        task.AddOnFailureListener(new FailureListener(tcs));
        task.AddOnCanceledListener(new CanceledListener(tcs));
        return tcs.Task;
    }

    sealed class SuccessListener(TaskCompletionSource<Java.Lang.Object?> tcs) : Java.Lang.Object, IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result) => tcs.TrySetResult(result);
    }

    sealed class FailureListener(TaskCompletionSource<Java.Lang.Object?> tcs) : Java.Lang.Object, IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception e) => tcs.TrySetException(new InvalidOperationException(e.Message));
    }

    sealed class CanceledListener(TaskCompletionSource<Java.Lang.Object?> tcs) : Java.Lang.Object, IOnCanceledListener
    {
        public void OnCanceled() => tcs.TrySetCanceled();
    }
}
