# Pipeline behaviors

A behavior wraps the handler. It receives the request and a `next` delegate, and decides what
happens before, after, instead of, or around the call.

```csharp
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
```

Always pass the token through to `next` so cancellation reaches the handler:

```csharp
var response = await next(cancellationToken);
```

## Registering

```csharp
builder.Services.AddDispatcher(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();

    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));        // every request
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddBehavior<CacheGetMemberBehavior>();              // only the request it closes over
    cfg.AddBehavior(typeof(IPipelineBehavior<GetMember, MemberDto>), typeof(MyBehavior));
    cfg.AddOpenBehavior(typeof(MetricsBehavior<,>), ServiceLifetime.Singleton);
});
```

| Method | Use for |
|---|---|
| `AddOpenBehavior(typeof(X<,>))` | An open generic behavior that applies to every request. |
| `AddBehavior<TImplementation>()` | A closed behavior; registered for each `IPipelineBehavior<,>` it implements. |
| `AddBehavior<TService, TImplementation>()` | A closed behavior registered against one specific service type. |
| `AddBehavior(Type implementationType)` / `AddBehavior(Type service, Type implementation)` | The same, when you only have `Type` values. |

Each overload takes an optional `ServiceLifetime` (default `Transient`). Registering a type that
does not implement `IPipelineBehavior<,>` throws `InvalidOperationException` at startup.

Behaviors are **not** picked up by assembly scanning — unlike handlers, they must be registered
explicitly. That is deliberate: a behavior applies to every request, so turning one on should be a
visible decision.

## Order

Behaviors nest in registration order. The first registered is the outermost:

```csharp
cfg.AddOpenBehavior(typeof(OuterBehavior<,>));
cfg.AddOpenBehavior(typeof(InnerBehavior<,>));
```

```
outer before → inner before → handler → inner after → outer after
```

The full pipeline, outermost first:

1. `RequestExceptionActionProcessorBehavior` — always registered
2. `RequestExceptionProcessorBehavior` — always registered
3. `RequestPreProcessorBehavior` — only when pre-processors exist
4. `RequestPostProcessorBehavior` — only when post-processors exist
5. **your behaviors, in the order you added them**
6. the handler

So the built-in exception handling wraps your behaviors: a failure thrown by your validation
behavior reaches exception handlers and actions just like a failure from the handler itself.

Two consequences worth knowing:

- Put ordering-sensitive behaviors in the order you want. Validation before transactions before
  the handler means a rejected request never opens a transaction.
- A behavior cannot see the ones registered after it, only the `next` delegate.

## Short-circuiting

A behavior that does not call `next` prevents the handler from running:

```csharp
public class CacheBehavior : IPipelineBehavior<GetMember, MemberDto>
{
    private readonly IMemoryCache _cache;

    public CacheBehavior(IMemoryCache cache) => _cache = cache;

    public async Task<MemberDto> Handle(GetMember request, RequestHandlerDelegate<MemberDto> next, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue($"member:{request.Id}", out MemberDto? cached))
        {
            return cached!;
        }

        var response = await next(cancellationToken);
        _cache.Set($"member:{request.Id}", response, TimeSpan.FromMinutes(5));
        return response;
    }
}
```

## Targeting a subset of requests

Constrain `TRequest` and the behavior is only closed for requests that satisfy it. The container
skips it elsewhere, with no runtime cost:

```csharp
// Only requests that opt in
public interface ITransactional { }

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITransactional
{
    // ...
}
```

```csharp
public record CreateMember(string Name) : ICommand<int>, ITransactional;
```

The same trick works with `ICommand<TResponse>`, a `TResponse` constraint, or any marker interface
of your own.

## Recipes

### Validation

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}
```

Injecting `IEnumerable<IValidator<TRequest>>` means the behavior is harmless for requests that have
no validator — the collection is simply empty.

### Transactions

```csharp
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    private readonly AppDbContext _db;

    public TransactionBehavior(AppDbContext db) => _db = db;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is not null)
        {
            return await next(cancellationToken);    // already inside one, do not nest
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var response = await next(cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return response;
    }
}
```

The `await using` rolls back if `next` throws. Register it **after** validation so invalid requests
never open a transaction.

### Logging and timing

```csharp
public class TimingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TimingBehavior<TRequest, TResponse>> _logger;

    public TimingBehavior(ILogger<TimingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            return await next(cancellationToken);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);

            if (elapsed > TimeSpan.FromSeconds(1))
            {
                _logger.LogWarning("{Request} took {Elapsed}ms", typeof(TRequest).Name, elapsed.TotalMilliseconds);
            }
        }
    }
}
```

`Stopwatch.GetElapsedTime` needs .NET 7 or later; on `netstandard2.0` use a `Stopwatch` instance.

### Retries

```csharp
public class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IQuery<TResponse>        // only retry reads, never writes
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await next(cancellationToken);
            }
            catch (TransientException) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
            }
        }
    }
}
```

Retrying a command re-runs the handler, including anything it already committed. Constrain retries
to idempotent requests.

### Ambient context

```csharp
public class TenantBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ITenantAccessor _tenant;
    private readonly IHttpContextAccessor _http;

    public TenantBehavior(ITenantAccessor tenant, IHttpContextAccessor http)
    {
        _tenant = tenant;
        _http = http;
    }

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _tenant.Current = _http.HttpContext?.User.FindFirst("tenant")?.Value;
        return next(cancellationToken);
    }
}
```

## Behavior or processor?

Use a **behavior** when you need to wrap the call: measure it, catch from it, replace its result,
decide whether it runs at all. Use a [pre/post processor](processors.md) when you only need to run
something before or after, and the code reads better without the `next` ceremony.

## Behaviors and void requests

A void request has `Unit` as its response type, so an open generic behavior
(`IPipelineBehavior<TRequest, TResponse>`) applies to it unchanged — `TResponse` is `Unit`. There is
nothing extra to register.

To write a behavior that only applies to void requests, keep both type parameters and constrain
`TRequest` to `IRequest` — only void requests implement it, so `TResponse` is always `Unit`:

```csharp
public class CommandAuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest
{
    // ...
}
```

```csharp
cfg.AddOpenBehavior(typeof(CommandAuditBehavior<,>));
```

Keep the arity at two. An open behavior declared as `IPipelineBehavior<TRequest, Unit>` with a
single type parameter cannot be registered with `AddOpenBehavior`: the container closes an open
generic registration by handing the service type's arguments to the implementation, and
`IPipelineBehavior<,>` has two. `AddOpenBehavior` rejects that shape at startup with an explanation
rather than letting it fail on the first dispatch. Register such a behavior fully closed instead:

```csharp
cfg.AddBehavior(typeof(IPipelineBehavior<ArchiveMember, Unit>), typeof(ArchiveAuditBehavior));
```
