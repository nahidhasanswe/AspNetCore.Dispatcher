# How it works

Useful if you are debugging a resolution problem, extending the library, or deciding whether its
performance characteristics suit you.

## The shape of a Send

```
dispatcher.Send(request)
  │
  ├─ look up a wrapper for request.GetType() in a static cache      (reflection once per type)
  │
  └─ wrapper.Handle(request, serviceProvider, ct)
        │
        ├─ resolve IEnumerable<IPipelineBehavior<TRequest, TResponse>>
        ├─ fold them into a RequestHandlerDelegate chain, innermost last
        └─ invoke the chain
              behavior 1 → behavior 2 → … → IRequestHandler<TRequest, TResponse>.Handle
```

`IDispatcher` itself is thin. The work of turning a runtime `Type` into a strongly typed call lives
in the wrappers.

### Wrappers

`Send` only knows the request as `IRequest<TResponse>` or as `object`, but the handler and behavior
interfaces need the concrete `TRequest`. A generic wrapper closes that gap:

```csharp
internal class RequestHandlerWrapperImpl<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
```

One instance is created per request type with `Activator.CreateInstance` over
`MakeGenericType(requestType, responseType)`, then stored in a `ConcurrentDictionary<Type, …>` on
the `Dispatcher` class. From the second call onwards the lookup is a dictionary hit — the
reflection cost is paid once per request type for the lifetime of the process.

There are three caches, one each for requests, notifications and stream requests. They are
`static`, so they are shared by every `Dispatcher` instance and every container in the process.
That is safe because a wrapper holds no state: the `IServiceProvider` is a parameter on every call,
so two containers using the same wrapper still resolve their own handlers.

### Per-call work

Cached wrapper aside, each `Send` does:

1. One `GetServices<IPipelineBehavior<TRequest, TResponse>>()` resolution.
2. A `Reverse().Aggregate(...)` fold producing one delegate per behavior.
3. One `GetRequiredService<IRequestHandler<TRequest, TResponse>>()` resolution, inside the
   innermost delegate.

So the per-call overhead is a container resolution plus a handful of delegate allocations — no
reflection, no expression compilation. If you have no behaviors registered, the fold collapses to
the handler call itself.

The behavior chain is composed per call rather than cached, because behavior instances come from
the container and may be scoped or transient.

### Cancellation plumbing

`RequestHandlerDelegate<TResponse>` takes an optional `CancellationToken`:

```csharp
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);
```

Each link passes `t == default ? cancellationToken : t`, so a behavior that calls `next()` without
arguments still forwards the original token, while a behavior that supplies its own — a timeout, a
linked token — overrides it. Passing the token explicitly is the habit worth keeping.

## Void requests

`IRequest` derives from `IRequest<Unit>`, so a void request's response type inside the pipeline is
`Unit` and every behavior written against `IPipelineBehavior<TRequest, TResponse>` applies to it
unchanged.

The handler interface, though, is deliberately *not* `IRequestHandler<TRequest, Unit>` — it returns
plain `Task`, which is nicer to implement. The two shapes are bridged by an adapter:

```csharp
public sealed class VoidRequestHandlerAdapter<TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest
{
    private readonly IRequestHandler<TRequest> _inner;

    public async Task<Unit> Handle(TRequest request, CancellationToken cancellationToken)
    {
        await _inner.Handle(request, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
```

During scanning, every discovered `IRequestHandler<TRequest>` gets a matching
`IRequestHandler<TRequest, Unit>` → `VoidRequestHandlerAdapter<TRequest>` registration. This is why
void handlers must be registered by scanning: a hand-written registration of the handler alone
leaves the adapter missing, and the wrapper will not find `IRequestHandler<TRequest, Unit>`.

The adapter is registered with `TryAdd`, so a type that directly implements
`IRequestHandler<TRequest, Unit>` takes precedence.

## Registration

`ServiceRegistrar` scans the configured assemblies once per handler interface:

| Interface | Registration |
|---|---|
| `IRequestHandler<,>`, `IRequestHandler<>`, `IStreamRequestHandler<,>` | `TryAdd` — one handler per request |
| `INotificationHandler<>`, `IRequestExceptionHandler<,,>`, `IRequestExceptionAction<,>` | `TryAddEnumerable` — many per message |
| `IRequestPreProcessor<>`, `IRequestPostProcessor<,>` | the same, but only with `AutoRegisterRequestProcessors` |

For each interface it collects the concrete implementations and the closed interfaces they
implement, then registers the pairs. Three extra passes handle the generic cases:

- **`AddConcretionsThatCouldBeClosed`** closes an open generic implementation over an already
  discovered closed interface, so `Handler<T> : IRequestHandler<Request, T>` can serve
  `IRequestHandler<Request, Foo>` once something in the assembly mentions `Foo`.
- **`RegisterOpenGenericMultiHandlers`** registers open generic implementations of the
  many-per-message interfaces as open generic services, letting the container close them on demand.
  This is what makes `Audit<T> : INotificationHandler<T>` work with no configuration. It registers
  only implementations that close the interface over **their own type parameters, in order**
  (`TypeExtensions.ClosesOverItsOwnTypeParameters`). That restriction matters: the container closes
  an open generic registration by handing the requested service type's arguments straight to the
  implementation, so a wrapped shape like `Handler<T> : INotificationHandler<Wrapped<T>>` would be
  closed as `Handler<Pinged>`, which does not implement `INotificationHandler<Pinged>` — and that
  failure would break *every* notification resolution, not just its own.
