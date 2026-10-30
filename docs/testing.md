# Testing

## Handlers

A handler is a plain class with constructor dependencies. There is nothing to mock about the
dispatcher — call `Handle` directly:

```csharp
[Fact]
public async Task Returns_the_member()
{
    await using var db = InMemoryDb();
    db.Members.Add(new Member { Id = 1, Name = "Ann" });
    await db.SaveChangesAsync();

    var handler = new GetMemberHandler(db);

    var result = await handler.Handle(new GetMember(1), CancellationToken.None);

    Assert.Equal("Ann", result.Name);
}
```

This is the bulk of the testing, and none of it involves the library.

## Behaviors

A behavior takes a `next` delegate, so supply one and assert on what the behavior did around it:

```csharp
[Fact]
public async Task Short_circuits_when_the_response_is_cached()
{
    var cache = new MemoryCache(new MemoryCacheOptions());
    cache.Set("member:1", new MemberDto { Name = "Cached" });

    var behavior = new CacheBehavior(cache);
    var handlerWasCalled = false;

    var response = await behavior.Handle(
        new GetMember(1),
        _ =>
        {
            handlerWasCalled = true;
            return Task.FromResult(new MemberDto { Name = "Fresh" });
        },
        CancellationToken.None);

    Assert.Equal("Cached", response.Name);
    Assert.False(handlerWasCalled);
}
```

`RequestHandlerDelegate<TResponse>` takes an optional `CancellationToken`, so a lambda taking one
parameter satisfies it. To assert the token is propagated, capture it:

```csharp
CancellationToken captured = default;

await behavior.Handle(new GetMember(1), token => { captured = token; return Task.FromResult(dto); }, expected);

Assert.Equal(expected, captured);
```

## Through a real container

To test that registration, pipeline order and handler resolution all line up, build a real
provider. This is the most valuable integration test for anything pipeline-shaped:

```csharp
[Fact]
public async Task Validation_runs_before_the_handler()
{
    var services = new ServiceCollection();
    services.AddSingleton<IValidator<CreateMember>, CreateMemberValidator>();

    services.AddDispatcher(cfg =>
    {
        cfg.RegisterServicesFromAssemblyContaining<CreateMemberHandler>();
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    });

    await using var provider = services.BuildServiceProvider();
    var dispatcher = provider.GetRequiredService<IDispatcher>();

    await Assert.ThrowsAsync<ValidationException>(
        () => dispatcher.Send(new CreateMember(Name: "")));
}
```

### Isolating one test's handlers

Scanning picks up every handler in the assembly, including the ones other tests declared. Use
`TypeEvaluator` to restrict a provider to the types you care about. Declaring messages and handlers
as nested classes and filtering on the declaring type is a tidy way to do it:

```csharp
internal static class TestServices
{
    public static ServiceProvider Build<TScope>(Action<DispatcherServiceConfiguration>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddDispatcher(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<TScope>();
            cfg.TypeEvaluator = type => IsDeclaredWithin(type, typeof(TScope));
            configure?.Invoke(cfg);
        });

        return services.BuildServiceProvider();
    }

    private static bool IsDeclaredWithin(Type type, Type scope)
    {
        for (var current = type; current != null; current = current.DeclaringType)
        {
            if (current == scope)
            {
                return true;
            }
        }

        return false;
    }
}
```

```csharp
public class CreateMemberTests
{
    public record Ping : IRequest<string>;

    public class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken ct) => Task.FromResult("pong");
    }

    [Fact]
    public async Task Handles_the_ping()
    {
        using var provider = TestServices.Build<CreateMemberTests>();

        Assert.Equal("pong", await provider.GetRequiredService<IDispatcher>().Send(new Ping()));
    }
}
```

This library's own test suite uses exactly this helper.

## Faking the dispatcher

When testing a controller or endpoint, fake the narrow interface rather than the whole dispatcher:

```csharp
var sender = Substitute.For<ISender>();
sender.Send(Arg.Any<GetMember>(), Arg.Any<CancellationToken>())
      .Returns(new MemberDto { Name = "Ann" });

var controller = new MembersController(sender);
```

Depending on `ISender` or `IPublisher` instead of `IDispatcher` makes this smaller — there are
fewer members to stub and the dependency documents what the class actually does.

A hand-rolled recording fake, with no mocking library:

```csharp
public class RecordingPublisher : IPublisher
{
    public List<object> Published { get; } = new();

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        Published.Add(notification!);
        return Task.CompletedTask;
    }
}
```

> Mocking `Send<TResponse>(IRequest<TResponse>, CancellationToken)` with a mocking library works,
> but note the overload set: `Send` for a void request binds to
> `Send<TRequest>(TRequest, CancellationToken)`, which returns `Task`, not `Task<Unit>`. Set up the
> overload the code under test actually calls.

## Asserting on notifications

To check that a handler published what it should, go through a real container and collect the
notifications with a test handler:

```csharp
public class CollectingHandler : INotificationHandler<MemberCreated>
{
    public static readonly List<MemberCreated> Received = new();

    public Task Handle(MemberCreated notification, CancellationToken cancellationToken)
    {
        Received.Add(notification);
        return Task.CompletedTask;
    }
}
```

Clear the collection at the start of each test — xUnit runs tests within one class sequentially, so
a static collection is safe there, but not across classes running in parallel. Prefer an instance
registered as a singleton when the tests may overlap:

```csharp
var collector = new NotificationCollector();
services.AddSingleton(collector);
services.AddSingleton<INotificationHandler<MemberCreated>>(collector);
```

## Integration tests with WebApplicationFactory

The dispatcher needs no special treatment. Replace the handler's dependencies, not the dispatcher:

```csharp
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FakeEmailSender>();
        });
    }
}
```

To silence a notification handler in tests, filter it out at registration instead of removing it
afterwards:

```csharp
cfg.TypeEvaluator = type => type != typeof(SendWelcomeEmail);
```

## Testing exception handlers

Exception handlers are reached through the pipeline, so they need a container:

```csharp
[Fact]
public async Task Falls_back_when_the_member_is_missing()
{
    using var provider = TestServices.Build<MemberFallbackTests>();

    var response = await provider.GetRequiredService<IDispatcher>().Send(new GetMember(999));

    Assert.Equal(MemberDto.Empty, response);
}
```

To test the handler class in isolation, construct the state object yourself:

```csharp
var state = new RequestExceptionHandlerState<MemberDto>();

await ((IRequestExceptionHandler<GetMember, MemberDto, MemberNotFoundException>)handler)
    .Handle(new GetMember(1), new MemberNotFoundException(), state, CancellationToken.None);

Assert.True(state.Handled);
Assert.Equal(MemberDto.Empty, state.Response);
```

The cast is needed because the base classes implement the interface explicitly.
