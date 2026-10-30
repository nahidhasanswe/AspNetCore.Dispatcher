using System.Collections.Generic;
using System.Threading;

namespace AspNetCore.Dispatcher;

/// <summary>
/// Stream pipeline behavior to surround the inner handler.
/// Implementations add additional behavior and iterate the next delegate.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Pipeline handler. Perform any additional behavior and iterate the next delegate as necessary.
    /// </summary>
    /// <param name="request">Incoming request</param>
    /// <param name="next">Delegate for the next action in the pipeline. Eventually this delegate represents the handler.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An async enumerable of <typeparamref name="TResponse"/></returns>
    IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