- **`CloseGenericHandlers`** runs only with `RegisterGenericHandlers = true`. For each open generic
  handler it builds the candidate set for every type parameter — the types in the scanned
  assemblies that satisfy its constraints — and registers the cartesian product of closures. The
  `MaxTypesClosing` and `MaxGenericTypeRegistrations` limits bound that product; the whole scan is
  additionally bounded by `RegistrationTimeout`.

For the many-per-message interfaces, the two passes are complementary and must not overlap. A
handler whose parameters map straight onto the interface is handled by the second pass, and the
third pass skips it — registering it in both would resolve two instances and invoke the handler
twice per publish. A wrapped shape is the reverse: skipped by the second pass, closed by the third.

The same shape rule is enforced up front by `AddOpenBehavior` and its siblings, which reject an
un-closable open generic at registration rather than letting it fail on the first dispatch.

Everything uses `TryAdd`/`TryAddEnumerable`, which is what makes your own earlier registration win
and makes scanning the same assembly twice idempotent.

## Exception handling

This is the one place where reflection happens per call — but only on the failure path.

`IRequestExceptionHandler<in TRequest, TResponse, in TException>` is **contravariant** in
`TException`. That has a consequence worth understanding: a handler declared for `Exception` is
assignable to `IRequestExceptionHandler<TRequest, TResponse, NotSupportedException>`, so the
registrar registers it under the closed interfaces of derived exception types too. Resolving the
handlers for a specific exception therefore returns catch-all handlers alongside specific ones, and
walking the exception hierarchy returns the same catch-all handler repeatedly.

`ExceptionHandlerResolver` deals with that:

1. Walk the thrown exception's type hierarchy, from the concrete type up to `Exception`, resolving
   `IEnumerable<IRequestExceptionHandler<TRequest, TResponse, X>>` at each step.
2. De-duplicate candidates **by implementation type**, so each handler runs at most once.
3. For each candidate, find the most derived exception type it actually declares a handler for, and
   measure its inheritance distance from the thrown type.
4. Order by that distance, ascending, so the most specific handler runs first. `HandlersOrderer`
   breaks ties by namespace proximity to the request and drops handlers that a derived handler in
   the same set overrides.
5. Invoke via the `MethodInfo` of the interface the candidate genuinely implements — not the one it
   was resolved under — so the call target is always valid.

`TargetInvocationException` is unwrapped with `ExceptionDispatchInfo` so the handler's own failure
keeps its stack trace.

Actions work the same way, except all of them run and the original exception is always rethrown.

## Notifications

`NotificationHandlerWrapperImpl<TNotification>` resolves
`IEnumerable<INotificationHandler<TNotification>>` and projects each into a
`NotificationHandlerExecutor` — the handler instance plus a callback that invokes it. The
collection is a lazy LINQ projection, so a publisher that never enumerates never resolves a
handler.

The executors are handed to `Dispatcher.PublishCore`, which forwards to the configured
`INotificationPublisher`. Overriding `PublishCore` lets you wrap publishing without replacing the
strategy.

Because the container matches `IEnumerable<INotificationHandler<T>>` on the closed service type
and does not apply interface variance, handlers are matched on the notification's **exact** runtime
type. [Notifications](notifications.md#handlers-are-matched-on-the-concrete-type) covers the two
ways around that.

## Performance notes

- **First call per request type** pays `MakeGenericType` + `Activator.CreateInstance` for the
  wrapper. Microseconds, once.
- **Subsequent calls** pay a dictionary lookup, one or two container resolutions, and one delegate
  allocation per registered behavior.
- **Behaviors are not free.** Every open generic behavior is resolved and composed on every
  request. Constrain `TRequest` so a behavior is only closed for the requests that need it.
- **The two exception behaviors are always registered**, so every request runs inside two
  `try`/`catch` frames. An untaken `try` costs nothing at runtime; the resolution of the two
  behavior instances is the only overhead.
- **Static caches grow with the number of distinct message types**, which is bounded by your code.

## Trimming and Native AOT

**Not supported.** Pipelines are constructed with `MakeGenericType` and `Activator.CreateInstance`
over types discovered by assembly scanning, none of which a trimmer or the AOT compiler can see.
Expect `MissingMethodException` or missing-handler failures in a trimmed or AOT-published app.

If you need AOT, call your handlers directly or generate the dispatch at compile time with a source
generator.

## Thread safety

`IDispatcher` is safe to use concurrently: it holds only the service provider and the publisher,
and the caches it consults are `ConcurrentDictionary`. Your handlers are resolved per call, so
their own lifetime and thread-safety rules apply — a `Scoped` handler shared across concurrent
`Send` calls in the same scope is as unsafe here as anywhere else.

## Source map

| Path | |
|---|---|
| `Dispatcher.cs` | `IDispatcher` implementation, the three wrapper caches |
| `Wrappers/` | Request, stream and notification wrappers; the pipeline fold |
| `Pipeline/` | Pre/post processor and exception handling contracts and behaviors |
| `NotificationPublishers/` | `ForeachAwaitPublisher`, `TaskWhenAllPublisher` |
| `Registration/` | `AddDispatcher`, `DispatcherServiceConfiguration`, `ServiceRegistrar`, scanning helpers |
| `Internal/` | `VoidRequestHandlerAdapter`, `ExceptionHandlerResolver`, `HandlersOrderer` |
