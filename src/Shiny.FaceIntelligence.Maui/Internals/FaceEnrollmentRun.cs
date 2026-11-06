namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// One guided enrollment run, owned by <see cref="FaceEnrollmentView"/>. Freezes the identity at the start so a
/// <c>PersonIdentifier</c> change mid-run can't split shots across two people, gives the run its own
/// cancellation, and tracks a store that was already under way when the run was cancelled so its id still
/// reaches the caller (via <c>CancelEnrollmentAsync</c>) for cleanup.
/// </summary>
sealed class FaceEnrollmentRun
{
    readonly object sync = new();
    readonly CancellationTokenSource cancellation = new();
    readonly List<FaceEnrollmentCapture> captures = [];
    Task<bool> pending = Task.FromResult(false);
    volatile bool uncertain;

    public FaceEnrollmentRun(string personIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personIdentifier);
        this.PersonIdentifier = personIdentifier;
    }

    public string PersonIdentifier { get; }
    public bool IsCancelled => this.cancellation.IsCancellationRequested;
    public CancellationToken CancellationToken => this.cancellation.Token;

    /// <summary>
    /// A store threw — including being cancelled mid-flight — so a shot may have been written without its id
    /// coming back. Only <c>Forget(identity)</c> can guarantee cleanup then; <c>ForgetShots</c> can't name it.
    /// </summary>
    public bool HasUncertainStorage => this.uncertain;

    public IReadOnlyList<FaceEnrollmentCapture> Captures { get { lock (this.sync) return this.captures.ToArray(); } }

    /// <summary>
    /// Store one shot. Returns true when it was stored and the run is still live; false when it was stored but
    /// the run was cancelled meanwhile (the capture is still recorded). Throws if the store throws.
    /// </summary>
    public Task<bool> StoreAsync(int stepIndex, byte[] jpeg, int width, int height,
        Func<string, byte[], CancellationToken, Task<Person>> store)
    {
        lock (this.sync)
        {
            this.cancellation.Token.ThrowIfCancellationRequested();
            if (!this.pending.IsCompleted)
                throw new InvalidOperationException("An enrollment capture is already being stored.");
            this.pending = this.StoreCoreAsync(stepIndex, jpeg, width, height, store);
            return this.pending;
        }
    }

    async Task<bool> StoreCoreAsync(int stepIndex, byte[] jpeg, int width, int height,
        Func<string, byte[], CancellationToken, Task<Person>> store)
    {
        try
        {
            var person = await store(this.PersonIdentifier, jpeg, this.cancellation.Token).ConfigureAwait(false);
            lock (this.sync)
                this.captures.Add(new FaceEnrollmentCapture(stepIndex, person.Id, jpeg, width, height));
            return !this.IsCancelled;
        }
        catch
        {
            // A provider can insert and then throw (or be cancelled after the insert) — we can't tell.
            this.uncertain = true;
            throw;
        }
    }

    public void Cancel() => this.cancellation.Cancel();

    /// <summary>Cancel, wait for any store already under way, and return everything this run stored.</summary>
    public async Task<IReadOnlyList<FaceEnrollmentCapture>> CancelAndDrainAsync()
    {
        this.Cancel();
        Task pendingWork;
        lock (this.sync) pendingWork = this.pending;
        try { await pendingWork.ConfigureAwait(false); }
        catch { /* surfaced through HasUncertainStorage */ }
        return this.Captures;
    }
}
