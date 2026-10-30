# Notifications

A notification is broadcast to every registered handler and returns nothing. Publishing a
notification nobody handles is a no-op, not an error.

```csharp
public record MemberCreated(int Id, string Name) : INotification;

public class SendWelcomeEmail : INotificationHandler<MemberCreated>
{
    public Task Handle(MemberCreated notification, CancellationToken cancellationToken) => /* ... */;
}

public class UpdateSearchIndex : INotificationHandler<MemberCreated>
{
    public Task Handle(MemberCreated notification, CancellationToken cancellationToken) => /* ... */;
}
```

```csharp
await dispatcher.Publish(new MemberCreated(id, name), cancellationToken);
```

`IEvent` is an alias of `INotification` if you prefer that vocabulary; handlers are still
`INotificationHandler<T>`.

## Synchronous handlers

```csharp
public class LogMemberCreated : NotificationHandler<MemberCreated>
{
    protected override void Handle(MemberCreated notification)
        => _logger.LogInformation("Member {Id} created", notification.Id);
}
```

## Handlers are matched on the concrete type

Handlers are resolved for the **exact runtime type** of the notification. A handler for a base
notification type is not invoked for a derived one:

```csharp
public record MemberChanged(int Id) : INotification;
public record MemberRenamed(int Id, string Name) : MemberChanged(Id);

// Publishing MemberRenamed does NOT reach INotificationHandler<MemberChanged>.
await dispatcher.Publish(new MemberRenamed(1, "Ann"));
```

This follows from how the DI container resolves `IEnumerable<INotificationHandler<T>>` — it matches
the closed service type and does not apply interface variance.

> An explicit type argument does **not** change this. `Publish<MemberChanged>(new MemberRenamed(...))`
> still dispatches on the runtime type, so it reaches `MemberRenamed` handlers and not
> `MemberChanged` ones — if no handler is registered for `MemberRenamed`, nothing runs at all.

To handle a whole hierarchy, **constrain an open generic handler to the base type**. The container
closes it for each concrete notification that satisfies the constraint:

```csharp
public class AnyMemberChangeHandler<TNotification> : INotificationHandler<TNotification>
    where TNotification : MemberChanged
{
    public Task Handle(TNotification notification, CancellationToken cancellationToken)
    {
        _log.LogInformation("{Change} happened", typeof(TNotification).Name);
        return Task.CompletedTask;
    }
}
```

Publishing `MemberRenamed` now reaches it, as does any other type deriving from `MemberChanged`.

The same trick with no constraint gives you a handler for **every** notification:

```csharp
public class AuditAnyNotification<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public Task Handle(TNotification notification, CancellationToken cancellationToken)
    {
        _audit.Record(typeof(TNotification).Name, notification);
        return Task.CompletedTask;
    }
}
```

Open generic notification handlers like these are registered automatically by scanning — no
configuration flag needed. What matters is that the handler closes the interface over **its own**
type parameter, as both examples above do.

A handler of a *wrapped* notification is a different shape and cannot be registered as an open
generic, because the container would close it over the published notification and produce a type
that does not implement the service:

```csharp
// INotificationHandler<Wrapped<T>>, not INotificationHandler<T>
public class WrappedHandler<TInner> : INotificationHandler<Wrapped<TInner>>
    where TInner : INotification
```

Those are closed by unification instead, which is opt-in:

```csharp
cfg.RegisterGenericHandlers = true;
```

Without the flag such a handler is simply left unregistered; it never breaks the handlers of other
notifications.

## Publishing a notification known only at runtime

```csharp
object notification = /* ... */;
await dispatcher.Publish(notification, cancellationToken);
```

Passing an object that does not implement `INotification` throws `ArgumentException`.

## Publishing strategies

The strategy decides how the handlers are invoked. Set it once at registration:

```csharp
using AspNetCore.Dispatcher.NotificationPublishers;

cfg.NotificationPublisher = new TaskWhenAllPublisher();
```

### ForeachAwaitPublisher (default)

Awaits each handler in turn, in registration order. The first handler that throws aborts the rest
and the exception propagates out of `Publish`.

