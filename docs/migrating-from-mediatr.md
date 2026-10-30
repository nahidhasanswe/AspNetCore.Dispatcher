# Migrating from MediatR

The API surface matches MediatR's. For most codebases the migration is a namespace change, a
registration rename, and `IMediator` → `IDispatcher`.

## Name mapping

### Core

| MediatR | AspNetCore.Dispatcher |
|---|---|
| `MediatR` (namespace) | `AspNetCore.Dispatcher` |
| `IMediator` | `IDispatcher` |
| `Mediator` | `Dispatcher` |
| `ISender` | `ISender` |
| `IPublisher` | `IPublisher` |
| `IBaseRequest` | `IBaseRequest` |
| `IRequest` | `IRequest` |
| `IRequest<TResponse>` | `IRequest<TResponse>` |
| `IRequestHandler<TRequest, TResponse>` | `IRequestHandler<TRequest, TResponse>` |
| `IRequestHandler<TRequest>` | `IRequestHandler<TRequest>` |
| `INotification` | `INotification` |
| `INotificationHandler<TNotification>` | `INotificationHandler<TNotification>` |
| `IStreamRequest<TResponse>` | `IStreamRequest<TResponse>` |
| `IStreamRequestHandler<TRequest, TResponse>` | `IStreamRequestHandler<TRequest, TResponse>` |
| `RequestHandler<TRequest, TResponse>` | `RequestHandler<TRequest, TResponse>` |
| `RequestHandler<TRequest>` | `RequestHandler<TRequest>` |
| `NotificationHandler<TNotification>` | `NotificationHandler<TNotification>` |
| `Unit` | `Unit` |

### Pipeline

| MediatR | AspNetCore.Dispatcher |
|---|---|
| `MediatR.Pipeline` (namespace) | `AspNetCore.Dispatcher.Pipeline` |
| `IPipelineBehavior<TRequest, TResponse>` | `IPipelineBehavior<TRequest, TResponse>` |
| `IStreamPipelineBehavior<TRequest, TResponse>` | `IStreamPipelineBehavior<TRequest, TResponse>` |
| `RequestHandlerDelegate<TResponse>` | `RequestHandlerDelegate<TResponse>` |
| `StreamHandlerDelegate<TResponse>` | `StreamHandlerDelegate<TResponse>` |
| `IRequestPreProcessor<TRequest>` | `IRequestPreProcessor<TRequest>` |
| `IRequestPostProcessor<TRequest, TResponse>` | `IRequestPostProcessor<TRequest, TResponse>` |
| `RequestPreProcessorBehavior<,>` | `RequestPreProcessorBehavior<,>` |
| `RequestPostProcessorBehavior<,>` | `RequestPostProcessorBehavior<,>` |
| `IRequestExceptionHandler<,,>` / `<,>` | same |
| `IRequestExceptionAction<,>` / `<>` | same |
| `AsyncRequestExceptionHandler<,,>` / `<,>` | same |
| `RequestExceptionHandler<,,>` / `<,>` | same |
| `AsyncRequestExceptionAction<,>` / `<>` | same |
| `RequestExceptionAction<,>` / `<>` | same |
| `RequestExceptionHandlerState<TResponse>` | `RequestExceptionHandlerState<TResponse>` |
| `RequestExceptionProcessorBehavior<,>` | `RequestExceptionProcessorBehavior<,>` |
| `RequestExceptionActionProcessorBehavior<,>` | `RequestExceptionActionProcessorBehavior<,>` |

### Notifications and registration

| MediatR | AspNetCore.Dispatcher |
|---|---|
| `INotificationPublisher` | `INotificationPublisher` |
| `NotificationHandlerExecutor` | `NotificationHandlerExecutor` |
| `MediatR.NotificationPublishers.ForeachAwaitPublisher` | `AspNetCore.Dispatcher.NotificationPublishers.ForeachAwaitPublisher` |
| `MediatR.NotificationPublishers.TaskWhenAllPublisher` | `AspNetCore.Dispatcher.NotificationPublishers.TaskWhenAllPublisher` |
| `services.AddMediatR(cfg => ...)` | `services.AddDispatcher(cfg => ...)` |
| `MediatRServiceConfiguration` | `DispatcherServiceConfiguration` |
| `cfg.MediatorImplementationType` | `cfg.DispatcherImplementationType` |
| `cfg.RegisterServicesFromAssembly*` | same |
| `cfg.AddBehavior` / `AddOpenBehavior` | same |
| `cfg.AddStreamBehavior` / `AddOpenStreamBehavior` | same |
| `cfg.AddRequestPreProcessor` / `AddOpenRequestPreProcessor` | same |
| `cfg.AddRequestPostProcessor` / `AddOpenRequestPostProcessor` | same |
| `cfg.AutoRegisterRequestProcessors` | same |
| `cfg.NotificationPublisher` / `NotificationPublisherType` | same |
| `cfg.Lifetime`, `cfg.TypeEvaluator` | same |
| `cfg.RegisterGenericHandlers` | same |
| `cfg.MaxGenericTypeParameters`, `MaxTypesClosing`, `MaxGenericTypeRegistrations` | same |

