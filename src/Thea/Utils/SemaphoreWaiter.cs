using System;
using System.Threading;
using System.Threading.Tasks;

namespace Thea;

public class SemaphoreWaiter
{
    private readonly object locker = new object();
    private int activeOperations = 0;
    private TaskCompletionSource<bool> idleSource = CreateCompletedSource();

    public bool IsBusying => Volatile.Read(ref this.activeOperations) > 0;
    public int ActiveOperations => Volatile.Read(ref this.activeOperations);

    public void Synchronize(Action worker)
    {
        if (worker == null)
            throw new ArgumentNullException(nameof(worker));
        lock (this.locker)
        {
            worker.Invoke();
        }
    }
    public TResult Synchronize<TResult>(Func<TResult> resultWorker)
    {
        if (resultWorker == null)
            throw new ArgumentNullException(nameof(resultWorker));
        lock (this.locker)
        {
            return resultWorker.Invoke();
        }
    }
    public bool TryEnter(Func<bool> isEnterWorker)
    {
        if (isEnterWorker == null)
            throw new ArgumentNullException(nameof(isEnterWorker));

        lock (this.locker)
        {
            if (!isEnterWorker.Invoke())
                return false;

            if (this.activeOperations == 0)
                this.idleSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.activeOperations++;
            return true;
        }
    }
    public void Enter()
    {
        lock (this.locker)
        {
            if (this.activeOperations == 0)
                this.idleSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.activeOperations++;
        }
    }
    public void Exit()
    {
        TaskCompletionSource<bool> completedSource = null;
        lock (this.locker)
        {
            if (this.activeOperations <= 0)
                return;
            this.activeOperations--;
            if (this.activeOperations == 0)
                completedSource = this.idleSource;
        }
        completedSource?.TrySetResult(true);
    }
    public async Task<bool> WaitAsync(TimeSpan timeout)
    {
        Task idleTask;
        lock (this.locker)
            idleTask = this.activeOperations == 0 ? Task.CompletedTask : this.idleSource.Task;
        if (idleTask.IsCompleted) return false;
        var completedTask = await Task.WhenAny(idleTask, Task.Delay(timeout));
        return completedTask != idleTask;
    }
    private static TaskCompletionSource<bool> CreateCompletedSource()
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult(true);
        return source;
    }
}