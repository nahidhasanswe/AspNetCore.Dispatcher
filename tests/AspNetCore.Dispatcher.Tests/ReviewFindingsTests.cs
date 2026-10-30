using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

/// <summary>
/// Cases found while reviewing the library. Each one was a real defect before the fix.
/// </summary>
public class ReviewFindingsTests
{
    public static readonly List<string> Log = new List<string>();

    // --- Finding 1: a void request sent through a base-typed variable ---

    public abstract class CommandBase : IRequest
    {
    }

    public class Archive : CommandBase
    {
    }

    public class ArchiveHandler : IRequestHandler<Archive>
    {
        public Task Handle(Archive request, CancellationToken cancellationToken)
        {
            Log.Add("archived");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Sends_a_void_request_declared_as_its_base_type()
    {
        Log.Clear();

        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(ArchiveHandler));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        // The static type drives generic inference, so TRequest is CommandBase, not Archive.
        CommandBase command = new Archive();

        await dispatcher.Send(command);

        Assert.Equal(new[] { "archived" }, Log);
    }

    [Fact]
    public async Task Sends_a_void_request_declared_as_the_marker_interface()
    {
        Log.Clear();

        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(ArchiveHandler));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        IRequest command = new Archive();

        await dispatcher.Send(command);

        Assert.Equal(new[] { "archived" }, Log);
    }

    /// <summary>
    /// The wrapper cache is keyed by request type and shared by every overload, so a base-typed
    /// send must not leave an entry that breaks the concrete send afterwards.
    /// </summary>
    [Fact]
    public async Task A_base_typed_send_does_not_poison_the_cache_for_the_concrete_type()
    {
        Log.Clear();

        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(ArchiveHandler));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        CommandBase asBase = new Archive();
        await dispatcher.Send(asBase);
        await dispatcher.Send(new Archive());

