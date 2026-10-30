using System;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCore.Dispatcher.Tests;

/// <summary>
/// Builds a provider that only sees the handler types declared inside the given test class, so
/// one test cannot register handlers for another test.
/// </summary>
internal static class TestServices
{
    public static ServiceProvider Build<TScope>(Action<DispatcherServiceConfiguration>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddDispatcher(configuration =>
        {
            configuration.RegisterServicesFromAssemblyContaining<TScope>();
            configuration.TypeEvaluator = type => IsDeclaredWithin(type, typeof(TScope));
            configure?.Invoke(configuration);
        });

        return services.BuildServiceProvider();
    }

    private static bool IsDeclaredWithin(Type type, Type scope)
    {
        var current = type;

        while (current != null)
        {
            if (current == scope)
            {
                return true;
            }

            current = current.DeclaringType;
        }

        return false;
    }
}
