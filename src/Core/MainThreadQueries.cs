using System.Collections.Concurrent;

namespace StardewCodexChat.Core;

/// <summary>Workers submit requests; only the owning game thread executes readers.</summary>
public sealed class MainThreadQueries : IGameQueries
{
    private sealed record Work(string Name, string Arguments, CancellationToken Token, TaskCompletionSource<string> Result);
    private readonly ConcurrentQueue<Work> queue = new();
    private readonly Func<string, string, string> read;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private int waiting;
    public MainThreadQueries(Func<string, string, string> read) { this.read = read; }
    public Task<string> QueryAsync(string name, string arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref waiting) > 32)
        {
            Interlocked.Decrement(ref waiting);
            return Task.FromResult("{\"error\":\"Game query queue is busy.\"}");
        }
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Enqueue(new(name, arguments, token, result));
        return result.Task.WaitAsync(token);
    }
    public void Tick()
    {
        if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Game queries must run on the game thread.");
        for (int count = 0; count < 4 && queue.TryDequeue(out var work); count++)
        {
            Interlocked.Decrement(ref waiting);
            if (work.Token.IsCancellationRequested) { work.Result.TrySetCanceled(work.Token); continue; }
            if (!GameTools.TryArguments(work.Name, work.Arguments, out _))
            {
                work.Result.TrySetResult("{\"error\":\"Invalid assistant tool.\"}");
                continue;
            }
            try { work.Result.TrySetResult(read(work.Name, work.Arguments)); }
            catch { work.Result.TrySetResult("{\"error\":\"Game state unavailable for this query; do not infer missing data.\"}"); }
        }
    }
}
