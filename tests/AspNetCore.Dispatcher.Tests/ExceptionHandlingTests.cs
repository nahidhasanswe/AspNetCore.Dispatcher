using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AspNetCore.Dispatcher.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

public class ExceptionHandlingTests
{
    public static readonly List<string> Log = new List<string>();

    public class Ping : IRequest<string>
    {
    }

    public class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
            => throw new NotSupportedException("handler failed");

    }

    public class VoidPing : IRequest
    {
    }

    public class VoidPingHandler : IRequestHandler<VoidPing>
    {
        public Task Handle(VoidPing request, CancellationToken cancellationToken)
            => throw new NotSupportedException("void handler failed");
    }

    public class SpecificHandler : IRequestExceptionHandler<Ping, string, NotSupportedException>
    {
        public Task Handle(Ping request, NotSupportedException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            Log.Add("specific");
            state.SetHandled("handled by specific");
            return Task.CompletedTask;
        }
    }

    public class CatchAllHandler : AsyncRequestExceptionHandler<Ping, string>
    {
        protected override Task Handle(Ping request, Exception exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            Log.Add("catch all");
            state.SetHandled("handled by catch all");
            return Task.CompletedTask;
        }
    }

    public class ObservingHandler : IRequestExceptionHandler<Ping, string, NotSupportedException>
    {
        public Task Handle(Ping request, NotSupportedException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
        {
            Log.Add("observing");
            return Task.CompletedTask;
        }
    }

    public class VoidExceptionHandler : RequestExceptionHandler<VoidPing, Unit>
    {
        protected override void Handle(VoidPing request, Exception exception, RequestExceptionHandlerState<Unit> state)
        {
            Log.Add("void handled");
            state.SetHandled(Unit.Value);
        }
    }

    public class LoggingAction : IRequestExceptionAction<Ping, NotSupportedException>
    {
        public Task Execute(Ping request, NotSupportedException exception, CancellationToken cancellationToken)
        {
            Log.Add("action:" + exception.Message);
            return Task.CompletedTask;
        }
    }

    public class SyncAction : RequestExceptionAction<Ping>
    {
        protected override void Execute(Ping request, Exception exception) => Log.Add("sync action");
    }

    [Fact]
    public async Task An_exception_handler_can_substitute_a_response()
    {
        Log.Clear();

        using var provider = TestServices.Build<ExceptionHandlingTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(PingHandler) || type == typeof(SpecificHandler));

        var response = await provider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Equal("handled by specific", response);
        Assert.Equal(new[] { "specific" }, Log);
    }

    [Fact]
    public async Task The_most_specific_exception_handler_wins()
    {
        Log.Clear();

        using var provider = TestServices.Build<ExceptionHandlingTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(PingHandler)
                                                  || type == typeof(SpecificHandler)
                                                  || type == typeof(CatchAllHandler));

        var response = await provider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Equal("handled by specific", response);
        Assert.DoesNotContain("catch all", Log);
    }

    [Fact]
    public async Task A_base_exception_handler_is_used_when_no_specific_one_matches()
    {
        Log.Clear();

        using var provider = TestServices.Build<ExceptionHandlingTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(PingHandler) || type == typeof(CatchAllHandler));

        var response = await provider.GetRequiredService<IDispatcher>().Send(new Ping());

        Assert.Equal("handled by catch all", response);
    }

    [Fact]
    public async Task The_exception_is_rethrown_when_no_handler_marks_it_handled()
    {
        Log.Clear();

        using var provider = TestServices.Build<ExceptionHandlingTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(PingHandler) || type == typeof(ObservingHandler));

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.GetRequiredService<IDispatcher>().Send(new Ping()));

        Assert.Equal("handler failed", exception.Message);
        Assert.Equal(new[] { "observing" }, Log);
    }

    [Fact]
    public async Task Exception_handlers_work_for_void_requests()
    {
        Log.Clear();

        using var provider = TestServices.Build<ExceptionHandlingTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(VoidPingHandler) || type == typeof(VoidExceptionHandler));

        await provider.GetRequiredService<IDispatcher>().Send(new VoidPing());

        Assert.Equal(new[] { "void handled" }, Log);
    }

    [Fact]
    public async Task Exception_actions_run_and_the_exception_still_propagates()
    {
        Log.Clear();

        using var provider = TestServices.Build<ExceptionHandlingTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(PingHandler)
                                                  || type == typeof(LoggingAction)
                                                  || type == typeof(SyncAction));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.GetRequiredService<IDispatcher>().Send(new Ping()));

        Assert.Contains("action:handler failed", Log);

        // Declared against Exception, so it is registered under every derived exception too and
        // must still run exactly once for a NotSupportedException.
        Assert.Equal(1, Log.Count(entry => entry == "sync action"));
    }

    [Fact]
    public async Task No_handlers_means_the_exception_propagates_untouched()
    {
        Log.Clear();

        using var provider = TestServices.Build<ExceptionHandlingTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(PingHandler));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.GetRequiredService<IDispatcher>().Send(new Ping()));

        Assert.Empty(Log);
    }
}
