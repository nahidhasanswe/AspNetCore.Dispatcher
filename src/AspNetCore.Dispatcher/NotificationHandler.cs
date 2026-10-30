using System;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher;

/// <summary>
/// Wrapper class for a synchronous notification handler.
/// </summary>
/// <typeparam name="TNotification">The type of notification being handled</typeparam>
public abstract class NotificationHandler<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    Task INotificationHandler<TNotification>.Handle(TNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            Handle(notification);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    /// <summary>
    /// Override in a derived class to handle the notification synchronously.
    /// </summary>
    protected abstract void Handle(TNotification notification);
}
