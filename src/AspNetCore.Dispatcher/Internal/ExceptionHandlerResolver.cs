using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCore.Dispatcher.Internal;

/// <summary>
/// One exception handler or action, paired with the method to invoke on it.
/// </summary>
internal readonly struct ResolvedExceptionHandler
{
    public ResolvedExceptionHandler(object instance, MethodInfo method)
    {
        Instance = instance;
        Method = method;
    }

    public object Instance { get; }

    public MethodInfo Method { get; }
}

/// <summary>
/// Resolves the exception handlers and actions that apply to a thrown exception.
/// </summary>
/// <remarks>
/// The handler interfaces are contravariant in the exception type, so a handler written against a
/// base exception is also registered under the closed interfaces of its derived exceptions. That
/// means the same handler can be resolved several times while walking the exception hierarchy, and
/// that a catch-all handler can come back alongside a specific one. Candidates are therefore
/// de-duplicated by implementation type and ordered so the handler declaring the most derived
/// exception type runs first.
/// </remarks>
internal static class ExceptionHandlerResolver
{
    public static IReadOnlyList<ResolvedExceptionHandler> Resolve<TRequest>(
        IServiceProvider serviceProvider,
        Type openInterface,
        Func<Type, Type> closeInterface,
        string methodName,
        Exception exception,
        TRequest request,
        ConcurrentDictionary<Type, MethodInfo> methodCache)
        where TRequest : notnull
    {
        var thrownType = exception.GetType();
        var seenImplementations = new HashSet<Type>();
        var candidates = new List<Candidate>();

        foreach (var exceptionType in ExceptionTypeWalker.GetExceptionTypes(thrownType))
        {
            var closedInterface = closeInterface(exceptionType);
            var enumerableInterface = typeof(IEnumerable<>).MakeGenericType(closedInterface);
            var resolved = (IEnumerable<object>)serviceProvider.GetRequiredService(enumerableInterface);

            foreach (var instance in resolved)
            {
                if (!seenImplementations.Add(instance.GetType()))
                {
                    continue;
                }

                var declared = GetBestDeclaredExceptionType(instance.GetType(), openInterface, thrownType);

                if (declared is null)
                {
                    continue;
                }

                var method = methodCache.GetOrAdd(closeInterface(declared), type => type.GetMethod(methodName)!);

                candidates.Add(new Candidate(instance, method, GetDistance(thrownType, declared)));
            }
        }

        return candidates
            .GroupBy(static candidate => candidate.Distance)
            .OrderBy(static grouping => grouping.Key)
            .SelectMany(grouping => Order(grouping.ToList(), request))
            .ToList();
    }

    private static IEnumerable<ResolvedExceptionHandler> Order<TRequest>(IList<Candidate> candidates, TRequest request)
        where TRequest : notnull
    {
        var prioritized = HandlersOrderer.Prioritize(candidates.Select(static candidate => candidate.Instance).ToList(), request);

        foreach (var instance in prioritized)
        {
            var candidate = candidates.First(c => ReferenceEquals(c.Instance, instance));
            yield return new ResolvedExceptionHandler(candidate.Instance, candidate.Method);
        }
    }

    /// <summary>
    /// Of the exception types this implementation declares a handler for, returns the most derived
    /// one that still applies to the thrown exception.
    /// </summary>
    private static Type? GetBestDeclaredExceptionType(Type implementationType, Type openInterface, Type thrownType)
    {
        Type? best = null;
        var bestDistance = int.MaxValue;

        foreach (var closedInterface in implementationType
                     .GetInterfaces()
                     .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface))
        {
            var arguments = closedInterface.GenericTypeArguments;
            var declared = arguments[arguments.Length - 1];
            var distance = GetDistance(thrownType, declared);

            if (distance != int.MaxValue && distance < bestDistance)
            {
                bestDistance = distance;
                best = declared;
            }
        }

        return best;
    }

    /// <summary>
    /// Number of inheritance steps from the thrown exception type up to the declared one, or
    /// <see cref="int.MaxValue"/> when the declared type does not apply.
    /// </summary>
    private static int GetDistance(Type thrownType, Type declaredType)
    {
        var distance = 0;
        Type? current = thrownType;

        while (current != null && current != declaredType)
        {
            current = current.BaseType;
            distance++;
        }

        return current == null ? int.MaxValue : distance;
    }

    private readonly struct Candidate
    {
        public Candidate(object instance, MethodInfo method, int distance)
        {
            Instance = instance;
            Method = method;
            Distance = distance;
        }

        public object Instance { get; }

        public MethodInfo Method { get; }

        public int Distance { get; }
    }
}
