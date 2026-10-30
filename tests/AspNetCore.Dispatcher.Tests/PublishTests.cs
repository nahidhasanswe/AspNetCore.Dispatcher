using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AspNetCore.Dispatcher.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

public class PublishTests
{
    public static readonly ConcurrentQueue<string> Log = new ConcurrentQueue<string>();

    public class Pinged : INotification
    {
    }

    public class FirstPingedHandler : INotificationHandler<Pinged>
    {
        public Task Handle(Pinged notification, CancellationToken cancellationToken)
        {
            Log.Enqueue("first");
            return Task.CompletedTask;
        }
    }

    public class SecondPingedHandler : INotificationHandler<Pinged>
    {
        public Task Handle(Pinged notification, CancellationToken cancellationToken)
        {
            Log.Enqueue("second");
            return Task.CompletedTask;
        }
    }

    public class SyncPingedHandler : NotificationHandler<Pinged>
    {
        protected override void Handle(Pinged notification) => Log.Enqueue("sync");
    }

    public class GenericHandler<TNotification> : INotificationHandler<TNotification>
        where TNotification : INotification
    {
        public Task Handle(TNotification notification, CancellationToken cancellationToken)
        {
            Log.Enqueue("generic:" + typeof(TNotification).Name);
            return Task.CompletedTask;
        }
    }

    public class EventNotification : IEvent
    {
    }

    public class EventNotificationHandler : INotificationHandler<EventNotification>
    {
        public Task Handle(EventNotification notification, CancellationToken cancellationToken)
        {
            Log.Enqueue("event");
            return Task.CompletedTask;
        }
    }

    public class Faulted : INotification
    {
    }

    public class ThrowingHandler : INotificationHandler<Faulted>
    {
        public Task Handle(Faulted notification, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    public class SurvivingHandler : INotificationHandler<Faulted>
    {
        public Task Handle(Faulted notification, CancellationToken cancellationToken)
        {
            Log.Enqueue("survived");
            return Task.CompletedTask;
        }
    }

    public class NotANotification
    {
    }

    public class CountingPublisher : INotificationPublisher
    {
        public static int Invocations;

        public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
        {
            Invocations++;

            foreach (var executor in handlerExecutors)
            {
                Assert.NotNull(executor.HandlerInstance);
                await executor.HandlerCallback(notification, cancellationToken);
            }
        }
    }

    private static void Clear()
    {
        while (Log.TryDequeue(out _))
        {
        }
    }

    [Fact]
    public async Task Publishes_to_every_handler()
    {
        Clear();

        using var provider = TestServices.Build<PublishTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new Pinged());

        var entries = Log.ToArray();
        Assert.Contains("first", entries);
        Assert.Contains("second", entries);
        Assert.Contains("sync", entries);
        Assert.Contains("generic:Pinged", entries);
    }

    [Fact]
    public async Task Publishes_via_object_dispatch()
    {
        Clear();

        using var provider = TestServices.Build<PublishTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish((object)new EventNotification());

        Assert.Contains("event", Log.ToArray());
    }

    [Fact]
    public async Task Stops_on_the_first_failing_handler_with_the_default_publisher()
    {
        Clear();

        using var provider = TestServices.Build<PublishTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(ThrowingHandler) || type == typeof(SurvivingHandler));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.Publish(new Faulted()));

        Assert.Empty(Log.ToArray());
    }

    [Fact]
    public async Task Runs_every_handler_with_the_task_when_all_publisher()
    {
        Clear();

        using var provider = TestServices.Build<PublishTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(ThrowingHandler) || type == typeof(SurvivingHandler);
            configuration.NotificationPublisher = new TaskWhenAllPublisher();
        });

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.Publish(new Faulted()));

        Assert.Contains("survived", Log.ToArray());
    }

    [Fact]
    public async Task Uses_a_custom_publisher_resolved_from_the_container()
    {
        Clear();
        CountingPublisher.Invocations = 0;

        using var provider = TestServices.Build<PublishTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(FirstPingedHandler);
            configuration.NotificationPublisherType = typeof(CountingPublisher);
        });

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new Pinged());

        Assert.Equal(1, CountingPublisher.Invocations);
        Assert.Equal(new[] { "first" }, Log.ToArray());
    }

    [Fact]
    public async Task Publishing_a_notification_with_no_handlers_is_a_no_op()
    {
        using var provider = TestServices.Build<PublishTests>(configuration =>
            configuration.TypeEvaluator = _ => false);

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await dispatcher.Publish(new Pinged());
    }

    [Fact]
    public async Task Throws_for_objects_that_are_not_notifications()
    {
        using var provider = TestServices.Build<PublishTests>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        await Assert.ThrowsAsync<ArgumentException>(() => dispatcher.Publish(new NotANotification()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.Publish((object)null!));
    }
}
