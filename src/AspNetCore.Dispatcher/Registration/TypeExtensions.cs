using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AspNetCore.Dispatcher.Registration;

internal static class TypeExtensions
{
    public static bool IsConcrete(this Type type) => !type.IsAbstract && !type.IsInterface;

    public static bool IsOpenGeneric(this Type type) => type.IsGenericTypeDefinition || type.ContainsGenericParameters;

    public static void Fill<T>(this IList<T> list, T value)
    {
        if (list.Contains(value))
        {
            return;
        }

        list.Add(value);
    }

    public static bool CanBeCastTo(this Type? pluggedType, Type pluginType)
    {
        if (pluggedType == null)
        {
            return false;
        }

        return pluggedType == pluginType || pluginType.IsAssignableFrom(pluggedType);
    }

    /// <summary>
    /// True when the open generic implementation can be closed over the generic arguments of the
    /// supplied closed interface, for example <c>Handler&lt;T&gt;</c> over <c>IRequestHandler&lt;Request, T&gt;</c>.
    /// </summary>
    public static bool CouldCloseTo(this Type openConcretion, Type closedInterface)
    {
        var openInterface = closedInterface.GetGenericTypeDefinition();
        var arguments = closedInterface.GenericTypeArguments;
        var concreteArguments = openConcretion.GenericTypeArguments;

        return arguments.Length == concreteArguments.Length && openConcretion.CanBeCastTo(openInterface);
    }

    /// <summary>
    /// Returns every interface implemented by <paramref name="pluggedType"/> that closes
    /// <paramref name="templateType"/>, walking base types as well.
    /// </summary>
    public static IEnumerable<Type> FindInterfacesThatClose(this Type pluggedType, Type templateType)
        => FindInterfacesThatClosesCore(pluggedType, templateType).Distinct();

    private static IEnumerable<Type> FindInterfacesThatClosesCore(Type? pluggedType, Type templateType)
    {
        if (pluggedType == null)
        {
            yield break;
        }

        if (!pluggedType.IsConcrete())
        {
            yield break;
        }

        if (templateType.IsInterface)
        {
            foreach (var interfaceType in pluggedType
                         .GetInterfaces()
                         .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == templateType))
            {
                yield return interfaceType;
            }
        }
        else if (pluggedType.BaseType != null &&
                 pluggedType.BaseType.IsGenericType &&
                 pluggedType.BaseType.GetGenericTypeDefinition() == templateType)
        {
            yield return pluggedType.BaseType;
        }

        if (pluggedType.BaseType == typeof(object))
        {
            yield break;
        }

        foreach (var interfaceType in FindInterfacesThatClosesCore(pluggedType.BaseType, templateType))
        {
            yield return interfaceType;
        }
    }

    /// <summary>
    /// True when the handler is a direct, non-inherited match for the closed interface. Used to
    /// disambiguate when several candidates implement the same single-handler interface.
    /// </summary>
    public static bool IsMatchingWithInterface(this Type? handlerType, Type? handlerInterface)
    {
        if (handlerType == null || handlerInterface == null)
        {
            return false;
        }

        if (handlerType == handlerInterface)
        {
            return true;
        }

        if (!handlerInterface.IsGenericType)
        {
            return false;
        }

        // Compare the closed interface directly rather than looking it up by name. Type.GetInterface
        // throws AmbiguousMatchException when a type implements the same generic interface with
        // different arguments, which a handler serving two requests legitimately does.
        var definition = handlerInterface.GetGenericTypeDefinition();

        return handlerType
            .GetInterfaces()
            .Any(implemented => implemented.IsGenericType
                                && implemented.GetGenericTypeDefinition() == definition
                                && implemented.GenericTypeArguments.SequenceEqual(handlerInterface.GenericTypeArguments));
    }

    /// <summary>
    /// True when the open generic implementation closes <paramref name="openInterface"/> over exactly
    /// its own type parameters, in order.
    /// </summary>
    /// <remarks>
    /// This is the only shape that can be registered as an open generic service. The container closes
    /// such a registration by handing the requested service type's arguments straight to the
    /// implementation, so <c>Handler&lt;T&gt; : INotificationHandler&lt;T&gt;</c> closes correctly while
    /// <c>Handler&lt;T&gt; : INotificationHandler&lt;Wrapped&lt;T&gt;&gt;</c> would produce a type that does
    /// not implement the requested service and fail every resolution of that service.
    /// </remarks>
    public static bool ClosesOverItsOwnTypeParameters(this Type implementationType, Type openInterface)
    {
        var parameters = implementationType.GetGenericArguments();

        return implementationType
            .GetInterfaces()
            .Where(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == openInterface)
            .Any(implemented => implemented.GenericTypeArguments.SequenceEqual(parameters));
    }

    /// <summary>
    /// Checks whether a candidate type satisfies the declared constraints of a generic parameter.
    /// </summary>
    public static bool SatisfiesConstraints(this Type genericParameter, Type candidate)
    {
        if (candidate.IsOpenGeneric())
        {
            return false;
        }

        var attributes = genericParameter.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;

        if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint) && candidate.IsValueType)
        {
            return false;
        }

        if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint) &&
            (!candidate.IsValueType || Nullable.GetUnderlyingType(candidate) != null))
        {
            return false;
        }

        if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) &&
            !candidate.IsValueType &&
            candidate.GetConstructor(Type.EmptyTypes) == null)
        {
            return false;
        }

        foreach (var constraint in genericParameter.GetGenericParameterConstraints())
        {
            if (constraint.ContainsGenericParameters)
            {
                continue;
            }

            if (!constraint.IsAssignableFrom(candidate))
            {
                return false;
            }
        }

        return true;
    }
}