        Assert.Equal(new[] { "archived", "archived" }, Log);
    }

    // --- Finding 2: a handler serving two requests, alongside a duplicate handler ---

    public class Ping : IRequest<string>
    {
    }

    public class Pong : IRequest<string>
    {
    }

    public class TwoRequestHandler : IRequestHandler<Ping, string>, IRequestHandler<Pong, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("two:ping");

        public Task<string> Handle(Pong request, CancellationToken cancellationToken) => Task.FromResult("two:pong");
    }

    public class DuplicatePingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("duplicate");
    }

    /// <summary>
    /// Two handlers claiming one request is a user error, but it must resolve to one of them rather
    /// than throw a reflection error out of AddDispatcher.
    /// </summary>
    [Fact]
    public async Task Registration_survives_a_duplicate_handler_next_to_a_multi_request_handler()
    {
        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(TwoRequestHandler) || type == typeof(DuplicatePingHandler));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        var response = await dispatcher.Send(new Ping());

        Assert.True(response is "two:ping" or "duplicate", $"unexpected response '{response}'");

        // The second request of the multi-handler must still resolve.
        Assert.Equal("two:pong", await dispatcher.Send(new Pong()));
    }

    // --- Finding 3: an open generic notification handler that is not an identity mapping ---

    public class Pinged : INotification
    {
    }

    public class PingedHandler : INotificationHandler<Pinged>
    {
        public Task Handle(Pinged notification, CancellationToken cancellationToken)
        {
            Log.Add("pinged");
            return Task.CompletedTask;
        }
    }

    public class Wrapped<TInner> : INotification
        where TInner : INotification
    {
    }

    /// <summary>
    /// Handles Wrapped&lt;T&gt;, not T. Registering this as an open generic
    /// INotificationHandler&lt;&gt; would have the container close it as
    /// WrappedOnlyHandler&lt;Pinged&gt;, which does not implement INotificationHandler&lt;Pinged&gt;.
    /// </summary>
    public class WrappedOnlyHandler<TInner> : INotificationHandler<Wrapped<TInner>>
        where TInner : INotification
    {
        public Task Handle(Wrapped<TInner> notification, CancellationToken cancellationToken)
        {
            Log.Add("wrapped");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task An_open_generic_handler_of_a_wrapped_notification_does_not_break_other_notifications()
    {
        Log.Clear();

        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(PingedHandler) || type == typeof(WrappedOnlyHandler<>));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new Pinged());

        Assert.Equal(new[] { "pinged" }, Log);
    }

    /// <summary>
    /// The container cannot close this shape from an open generic registration, so it is closed by
    /// unification instead — which is opt-in, like every other generic handler.
    /// </summary>
    [Fact]
    public async Task An_open_generic_handler_of_a_wrapped_notification_works_with_generic_handlers_enabled()
    {
        Log.Clear();

        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(WrappedOnlyHandler<>);
            configuration.RegisterGenericHandlers = true;
            configuration.MaxTypesClosing = 0;
        });

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new Wrapped<Pinged>());

        Assert.Equal(new[] { "wrapped" }, Log);
    }

    /// <summary>
    /// And it must be invoked exactly once — it is reachable both as an open generic registration and
    /// as a closed one, which would otherwise double up.
    /// </summary>
    [Fact]
    public async Task A_generic_notification_handler_is_invoked_once_per_publish()
    {
        Log.Clear();

        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(EveryNotificationHandler<>);
            configuration.RegisterGenericHandlers = true;
            configuration.MaxTypesClosing = 0;
        });

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new Pinged());

        Assert.Equal(new[] { "every" }, Log);
    }

    public class EveryNotificationHandler<TNotification> : INotificationHandler<TNotification>
        where TNotification : INotification
    {
        public Task Handle(TNotification notification, CancellationToken cancellationToken)
        {
            Log.Add("every");
            return Task.CompletedTask;
        }
    }

    // --- Finding 5: how a base-typed notification actually dispatches ---

    public class MemberChanged : INotification
    {
    }

    public class MemberRenamed : MemberChanged
    {
    }

    public class MemberChangedHandler : INotificationHandler<MemberChanged>
    {
        public Task Handle(MemberChanged notification, CancellationToken cancellationToken)
        {
            Log.Add("base handler");
            return Task.CompletedTask;
        }
    }

    /// <summary>Handles the base type and every type derived from it.</summary>
    public class AnyMemberChangeHandler<TNotification> : INotificationHandler<TNotification>
        where TNotification : MemberChanged
    {
        public Task Handle(TNotification notification, CancellationToken cancellationToken)
        {
            Log.Add("open base handler:" + typeof(TNotification).Name);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Dispatch always uses the notification's runtime type, so an explicit type argument on Publish
    /// does not redirect it to the base type's handlers. The docs claimed otherwise.
    /// </summary>
    [Fact]
    public async Task An_explicit_type_argument_does_not_change_which_handlers_run()
    {
        Log.Clear();

        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(MemberChangedHandler));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish<MemberChanged>(new MemberRenamed());

        Assert.Empty(Log);
    }

    /// <summary>The working way to handle a whole notification hierarchy.</summary>
    [Fact]
    public async Task An_open_generic_handler_constrained_to_a_base_type_handles_derived_notifications()
    {
        Log.Clear();

        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(AnyMemberChangeHandler<>));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new MemberRenamed());

        Assert.Equal(new[] { "open base handler:MemberRenamed" }, Log);
    }

    // --- Finding 4: an open behavior the container cannot close ---

    /// <summary>One type parameter, but IPipelineBehavior has two.</summary>
    public class VoidOnlyBehavior<TRequest> : IPipelineBehavior<TRequest, Unit>
        where TRequest : IRequest
    {
        public Task<Unit> Handle(TRequest request, RequestHandlerDelegate<Unit> next, CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    public class WellShapedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    /// <summary>
    /// Registering an un-closable shape has to fail at startup with an explanation, not with an
    /// opaque container error on the first dispatch.
    /// </summary>
    [Fact]
    public void An_open_behavior_with_the_wrong_arity_is_rejected_at_registration()
    {
        var configuration = new DispatcherServiceConfiguration();

        var exception = Assert.Throws<InvalidOperationException>(
            () => configuration.AddOpenBehavior(typeof(VoidOnlyBehavior<>)));

        Assert.Contains("open generic", exception.Message);
        Assert.Contains("VoidOnlyBehavior", exception.Message);
    }

    [Fact]
    public void A_well_shaped_open_behavior_is_still_accepted()
    {
        var configuration = new DispatcherServiceConfiguration();

        configuration.AddOpenBehavior(typeof(WellShapedBehavior<,>));

        Assert.Single(configuration.BehaviorsToRegister);
    }

    /// <summary>The same behavior registered closed is the documented way to apply it to one request.</summary>
    [Fact]
    public async Task An_un_closable_behavior_can_still_be_registered_closed()
    {
        Log.Clear();

        using var provider = TestServices.Build<ReviewFindingsTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(ArchiveHandler);
            configuration.AddBehavior(typeof(IPipelineBehavior<Archive, Unit>), typeof(VoidOnlyBehavior<Archive>));
        });

        await provider.GetRequiredService<IDispatcher>().Send(new Archive());

        Assert.Equal(new[] { "archived" }, Log);
    }
}
