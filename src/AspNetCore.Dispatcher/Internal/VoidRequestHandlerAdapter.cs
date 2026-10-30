using System.Threading;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher.Internal;

/// <summary>
/// Infrastructure. Adapts an <see cref="IRequestHandler{TRequest}"/> (void response) to the
/// uniform <see cref="IRequestHandler{TRequest, TResponse}"/> shape used by the pipeline, so that
/// behaviors written against <c>IPipelineBehavior&lt;TRequest, Unit&gt;</c> apply to void requests too.
/// Registered automatically for every discovered void handler.
/// </summary>
/// <typeparam name="TRequest">The void request type</typeparam>
public sealed class VoidRequestHandlerAdapter<TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest
{
    private readonly IRequestHandler<TRequest> _inner;

    /// <summary>
    /// Creates the adapter around the real void handler.
    /// </summary>
    public VoidRequestHandlerAdapter(IRequestHandler<TRequest> inner) => _inner = inner;

    /// <inheritdoc />
    public async Task<Unit> Handle(TRequest request, CancellationToken cancellationToken)
    {
        await _inner.Handle(request, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
