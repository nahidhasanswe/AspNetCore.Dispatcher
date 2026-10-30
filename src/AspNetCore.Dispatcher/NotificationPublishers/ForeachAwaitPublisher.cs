using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher.NotificationPublishers;

/// <summary>
/// Awaits each handler in sequence, in registration order. The first handler that throws
/// stops the remaining handlers from being invoked. This is the default publisher.
/// </summary>
public class ForeachAwaitPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        foreach (var handler in handlerExecutors)
        {
            await handler.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}
