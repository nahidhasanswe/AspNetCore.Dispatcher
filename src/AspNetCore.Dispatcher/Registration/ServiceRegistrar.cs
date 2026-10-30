using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using AspNetCore.Dispatcher.Internal;
using AspNetCore.Dispatcher.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AspNetCore.Dispatcher.Registration;

/// <summary>
/// Scans assemblies and registers handlers, processors, behaviors and the dispatcher itself.
/// </summary>
internal static class ServiceRegistrar
{
    public static void AddDispatcherClassesWithTimeout(IServiceCollection services, DispatcherServiceConfiguration configuration)
    {
        var timeout = configuration.RegistrationTimeout <= 0 ? Timeout.Infinite : configuration.RegistrationTimeout;

        using (var cancellationTokenSource = new CancellationTokenSource(timeout))
        {
            try
            {
                AddDispatcherClasses(services, configuration, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"The dispatcher registration scan did not complete within {configuration.RegistrationTimeout}ms. Narrow the assemblies being scanned, set a TypeEvaluator, or raise RegistrationTimeout.");
            }
        }
    }

    public static void AddDispatcherClasses(IServiceCollection services, DispatcherServiceConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var assembliesToScan = configuration.AssembliesToRegister.Distinct().ToArray();

        ConnectImplementationsToTypesClosing(typeof(IRequestHandler<,>), services, assembliesToScan, false, configuration, cancellationToken);
        ConnectImplementationsToTypesClosing(typeof(IRequestHandler<>), services, assembliesToScan, false, configuration, cancellationToken);
        ConnectImplementationsToTypesClosing(typeof(IStreamRequestHandler<,>), services, assembliesToScan, false, configuration, cancellationToken);
        ConnectImplementationsToTypesClosing(typeof(INotificationHandler<>), services, assembliesToScan, true, configuration, cancellationToken);
        ConnectImplementationsToTypesClosing(typeof(IRequestExceptionHandler<,,>), services, assembliesToScan, true, configuration, cancellationToken);
        ConnectImplementationsToTypesClosing(typeof(IRequestExceptionAction<,>), services, assembliesToScan, true, configuration, cancellationToken);

        if (configuration.AutoRegisterRequestProcessors)
        {
            ConnectImplementationsToTypesClosing(typeof(IRequestPreProcessor<>), services, assembliesToScan, true, configuration, cancellationToken);
            ConnectImplementationsToTypesClosing(typeof(IRequestPostProcessor<,>), services, assembliesToScan, true, configuration, cancellationToken);
        }

        RegisterVoidHandlerAdapters(services, assembliesToScan, configuration, cancellationToken);
        RegisterOpenGenericMultiHandlers(services, assembliesToScan, configuration, cancellationToken);

        if (configuration.RegisterGenericHandlers)
        {
            var registrationCount = CloseGenericHandlers(services, typeof(IRequestHandler<,>), false, assembliesToScan, configuration, 0, cancellationToken);
            registrationCount += CloseGenericHandlers(services, typeof(IStreamRequestHandler<,>), false, assembliesToScan, configuration, registrationCount, cancellationToken);

            // Notification handlers whose type parameters map straight onto the interface are already
            // registered as open generics, and closing those again would invoke them twice per publish.
            // Only the wrapped shapes the container cannot close are handled here.
            CloseGenericHandlers(services, typeof(INotificationHandler<>), true, assembliesToScan, configuration, registrationCount, cancellationToken);
        }
    }

    public static void AddRequiredServices(IServiceCollection services, DispatcherServiceConfiguration configuration)
    {
        services.TryAdd(new ServiceDescriptor(typeof(IDispatcher), configuration.DispatcherImplementationType, configuration.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(ISender), static sp => sp.GetRequiredService<IDispatcher>(), configuration.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), static sp => sp.GetRequiredService<IDispatcher>(), configuration.Lifetime));

        var notificationPublisherDescriptor = configuration.NotificationPublisherType != null
            ? new ServiceDescriptor(typeof(INotificationPublisher), configuration.NotificationPublisherType, configuration.Lifetime)
            : new ServiceDescriptor(typeof(INotificationPublisher), configuration.NotificationPublisher);

