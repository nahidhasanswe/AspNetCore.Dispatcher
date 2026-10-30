# ASP.NET Core integration

## Registration

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDispatcher(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.RegisterServicesFromAssemblyContaining<ApplicationMarker>();

    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

var app = builder.Build();
```

The default `Transient` lifetime is right for web apps: a handler is created per `Send`, and any
scoped dependency it injects comes from the current request scope.

## Minimal APIs

```csharp
app.MapGet("/members/{id:int}", async (int id, IDispatcher dispatcher, CancellationToken ct)
    => await dispatcher.Send(new GetMember(id), ct));

app.MapPost("/members", async (CreateMember command, IDispatcher dispatcher, CancellationToken ct)
    => Results.Created($"/members/{await dispatcher.Send(command, ct)}", null));

app.MapDelete("/members/{id:int}", async (int id, IDispatcher dispatcher, CancellationToken ct) =>
{
    await dispatcher.Send(new DeleteMember(id), ct);
    return Results.NoContent();
});
```

The `CancellationToken` parameter binds to `HttpContext.RequestAborted`, so a client that
disconnects cancels the handler. Always take it and pass it along.

Binding the request body straight onto the message type keeps endpoints to one line. If the message
carries values from more than one place — route, body, claims — compose it in the endpoint:

```csharp
app.MapPut("/members/{id:int}", async (
    int id,
    UpdateMemberBody body,
    ClaimsPrincipal user,
    IDispatcher dispatcher,
    CancellationToken ct) =>
{
    await dispatcher.Send(new UpdateMember(id, body.Name, user.GetUserId()), ct);
    return Results.NoContent();
});
```

## Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class MembersController : ControllerBase
{
    private readonly IDispatcher _dispatcher;

    public MembersController(IDispatcher dispatcher) => _dispatcher = dispatcher;

    [HttpGet("{id:int}")]
    public Task<MemberDto> Get(int id, CancellationToken cancellationToken)
        => _dispatcher.Send(new GetMember(id), cancellationToken);

    [HttpPost]
    public async Task<ActionResult<int>> Create(CreateMember command, CancellationToken cancellationToken)
    {
        var id = await _dispatcher.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, id);
    }
}
```

A base controller keeps the dependency out of every subclass:

```csharp
public abstract class ApiController : ControllerBase
{
    private IDispatcher? _dispatcher;

    protected IDispatcher Dispatcher =>
        _dispatcher ??= HttpContext.RequestServices.GetRequiredService<IDispatcher>();
}
```

## Streaming endpoints

Return the stream directly and let the framework serialise it incrementally:

```csharp
app.MapGet("/calls", (DateTime from, IDispatcher dispatcher, CancellationToken ct)
    => dispatcher.CreateStream(new StreamCalls(from), ct));
```

Do not buffer it with `ToListAsync()` first. For newline-delimited JSON or server-sent events,
write inside the loop:

```csharp
app.MapGet("/calls/stream", async (DateTime from, HttpContext context, IDispatcher dispatcher, CancellationToken ct) =>
{
    context.Response.ContentType = "application/x-ndjson";

    await foreach (var call in dispatcher.CreateStream(new StreamCalls(from), ct))
    {
        await JsonSerializer.SerializeAsync(context.Response.Body, call, cancellationToken: ct);
        await context.Response.Body.WriteAsync("\n"u8.ToArray(), ct);
        await context.Response.Body.FlushAsync(ct);
    }
});
```

## Mapping failures onto HTTP responses

Let domain failures propagate out of the handler and translate them once, at the edge. Do not write
exception handlers for failures that should become a 4xx — that would swallow them.

```csharp
public class ValidationException : Exception
{
    public ValidationException(IEnumerable<ValidationFailure> failures) => Failures = failures.ToList();
    public IReadOnlyList<ValidationFailure> Failures { get; }
}
```

With `IExceptionHandler` (.NET 8 and later):

```csharp
public class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validation)
        {
            return false;
        }

        var problem = new HttpValidationProblemDetails(
            validation.Failures
                .GroupBy(f => f.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray()))
        {
            Status = StatusCodes.Status400BadRequest
        };

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
```

```csharp
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddProblemDetails();

app.UseExceptionHandler();
```

Where the two mechanisms belong:

| Failure | Where to handle it |
|---|---|
| Invalid input | Let it propagate; map to 400 at the edge. |
| Not found | Let it propagate; map to 404. Or return a nullable response and let the endpoint decide. |
| Transient infrastructure failure the request can absorb | A dispatcher [exception handler](exception-handling.md) with a fallback response. |
| Something to record but not change | A dispatcher [exception action](exception-handling.md#actions-observing). |

## Sending from a background service

A `BackgroundService` is a singleton, so it cannot inject a scoped handler's dependencies. Create a
scope per unit of work:

```csharp
public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OutboxWorker(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

            await dispatcher.Send(new DrainOutbox(), stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
```

Injecting `IDispatcher` straight into a singleton works only if every handler it reaches is itself
singleton-safe. The scope is cheaper than the debugging.

## Publishing domain events

A common pattern is to collect events while handling a command and publish them once the
transaction commits, so no handler reacts to a change that gets rolled back:

```csharp
public class DomainEventBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly AppDbContext _db;
    private readonly IPublisher _publisher;

    public DomainEventBehavior(AppDbContext db, IPublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        var events = _db.ChangeTracker
            .Entries<IHasDomainEvents>()
            .SelectMany(entry => entry.Entity.DrainDomainEvents())
            .ToList();

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var domainEvent in events)
        {
            await _publisher.Publish(domainEvent, cancellationToken);
        }

        return response;
    }
}
```

Note `IPublisher` rather than `IDispatcher` — the behavior only publishes, and saying so keeps it
honest.

## Health of the registration

To catch a bad registration at startup rather than on the first request, validate the container:

```csharp
builder.Host.UseDefaultServiceProvider((context, options) =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
```

`ValidateOnBuild` will surface a handler whose dependency is missing or whose lifetime is captive.
Constrained open generic behaviors are handled correctly by validation and will not trip it.
