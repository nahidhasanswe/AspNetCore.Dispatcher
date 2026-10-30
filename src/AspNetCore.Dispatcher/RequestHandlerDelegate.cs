using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher;

/// <summary>
/// Represents an async continuation for the next task to execute in the pipeline.
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>Awaitable task returning a <typeparamref name="TResponse" /></returns>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);

/// <summary>
/// Represents a continuation for the next step to execute in the stream pipeline.
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>An async enumerable of <typeparamref name="TResponse" /></returns>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);
