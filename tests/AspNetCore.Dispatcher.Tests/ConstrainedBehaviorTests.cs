using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

/// <summary>
/// Behaviors in the docs are constrained so they only apply to a subset of requests. These tests
/// pin down what the container does with an open generic registration whose constraints a given
/// request does not satisfy.
/// </summary>
public class ConstrainedBehaviorTests
{
    public static readonly List<string> Log = new List<string>();

    public interface ITransactional
    {
    }

    public class MarkedPing : IRequest<string>, ITransactional
    {
    }

    public class UnmarkedPing : IRequest<string>
    {
    }

    public class VoidPing : IRequest
    {
    }

    public class MarkedPingHandler : IRequestHandler<MarkedPing, string>
    {
        public Task<string> Handle(MarkedPing request, CancellationToken cancellationToken) => Task.FromResult("marked");
    }

    public class UnmarkedPingHandler : IRequestHandler<UnmarkedPing, string>
    {
        public Task<string> Handle(UnmarkedPing request, CancellationToken cancellationToken) => Task.FromResult("unmarked");
    }

    public class VoidPingHandler : IRequestHandler<VoidPing>
    {
        public Task Handle(VoidPing request, CancellationToken cancellationToken)
        {
            Log.Add("void handler");
            return Task.CompletedTask;
        }
    }

    /// <summary>Applies only to requests marked with the interface.</summary>
    public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : ITransactional
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            Log.Add("transaction");
            return next(cancellationToken);
        }
    }

    /// <summary>Applies only to void requests, because only those implement IRequest.</summary>
    public class VoidOnlyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            Log.Add("void only");
            return next(cancellationToken);
        }
    }

    [Fact]
    public async Task A_constrained_behavior_applies_only_to_requests_that_satisfy_the_constraint()
    {
        Log.Clear();

        using var provider = TestServices.Build<ConstrainedBehaviorTests>(configuration =>
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>)));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        Assert.Equal("marked", await dispatcher.Send(new MarkedPing()));
        Assert.Equal(new[] { "transaction" }, Log);

        Log.Clear();

        Assert.Equal("unmarked", await dispatcher.Send(new UnmarkedPing()));
        Assert.Empty(Log);
    }

    [Fact]
    public async Task A_behavior_constrained_to_void_requests_skips_requests_with_a_response()
    {
        Log.Clear();

        using var provider = TestServices.Build<ConstrainedBehaviorTests>(configuration =>
            configuration.AddOpenBehavior(typeof(VoidOnlyBehavior<,>)));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Send(new VoidPing());
        Assert.Equal(new[] { "void only", "void handler" }, Log);

        Log.Clear();

        Assert.Equal("unmarked", await dispatcher.Send(new UnmarkedPing()));
        Assert.Empty(Log);
    }

    [Fact]
    public void Constrained_behaviors_survive_container_validation()
    {
        var services = new ServiceCollection();

        services.AddDispatcher(configuration =>
        {
            configuration.RegisterServicesFromAssemblyContaining<ConstrainedBehaviorTests>();
            configuration.TypeEvaluator = type => type == typeof(MarkedPingHandler) || type == typeof(UnmarkedPingHandler);
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<IDispatcher>());
    }
}
