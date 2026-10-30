using System;
using System.Reflection;
using AspNetCore.Dispatcher;
using AspNetCore.Dispatcher.Registration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions to scan for dispatcher handlers and register them with the container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the dispatcher and every handler, processor and behavior found in the configured assemblies.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration callback</param>
    public static IServiceCollection AddDispatcher(this IServiceCollection services, Action<DispatcherServiceConfiguration> configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var serviceConfiguration = new DispatcherServiceConfiguration();
        configuration(serviceConfiguration);

        return services.AddDispatcher(serviceConfiguration);
    }

    /// <summary>
    /// Registers the dispatcher and every handler, processor and behavior found in the configured assemblies.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Prepared configuration</param>
    public static IServiceCollection AddDispatcher(this IServiceCollection services, DispatcherServiceConfiguration configuration)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (configuration.AssembliesToRegister.Count == 0)
        {
            throw new ArgumentException("No assemblies found to scan. Supply at least one assembly to scan for handlers.", nameof(configuration));
        }

        ServiceRegistrar.AddDispatcherClassesWithTimeout(services, configuration);
        ServiceRegistrar.AddRequiredServices(services, configuration);

        return services;
    }

    /// <summary>
    /// Registers the dispatcher and every handler found in the supplied assemblies, using the defaults.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="assemblies">Assemblies to scan</param>
    public static IServiceCollection AddDispatcher(this IServiceCollection services, params Assembly[] assemblies)
        => services.AddDispatcher(configuration => configuration.RegisterServicesFromAssemblies(assemblies));

    /// <summary>
    /// Registers the dispatcher and every handler found in the assembly containing <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type whose assembly is scanned</typeparam>
    /// <param name="services">Service collection</param>
    public static IServiceCollection AddDispatcherFromAssemblyContaining<T>(this IServiceCollection services)
        => services.AddDispatcher(configuration => configuration.RegisterServicesFromAssemblyContaining<T>());
}