Choose it when handlers share a scoped resource that is not safe for concurrent use — an EF Core
`DbContext` is the common case — or when the order matters.

### TaskWhenAllPublisher

Starts every handler, then awaits them together. Every handler runs even if an earlier one fails,
and the first exception surfaces when the returned task is awaited.

Choose it when handlers are independent and each one does its own I/O.

> Handlers now run concurrently, so a scoped dependency shared between them must be thread-safe.
> `DbContext` is not. Give each handler its own scope (see below) or keep the default publisher.

### A note on exceptions

With either strategy an exception reaching `Publish` means the caller's work failed too. If
notification handlers are meant to be fire-and-forget, catch inside the handler, or use a custom
publisher that isolates failures.

## Custom publishers

Implement `INotificationPublisher` for anything else — isolating failures, logging timings, giving
each handler its own scope, or fanning out onto the thread pool:

```csharp
using AspNetCore.Dispatcher;

public class ResilientPublisher : INotificationPublisher
{
    private readonly ILogger<ResilientPublisher> _logger;

    public ResilientPublisher(ILogger<ResilientPublisher> logger) => _logger = logger;

    public async Task Publish(
        IEnumerable<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken)
    {
        foreach (var handler in handlerExecutors)
        {
            try
            {
                await handler.HandlerCallback(notification, cancellationToken);
            }
            catch (Exception exception)
            {
                // One bad handler should not stop the others or fail the caller.
                _logger.LogError(exception, "Handler {Handler} failed for {Notification}",
                    handler.HandlerInstance.GetType().Name, notification.GetType().Name);
            }
        }
    }
}
```

Register it by type so the container can inject its dependencies:

```csharp
cfg.NotificationPublisherType = typeof(ResilientPublisher);
```

`NotificationPublisherType` takes precedence over `NotificationPublisher`. Use the instance
property for a publisher with no dependencies, the type property otherwise.

### NotificationHandlerExecutor

Each entry a publisher receives pairs the resolved handler with the callback that invokes it:

| Member | Purpose |
|---|---|
| `HandlerInstance` | The resolved handler object. Useful for logging its type or reading an attribute off it. |
| `HandlerCallback` | `Func<INotification, CancellationToken, Task>` — invokes the handler. |

The executors are lazily enumerated, so a publisher that never iterates them never resolves the
handlers.

### Giving each handler its own scope

Useful with concurrent publishing and scoped dependencies:

```csharp
public class ScopePerHandlerPublisher : INotificationPublisher
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopePerHandlerPublisher(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public Task Publish(
        IEnumerable<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken)
    {
        // Resolve the handler types now, then re-resolve each in its own scope.
        var handlerTypes = handlerExecutors.Select(e => e.HandlerInstance.GetType()).ToArray();

        var tasks = handlerTypes.Select(async handlerType =>
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetServices(
                    typeof(INotificationHandler<>).MakeGenericType(notification.GetType()))
                .First(h => h!.GetType() == handlerType)!;

            await (Task)handlerType
                .GetMethod(nameof(INotificationHandler<INotification>.Handle))!
                .Invoke(handler, new object[] { notification, cancellationToken })!;
        });

        return Task.WhenAll(tasks);
    }
}
```

## Overriding PublishCore

For a cross-cutting change to publishing without replacing the strategy, subclass `Dispatcher`:

```csharp
public class TracingDispatcher : Dispatcher
{
    public TracingDispatcher(IServiceProvider serviceProvider, INotificationPublisher publisher)
        : base(serviceProvider, publisher)
    {
    }

    protected override Task PublishCore(
        IEnumerable<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Source.StartActivity($"publish {notification.GetType().Name}");
        return base.PublishCore(handlerExecutors, notification, cancellationToken);
    }
}
```

```csharp
cfg.DispatcherImplementationType = typeof(TracingDispatcher);
```

## Notifications and behaviors

`IPipelineBehavior` applies to **requests only**. Notifications do not run through a pipeline — if
you need cross-cutting work around publishing, wrap it in a custom publisher or override
`PublishCore` as above.
