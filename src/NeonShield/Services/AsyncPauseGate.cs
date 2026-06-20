namespace NeonShield.Services;

public sealed class AsyncPauseGate
{
    private readonly object _gate = new();
    private TaskCompletionSource _resumeSource = CreateCompletedSource();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        lock (_gate)
        {
            if (IsPaused)
            {
                return;
            }

            IsPaused = true;
            _resumeSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            _resumeSource.TrySetResult();
        }
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return IsPaused
                ? _resumeSource.Task.WaitAsync(cancellationToken)
                : Task.CompletedTask;
        }
    }

    private static TaskCompletionSource CreateCompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
