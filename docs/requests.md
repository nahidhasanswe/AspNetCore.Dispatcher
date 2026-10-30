# Requests

A request goes to exactly one handler. If no handler is registered, `Send` throws
`InvalidOperationException`; if two handlers claim the same request, the first one found during
scanning wins.

## Request with a response

```csharp
public record GetMember(int Id) : IRequest<MemberDto>;

public class GetMemberHandler : IRequestHandler<GetMember, MemberDto>
{
    public async Task<MemberDto> Handle(GetMember request, CancellationToken cancellationToken)
        => /* ... */;
}

MemberDto member = await dispatcher.Send(new GetMember(42), cancellationToken);
```

The response type is carried by the request, so `Send` infers it — there is no type argument to
pass and no cast at the call site.

## Request with no response

Implement `IRequest` and handle it with `IRequestHandler<TRequest>`:

```csharp
public record DeleteMember(int Id) : IRequest;

public class DeleteMemberHandler : IRequestHandler<DeleteMember>
{
    public async Task Handle(DeleteMember request, CancellationToken cancellationToken)
        => /* ... */;
}

await dispatcher.Send(new DeleteMember(42), cancellationToken);
```

The handler returns plain `Task`, not `Task<Unit>`. Internally the response type is still
[`Unit`](#unit), which is what lets behaviors apply to void requests unchanged — see
[How it works](architecture.md#void-requests).

> Register void handlers by assembly scanning, not with a hand-written
> `services.AddTransient<IRequestHandler<DeleteMember>, DeleteMemberHandler>()`. Scanning also
> registers the adapter that bridges them into the pipeline; a manual registration does not.

## Commands and queries

`ICommand`, `ICommand<TResponse>` and `IQuery<TResponse>` are marker interfaces over `IRequest` for
teams that like the distinction visible in the type:

```csharp
public record CreateMember(string Name) : ICommand<int>;
public record FindMembers(string Term) : IQuery<IReadOnlyList<MemberDto>>;
public record ArchiveMember(int Id) : ICommand;
```

Handlers are still `IRequestHandler<,>` — these markers change nothing at runtime, so you can also
use them to target a behavior at one half of your messages:

```csharp
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>   // commands get a transaction, queries do not
{
    // ...
}
```

## Synchronous handlers

When a handler has no real asynchronous work, derive from `RequestHandler<,>` or
`RequestHandler<>` instead of returning `Task.FromResult`:

```csharp
public class CalculateAgeHandler : RequestHandler<CalculateAge, int>
{
    protected override int Handle(CalculateAge request)
        => (DateTime.UtcNow - request.BornOn).Days / 365;
}

public class ClearCacheHandler : RequestHandler<ClearCache>
{
    protected override void Handle(ClearCache request) => _cache.Clear();
}
```

The base class implements the interface explicitly, so `Handle` is `protected` and cannot be called
directly — everything goes through the dispatcher. An exception thrown inside returns a faulted
task rather than throwing synchronously, which keeps the pipeline's exception handling working.

## Cancellation

The token you pass to `Send` reaches the handler and every behavior in between:

```csharp
await dispatcher.Send(new GetMember(42), cancellationToken);
```

In ASP.NET Core, pass the request-aborted token so a disconnected client stops the work:

```csharp
app.MapGet("/members/{id:int}", async (int id, IDispatcher dispatcher, CancellationToken ct)
    => await dispatcher.Send(new GetMember(id), ct));
```

Minimal APIs and controllers both bind `CancellationToken` to `HttpContext.RequestAborted`
automatically.

## Dispatching a request known only at runtime

When the request type is not known at compile time — a queue consumer, a scheduled job table, a
generic endpoint — use the object overload:

```csharp
object request = JsonSerializer.Deserialize(payload, resolvedType)!;
object? response = await dispatcher.Send(request, cancellationToken);
```

The response is boxed; for a void request it is `Unit.Value`. Passing something that does not
implement `IRequest<T>` throws `ArgumentException`.

## Unit

`Unit` is a struct with a single value, used as the response type of a void request because `void`
is not a valid generic argument in C#.

```csharp
Unit.Value          // the only value
Unit.Task           // a completed Task<Unit>, handy when implementing a behavior
```

All `Unit` values are equal, `ToString()` returns `()`, and it implements `IEquatable<Unit>` and
`IComparable<Unit>`.

You only name it when writing something generic over the response, for example an exception handler
for a void request:

```csharp
public class ArchiveFailedHandler : RequestExceptionHandler<ArchiveMember, Unit>
{
    protected override void Handle(ArchiveMember request, Exception exception, RequestExceptionHandlerState<Unit> state)
        => state.SetHandled(Unit.Value);
}
```

## ISender

If a class only sends requests, depend on `ISender` rather than `IDispatcher`. It carries `Send` and
`CreateStream` but not `Publish`, which documents the intent and narrows what a test needs to fake.
`IPublisher` is the mirror image. Both resolve to the same `Dispatcher` instance shape as
`IDispatcher`.

```csharp
public class MemberEndpoint
{
    private readonly ISender _sender;

    public MemberEndpoint(ISender sender) => _sender = sender;
}
```
