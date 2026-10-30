using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AspNetCore.Dispatcher.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

public class RequestProcessorTests
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

    public class GenericPreProcessor<TRequest> : IRequestPreProcessor<TRequest>
        where TRequest : notnull
    {
        public Task Process(TRequest request, CancellationToken cancellationToken)
        {
            Log.Add("generic pre");
            return Task.CompletedTask;
        }
    }

    public class GenericPostProcessor<TRequest, TResponse> : IRequestPostProcessor<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task Process(TRequest request, TResponse response, CancellationToken cancellationToken)
        {
            Log.Add("generic post:" + response);
            return Task.CompletedTask;
        }
    }

    public class PingPreProcessor : IRequestPreProcessor<Ping>
    {
        public Task Process(Ping request, CancellationToken cancellationToken)
        {
            Log.Add("ping pre");
            return Task.CompletedTask;
        }
    }

    public class PingPostProcessor : IRequestPostProcessor<Ping, string>
    {
        public Task Process(Ping request, string response, CancellationToken cancellationToken)
        {
            Log.Add("ping post:" + response);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Runs_explicitly_registered_processors_around_the_handler()
    {
        Log.Clear();

        using var provider = TestServices.Build<RequestProcessorTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(PingHandler);
            configuration.AddRequestPreProcessor<PingPreProcessor>();
            configuration.AddRequestPostProcessor<PingPostProcessor>();
        });

        var response = await provider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Equal("pong", response);
        Assert.Equal(new[] { "ping pre", "handler", "ping post:pong" }, Log);
    }

    [Fact]
    public async Task Runs_open_generic_processors()
    {
        Log.Clear();

        using var provider = TestServices.Build<RequestProcessorTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(PingHandler);
            configuration.AddOpenRequestPreProcessor(typeof(GenericPreProcessor<>));
            configuration.AddOpenRequestPostProcessor(typeof(GenericPostProcessor<,>));
        });

        await provider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Equal(new[] { "generic pre", "handler", "generic post:pong" }, Log);
    }

    [Fact]
    public async Task Auto_registers_every_processor_found_while_scanning()
    {
        Log.Clear();

        using var provider = TestServices.Build<RequestProcessorTests>(configuration =>
            configuration.AutoRegisterRequestProcessors = true);

        await provider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Contains("ping pre", Log);
        Assert.Contains("generic pre", Log);
        Assert.Contains("handler", Log);
        Assert.Contains("ping post:pong", Log);
        Assert.Contains("generic post:pong", Log);
    }

    [Fact]
    public async Task Does_not_register_processors_by_default()
    {
        Log.Clear();

        using var provider = TestServices.Build<RequestProcessorTests>();

        await provider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Equal(new[] { "handler" }, Log);
    }
}
