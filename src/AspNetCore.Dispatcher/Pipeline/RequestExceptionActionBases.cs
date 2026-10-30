using System;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher.Pipeline;

/// <summary>
/// Base class for asynchronous exception actions.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TException">Exception type</typeparam>
public abstract class AsyncRequestExceptionAction<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
    where TRequest : notnull
    where TException : Exception
{
    Task IRequestExceptionAction<TRequest, TException>.Execute(TRequest request, TException exception, CancellationToken cancellationToken)
        => Execute(request, exception, cancellationToken);

    /// <summary>
    /// Override in a derived class to react to the exception asynchronously.
    /// </summary>
    protected abstract Task Execute(TRequest request, TException exception, CancellationToken cancellationToken);
}

/// <summary>
/// Base class for asynchronous actions covering every exception type.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
public abstract class AsyncRequestExceptionAction<TRequest> : AsyncRequestExceptionAction<TRequest, Exception>
    where TRequest : notnull
{
}

/// <summary>
/// Base class for synchronous exception actions.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TException">Exception type</typeparam>
public abstract class RequestExceptionAction<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
    where TRequest : notnull
    where TException : Exception
{
    Task IRequestExceptionAction<TRequest, TException>.Execute(TRequest request, TException exception, CancellationToken cancellationToken)
    {
        try
        {
            Execute(request, exception);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    /// <summary>
    /// Override in a derived class to react to the exception synchronously.
    /// </summary>
    protected abstract void Execute(TRequest request, TException exception);
}

/// <summary>
/// Base class for synchronous actions covering every exception type.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
public abstract class RequestExceptionAction<TRequest> : RequestExceptionAction<TRequest, Exception>
    where TRequest : notnull
{
}