        services.TryAdd(notificationPublisherDescriptor);

        // Order matters: the first registered behavior is the outermost one in the pipeline.
        services.TryAddEnumerable(new ServiceDescriptor(typeof(IPipelineBehavior<,>), typeof(RequestExceptionActionProcessorBehavior<,>), ServiceLifetime.Transient));
        services.TryAddEnumerable(new ServiceDescriptor(typeof(IPipelineBehavior<,>), typeof(RequestExceptionProcessorBehavior<,>), ServiceLifetime.Transient));

        if (configuration.RequestPreProcessorsToRegister.Count > 0 || configuration.AutoRegisterRequestProcessors)
        {
            services.TryAddEnumerable(new ServiceDescriptor(typeof(IPipelineBehavior<,>), typeof(RequestPreProcessorBehavior<,>), ServiceLifetime.Transient));
        }

        if (configuration.RequestPostProcessorsToRegister.Count > 0 || configuration.AutoRegisterRequestProcessors)
        {
            services.TryAddEnumerable(new ServiceDescriptor(typeof(IPipelineBehavior<,>), typeof(RequestPostProcessorBehavior<,>), ServiceLifetime.Transient));
        }

        foreach (var descriptor in configuration.BehaviorsToRegister)
        {
            services.TryAddEnumerable(descriptor);
        }

        foreach (var descriptor in configuration.StreamBehaviorsToRegister)
        {
            services.TryAddEnumerable(descriptor);
        }

        foreach (var descriptor in configuration.RequestPreProcessorsToRegister)
        {
            services.TryAddEnumerable(descriptor);
        }

