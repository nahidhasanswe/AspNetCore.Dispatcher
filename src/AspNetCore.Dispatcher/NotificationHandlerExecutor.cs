using System;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher;

/// <summary>
/// Pairs a resolved notification handler instance with the callback used to invoke it.
/// </summary>
public readonly struct NotificationHandlerExecutor
{
    /// <summary>
    /// Creates a new executor.
    /// </summary>
    /// <param name="handlerInstance">The resolved handler instance</param>
    /// <param name="handlerCallback">Callback invoking the handler</param>
    public NotificationHandlerExecutor(object handlerInstance, Func<INotification, CancellationToken, Task> handlerCallback)
    {
        HandlerInstance = handlerInstance;
        HandlerCallback = handlerCallback;
    }

    /// <summary>
    /// The resolved handler instance. Useful for inspecting handler metadata when writing a custom publisher.
    /// </summary>
    public object HandlerInstance { get; }

    /// <summary>
    /// Callback that invokes the handler with the notification.
    /// </summary>
    public Func<INotification, CancellationToken, Task> HandlerCallback { get; }
}
