using System;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher.Pipeline;

/// <summary>
/// Defines an exception handler for a request and response, able to supply a fallback response.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
/// <typeparam name="TException">Exception type</typeparam>
public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>
    /// Called when the request handler throws an exception assignable to <typeparamref name="TException"/>.
    /// Call <see cref="RequestExceptionHandlerState{TResponse}.SetHandled"/> to stop the exception from propagating.
    /// </summary>
    /// <param name="request">Request instance</param>
    /// <param name="exception">The thrown exception</param>
    /// <param name="state">State of the exception handling, used to return a response</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task Handle(TRequest request, TException exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken);
}

/// <summary>
/// Defines an exception handler for every exception type of a request and response.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public interface IRequestExceptionHandler<in TRequest, TResponse> : IRequestExceptionHandler<TRequest, TResponse, Exception>
    where TRequest : notnull
{
}
