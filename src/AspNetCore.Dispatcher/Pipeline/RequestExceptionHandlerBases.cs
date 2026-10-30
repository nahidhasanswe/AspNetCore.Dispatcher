using System;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher.Pipeline;

/// <summary>
/// Base class for asynchronous exception handlers.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
/// <typeparam name="TException">Exception type</typeparam>
public abstract class AsyncRequestExceptionHandler<TRequest, TResponse, TException> : IRequestExceptionHandler<TRequest, TResponse, TException>
    where TRequest : notnull
    where TException : Exception
{
    Task IRequestExceptionHandler<TRequest, TResponse, TException>.Handle(TRequest request, TException exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken)
        => Handle(request, exception, state, cancellationToken);

    /// <summary>
    /// Override in a derived class to handle the exception asynchronously.
    /// </summary>
    protected abstract Task Handle(TRequest request, TException exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken);
}

/// <summary>
/// Base class for asynchronous handlers of every exception type.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public abstract class AsyncRequestExceptionHandler<TRequest, TResponse> : AsyncRequestExceptionHandler<TRequest, TResponse, Exception>
    where TRequest : notnull
{
}

/// <summary>
/// Base class for synchronous exception handlers.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
/// <typeparam name="TException">Exception type</typeparam>
public abstract class RequestExceptionHandler<TRequest, TResponse, TException> : IRequestExceptionHandler<TRequest, TResponse, TException>
    where TRequest : notnull
    where TException : Exception
{
    Task IRequestExceptionHandler<TRequest, TResponse, TException>.Handle(TRequest request, TException exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken)
    {
        try
        {
            Handle(request, exception, state);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    /// <summary>
    /// Override in a derived class to handle the exception synchronously.
    /// </summary>
    protected abstract void Handle(TRequest request, TException exception, RequestExceptionHandlerState<TResponse> state);
}

/// <summary>
/// Base class for synchronous handlers of every exception type.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public abstract class RequestExceptionHandler<TRequest, TResponse> : RequestExceptionHandler<TRequest, TResponse, Exception>
    where TRequest : notnull
{
}