        foreach (var descriptor in configuration.RequestPostProcessorsToRegister)
        {
            services.TryAddEnumerable(descriptor);
        }
    }

    private static void ConnectImplementationsToTypesClosing(
        Type openRequestInterface,
        IServiceCollection services,
        IEnumerable<Assembly> assembliesToScan,
        bool addIfAlreadyExists,
        DispatcherServiceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var concretions = new List<Type>();
        var interfaces = new List<Type>();
        var genericConcretions = new List<Type>();

        foreach (var type in GetScannableTypes(assembliesToScan, configuration))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var interfaceTypes = type.FindInterfacesThatClose(openRequestInterface).ToArray();

            if (interfaceTypes.Length == 0)
            {
                continue;
            }

            if (type.IsOpenGeneric())
            {
                genericConcretions.Add(type);
                continue;
            }

            concretions.Add(type);

            foreach (var interfaceType in interfaceTypes)
            {
                interfaces.Fill(interfaceType);
            }
        }

        foreach (var @interface in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var exactMatches = concretions.Where(type => type.CanBeCastTo(@interface)).ToList();

            if (addIfAlreadyExists)
            {
                foreach (var type in exactMatches)
                {
                    services.TryAddEnumerable(new ServiceDescriptor(@interface, type, configuration.Lifetime));
                }
            }
            else
            {
                if (exactMatches.Count > 1)
                {
                    exactMatches.RemoveAll(match => !match.IsMatchingWithInterface(@interface));
                }

                foreach (var type in exactMatches)
                {
                    services.TryAdd(new ServiceDescriptor(@interface, type, configuration.Lifetime));
                }
            }

            AddConcretionsThatCouldBeClosed(@interface, genericConcretions, services, addIfAlreadyExists, configuration);
        }
    }

    /// <summary>
    /// Closes an open generic implementation over an already discovered closed interface, so that
    /// <c>Handler&lt;T&gt; : IRequestHandler&lt;Request, T&gt;</c> serves <c>IRequestHandler&lt;Request, Foo&gt;</c>.
    /// </summary>
    private static void AddConcretionsThatCouldBeClosed(
        Type @interface,
        List<Type> genericConcretions,
        IServiceCollection services,
        bool addIfAlreadyExists,
        DispatcherServiceConfiguration configuration)
    {
        if (@interface.IsOpenGeneric())
        {
            return;
        }

        foreach (var type in genericConcretions.Where(type => type.CouldCloseTo(@interface)))
        {
            try
            {
                var closed = type.MakeGenericType(@interface.GenericTypeArguments);

                if (addIfAlreadyExists)
                {
                    services.TryAddEnumerable(new ServiceDescriptor(@interface, closed, configuration.Lifetime));
                }
                else
                {
                    services.TryAdd(new ServiceDescriptor(@interface, closed, configuration.Lifetime));
                }
            }
            catch (ArgumentException)
            {
                // The type arguments did not satisfy the implementation constraints.
            }
        }
    }

    /// <summary>
    /// Registers the adapter that lets a void <see cref="IRequestHandler{TRequest}"/> take part in the
    /// same pipeline as handlers that return a response.
    /// </summary>
    private static void RegisterVoidHandlerAdapters(
        IServiceCollection services,
        IEnumerable<Assembly> assembliesToScan,
        DispatcherServiceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var voidHandlerInterfaces = GetScannableTypes(assembliesToScan, configuration)
            .Where(static type => !type.IsOpenGeneric())
            .SelectMany(static type => type.FindInterfacesThatClose(typeof(IRequestHandler<>)))
            .Distinct();

        foreach (var handlerInterface in voidHandlerInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestType = handlerInterface.GenericTypeArguments[0];

            if (requestType.IsOpenGeneric())
            {
                continue;
            }

            var serviceType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(Unit));
            var implementationType = typeof(VoidRequestHandlerAdapter<>).MakeGenericType(requestType);

            services.TryAdd(new ServiceDescriptor(serviceType, implementationType, configuration.Lifetime));
        }
    }

    /// <summary>
    /// Registers open generic implementations of the multi-handler interfaces as open generic
    /// services, letting the container close them per notification or request.
    /// </summary>
    private static void RegisterOpenGenericMultiHandlers(
        IServiceCollection services,
        IEnumerable<Assembly> assembliesToScan,
        DispatcherServiceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var multiOpenInterfaces = new List<Type>
        {
            typeof(INotificationHandler<>),
            typeof(IRequestExceptionHandler<,,>),
            typeof(IRequestExceptionAction<,>)
        };

        if (configuration.AutoRegisterRequestProcessors)
        {
            multiOpenInterfaces.Add(typeof(IRequestPreProcessor<>));
            multiOpenInterfaces.Add(typeof(IRequestPostProcessor<,>));
        }

        var scannableTypes = GetScannableTypes(assembliesToScan, configuration).ToList();

        foreach (var multiOpenInterface in multiOpenInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var arity = multiOpenInterface.GetGenericArguments().Length;

            var concretions = scannableTypes
                .Where(type => type.IsGenericTypeDefinition)
                .Where(type => type.GetGenericArguments().Length == arity)
                .Where(type => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == multiOpenInterface))
                // A handler of a wrapped message, such as INotificationHandler<Wrapped<T>>, cannot be
                // registered as an open generic: the container would close it over the requested
                // notification and produce a type that does not implement the service, breaking every
                // resolution of it. Those are closed by unification instead, under
                // RegisterGenericHandlers.
                .Where(type => type.ClosesOverItsOwnTypeParameters(multiOpenInterface))
                .ToList();

            foreach (var type in concretions)
            {
                services.TryAddEnumerable(new ServiceDescriptor(multiOpenInterface, type, configuration.Lifetime));
            }
        }
    }

    /// <summary>
    /// Closes handlers written against generic requests over every candidate type argument found in
    /// the scanned assemblies. Only runs when <see cref="DispatcherServiceConfiguration.RegisterGenericHandlers"/> is on.
    /// </summary>
    private static int CloseGenericHandlers(
        IServiceCollection services,
        Type openHandlerInterface,
        bool multiple,
        IReadOnlyCollection<Assembly> assembliesToScan,
        DispatcherServiceConfiguration configuration,
        int registrationCount,
        CancellationToken cancellationToken)
    {
        var scannableTypes = GetScannableTypes(assembliesToScan, configuration).ToList();

        var candidatePool = assembliesToScan
            .SelectMany(static assembly => assembly.DefinedTypes)
            .Select(static type => type.AsType())
            .Where(static type => !type.IsOpenGeneric())
            .ToList();

        var openImplementations = scannableTypes
            .Where(static type => type.IsGenericTypeDefinition)
            .Where(type => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openHandlerInterface))
            // For the many-per-message interfaces, an implementation whose parameters map straight
            // onto the interface is already registered as an open generic. Closing it here too would
            // register the same handler twice.
            .Where(type => !multiple || !type.ClosesOverItsOwnTypeParameters(openHandlerInterface))
            .ToList();

        var added = 0;

        foreach (var implementation in openImplementations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = implementation.GetGenericArguments();

            if (configuration.MaxGenericTypeParameters > 0 && parameters.Length > configuration.MaxGenericTypeParameters)
            {
                throw new ArgumentException($"The generic handler '{implementation.Name}' declares {parameters.Length} type parameters, which exceeds MaxGenericTypeParameters of {configuration.MaxGenericTypeParameters}.");
            }

            var candidatesPerParameter = new List<Type[]>(parameters.Length);

            foreach (var parameter in parameters)
            {
                var candidates = candidatePool.Where(candidate => parameter.SatisfiesConstraints(candidate)).ToArray();

                if (configuration.MaxTypesClosing > 0 && candidates.Length > configuration.MaxTypesClosing)
                {
                    throw new ArgumentException($"The generic handler '{implementation.Name}' has {candidates.Length} candidate types for parameter '{parameter.Name}', which exceeds MaxTypesClosing of {configuration.MaxTypesClosing}. Constrain the type parameter or raise the limit.");
                }

                candidatesPerParameter.Add(candidates);
            }

            foreach (var combination in CartesianProduct(candidatesPerParameter))
            {
                cancellationToken.ThrowIfCancellationRequested();

                Type closedImplementation;

                try
                {
                    closedImplementation = implementation.MakeGenericType(combination);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                var closedInterfaces = closedImplementation
                    .GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openHandlerInterface && !i.IsOpenGeneric());

                foreach (var closedInterface in closedInterfaces)
                {
                    if (multiple)
                    {
                        services.TryAddEnumerable(new ServiceDescriptor(closedInterface, closedImplementation, configuration.Lifetime));
                    }
                    else
                    {
                        services.TryAdd(new ServiceDescriptor(closedInterface, closedImplementation, configuration.Lifetime));
                    }

                    added++;

                    if (configuration.MaxGenericTypeRegistrations > 0 && registrationCount + added > configuration.MaxGenericTypeRegistrations)
                    {
                        throw new ArgumentException($"Closing generic handlers produced more than MaxGenericTypeRegistrations ({configuration.MaxGenericTypeRegistrations}) registrations. Constrain the type parameters or raise the limit.");
                    }
                }
            }
        }

        return added;
    }

    private static IEnumerable<Type[]> CartesianProduct(IReadOnlyList<Type[]> sets)
    {
        if (sets.Count == 0 || sets.Any(static set => set.Length == 0))
        {
            yield break;
        }

        var indices = new int[sets.Count];

        while (true)
        {
            var combination = new Type[sets.Count];

            for (var i = 0; i < sets.Count; i++)
            {
                combination[i] = sets[i][indices[i]];
            }

            yield return combination;

            var position = sets.Count - 1;

            while (position >= 0)
            {
                indices[position]++;

                if (indices[position] < sets[position].Length)
                {
                    break;
                }

                indices[position] = 0;
                position--;
            }

            if (position < 0)
            {
                yield break;
            }
        }
    }

    private static IEnumerable<Type> GetScannableTypes(IEnumerable<Assembly> assembliesToScan, DispatcherServiceConfiguration configuration)
        => assembliesToScan
            .SelectMany(static assembly => assembly.DefinedTypes)
            .Select(static type => type.AsType())
            .Where(static type => type.IsConcrete())
            .Where(type => configuration.TypeEvaluator(type));
}
