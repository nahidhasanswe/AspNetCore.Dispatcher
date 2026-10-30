using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher.Pipeline;

/// <summary>
/// Defines a pre-processor for a request, executed before the handler.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
public interface IRequestPreProcessor<in TRequest>
    where TRequest : notnull
{
    /// <summary>
    /// Process method executes before calling the handler.
    /// </summary>
    /// <param name="request">Incoming request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task Process(TRequest request, CancellationToken cancellationToken);
}
