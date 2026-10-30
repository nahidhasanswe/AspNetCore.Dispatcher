using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher.NotificationPublishers;

/// <summary>
/// Starts every handler and awaits them all together. Every handler is invoked even if an earlier
/// one fails, and the first exception surfaces when the returned task is awaited.
/// </summary>
public class TaskWhenAllPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        var tasks = handlerExecutors
            .Select(handler => Invoke(handler, notification, cancellationToken))
            .ToArray();

        return Task.WhenAll(tasks);
    }

    /// <summary>
    /// A handler that throws before its first await would otherwise abort the fan-out, so a
    /// synchronous throw is turned into a faulted task.
    /// </summary>
    private static Task Invoke(NotificationHandlerExecutor handler, INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            return handler.HandlerCallback(notification, cancellationToken);
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }
}
