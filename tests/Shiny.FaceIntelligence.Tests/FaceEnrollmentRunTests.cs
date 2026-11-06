using Shiny.FaceIntelligence.Maui;
using Xunit;

namespace Shiny.FaceIntelligence.Tests;

public class FaceEnrollmentRunTests
{
    [Fact]
    public async Task Cancel_DrainsLateStoreAndRetainsItsOriginalIndexForCleanup()
    {
        var stored = new TaskCompletionSource<Person>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new FaceEnrollmentRun("7");
        var work = run.StoreAsync(4, [1, 2], 720, 960, (id, bytes, ct) =>
        {
            Assert.Equal("7", id);
            return stored.Task;
        });
        var drain = run.CancelAndDrainAsync();
        Assert.False(drain.IsCompleted);
        stored.SetResult(new Person { Id = "late-id", PersonIdentifier = "7" });
        Assert.False(await work);
        var capture = Assert.Single(await drain);
        Assert.Equal(4, capture.StepIndex);
        Assert.Equal("late-id", capture.ShotId);
        Assert.False(run.HasUncertainStorage);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await run.StoreAsync(5, [3], 1, 1, (_, _, _) => throw new Exception()));
    }

    [Fact]
    public async Task FailedStore_IsNotAcceptedAndRequiresWholeIdentityCleanup()
    {
        var run = new FaceEnrollmentRun("7");
        await Assert.ThrowsAsync<IOException>(() => run.StoreAsync(0, [1], 1, 1,
            (_, _, _) => Task.FromException<Person>(new IOException("uncertain insert"))));
        Assert.Empty(await run.CancelAndDrainAsync());
        Assert.True(run.HasUncertainStorage);
    }

    [Fact]
    public async Task SecondStore_WhileOneIsPending_Throws()
    {
        var pending = new TaskCompletionSource<Person>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new FaceEnrollmentRun("7");
        var first = run.StoreAsync(0, [1], 1, 1, (_, _, _) => pending.Task);
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.StoreAsync(1, [2], 1, 1, (_, _, _) => pending.Task));
        pending.SetResult(new Person { Id = "a" });
        Assert.True(await first);
    }
}
