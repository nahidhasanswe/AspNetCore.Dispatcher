using System;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetCore.Dispatcher.Wrappers;

/// <summary>
/// Base for the generated handler wrappers. Resolves handlers from the container and
/// turns container failures into a message that names the missing handler.
/// </summary>
internal abstract class HandlerBase
{
    protected static THandler GetHandler<THandler>(IServiceProvider serviceProvider)
    {
        THandler handler;

        try
        {
            handler = (THandler)serviceProvider.GetRequiredService(typeof(THandler));
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Error constructing handler for request of type {typeof(THandler)}. Register your handlers with the container. See the inner exception for registration errors.", e);
        }

        if (handler == null)
        {
            throw new InvalidOperationException($"Handler was not found for request of type {typeof(THandler)}. Register your handlers with the container.");
        }

        return handler;
    }
}
