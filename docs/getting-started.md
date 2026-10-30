# Getting started

## Install

```shell
dotnet add package AspNetCore.Dispatcher --prerelease
```

Or in the project file:

```xml
<PackageReference Include="AspNetCore.Dispatcher" Version="1.0.0-preview.2" />
```

The package targets `netstandard2.0`, `net8.0` and `net9.0`, so it works on modern .NET and on
.NET Framework 4.6.1 and later. Its only dependency is
`Microsoft.Extensions.DependencyInjection.Abstractions` (plus `Microsoft.Bcl.AsyncInterfaces` on
`netstandard2.0`, for `IAsyncEnumerable`).

## Register

`AddDispatcher` lives in the `Microsoft.Extensions.DependencyInjection` namespace, so it is already
in scope wherever you configure services. It needs at least one assembly to scan:

```csharp
builder.Services.AddDispatcher(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
});
```

Scanning registers everything it finds in those assemblies: request handlers, notification
handlers, stream handlers, exception handlers and exception actions. It also registers
`IDispatcher`, `ISender` and `IPublisher`.

Multiple assemblies — typical when handlers live in an application layer separate from the web
project:

```csharp
builder.Services.AddDispatcher(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();              // the web project
    cfg.RegisterServicesFromAssemblyContaining<CreateMemberHandler>();  // the application project
});
```

Shorthands for the simple cases:

```csharp
builder.Services.AddDispatcherFromAssemblyContaining<Program>();
builder.Services.AddDispatcher(typeof(Program).Assembly, typeof(ApplicationMarker).Assembly);
```

Forgetting the assembly throws immediately at startup rather than failing later on the first
`Send`:

```
ArgumentException: No assemblies found to scan. Supply at least one assembly to scan for handlers.
```

## Your first request

A request is a message; a handler is the single thing that answers it.

```csharp
using AspNetCore.Dispatcher;

public record GetMember(int Id) : IRequest<MemberDto>;
```

```csharp
public class GetMemberHandler : IRequestHandler<GetMember, MemberDto>
{
    private readonly AppDbContext _db;

    public GetMemberHandler(AppDbContext db) => _db = db;

    public async Task<MemberDto> Handle(GetMember request, CancellationToken cancellationToken)
    {
        var member = await _db.Members
            .SingleOrDefaultAsync(m => m.Id == request.Id, cancellationToken);

        return MemberDto.From(member);
    }
}
```

Handlers are constructed by the container, so constructor injection works exactly as it does
anywhere else.

Send it from wherever you have an `IDispatcher` (or an `ISender`, if that endpoint only sends):

```csharp
public class MembersController : ControllerBase
{
    private readonly IDispatcher _dispatcher;

    public MembersController(IDispatcher dispatcher) => _dispatcher = dispatcher;

    [HttpGet("{id:int}")]
    public Task<MemberDto> Get(int id, CancellationToken cancellationToken)
        => _dispatcher.Send(new GetMember(id), cancellationToken);
}
```

## Your first notification

A notification is a fact that has already happened. Zero or more handlers react to it, and none of
them return anything.

```csharp
public record MemberCreated(int Id) : INotification;

public class SendWelcomeEmail : INotificationHandler<MemberCreated>
{
    public Task Handle(MemberCreated notification, CancellationToken cancellationToken) => /* ... */;
}

public class UpdateSearchIndex : INotificationHandler<MemberCreated>
{
    public Task Handle(MemberCreated notification, CancellationToken cancellationToken) => /* ... */;
}
```

```csharp
await _dispatcher.Publish(new MemberCreated(member.Id), cancellationToken);
```

Both handlers run. By default they run one after another, and the first one that throws stops the
rest — see [Notifications](notifications.md) to change that.

## Request or notification?

| Use a request when | Use a notification when |
|---|---|
| Exactly one thing should handle it | Any number of things may react |
| You need a result back | You need nothing back |
| Not handling it is a bug | Not handling it is fine |
| Named as an instruction: `CreateMember` | Named as a past event: `MemberCreated` |

Sending a request with no registered handler throws. Publishing a notification with no handlers is
a no-op.

## Suggested project layout

Nothing in the library requires a particular layout, but keeping each message beside its handler
keeps features self-contained:

```
Application/
  Members/
    CreateMember.cs           // the request, its handler, its validator
    GetMember.cs
    MemberCreated.cs          // the notification and its handlers
  Behaviors/
    LoggingBehavior.cs
    ValidationBehavior.cs
    TransactionBehavior.cs
  ApplicationMarker.cs        // an empty class to point RegisterServicesFromAssemblyContaining at
```

A dedicated marker type is worth it — `RegisterServicesFromAssemblyContaining<ApplicationMarker>()`
keeps the registration from breaking when you move a handler between files.

## Where to next

- [Requests](requests.md) for void requests, commands/queries and synchronous handlers
- [Pipeline behaviors](behaviors.md) to add logging, validation or transactions across every request
- [Configuration](configuration.md) for lifetimes and scanning control
