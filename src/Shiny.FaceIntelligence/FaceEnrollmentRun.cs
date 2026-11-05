namespace Shiny.FaceIntelligence;

/// <summary>One identity-frozen guided run. Cancellation prevents new storage; draining waits for
/// already-started storage and retains its returned IDs for caller-owned cleanup.</summary>
public sealed class FaceEnrollmentRun
{
    readonly object sync = new();
    readonly CancellationTokenSource cancellation = new();
    readonly List<FaceEnrollmentCapture> captures = [];
    Task<bool> pending = Task.FromResult(false);

    public FaceEnrollmentRun(string personIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personIdentifier);
        this.PersonIdentifier = personIdentifier;
    }

    public string PersonIdentifier { get; }
    public bool IsCancelled => this.cancellation.IsCancellationRequested;
    public CancellationToken CancellationToken => this.cancellation.Token;
    public bool HasUncertainStorage { get; private set; }
    public IReadOnlyList<FaceEnrollmentCapture> Captures { get { lock (this.sync) return this.captures.ToArray(); } }

    public Task<bool> StoreAsync(int stepIndex, byte[] jpeg, int width, int height,
        Func<string, byte[], CancellationToken, Task<Person>> store)
    {
        lock (this.sync)
        {
            this.cancellation.Token.ThrowIfCancellationRequested();
            if (!this.pending.IsCompleted)
                throw new InvalidOperationException("An enrollment capture is already being stored.");
            this.pending = this.StoreCoreAsync(stepIndex, jpeg.ToArray(), width, height, store);
            return this.pending;
        }
    }

    async Task<bool> StoreCoreAsync(int stepIndex, byte[] jpeg, int width, int height,
        Func<string, byte[], CancellationToken, Task<Person>> store)
    {
        try
        {
            var person = await store(this.PersonIdentifier, jpeg, this.cancellation.Token).ConfigureAwait(false);
            var capture = new FaceEnrollmentCapture(stepIndex, person.Id, jpeg, width, height);
            lock (this.sync) this.captures.Add(capture);
            return !this.IsCancelled;
        }
        catch
        {
            // Cancellation cannot prove that a provider did not insert before throwing.
            this.HasUncertainStorage = true;
            throw;
        }
    }

    public void Cancel() => this.cancellation.Cancel();

    public async Task<IReadOnlyList<FaceEnrollmentCapture>> CancelAndDrainAsync()
    {
        this.Cancel();
        Task pendingWork;
        lock (this.sync) pendingWork = this.pending;
        try { await pendingWork.ConfigureAwait(false); }
        catch { /* HasUncertainStorage tells the owner to perform whole-identity cleanup. */ }
        return this.Captures;
    }
}
