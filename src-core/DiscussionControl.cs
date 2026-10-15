namespace MultiAgentHub.Core;

/// <summary>Wakes a pause without cancelling a model call or changing the session budget.</summary>
internal sealed class DiscussionControl : IDisposable
{
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly object gate = new();
    private int finishRequested;
    public bool FinishRequested => Volatile.Read(ref finishRequested) != 0;
    public void Finish() { Interlocked.Exchange(ref finishRequested, 1); Wake(); }
    public void Wake() { lock (gate) { if (wake.CurrentCount == 0) wake.Release(); } }
    public async Task Pause(TimeSpan duration, TimeProvider clock, CancellationToken token)
    {
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(token);
        var delay = Task.Delay(duration, clock, pending.Token);
        var signal = wake.WaitAsync(pending.Token);
        var completed = await Task.WhenAny(delay, signal);
        pending.Cancel();
        // Observe both tasks and release the losing timer/waiter before the next pause.
        try { await completed; }
        finally {
            try { await (completed == delay ? signal : delay); }
            catch (OperationCanceledException) { }
        }
        token.ThrowIfCancellationRequested();
    }
    public void Dispose() => wake.Dispose();
}
