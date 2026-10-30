# Configuration

Everything is configured through `DispatcherServiceConfiguration` inside `AddDispatcher`.

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddDispatcher(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.Lifetime = ServiceLifetime.Scoped;
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
});
```

## Entry points

| Method | |
|---|---|
| `AddDispatcher(Action<DispatcherServiceConfiguration>)` | The usual form. |
| `AddDispatcher(DispatcherServiceConfiguration)` | Pass a configuration you built yourself, for example one shared between an app and its tests. |
| `AddDispatcher(params Assembly[])` | Scan these assemblies with all defaults. |
| `AddDispatcherFromAssemblyContaining<T>()` | Scan the assembly holding `T`, with all defaults. |

`AddDispatcher` throws `ArgumentException` if no assembly was registered.

## Assembly scanning

| Method | |
|---|---|
| `RegisterServicesFromAssembly(Assembly)` | One assembly. |
| `RegisterServicesFromAssemblies(params Assembly[])` | Several. |
| `RegisterServicesFromAssemblyContaining<T>()` | The assembly holding `T`. |
| `RegisterServicesFromAssemblyContaining(Type)` | The same, with a `Type`. |

Duplicates are removed, so registering the same assembly twice does not produce duplicate
handlers.

Scanning registers:

- `IRequestHandler<,>` and `IRequestHandler<>` — single handler per request, first match wins
- `IStreamRequestHandler<,>` — single handler per stream request
- `INotificationHandler<>` — all matches, including open generic handlers
- `IRequestExceptionHandler<,,>` and `IRequestExceptionAction<,>` — all matches
- `IRequestPreProcessor<>` and `IRequestPostProcessor<,>` — only when `AutoRegisterRequestProcessors` is on
- the `VoidRequestHandlerAdapter<>` for every void handler it finds

It does **not** register pipeline behaviors. Those are always explicit — see
[behaviors](behaviors.md#registering).

### TypeEvaluator

A filter applied to every scanned type. Return `false` to skip it:

```csharp
cfg.TypeEvaluator = type => !type.Name.EndsWith("Draft");
cfg.TypeEvaluator = type => type.Namespace?.StartsWith("MyApp.Application") == true;
```

Useful for keeping experimental or test-only handlers out of production, and for narrowing a scan
over a large assembly.

## Lifetimes

```csharp
cfg.Lifetime = ServiceLifetime.Scoped;      // default: Transient
```

This one setting applies to the dispatcher and to every handler, processor and exception handler
found by scanning. Behaviors and processors registered explicitly take their own lifetime argument:

```csharp
cfg.AddOpenBehavior(typeof(MetricsBehavior<,>), ServiceLifetime.Singleton);
```

Which to choose:

| Lifetime | When |
|---|---|
| `Transient` (default) | Safe everywhere. A new handler per `Send`; scoped dependencies still come from the ambient scope. |
| `Scoped` | Handlers that should share state within one web request. Requires an active scope — a `Send` from a singleton or a background service will throw unless you create one. |
| `Singleton` | Only for stateless handlers with singleton-safe dependencies. A singleton handler can never depend on a scoped `DbContext`. |

The dispatcher itself holds no per-request state; it resolves everything from the
`IServiceProvider` it was given, so `Transient` and `Scoped` behave identically for it in a web
request.

> **Captive dependencies.** If you set `Lifetime = ServiceLifetime.Singleton` and a handler injects
> a scoped `DbContext`, the container will either throw (with `ValidateScopes`, which is on by
> default in Development) or silently leak the first scope's instance. Leave the default unless you
> have a reason.

## Swapping the implementation

```csharp
cfg.DispatcherImplementationType = typeof(TracingDispatcher);
```

The type must be assignable to `IDispatcher` — in practice, derive from `Dispatcher` so you can
override `PublishCore`. `ISender` and `IPublisher` both resolve to whatever `IDispatcher` resolves
to.

Every registration uses `TryAdd`/`TryAddEnumerable`, so your own registration made *before*
`AddDispatcher` wins:

```csharp
services.AddScoped<IDispatcher, MyDispatcher>();
services.AddDispatcher(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
// IDispatcher is still MyDispatcher
```

The same applies to handlers: register one by hand to override the one scanning would have picked.

## Notification publishing

```csharp
cfg.NotificationPublisher = new TaskWhenAllPublisher();   // an instance, no dependencies
cfg.NotificationPublisherType = typeof(ResilientPublisher);  // resolved from the container
```

`NotificationPublisherType` wins if both are set. Details in
[Notifications](notifications.md#publishing-strategies).

## Processors

```csharp
cfg.AutoRegisterRequestProcessors = true;
```

Off by default. See [Pre and post processors](processors.md#registering).

## Generic handlers

A handler written against a *generic request* needs an explicit opt-in, because the container has
to be told which type arguments to close it over:

```csharp
public record Wrapped<TPayload>(TPayload Payload) : IRequest<string>;

public class WrappedHandler<TPayload> : IRequestHandler<Wrapped<TPayload>, string>
{
    public Task<string> Handle(Wrapped<TPayload> request, CancellationToken cancellationToken) => /* ... */;
}
```

```csharp
cfg.RegisterGenericHandlers = true;
cfg.MaxTypesClosing = 0;          // unconstrained type parameters have many candidates
```

With the flag on, each generic handler is closed over every type in the scanned assemblies that
satisfies its type parameter constraints, and the resulting closed handlers are registered. The
number of registrations is the product of the candidate sets, so the limits below exist to stop a
single unconstrained parameter from producing thousands of registrations. Constraining the
parameter is the better fix:

```csharp
public class WrappedHandler<TPayload> : IRequestHandler<Wrapped<TPayload>, string>
    where TPayload : IPayload       // only a handful of candidates
```

You do **not** need this flag for:

- Handlers that inherit from a generic base: `class EchoHandler : EchoHandlerBase<EchoRequest>`
- Open generic notification handlers: `class Audit<T> : INotificationHandler<T> where T : INotification`
- Open generic behaviors and processors, which are registered as open generics

You **do** need it for a handler of a *wrapped* message — one that closes the interface over
something built from its type parameter rather than over the parameter itself:

```csharp
class WrappedHandler<T> : INotificationHandler<Wrapped<T>>   // not INotificationHandler<T>
```

The container cannot close that from an open generic registration, so it is registered by
unification or not at all. Without the flag it is skipped silently and the handlers of other
messages keep working.

### Limits

| Setting | Default | Effect when exceeded |
|---|---|---|
| `MaxGenericTypeParameters` | `10` | `ArgumentException` naming the handler. `0` disables the check. |
| `MaxTypesClosing` | `100` | `ArgumentException` naming the handler and parameter. `0` disables the check. |
| `MaxGenericTypeRegistrations` | `125000` | `ArgumentException`. `0` disables the check. |
| `RegistrationTimeout` | `15000` ms | `TimeoutException`. `0` or less means no timeout. |

These only come into play with `RegisterGenericHandlers = true`, except `RegistrationTimeout`,
which bounds the whole scan.

## Full reference

| Member | Default | |
|---|---|---|
| `AssembliesToRegister` | empty | Read-only view of the assemblies to scan. |
| `Lifetime` | `Transient` | Lifetime of the dispatcher and scanned handlers. |
| `TypeEvaluator` | accept all | Per-type scan filter. |
| `DispatcherImplementationType` | `Dispatcher` | Implementation behind `IDispatcher`/`ISender`/`IPublisher`. |
| `NotificationPublisher` | `ForeachAwaitPublisher` | Publishing strategy instance. |
| `NotificationPublisherType` | `null` | Publishing strategy type; takes precedence. |
| `AutoRegisterRequestProcessors` | `false` | Register every scanned pre/post processor. |
| `RegisterGenericHandlers` | `false` | Close generic request handlers over candidate type arguments. |
| `MaxGenericTypeParameters` | `10` | Guard on generic handler arity. |
| `MaxTypesClosing` | `100` | Guard on candidates per type parameter. |
| `MaxGenericTypeRegistrations` | `125000` | Guard on total closed registrations. |
| `RegistrationTimeout` | `15000` | Scan timeout in milliseconds. |
| `BehaviorsToRegister` | empty | Behavior descriptors, in order. Populated by `AddBehavior`/`AddOpenBehavior`. |
| `StreamBehaviorsToRegister` | empty | Stream behavior descriptors, in order. |
| `RequestPreProcessorsToRegister` | empty | Pre-processor descriptors, in order. |
| `RequestPostProcessorsToRegister` | empty | Post-processor descriptors, in order. |

The four descriptor lists are public, so you can inspect or reorder them before the container is
built if you need to.

### Registration methods

| Behaviors | Stream behaviors | Pre-processors | Post-processors |
|---|---|---|---|
| `AddBehavior<TImpl>()` | `AddStreamBehavior<TImpl>()` | `AddRequestPreProcessor<TImpl>()` | `AddRequestPostProcessor<TImpl>()` |
| `AddBehavior<TService, TImpl>()` | `AddStreamBehavior<TService, TImpl>()` | `AddRequestPreProcessor<TService, TImpl>()` | `AddRequestPostProcessor<TService, TImpl>()` |
| `AddBehavior(Type)` | `AddStreamBehavior(Type)` | `AddRequestPreProcessor(Type)` | `AddRequestPostProcessor(Type)` |
| `AddBehavior(Type, Type)` | `AddStreamBehavior(Type, Type)` | `AddRequestPreProcessor(Type, Type)` | `AddRequestPostProcessor(Type, Type)` |
| `AddOpenBehavior(Type)` | `AddOpenStreamBehavior(Type)` | `AddOpenRequestPreProcessor(Type)` | `AddOpenRequestPostProcessor(Type)` |

All of them take an optional trailing `ServiceLifetime` and return the configuration for chaining.

## A worked example

```csharp
builder.Services.AddDispatcher(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<ApplicationMarker>();
    cfg.RegisterServicesFromAssemblyContaining<Program>();

    cfg.Lifetime = ServiceLifetime.Scoped;
    cfg.TypeEvaluator = type => type.Namespace?.Contains(".Experimental") != true;

    // Outermost first.
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));

    cfg.AddOpenStreamBehavior(typeof(StreamLoggingBehavior<,>));

    cfg.AutoRegisterRequestProcessors = true;
    cfg.NotificationPublisherType = typeof(ResilientPublisher);
});
```
