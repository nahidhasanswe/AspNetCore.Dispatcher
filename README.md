# AspNet.Core.Dispatcher

[![NuGet](https://img.shields.io/nuget/vpre/AspNet.Core.Dispatcher.svg)](https://www.nuget.org/packages/AspNet.Core.Dispatcher)
[![Downloads](https://img.shields.io/nuget/dt/AspNet.Core.Dispatcher.svg)](https://www.nuget.org/packages/AspNet.Core.Dispatcher)
[![Build](https://img.shields.io/github/actions/workflow/status/nahidhasanswe/AspNetCore.Dispatcher/ci.yml?branch=main)](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/LICENSE)

In-process messaging for .NET. Send a request to exactly one handler, publish a notification to
many, stream results with `IAsyncEnumerable`, and wrap all of it in a pipeline of behaviors,
pre/post processors and exception handlers.

If you know MediatR, you already know this library — the API surface is the same, under the
`AspNetCore.Dispatcher` namespace. See
[Migrating from MediatR](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/migrating-from-mediatr.md).

- Targets `netstandard2.0`, `net8.0`, `net9.0`
- One dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`
- Reflection happens once per request type and is then cached; the per-call path is a container
  resolution and delegate composition

## Install

```shell
dotnet add package AspNet.Core.Dispatcher --prerelease
```

## Quick start

Register it, pointing at the assemblies that hold your handlers:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddDispatcher(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
});
```

Define a request and its handler:

```csharp
using AspNetCore.Dispatcher;

public record GetMember(int Id) : IRequest<MemberDto>;

public class GetMemberHandler : IRequestHandler<GetMember, MemberDto>
{
    private readonly AppDbContext _db;

    public GetMemberHandler(AppDbContext db) => _db = db;

    public async Task<MemberDto> Handle(GetMember request, CancellationToken cancellationToken)
    {
        var member = await _db.Members.FindAsync(new object[] { request.Id }, cancellationToken);
        return MemberDto.From(member);
    }
}
```

Send it:

```csharp
app.MapGet("/members/{id:int}", async (int id, IDispatcher dispatcher, CancellationToken ct)
    => await dispatcher.Send(new GetMember(id), ct));
```

That is the whole loop. Everything else below is optional.

## What you get

| Capability | Contract | Dispatch |
|---|---|---|
| Request with a response | `IRequest<TResponse>` + `IRequestHandler<TRequest, TResponse>` | `Send` |
| Request with no response | `IRequest` + `IRequestHandler<TRequest>` | `Send` |
| Commands and queries | `ICommand`, `ICommand<T>`, `IQuery<T>` | `Send` |
| Notification, many handlers | `INotification` / `IEvent` + `INotificationHandler<T>` | `Publish` |
| Streaming response | `IStreamRequest<T>` + `IStreamRequestHandler<TRequest, T>` | `CreateStream` |
| Wrap a request | `IPipelineBehavior<TRequest, TResponse>` | automatic |
| Wrap a stream | `IStreamPipelineBehavior<TRequest, TResponse>` | automatic |
| Run before / after a handler | `IRequestPreProcessor<T>`, `IRequestPostProcessor<T, TResponse>` | automatic |
| Recover from an exception | `IRequestExceptionHandler<TRequest, TResponse, TException>` | automatic |
| Observe an exception | `IRequestExceptionAction<TRequest, TException>` | automatic |
| Control notification fan-out | `INotificationPublisher` | configured |

Synchronous base classes (`RequestHandler<,>`, `RequestHandler<>`, `NotificationHandler<>`) are
there when a handler has no real async work, and `Unit` is the stand-in response type for void
requests.

## A behavior, end to end

Behaviors are where logging, validation, transactions, caching and retries live. They nest in
registration order — the first registered is the outermost:

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next(cancellationToken);
        _logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}
```

```csharp
cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
```

## Documentation

| Guide | Covers |
|---|---|
| [Getting started](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/getting-started.md) | Install, register, first request, project layout |
| [Requests](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/requests.md) | Requests, commands, queries, void requests, sync handlers, runtime dispatch |
| [Notifications](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/notifications.md) | Publishing, handler fan-out, publishing strategies, custom publishers |
| [Streaming](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/streaming.md) | `IStreamRequest`, back-pressure, cancellation, stream behaviors |
| [Pipeline behaviors](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/behaviors.md) | Ordering, short-circuiting, validation / transaction / caching / retry recipes |
| [Pre and post processors](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/processors.md) | When to prefer a processor over a behavior |
| [Exception handling](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/exception-handling.md) | Recovering handlers, observing actions, specificity rules |
| [Configuration](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/configuration.md) | Every `DispatcherServiceConfiguration` option, lifetimes, scanning, generic handlers |
| [ASP.NET Core integration](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/aspnet-core.md) | Controllers, minimal APIs, scopes, background services, exception mapping |
| [Testing](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/testing.md) | Unit-testing handlers and behaviors, integration tests, faking the dispatcher |
| [Migrating from MediatR](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/migrating-from-mediatr.md) | Name-by-name mapping, the two behavior differences, bulk find-and-replace |
| [How it works](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/docs/architecture.md) | Wrappers, caching, the void-handler adapter, performance and AOT notes |

## Compatibility

| | |
|---|---|
| Frameworks | `netstandard2.0` (covers .NET Framework 4.6.1+), `net8.0`, `net9.0` |
| Container | Any `IServiceCollection` / `IServiceProvider`. `AddDispatcher` uses only the standard abstractions. |
| Trimming / Native AOT | Not supported. Handler pipelines are constructed with `MakeGenericType` and `Activator.CreateInstance`, which trimming cannot see. |
| Thread safety | `IDispatcher` is safe to use concurrently. Handlers are resolved per call, so their own lifetime rules apply. |

## Versioning

[Semantic Versioning](https://semver.org). Prerelease versions (`1.0.0-preview.*`) may change the
public API; once `1.0.0` ships, breaking changes wait for a major bump. See
[CHANGELOG.md](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/CHANGELOG.md).

## Contributing

Issues and pull requests are welcome — see
[CONTRIBUTING.md](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/CONTRIBUTING.md).

```shell
dotnet build
dotnet test
```

## License

[MIT](https://github.com/nahidhasanswe/AspNetCore.Dispatcher/blob/main/LICENSE)
