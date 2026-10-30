using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AspNetCore.Dispatcher.NotificationPublishers;
using AspNetCore.Dispatcher.Registration;
using AspNetCore.Dispatcher.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCore.Dispatcher;

/// <summary>
/// Configures how <c>AddDispatcher</c> scans assemblies and registers services.
/// </summary>
public class DispatcherServiceConfiguration
{
    /// <summary>
    /// Implementation type registered for <see cref="IDispatcher"/>, <see cref="ISender"/> and
    /// <see cref="IPublisher"/>. Defaults to <see cref="Dispatcher"/>.
    /// </summary>
    public Type DispatcherImplementationType { get; set; } = typeof(Dispatcher);

    /// <summary>
    /// Notification publishing strategy instance. Defaults to <see cref="ForeachAwaitPublisher"/>.
    /// Ignored when <see cref="NotificationPublisherType"/> is set.
    /// </summary>
    public INotificationPublisher NotificationPublisher { get; set; } = new ForeachAwaitPublisher();

    /// <summary>
    /// Notification publishing strategy type, resolved from the container so it can take
    /// dependencies. Takes precedence over <see cref="NotificationPublisher"/>.
    /// </summary>
    public Type? NotificationPublisherType { get; set; }

    /// <summary>
    /// Lifetime of the dispatcher and of every scanned handler. Defaults to
    /// <see cref="ServiceLifetime.Transient"/>.
    /// </summary>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Filter applied to every scanned type. Return false to skip a type.
    /// </summary>
    public Func<Type, bool> TypeEvaluator { get; set; } = _ => true;

    /// <summary>
    /// Assemblies scanned for handlers, behaviors and processors.
    /// </summary>
    public IReadOnlyList<Assembly> AssembliesToRegister => _assembliesToRegister;

    private readonly List<Assembly> _assembliesToRegister = new List<Assembly>();

    /// <summary>
    /// Pipeline behaviors to register, in order. The first registered behavior is the outermost.
    /// </summary>
    public List<ServiceDescriptor> BehaviorsToRegister { get; } = new List<ServiceDescriptor>();

    /// <summary>
    /// Stream pipeline behaviors to register, in order.
    /// </summary>
    public List<ServiceDescriptor> StreamBehaviorsToRegister { get; } = new List<ServiceDescriptor>();

    /// <summary>
    /// Request pre-processors to register, in order.
    /// </summary>
    public List<ServiceDescriptor> RequestPreProcessorsToRegister { get; } = new List<ServiceDescriptor>();

    /// <summary>
    /// Request post-processors to register, in order.
    /// </summary>
    public List<ServiceDescriptor> RequestPostProcessorsToRegister { get; } = new List<ServiceDescriptor>();

    /// <summary>
    /// When true, every <see cref="IRequestPreProcessor{TRequest}"/> and
    /// <see cref="IRequestPostProcessor{TRequest,TResponse}"/> found while scanning is registered
    /// automatically. Defaults to false.
    /// </summary>
    public bool AutoRegisterRequestProcessors { get; set; }

    /// <summary>
    /// When true, handlers written against generic requests are closed over every type argument
    /// combination discovered in the scanned assemblies. Off by default because the number of
    /// registrations grows with the number of candidate types.
    /// </summary>
    public bool RegisterGenericHandlers { get; set; }

    /// <summary>
    /// Maximum number of generic type parameters a generic handler may declare. 0 means no limit.
    /// </summary>
    public int MaxGenericTypeParameters { get; set; } = 10;

    /// <summary>
    /// Maximum number of candidate types considered for a single generic type parameter. 0 means no limit.
    /// </summary>
    public int MaxTypesClosing { get; set; } = 100;

    /// <summary>
    /// Maximum number of closed registrations produced from generic handlers. 0 means no limit.
    /// </summary>
    public int MaxGenericTypeRegistrations { get; set; } = 125000;

    /// <summary>
    /// Timeout in milliseconds for the registration scan.
    /// </summary>
    public int RegistrationTimeout { get; set; } = 15000;

