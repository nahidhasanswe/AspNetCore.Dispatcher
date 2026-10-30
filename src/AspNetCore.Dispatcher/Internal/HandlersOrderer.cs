using System;
using System.Collections.Generic;
using System.Linq;

namespace AspNetCore.Dispatcher.Internal;

/// <summary>
/// Orders exception handlers and actions so the most specific one runs first, and drops
/// a handler whose behavior is already overridden by a derived handler in the same set.
/// </summary>
internal static class HandlersOrderer
{
    public static IList<object> Prioritize<TRequest>(IList<object> handlers, TRequest request)
        where TRequest : notnull
    {
        if (handlers.Count < 2)
        {
            return handlers;
        }

        var requestDetails = new ObjectDetails(request);
        var handlerDetails = handlers.Select(static handler => new ObjectDetails(handler)).ToList();

        var uniqueHandlers = RemoveOverridden(handlerDetails).ToArray();
        Array.Sort(uniqueHandlers, requestDetails);

        return uniqueHandlers.Select(static handler => handler.Value).ToList();
    }

    private static IEnumerable<ObjectDetails> RemoveOverridden(IList<ObjectDetails> handlers)
    {
        for (var i = 0; i < handlers.Count; i++)
        {
            for (var j = i + 1; j < handlers.Count; j++)
            {
                if (handlers[i].Type.IsAssignableFrom(handlers[j].Type))
                {
                    handlers[i].IsOverridden = true;
                    break;
                }

                if (handlers[j].Type.IsAssignableFrom(handlers[i].Type))
                {
                    handlers[j].IsOverridden = true;
                }
            }
        }

        return handlers.Where(static handler => !handler.IsOverridden);
    }
}
