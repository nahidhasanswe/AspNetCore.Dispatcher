using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher.Pipeline;

/// <summary>
/// Defines a post-processor for a request, executed after the handler returns.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public interface IRequestPostProcessor<in TRequest, in TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Process method executes after the handler.
    /// </summary>
    /// <param name="request">Incoming request</param>
    /// <param name="response">Response from the handler</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}
