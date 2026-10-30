using System;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher;

/// <summary>
/// Wrapper class for a synchronous request handler which returns a response.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled</typeparam>
/// <typeparam name="TResponse">The type of response from the handler</typeparam>
public abstract class RequestHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> IRequestHandler<TRequest, TResponse>.Handle(TRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(Handle(request));
        }
        catch (Exception ex)
        {
            return Task.FromException<TResponse>(ex);
        }
    }

    /// <summary>
    /// Override in a derived class to handle the request synchronously.
    /// </summary>
    protected abstract TResponse Handle(TRequest request);
}

/// <summary>
/// Wrapper class for a synchronous request handler without a response.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled</typeparam>
public abstract class RequestHandler<TRequest> : IRequestHandler<TRequest>
    where TRequest : IRequest
{
    Task IRequestHandler<TRequest>.Handle(TRequest request, CancellationToken cancellationToken)
    {
        try
        {
            Handle(request);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    /// <summary>
    /// Override in a derived class to handle the request synchronously.
    /// </summary>
    protected abstract void Handle(TRequest request);
}