    /// <summary>
    /// Registers the assembly containing <typeparamref name="T"/>.
    /// </summary>
    public DispatcherServiceConfiguration RegisterServicesFromAssemblyContaining<T>()
        => RegisterServicesFromAssemblyContaining(typeof(T));

    /// <summary>
    /// Registers the assembly containing the supplied type.
    /// </summary>
    public DispatcherServiceConfiguration RegisterServicesFromAssemblyContaining(Type type)
        => RegisterServicesFromAssembly(type.Assembly);

    /// <summary>
    /// Registers a single assembly to scan.
    /// </summary>
    public DispatcherServiceConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        _assembliesToRegister.Add(assembly);
        return this;
    }

    /// <summary>
    /// Registers several assemblies to scan.
    /// </summary>
    public DispatcherServiceConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        _assembliesToRegister.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Registers an open generic pipeline behavior, applied to every request.
    /// </summary>
    public DispatcherServiceConfiguration AddOpenBehavior(Type openBehaviorType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddOpenType(openBehaviorType, typeof(IPipelineBehavior<,>), BehaviorsToRegister, serviceLifetime);

    /// <summary>
    /// Registers a closed pipeline behavior for the interfaces it implements.
    /// </summary>
    public DispatcherServiceConfiguration AddBehavior<TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddBehavior(typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Registers a closed pipeline behavior for a specific service type.
    /// </summary>
    public DispatcherServiceConfiguration AddBehavior<TServiceType, TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddBehavior(typeof(TServiceType), typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Registers a closed pipeline behavior for the interfaces it implements.
    /// </summary>
    public DispatcherServiceConfiguration AddBehavior(Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddClosedType(implementationType, typeof(IPipelineBehavior<,>), BehaviorsToRegister, serviceLifetime);

    /// <summary>
    /// Registers a closed pipeline behavior for a specific service type.
    /// </summary>
    public DispatcherServiceConfiguration AddBehavior(Type serviceType, Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        BehaviorsToRegister.Add(new ServiceDescriptor(serviceType, implementationType, serviceLifetime));
        return this;
    }

    /// <summary>
    /// Registers an open generic stream pipeline behavior.
    /// </summary>
    public DispatcherServiceConfiguration AddOpenStreamBehavior(Type openBehaviorType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddOpenType(openBehaviorType, typeof(IStreamPipelineBehavior<,>), StreamBehaviorsToRegister, serviceLifetime);

    /// <summary>
    /// Registers a closed stream pipeline behavior for the interfaces it implements.
    /// </summary>
    public DispatcherServiceConfiguration AddStreamBehavior<TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddStreamBehavior(typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Registers a closed stream pipeline behavior for a specific service type.
    /// </summary>
    public DispatcherServiceConfiguration AddStreamBehavior<TServiceType, TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddStreamBehavior(typeof(TServiceType), typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Registers a closed stream pipeline behavior for the interfaces it implements.
    /// </summary>
    public DispatcherServiceConfiguration AddStreamBehavior(Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddClosedType(implementationType, typeof(IStreamPipelineBehavior<,>), StreamBehaviorsToRegister, serviceLifetime);

    /// <summary>
    /// Registers a closed stream pipeline behavior for a specific service type.
    /// </summary>
    public DispatcherServiceConfiguration AddStreamBehavior(Type serviceType, Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        StreamBehaviorsToRegister.Add(new ServiceDescriptor(serviceType, implementationType, serviceLifetime));
        return this;
    }

    /// <summary>
    /// Registers an open generic request pre-processor, applied to every request.
    /// </summary>
    public DispatcherServiceConfiguration AddOpenRequestPreProcessor(Type openBehaviorType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddOpenType(openBehaviorType, typeof(IRequestPreProcessor<>), RequestPreProcessorsToRegister, serviceLifetime);

    /// <summary>
    /// Registers a closed request pre-processor for the interfaces it implements.
    /// </summary>
    public DispatcherServiceConfiguration AddRequestPreProcessor<TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddRequestPreProcessor(typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Registers a closed request pre-processor for a specific service type.
    /// </summary>
    public DispatcherServiceConfiguration AddRequestPreProcessor<TServiceType, TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddRequestPreProcessor(typeof(TServiceType), typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Registers a closed request pre-processor for the interfaces it implements.
    /// </summary>
    public DispatcherServiceConfiguration AddRequestPreProcessor(Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddClosedType(implementationType, typeof(IRequestPreProcessor<>), RequestPreProcessorsToRegister, serviceLifetime);

    /// <summary>
    /// Registers a closed request pre-processor for a specific service type.
    /// </summary>
    public DispatcherServiceConfiguration AddRequestPreProcessor(Type serviceType, Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        RequestPreProcessorsToRegister.Add(new ServiceDescriptor(serviceType, implementationType, serviceLifetime));
        return this;
    }

    /// <summary>
    /// Registers an open generic request post-processor, applied to every request.
    /// </summary>
    public DispatcherServiceConfiguration AddOpenRequestPostProcessor(Type openBehaviorType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddOpenType(openBehaviorType, typeof(IRequestPostProcessor<,>), RequestPostProcessorsToRegister, serviceLifetime);

    /// <summary>
    /// Registers a closed request post-processor for the interfaces it implements.
    /// </summary>
    public DispatcherServiceConfiguration AddRequestPostProcessor<TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddRequestPostProcessor(typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Registers a closed request post-processor for a specific service type.
    /// </summary>
    public DispatcherServiceConfiguration AddRequestPostProcessor<TServiceType, TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddRequestPostProcessor(typeof(TServiceType), typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Registers a closed request post-processor for the interfaces it implements.
    /// </summary>
    public DispatcherServiceConfiguration AddRequestPostProcessor(Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddClosedType(implementationType, typeof(IRequestPostProcessor<,>), RequestPostProcessorsToRegister, serviceLifetime);

    /// <summary>
    /// Registers a closed request post-processor for a specific service type.
    /// </summary>
    public DispatcherServiceConfiguration AddRequestPostProcessor(Type serviceType, Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        RequestPostProcessorsToRegister.Add(new ServiceDescriptor(serviceType, implementationType, serviceLifetime));
        return this;
    }

    private DispatcherServiceConfiguration AddClosedType(Type implementationType, Type openInterface, List<ServiceDescriptor> target, ServiceLifetime serviceLifetime)
    {
        var implementedInterfaces = implementationType
            .FindInterfacesThatClose(openInterface)
            .ToList();

        if (implementedInterfaces.Count == 0)
        {
            throw new InvalidOperationException($"{implementationType.Name} must implement {openInterface.FullName}");
        }

        foreach (var implementedInterface in implementedInterfaces)
        {
            target.Add(new ServiceDescriptor(implementedInterface, implementationType, serviceLifetime));
        }

        return this;
    }

    private DispatcherServiceConfiguration AddOpenType(Type openType, Type openInterface, List<ServiceDescriptor> target, ServiceLifetime serviceLifetime)
    {
        if (!openType.IsGenericType)
        {
            throw new InvalidOperationException($"{openType.Name} must be generic");
        }

        var implementedGenericInterfaces = openType
            .GetInterfaces()
            .Where(static i => i.IsGenericType)
            .Select(static i => i.GetGenericTypeDefinition())
            .ToList();

        if (!implementedGenericInterfaces.Contains(openInterface))
        {
            throw new InvalidOperationException($"{openType.Name} must implement {openInterface.FullName}");
        }

        // The container closes an open generic registration by handing the requested service type's
        // arguments straight to the implementation, so the implementation has to close the interface
        // over its own type parameters. Catching it here beats an opaque "can't be converted to
        // service type" on the first dispatch.
        if (!openType.ClosesOverItsOwnTypeParameters(openInterface))
        {
            var expected = openInterface.GetGenericArguments().Length;

            throw new InvalidOperationException(
                $"{openType.Name} cannot be registered as an open generic. It must implement {openInterface.Name} over its own type parameters, in order, and declare exactly {expected} of them. Register it closed instead, with the overload that takes a service type and an implementation type.");
        }

        target.Add(new ServiceDescriptor(openInterface, openType, serviceLifetime));

        return this;
    }
}
