using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher;

/// <summary>
/// Strategy for invoking the handlers of a published notification.
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Invokes every resolved handler for the notification.
    /// </summary>
    /// <param name="handlerExecutors">The resolved handlers</param>
    /// <param name="notification">The notification being published</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken);
}
