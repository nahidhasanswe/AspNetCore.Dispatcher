using System;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher.Pipeline;

/// <summary>
/// Defines an action to run when a request throws. Unlike an exception handler, an action
/// cannot swallow the exception, it only reacts to it (logging, metrics, compensations).
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TException">Exception type</typeparam>
public interface IRequestExceptionAction<in TRequest, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>
    /// Called when the request handler throws an exception assignable to <typeparamref name="TException"/>.
    /// </summary>
    /// <param name="request">Request instance</param>
    /// <param name="exception">The thrown exception</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task Execute(TRequest request, TException exception, CancellationToken cancellationToken);
}

/// <summary>
/// Defines an action to run for every exception type thrown by a request.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
public interface IRequestExceptionAction<in TRequest> : IRequestExceptionAction<TRequest, Exception>
    where TRequest : notnull
{
}
