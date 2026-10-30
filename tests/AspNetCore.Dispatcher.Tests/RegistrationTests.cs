using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

public class RegistrationTests
{
    public class Ping : IRequest<string>
    {
    }

    public class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("pong");
    }

    public class SkippedPing : IRequest<string>
    {
    }

    public class SkippedPingHandler : IRequestHandler<SkippedPing, string>
    {
        public Task<string> Handle(SkippedPing request, CancellationToken cancellationToken) => Task.FromResult("skipped");
    }

    public class CustomDispatcher : Dispatcher
    {
        public static int Instances;

        public CustomDispatcher(IServiceProvider serviceProvider, INotificationPublisher publisher)
            : base(serviceProvider, publisher)
            => Instances++;
    }

    [Fact]
    public void Throws_when_no_assembly_is_supplied()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddDispatcher(_ => { }));
    }

    [Fact]
    public async Task Registers_from_an_assembly_marker_type()
    {
        var services = new ServiceCollection();
        services.AddDispatcherFromAssemblyContaining<RegistrationTests>();

        using var provider = services.BuildServiceProvider();

        Assert.Equal("pong", await provider.GetRequiredService<IDispatcher>().Send(new Ping()));
    }

    [Fact]
    public async Task Registers_from_an_assembly_instance()
    {
        var services = new ServiceCollection();
        services.AddDispatcher(typeof(RegistrationTests).Assembly);

        using var provider = services.BuildServiceProvider();

        Assert.Equal("pong", await provider.GetRequiredService<IDispatcher>().Send(new Ping()));
    }

    [Fact]
    public void The_type_evaluator_skips_types()
    {
        using var provider = TestServices.Build<RegistrationTests>(configuration =>
            configuration.TypeEvaluator = type => type != typeof(SkippedPingHandler));

        Assert.NotNull(provider.GetService<IRequestHandler<Ping, string>>());
        Assert.Null(provider.GetService<IRequestHandler<SkippedPing, string>>());
    }

    [Fact]
    public void Honours_the_configured_lifetime()
    {
        var services = new ServiceCollection();

        services.AddDispatcher(configuration =>
        {
            configuration.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            configuration.TypeEvaluator = type => type == typeof(PingHandler);
            configuration.Lifetime = ServiceLifetime.Singleton;
        });

        var descriptor = services.Single(d => d.ServiceType == typeof(IRequestHandler<Ping, string>));
        var dispatcherDescriptor = services.Single(d => d.ServiceType == typeof(IDispatcher));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, dispatcherDescriptor.Lifetime);
    }

    [Fact]
    public async Task Uses_a_custom_dispatcher_implementation()
    {
        CustomDispatcher.Instances = 0;

        using var provider = TestServices.Build<RegistrationTests>(configuration =>
            configuration.DispatcherImplementationType = typeof(CustomDispatcher));

        var dispatcher = provider.GetRequiredService<IDispatcher>();

        Assert.IsType<CustomDispatcher>(dispatcher);
        Assert.Equal("pong", await dispatcher.Send(new Ping()));
        Assert.True(CustomDispatcher.Instances > 0);
    }

    [Fact]
    public void Existing_registrations_are_not_overwritten()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher, CustomDispatcher>();

        services.AddDispatcher(configuration =>
            configuration.RegisterServicesFromAssemblyContaining<RegistrationTests>());

        var descriptors = services.Where(d => d.ServiceType == typeof(IDispatcher)).ToList();

        Assert.Single(descriptors);
        Assert.Equal(typeof(CustomDispatcher), descriptors[0].ImplementationType);
    }

    [Fact]
    public void Scanning_the_same_assembly_twice_does_not_duplicate_handlers()
    {
        var services = new ServiceCollection();

        services.AddDispatcher(configuration =>
        {
            configuration.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            configuration.RegisterServicesFromAssemblyContaining<RegistrationTests>();
            configuration.TypeEvaluator = type => type == typeof(PingHandler);
        });

        Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<Ping, string>));
    }

    [Fact]
    public void Registers_the_notification_publisher()
    {
        using var provider = TestServices.Build<RegistrationTests>();

        Assert.NotNull(provider.GetService<INotificationPublisher>());
    }
}
