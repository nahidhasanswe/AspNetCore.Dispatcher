using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using AspNetCore.Dispatcher.Internal;

namespace AspNetCore.Dispatcher.Pipeline;

/// <summary>
/// Behavior that offers a thrown exception to every matching
/// <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>, most specific first. The
/// first handler that marks the state handled supplies the response and the exception is swallowed;
/// otherwise the exception is rethrown with its original stack trace.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public class RequestExceptionProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> MethodCache = new ConcurrentDictionary<Type, MethodInfo>();

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates the behavior.
    /// </summary>
    /// <param name="serviceProvider">Provider used to resolve exception handlers on demand</param>
    public RequestExceptionProcessorBehavior(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var state = new RequestExceptionHandlerState<TResponse>();

            var handlers = ExceptionHandlerResolver.Resolve(
                _serviceProvider,
                typeof(IRequestExceptionHandler<,,>),
                exceptionType => typeof(IRequestExceptionHandler<,,>).MakeGenericType(typeof(TRequest), typeof(TResponse), exceptionType),
                nameof(IRequestExceptionHandler<TRequest, TResponse, Exception>.Handle),
                exception,
                request,
                MethodCache);

            foreach (var handler in handlers)
            {
                await Invoke(handler, request, exception, state, cancellationToken).ConfigureAwait(false);

                if (state.Handled)
                {
                    break;
                }
            }

            if (!state.Handled)
            {
                throw;
            }

            return state.Response!;
        }
    }

    private static async Task Invoke(ResolvedExceptionHandler handler, TRequest request, Exception exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken)
    {
        try
        {
            await ((Task)handler.Method.Invoke(handler.Instance, new object[] { request, exception, state, cancellationToken })!).ConfigureAwait(false);
        }
        catch (TargetInvocationException invocationException) when (invocationException.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(invocationException.InnerException).Throw();
        }
    }
}
