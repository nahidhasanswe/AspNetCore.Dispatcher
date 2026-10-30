using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

public class SendTests
{
    public class Ping : IRequest<string>
    {
        public string Message { get; set; } = string.Empty;
    }

    public class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
            => Task.FromResult(request.Message + " Pong");
    }

    public class VoidPing : IRequest
    {
    }

    public class VoidPingHandler : IRequestHandler<VoidPing>
    {
        public static int Calls;

        public Task Handle(VoidPing request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    public class SyncPing : IRequest<string>
    {
    }

    public class SyncPingHandler : RequestHandler<SyncPing, string>
    {
        protected override string Handle(SyncPing request) => "sync pong";
    }

    public class SyncVoidPing : IRequest
    {
    }

    public class SyncVoidPingHandler : RequestHandler<SyncVoidPing>
    {
        public static bool Called;

        protected override void Handle(SyncVoidPing request) => Called = true;
    }

    public class CommandPing : ICommand<string>
    {
    }

    public class QueryPing : IQuery<int>
    {
    }

    public class CommandAndQueryHandler : IRequestHandler<CommandPing, string>, IRequestHandler<QueryPing, int>
    {
        public Task<string> Handle(CommandPing request, CancellationToken cancellationToken) => Task.FromResult("command");

        public Task<int> Handle(QueryPing request, CancellationToken cancellationToken) => Task.FromResult(42);
    }

    public class CancellablePing : IRequest<bool>
    {
    }

    public class CancellablePingHandler : IRequestHandler<CancellablePing, bool>
    {
        public Task<bool> Handle(CancellablePing request, CancellationToken cancellationToken)
            => Task.FromResult(cancellationToken.IsCancellationRequested);
    }

    public class Unhandled : IRequest<string>
    {
    }

    public class NotARequest
    {
    }

    [Fact]
    public async Task Sends_request_to_handler_and_returns_response()
    {
        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var response = await dispatcher.Send(new Ping { Message = "Ping" });

        Assert.Equal("Ping Pong", response);
    }

    [Fact]
    public async Task Sends_void_request_to_handler()
    {
        VoidPingHandler.Calls = 0;

        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Send(new VoidPing());

        Assert.Equal(1, VoidPingHandler.Calls);
    }

    [Fact]
    public async Task Sends_request_via_object_dispatch()
    {
        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var response = await dispatcher.Send((object)new Ping { Message = "Ping" });

        Assert.Equal("Ping Pong", response);
    }

    [Fact]
    public async Task Sends_void_request_via_object_dispatch_and_returns_unit()
    {
        VoidPingHandler.Calls = 0;

        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var response = await dispatcher.Send((object)new VoidPing());

        Assert.Equal(1, VoidPingHandler.Calls);
        Assert.Equal(Unit.Value, Assert.IsType<Unit>(response));
    }

    [Fact]
    public async Task Supports_synchronous_handler_base_classes()
    {
        SyncVoidPingHandler.Called = false;

        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        Assert.Equal("sync pong", await dispatcher.Send(new SyncPing()));

        await dispatcher.Send(new SyncVoidPing());
        Assert.True(SyncVoidPingHandler.Called);
    }

    [Fact]
    public async Task Supports_command_and_query_marker_interfaces()
    {
        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        Assert.Equal("command", await dispatcher.Send(new CommandPing()));
        Assert.Equal(42, await dispatcher.Send(new QueryPing()));
    }

    [Fact]
    public async Task Flows_the_cancellation_token_to_the_handler()
    {
        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.True(await dispatcher.Send(new CancellablePing(), cts.Token));
        Assert.False(await dispatcher.Send(new CancellablePing()));
    }

    [Fact]
    public async Task Resolves_sender_and_publisher_as_the_dispatcher()
    {
        using var provider = TestServices.Build<SendTests>();

        var sender = provider.GetRequiredService<ISender>();
        var publisher = provider.GetRequiredService<IPublisher>();

        Assert.IsType<Dispatcher>(sender);
        Assert.IsType<Dispatcher>(publisher);
        Assert.Equal("Ping Pong", await sender.Send(new Ping { Message = "Ping" }));
    }

    [Fact]
    public async Task Throws_when_no_handler_is_registered()
    {
        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.Send(new Unhandled()));
    }

    [Fact]
    public async Task Throws_when_the_object_is_not_a_request()
    {
        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<ArgumentException>(() => dispatcher.Send(new NotARequest()));
    }

    [Fact]
    public async Task Throws_when_the_request_is_null()
    {
        using var provider = TestServices.Build<SendTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.Send((Ping)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.Send((object)null!));
    }
}
