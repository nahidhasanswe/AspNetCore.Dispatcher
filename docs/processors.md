# Pre and post processors

A processor runs before or after a handler without wrapping it. It is the simpler tool: no `next`
delegate to remember, no chance of forgetting to call it. The trade-off is that a processor cannot
short-circuit the handler, catch its exceptions, or change its response.

```csharp
public interface IRequestPreProcessor<in TRequest> where TRequest : notnull
{
    Task Process(TRequest request, CancellationToken cancellationToken);
}

public interface IRequestPostProcessor<in TRequest, in TResponse> where TRequest : notnull
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}
```

A post-processor receives the response but cannot replace it — `TResponse` is contravariant and the
method returns `Task`. Use a behavior if you need to rewrite the result.

## Writing one

For a single request:

```csharp
public class StampCreateMember : IRequestPreProcessor<CreateMember>
{
    private readonly ICurrentUser _user;

    public StampCreateMember(ICurrentUser user) => _user = user;

    public Task Process(CreateMember request, CancellationToken cancellationToken)
    {
        request.CreatedBy = _user.Id;
        return Task.CompletedTask;
    }
}
```

For every request, make it generic:

```csharp
public class AuditPreProcessor<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    private readonly IAuditLog _audit;

    public AuditPreProcessor(IAuditLog audit) => _audit = audit;

    public Task Process(TRequest request, CancellationToken cancellationToken)
        => _audit.Record(typeof(TRequest).Name, request, cancellationToken);
}

public class AuditPostProcessor<TRequest, TResponse> : IRequestPostProcessor<TRequest, TResponse>
    where TRequest : notnull
{
    public Task Process(TRequest request, TResponse response, CancellationToken cancellationToken)
        => _audit.RecordCompletion(typeof(TRequest).Name, cancellationToken);
}
```

## Registering

Explicitly, which is the clearer default:

```csharp
cfg.AddRequestPreProcessor<StampCreateMember>();
cfg.AddOpenRequestPreProcessor(typeof(AuditPreProcessor<>));

cfg.AddRequestPostProcessor<NotifyOnCreate>();
cfg.AddOpenRequestPostProcessor(typeof(AuditPostProcessor<,>));
```

Or let scanning find every one of them:

```csharp
cfg.AutoRegisterRequestProcessors = true;
```

With `AutoRegisterRequestProcessors`, any type in the scanned assemblies implementing
`IRequestPreProcessor<>` or `IRequestPostProcessor<,>` is registered, open generics included. It is
off by default so that adding a processor class never silently changes the behavior of every
request in the application.

Each registration overload also accepts a `ServiceLifetime`:

```csharp
cfg.AddOpenRequestPreProcessor(typeof(AuditPreProcessor<>), ServiceLifetime.Scoped);
```

Registering a type that does not implement the matching interface throws
`InvalidOperationException` at startup.

The behaviors that drive the processors (`RequestPreProcessorBehavior<,>` and
`RequestPostProcessorBehavior<,>`) are only added to the pipeline when at least one processor is
registered or `AutoRegisterRequestProcessors` is on — if you use none, you pay nothing.

## Order

Processors sit outside your own behaviors and inside the exception handling:

```
exception actions → exception handlers → pre-processors → post-processors → your behaviors → handler
```

Within each group, processors run in registration order. All pre-processors run before the handler;
all post-processors run after it returns.

A post-processor does **not** run when the handler throws — the exception propagates past
`RequestPostProcessorBehavior` before it gets to its own code. If you need cleanup on both paths,
use a behavior with a `finally`.

## Processor or behavior?

| | Processor | Behavior |
|---|---|---|
| Run code before the handler | yes | yes |
| Run code after the handler | yes (on success only) | yes |
| See the response | yes, read-only | yes |
| Change the response | no | yes |
| Skip the handler | no | yes |
| Catch the handler's exception | no | yes |
| Run code on failure | no | yes, with `try`/`finally` |
| Auto-registered by scanning | optional | never |

Reach for a processor when the work is genuinely one-directional — stamping a field, writing an
audit row, invalidating a cache after a write. Reach for a [behavior](behaviors.md) for everything
else.
