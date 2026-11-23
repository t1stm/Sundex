using System.Runtime.ExceptionServices;
using Serilog;
using Sundex.Reactive;

namespace Sundex.Engine.Threading;

/// <summary>
///     Runs work off the frame. An exception is caught and thrown again on the frame thread by
///     <see cref="Update" />. Where threads can't start (the browser), work that would get a
///     thread of its own runs on the frame thread between frames instead.
/// </summary>
public class ThreadRunner(ILogger logger)
{
    private readonly Queue<ExceptionDispatchInfo> _exceptions = [];
    public ILogger Logger { get; } = logger;

    /// <summary>Whether work can run on a thread of its own; false in the browser.</summary>
    public static bool CanStartThreads { get; } = !OperatingSystem.IsBrowser();

    /// <summary>Runs <paramref name="action" /> on a thread of its own.</summary>
    public void RunThread(Action action)
    {
        if (!CanStartThreads)
        {
            RunTask(action);
            return;
        }

        new Thread(() => ActionWrapper(action, AddException)).Start();
    }

    public Task RunTask(Action action)
    {
        return Task.Run(() => { ActionWrapper(action, AddException); });
    }

    /// <summary>Runs asynchronous work on the thread pool, which is the frame thread in the browser.</summary>
    public Task RunTask(Func<Task> work)
    {
        return Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception exception)
            {
                AddException(ExceptionDispatchInfo.Capture(exception));
            }
        });
    }

    /// <summary>
    ///     Calls <paramref name="step" /> until it returns false: on a thread of its own with a
    ///     millisecond's pause between calls, or once a frame where threads can't start.
    /// </summary>
    public void RunLoop(Func<bool> step)
    {
        if (CanStartThreads)
        {
            RunThread(() =>
            {
                while (step()) Thread.Sleep(1);
            });
            return;
        }

        Frame.Tick(() =>
        {
            try
            {
                return step();
            }
            catch (Exception exception)
            {
                AddException(ExceptionDispatchInfo.Capture(exception));
                return false;
            }
        });
    }

    public void AddException(ExceptionDispatchInfo exception)
    {
        lock (_exceptions)
        {
            _exceptions.Enqueue(exception);
        }
    }

    public void Update()
    {
        lock (_exceptions)
        {
            while (_exceptions.TryDequeue(out var exception)) exception.Throw();
        }
    }

    private static void ActionWrapper(Action action, Action<ExceptionDispatchInfo> exceptionHandler)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            exceptionHandler(ExceptionDispatchInfo.Capture(exception));
        }
    }
}
