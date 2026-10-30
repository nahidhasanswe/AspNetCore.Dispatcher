using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

public class StreamTests
{
    public static readonly List<string> Log = new List<string>();

    public class Counter : IStreamRequest<int>
    {
        public int To { get; set; }
    }

    public class CounterHandler : IStreamRequestHandler<Counter, int>
    {
        public async IAsyncEnumerable<int> Handle(Counter request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 1; i <= request.To; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i;
            }
        }
    }

    public class DoublingBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Log.Add("stream behavior start");

            await foreach (var item in next(cancellationToken).WithCancellation(cancellationToken))
            {
                yield return item;
            }

            Log.Add("stream behavior end");
        }
    }

    public class ClosedStreamBehavior : IStreamPipelineBehavior<Counter, int>
    {
        public async IAsyncEnumerable<int> Handle(Counter request, StreamHandlerDelegate<int> next, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in next(cancellationToken).WithCancellation(cancellationToken))
            {
                yield return item * 10;
            }
        }
    }

    public class NotAStreamRequest
    {
    }

    [Fact]
    public async Task Creates_a_stream_from_the_handler()
    {
        using var provider = TestServices.Build<StreamTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(CounterHandler));

        var results = new List<int>();

        await foreach (var item in provider.GetRequiredService<IDispatcher>().CreateStream(new Counter { To = 3 }))
        {
            results.Add(item);
        }

        Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    [Fact]
    public async Task Creates_a_stream_via_object_dispatch()
    {
        using var provider = TestServices.Build<StreamTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(CounterHandler));

        var results = new List<object?>();

        await foreach (var item in provider.GetRequiredService<IDispatcher>().CreateStream((object)new Counter { To = 2 }))
        {
            results.Add(item);
        }

        Assert.Equal(new object?[] { 1, 2 }, results);
    }

    [Fact]
    public async Task Runs_stream_pipeline_behaviors()
    {
        Log.Clear();

        using var provider = TestServices.Build<StreamTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(CounterHandler);
            configuration.AddOpenStreamBehavior(typeof(DoublingBehavior<,>));
        });

        var results = new List<int>();

        await foreach (var item in provider.GetRequiredService<IDispatcher>().CreateStream(new Counter { To = 2 }))
        {
            results.Add(item);
        }

        Assert.Equal(new[] { 1, 2 }, results);
        Assert.Equal(new[] { "stream behavior start", "stream behavior end" }, Log);
    }

    [Fact]
    public async Task Runs_closed_stream_behaviors()
    {
        using var provider = TestServices.Build<StreamTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(CounterHandler);
            configuration.AddStreamBehavior<ClosedStreamBehavior>();
        });

        var results = new List<int>();

        await foreach (var item in provider.GetRequiredService<IDispatcher>().CreateStream(new Counter { To = 2 }))
        {
            results.Add(item);
        }

        Assert.Equal(new[] { 10, 20 }, results);
    }

    [Fact]
    public async Task Cancels_the_stream()
    {
        using var provider = TestServices.Build<StreamTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(CounterHandler));

        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in provider.GetRequiredService<IDispatcher>().CreateStream(new Counter { To = 100 }, cts.Token))
            {
                if (item == 2)
                {
                    cts.Cancel();
                }
            }
        });
    }

    [Fact]
    public async Task Throws_for_objects_that_are_not_stream_requests()
    {
        using var provider = TestServices.Build<StreamTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in dispatcher.CreateStream(new NotAStreamRequest()))
            {
            }
        });
    }
}
