# Exception handling

Two contracts, with different powers:

| | Exception **handler** | Exception **action** |
|---|---|---|
| Interface | `IRequestExceptionHandler<TRequest, TResponse, TException>` | `IRequestExceptionAction<TRequest, TException>` |
| Can supply a response | yes | no |
| Can stop the exception | yes | no — it is always rethrown |
| Typical use | turn a domain failure into a fallback result | log, trace, emit a metric, compensate |

Both are found by assembly scanning — there is nothing to register. The behaviors that drive them
(`RequestExceptionActionProcessorBehavior<,>` and `RequestExceptionProcessorBehavior<,>`) are
always in the pipeline, as its two outermost layers, so they also catch exceptions thrown by your
own behaviors and processors.

Both live in `AspNetCore.Dispatcher.Pipeline`.

## Handlers: recovering

```csharp
using AspNetCore.Dispatcher.Pipeline;

public class MemberNotFoundHandler
    : IRequestExceptionHandler<GetMember, MemberDto, MemberNotFoundException>
{
    public Task Handle(
        GetMember request,
        MemberNotFoundException exception,
        RequestExceptionHandlerState<MemberDto> state,
        CancellationToken cancellationToken)
    {
        state.SetHandled(MemberDto.Empty);
        return Task.CompletedTask;
    }
}
```

Calling `SetHandled` swallows the exception and `Send` returns the response you passed. Returning
without calling it leaves the exception to the next candidate, and if none handles it, the original
exception is rethrown with its stack trace intact.

`RequestExceptionHandlerState<TResponse>`:

| Member | |
|---|---|
| `SetHandled(TResponse response)` | Marks the exception handled and sets the response. |
| `Handled` | Whether some handler has called `SetHandled`. |
| `Response` | The response that was set. |

### Base classes

| Base class | Override |
|---|---|
| `AsyncRequestExceptionHandler<TRequest, TResponse, TException>` | `Task Handle(request, exception, state, ct)` |
| `AsyncRequestExceptionHandler<TRequest, TResponse>` | the same, for every `Exception` |
| `RequestExceptionHandler<TRequest, TResponse, TException>` | `void Handle(request, exception, state)` |
| `RequestExceptionHandler<TRequest, TResponse>` | the same, for every `Exception` |

```csharp
public class AnyGetMemberFailure : AsyncRequestExceptionHandler<GetMember, MemberDto>
{
    protected override Task Handle(
        GetMember request,
        Exception exception,
        RequestExceptionHandlerState<MemberDto> state,
        CancellationToken cancellationToken)
    {
        state.SetHandled(MemberDto.Empty);
        return Task.CompletedTask;
    }
}
```

### Void requests

A void request's response type is `Unit`:

```csharp
public class ArchiveFailedHandler : RequestExceptionHandler<ArchiveMember, Unit>
{
    protected override void Handle(ArchiveMember request, Exception exception, RequestExceptionHandlerState<Unit> state)
    {
        _logger.LogWarning(exception, "Archiving {Id} failed, ignoring", request.Id);
        state.SetHandled(Unit.Value);
    }
}
```

## Actions: observing

An action cannot suppress the failure. Every matching action runs, then the exception is rethrown.

```csharp
public class LogMemberFailure : IRequestExceptionAction<GetMember, Exception>
{
    private readonly ILogger<LogMemberFailure> _logger;

    public LogMemberFailure(ILogger<LogMemberFailure> logger) => _logger = logger;

    public Task Execute(GetMember request, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "GetMember {Id} failed", request.Id);
        return Task.CompletedTask;
    }
}
```

### Base classes

| Base class | Override |
|---|---|
| `AsyncRequestExceptionAction<TRequest, TException>` | `Task Execute(request, exception, ct)` |
| `AsyncRequestExceptionAction<TRequest>` | the same, for every `Exception` |
| `RequestExceptionAction<TRequest, TException>` | `void Execute(request, exception)` |
| `RequestExceptionAction<TRequest>` | the same, for every `Exception` |

An exception thrown by an action replaces the original one, so keep actions defensive.

## Matching and ordering

When a request fails, the exception's whole type hierarchy is considered — from the concrete type
up through its base types to `Exception`. Candidates are then ordered **most specific first**:

```csharp
class NotFoundHandler  : IRequestExceptionHandler<GetMember, MemberDto, MemberNotFoundException> { }
class DbHandler        : IRequestExceptionHandler<GetMember, MemberDto, DbException> { }
class CatchAllHandler  : AsyncRequestExceptionHandler<GetMember, MemberDto> { }   // Exception
```

A thrown `MemberNotFoundException` reaches `NotFoundHandler` first. If it calls `SetHandled`, the
others never run. If it does not, `CatchAllHandler` gets a turn.

Three rules settle the order:

1. **Specificity.** The handler whose declared exception type is closest to the thrown type in the
   inheritance chain goes first.
2. **Each implementation runs at most once per failure.** A handler declared for `Exception` is
   also reachable while inspecting every derived exception type, so candidates are de-duplicated by
   implementation type.
3. **Proximity, then name,** as a tiebreaker between equally specific candidates: one whose
   namespace shares a longer prefix with the request's namespace wins, so a feature-local handler
   beats a shared one. A handler that a derived handler in the same set overrides is dropped
   entirely.

The same rules apply to actions, except that all of them run — there is no early exit.

## Scope

Exception handling applies to **requests**, including void requests. It does not apply to:

- **Notifications.** Use a [custom publisher](notifications.md#custom-publishers) to isolate handler
  failures.
- **Stream requests.** Recover inside a
  [stream behavior](streaming.md#differences-from-the-request-pipeline).

## Choosing between this and a behavior

An exception handler is worth it when the recovery is specific to a request and a failure type, and
you want it discovered by scanning rather than wired into a pipeline. A plain
[behavior](behaviors.md) with `try`/`catch` is simpler when the policy is global:

```csharp
public class FallbackBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (TransientException exception)
        {
            _logger.LogWarning(exception, "Falling back for {Request}", typeof(TRequest).Name);
            return default!;
        }
    }
}
```

Note that a behavior's `catch` sits *inside* the exception handlers, since they are the outermost
layers of the pipeline — so a behavior that swallows an exception prevents handlers and actions from
seeing it at all.

## Letting failures reach the caller

If you want validation and domain failures to become HTTP responses rather than being swallowed,
do not write exception handlers for them — let them propagate and map them at the edge. See
[ASP.NET Core integration](aspnet-core.md#mapping-failures-onto-http-responses).
