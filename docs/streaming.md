# Streaming requests

A stream request has one handler that yields many responses over time. Use it when the result set
is large enough that materialising it would cost too much memory, or when the consumer should start
working before the producer finishes.

```csharp
public record StreamCalls(DateTime From) : IStreamRequest<CallDto>;

public class StreamCallsHandler : IStreamRequestHandler<StreamCalls, CallDto>
{
    private readonly AppDbContext _db;

    public StreamCallsHandler(AppDbContext db) => _db = db;

    public async IAsyncEnumerable<CallDto> Handle(
        StreamCalls request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = _db.Calls
            .Where(c => c.StartedOn >= request.From)
            .OrderBy(c => c.StartedOn)
            .AsAsyncEnumerable();

        await foreach (var call in query.WithCancellation(cancellationToken))
        {
            yield return CallDto.From(call);
        }
    }
}
```

```csharp
await foreach (var call in dispatcher.CreateStream(new StreamCalls(from), cancellationToken))
{
    // ...
}
```

The `[EnumeratorCancellation]` attribute (from `System.Runtime.CompilerServices`) is what lets the
token passed to `CreateStream` flow into the iterator. Without it the parameter is ignored when the
consumer uses `WithCancellation`, and your handler silently becomes uncancellable.

## Laziness

Nothing runs until the consumer starts enumerating, and the handler is suspended at each `yield
return` until the consumer asks for the next item. Back-pressure is therefore automatic: a slow
consumer slows the producer.

Breaking out of the loop early disposes the enumerator, which runs the handler's `finally` blocks
and disposes anything it holds.

## Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

await foreach (var call in dispatcher.CreateStream(new StreamCalls(from), cts.Token))
{
    // An OperationCanceledException surfaces from the await foreach when the token fires,
    // provided the handler observes the token.
}
```

The handler is responsible for observing the token — either by passing it to the I/O it awaits, or
with an explicit `cancellationToken.ThrowIfCancellationRequested()` in the loop.

## Stream pipeline behaviors

`IStreamPipelineBehavior<TRequest, TResponse>` is the streaming counterpart of
`IPipelineBehavior`. Because the response is a stream rather than a single value, a behavior can
observe, transform, filter or throttle each item as it passes:

```csharp
public class StreamLoggingBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<StreamLoggingBehavior<TRequest, TResponse>> _logger;

    public StreamLoggingBehavior(ILogger<StreamLoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var count = 0;
        _logger.LogInformation("Streaming {Request}", typeof(TRequest).Name);

        await foreach (var item in next(cancellationToken).WithCancellation(cancellationToken))
        {
            count++;
            yield return item;
        }

        _logger.LogInformation("Streamed {Count} items for {Request}", count, typeof(TRequest).Name);
    }
}
```

Register the same way as request behaviors, and in the same outermost-first order:

```csharp
cfg.AddOpenStreamBehavior(typeof(StreamLoggingBehavior<,>));
cfg.AddStreamBehavior<TakeFirstHundredBehavior>();                   // closed, for one request
cfg.AddStreamBehavior(typeof(IStreamPipelineBehavior<StreamCalls, CallDto>), typeof(MyBehavior));
```

A behavior that transforms items:

```csharp
public class RedactingBehavior : IStreamPipelineBehavior<StreamCalls, CallDto>
{
    public async IAsyncEnumerable<CallDto> Handle(
        StreamCalls request,
        StreamHandlerDelegate<CallDto> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var call in next(cancellationToken).WithCancellation(cancellationToken))
        {
            yield return call with { CallerNumber = Redact(call.CallerNumber) };
        }
    }
}
```

## Differences from the request pipeline

| | Requests | Streams |
|---|---|---|
| Behavior interface | `IPipelineBehavior<,>` | `IStreamPipelineBehavior<,>` |
| Continuation | `RequestHandlerDelegate<T>` → `Task<T>` | `StreamHandlerDelegate<T>` → `IAsyncEnumerable<T>` |
| Pre/post processors | Supported | Not applicable |
| Exception handlers / actions | Supported | Not applicable — wrap the enumeration in a stream behavior instead |

Stream requests do **not** go through `RequestExceptionProcessorBehavior`. To recover from a
failure mid-stream, do it in a stream behavior:

```csharp
public async IAsyncEnumerable<TResponse> Handle(
    TRequest request,
    StreamHandlerDelegate<TResponse> next,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    await using var enumerator = next(cancellationToken).GetAsyncEnumerator(cancellationToken);

    while (true)
    {
        TResponse item;

        try
        {
            if (!await enumerator.MoveNextAsync())
            {
                break;
            }

            item = enumerator.Current;
        }
        catch (TransientException exception)
        {
            _logger.LogWarning(exception, "Stream ended early");
            break;      // end the stream cleanly instead of faulting the consumer
        }

        yield return item;
    }
}
```

A `yield return` cannot sit inside a `try` block that has a `catch`, which is why the enumerator is
driven by hand here.

## Streaming over HTTP

ASP.NET Core serialises an `IAsyncEnumerable<T>` returned from an endpoint as a JSON array,
flushing as it goes:

```csharp
app.MapGet("/calls", (DateTime from, IDispatcher dispatcher, CancellationToken ct)
    => dispatcher.CreateStream(new StreamCalls(from), ct));
```

Do not `await` or `ToListAsync()` it first — that defeats the point. For server-sent events or
newline-delimited JSON, write to the response body inside the loop instead.

## Runtime dispatch

```csharp
await foreach (object? item in dispatcher.CreateStream(requestObject, cancellationToken))
{
    // ...
}
```

Each item is boxed. Passing an object that does not implement `IStreamRequest<T>` throws
`ArgumentException` when `CreateStream` is called, before enumeration begins.
