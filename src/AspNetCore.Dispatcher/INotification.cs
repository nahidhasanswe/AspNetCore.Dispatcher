namespace AspNetCore.Dispatcher;

/// <summary>
/// Marker interface to represent a notification, dispatched to zero or more handlers.
/// </summary>
public interface INotification
{
}

/// <summary>
/// Marker interface to represent a domain event. Alias of <see cref="INotification"/>.
/// </summary>
public interface IEvent : INotification
{
}
