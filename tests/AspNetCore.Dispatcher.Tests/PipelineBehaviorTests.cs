using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

public class PipelineBehaviorTests
{
    public static readonly List<string> Log = new List<string>();

    public class Ping : IRequest<string>
    {
    }

    public class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            Log.Add("handler");
            return Task.FromResult("pong");
        }
    }

    public class VoidPing : IRequest
    {
    }

    public class VoidPingHandler : IRequestHandler<VoidPing>
    {
        public Task Handle(VoidPing request, CancellationToken cancellationToken)
        {
            Log.Add("void handler");
            return Task.CompletedTask;
        }
    }

    public class OuterBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            Log.Add("outer before");
            var response = await next(cancellationToken);
            Log.Add("outer after");
            return response;
        }
    }

    public class InnerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            Log.Add("inner before");
            var response = await next(cancellationToken);
            Log.Add("inner after");
            return response;
        }
    }

    public class ClosedBehavior : IPipelineBehavior<Ping, string>
    {
        public async Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            var response = await next(cancellationToken);
            return response + " (closed)";
        }
    }

    public class ShortCircuitBehavior : IPipelineBehavior<Ping, string>
    {
        public Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            Log.Add("short circuit");
            return Task.FromResult("cached");
        }
    }

    public class TokenCapturingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public static CancellationToken Captured;

        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            Captured = cancellationToken;
            return next(cancellationToken);
        }
    }

    [Fact]
    public async Task Runs_open_generic_behaviors_in_registration_order()
    {
        Log.Clear();

        using var provider = TestServices.Build<PipelineBehaviorTests>(configuration =>
        {
            configuration.AddOpenBehavior(typeof(OuterBehavior<,>));
            configuration.AddOpenBehavior(typeof(InnerBehavior<,>));
        });

        var response = await provider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Equal("pong", response);
        Assert.Equal(new[] { "outer before", "inner before", "handler", "inner after", "outer after" }, Log);
    }

    [Fact]
    public async Task Runs_behaviors_for_void_requests()
    {
        Log.Clear();

        using var provider = TestServices.Build<PipelineBehaviorTests>(configuration =>
            configuration.AddOpenBehavior(typeof(OuterBehavior<,>)));

        await provider.GetRequiredService<IDispatcher>().Send(new VoidPing());

        Assert.Equal(new[] { "outer before", "void handler", "outer after" }, Log);
    }

    [Fact]
    public async Task Runs_closed_behaviors_for_their_request_only()
    {
        Log.Clear();

        using var provider = TestServices.Build<PipelineBehaviorTests>(configuration =>
            configuration.AddBehavior<ClosedBehavior>());

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        Assert.Equal("pong (closed)", await dispatcher.Send(new Ping()));
        await dispatcher.Send(new VoidPing());
    }

    [Fact]
    public async Task A_behavior_can_short_circuit_the_handler()
    {
        Log.Clear();

        using var provider = TestServices.Build<PipelineBehaviorTests>(configuration =>
            configuration.AddBehavior(typeof(ShortCircuitBehavior)));

        var response = await provider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Equal("cached", response);
        Assert.Equal(new[] { "short circuit" }, Log);
    }

    [Fact]
    public async Task Flows_the_cancellation_token_through_the_pipeline()
    {
        Log.Clear();

        using var provider = TestServices.Build<PipelineBehaviorTests>(configuration =>
            configuration.AddOpenBehavior(typeof(TokenCapturingBehavior<,>)));

        using var cts = new CancellationTokenSource();

        await provider.GetRequiredService<IDispatcher>().Send(new Ping(), cts.Token);

        Assert.Equal(cts.Token, TokenCapturingBehavior<Ping, string>.Captured);
    }

    [Fact]
    public void Rejects_a_behavior_that_does_not_implement_the_interface()
    {
        var configuration = new DispatcherServiceConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.AddBehavior(typeof(PingHandler)));
        Assert.Throws<InvalidOperationException>(() => configuration.AddOpenBehavior(typeof(ClosedBehavior)));
    }

    [Fact]
    public async Task Behaviors_can_be_registered_with_a_scoped_lifetime()
    {
        Log.Clear();

        using var provider = TestServices.Build<PipelineBehaviorTests>(configuration =>
            configuration.AddOpenBehavior(typeof(OuterBehavior<,>), ServiceLifetime.Scoped));

        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Contains("outer before", Log);
    }
}