### Additions

Not in MediatR, safe to ignore:

| Member | |
|---|---|
| `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>` | Marker aliases over `IRequest`, handy for constraining a behavior to one half of your messages. |
| `IEvent` | Alias of `INotification`. |
| `AddDispatcherFromAssemblyContaining<T>()`, `AddDispatcher(params Assembly[])` | Shorthand registrations. |
| `RegistrationTimeout` | Bounds the assembly scan. |

## The two behavioral differences

### 1. Void handlers return `Task`, not `Task<Unit>`

In MediatR 12, `IRequestHandler<TRequest>` inherits `IRequestHandler<TRequest, Unit>`, so a void
handler returns `Task<Unit>` and has to end with `return Unit.Value;`. Here the interface is
standalone and returns plain `Task`:

```csharp
// MediatR
public class DeleteMemberHandler : IRequestHandler<DeleteMember>
{
    public async Task<Unit> Handle(DeleteMember request, CancellationToken cancellationToken)
    {
        await _db.Delete(request.Id);
        return Unit.Value;
    }
}
```

```csharp
// AspNetCore.Dispatcher
public class DeleteMemberHandler : IRequestHandler<DeleteMember>
{
    public Task Handle(DeleteMember request, CancellationToken cancellationToken)
        => _db.Delete(request.Id);
}
```

Behaviors are unaffected — the response type inside the pipeline is still `Unit`, so
`IPipelineBehavior<TRequest, Unit>` applies as before. A generated adapter bridges the two shapes;
see [How it works](architecture.md#void-requests).

**One consequence:** void handlers must be registered by assembly scanning, which also registers
the adapter. A hand-written registration does not:

```csharp
// Does not get the adapter — Send(new DeleteMember(1)) will not find a handler.
services.AddTransient<IRequestHandler<DeleteMember>, DeleteMemberHandler>();
```

If you must register one by hand, register the adapter too:

```csharp
services.AddTransient<IRequestHandler<DeleteMember>, DeleteMemberHandler>();
services.AddTransient<IRequestHandler<DeleteMember, Unit>, VoidRequestHandlerAdapter<DeleteMember>>();
```

### 2. `SetHandled(null)` is honoured

MediatR throws if a handler marks an exception handled but leaves the response null. Here the null
response is returned as-is, on the view that an explicit `SetHandled` is an explicit decision.

## Running the migration

With no MediatR-specific code beyond the two points above, this is mostly mechanical. From the
repository root:

```shell
# 1. Swap the package
dotnet remove package MediatR
dotnet add package AspNet.Core.Dispatcher --prerelease

# 2. Namespaces
grep -rl "using MediatR" --include=*.cs . | xargs sed -i 's/using MediatR.Pipeline;/using AspNetCore.Dispatcher.Pipeline;/g'
grep -rl "using MediatR" --include=*.cs . | xargs sed -i 's/using MediatR.NotificationPublishers;/using AspNetCore.Dispatcher.NotificationPublishers;/g'
grep -rl "using MediatR" --include=*.cs . | xargs sed -i 's/using MediatR;/using AspNetCore.Dispatcher;/g'

# 3. The mediator itself
grep -rl "IMediator" --include=*.cs . | xargs sed -i 's/\bIMediator\b/IDispatcher/g'

# 4. Registration
grep -rl "AddMediatR" --include=*.cs . | xargs sed -i 's/\bAddMediatR\b/AddDispatcher/g'
grep -rl "MediatRServiceConfiguration" --include=*.cs . | xargs sed -i 's/\bMediatRServiceConfiguration\b/DispatcherServiceConfiguration/g'
grep -rl "MediatorImplementationType" --include=*.cs . | xargs sed -i 's/\bMediatorImplementationType\b/DispatcherImplementationType/g'

# 5. Build and fix what is left — in practice, the void handlers
dotnet build
```

On Windows, run these in Git Bash, or use your editor's find-and-replace across the solution.

Then fix the void handlers the compiler points at: change `Task<Unit>` to `Task` and drop
`return Unit.Value;`.

A handler that derived from MediatR's removed `AsyncRequestHandler<TRequest>` becomes a plain
`IRequestHandler<TRequest>` implementation.

## Running both side by side

The two libraries share no types, so they can coexist during a staged migration — register both and
move one feature at a time. Watch for two things:

- A class injecting both `IMediator` and `IDispatcher` is a sign the migration stalled mid-feature.
- Messages must not implement both `MediatR.IRequest<T>` and `AspNetCore.Dispatcher.IRequest<T>`;
  pick one per message type, or the `using` directives in that file will be ambiguous.

## What is not carried over

- **MediatR's commercial licence terms.** This package is MIT.
- **`MediatR.Contracts`.** The contracts live in the main package here. If you split messages into
  a contracts-only assembly, reference `AspNet.Core.Dispatcher` from it — the package is small and
  has one dependency.
