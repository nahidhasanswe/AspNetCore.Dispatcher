using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using AspNetCore.Dispatcher.Internal;

namespace AspNetCore.Dispatcher.Pipeline;

/// <summary>
/// Behavior that runs every matching <see cref="IRequestExceptionAction{TRequest,TException}"/> when a
/// request throws, most specific first, then rethrows. Actions observe the failure, they cannot
/// suppress it.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public class RequestExceptionActionProcessorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> MethodCache = new ConcurrentDictionary<Type, MethodInfo>();

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates the behavior.
    /// </summary>
    /// <param name="serviceProvider">Provider used to resolve exception actions on demand</param>
    public RequestExceptionActionProcessorBehavior(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var actions = ExceptionHandlerResolver.Resolve(
                _serviceProvider,
                typeof(IRequestExceptionAction<,>),
                exceptionType => typeof(IRequestExceptionAction<,>).MakeGenericType(typeof(TRequest), exceptionType),
                nameof(IRequestExceptionAction<TRequest, Exception>.Execute),
                exception,
                request,
                MethodCache);

            foreach (var action in actions)
            {
                await Invoke(action, request, exception, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }

    private static async Task Invoke(ResolvedExceptionHandler action, TRequest request, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            await ((Task)action.Method.Invoke(action.Instance, new object[] { request, exception, cancellationToken })!).ConfigureAwait(false);
        }
        catch (TargetInvocationException invocationException) when (invocationException.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(invocationException.InnerException).Throw();
        }
    }
}
