using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AspNetCore.Dispatcher.NotificationPublishers;
using AspNetCore.Dispatcher.Wrappers;

namespace AspNetCore.Dispatcher;

/// <summary>
/// Default <see cref="IDispatcher"/> implementation. Resolves handlers, behaviors and
/// notification handlers from an <see cref="IServiceProvider"/>.
/// </summary>
public class Dispatcher : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerBase> RequestHandlers = new ConcurrentDictionary<Type, RequestHandlerBase>();
    private static readonly ConcurrentDictionary<Type, NotificationHandlerWrapper> NotificationHandlers = new ConcurrentDictionary<Type, NotificationHandlerWrapper>();
    private static readonly ConcurrentDictionary<Type, StreamRequestHandlerBase> StreamRequestHandlers = new ConcurrentDictionary<Type, StreamRequestHandlerBase>();

    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationPublisher _publisher;

    /// <summary>
    /// Initializes a new instance using the default <see cref="ForeachAwaitPublisher"/>.
    /// </summary>
    /// <param name="serviceProvider">Service provider used to resolve handlers and behaviors</param>
    public Dispatcher(IServiceProvider serviceProvider)
        : this(serviceProvider, new ForeachAwaitPublisher())
    {
    }

    /// <summary>
    /// Initializes a new instance with a custom notification publishing strategy.
    /// </summary>
    /// <param name="serviceProvider">Service provider used to resolve handlers and behaviors</param>
    /// <param name="publisher">Notification publishing strategy</param>
    public Dispatcher(IServiceProvider serviceProvider, INotificationPublisher publisher)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    /// <inheritdoc />
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var handler = (RequestHandlerWrapper<TResponse>)RequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            var wrapper = Activator.CreateInstance(typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, typeof(TResponse)))
                          ?? throw new InvalidOperationException($"Could not create wrapper type for {requestType}");

            return (RequestHandlerBase)wrapper;
        });

        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Key on the runtime type, not typeof(TRequest): generic inference picks up the static
        // type, so a request held in a base-typed or interface-typed variable would otherwise look
        // for a handler of the base type and cache that wrapper for everyone else.
        var handler = (RequestHandlerWrapper<Unit>)RequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            var wrapper = Activator.CreateInstance(typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, typeof(Unit)))
                          ?? throw new InvalidOperationException($"Could not create wrapper type for {requestType}");

            return (RequestHandlerBase)wrapper;
        });

        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var handler = RequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            var requestInterfaceType = requestType
                .GetInterfaces()
                .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

            if (requestInterfaceType is null)
            {
                throw new ArgumentException($"{requestType.Name} does not implement {nameof(IRequest)}", nameof(request));
            }

            var responseType = requestInterfaceType.GetGenericArguments()[0];
            var wrapper = Activator.CreateInstance(typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType))
                          ?? throw new InvalidOperationException($"Could not create wrapper for type {requestType}");

            return (RequestHandlerBase)wrapper;
        });

        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var streamHandler = (StreamRequestHandlerWrapper<TResponse>)StreamRequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            var wrapper = Activator.CreateInstance(typeof(StreamRequestHandlerWrapperImpl<,>).MakeGenericType(requestType, typeof(TResponse)))
                          ?? throw new InvalidOperationException($"Could not create wrapper for type {requestType}");

            return (StreamRequestHandlerBase)wrapper;
        });

        return streamHandler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var handler = StreamRequestHandlers.GetOrAdd(request.GetType(), static requestType =>
        {
            var requestInterfaceType = requestType
                .GetInterfaces()
                .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamRequest<>));

            if (requestInterfaceType is null)
            {
                throw new ArgumentException($"{requestType.Name} does not implement IStreamRequest<TResponse>", nameof(request));
            }

            var responseType = requestInterfaceType.GetGenericArguments()[0];
            var wrapper = Activator.CreateInstance(typeof(StreamRequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType))
                          ?? throw new InvalidOperationException($"Could not create wrapper for type {requestType}");

            return (StreamRequestHandlerBase)wrapper;
        });

        return handler.Handle(request, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification == null)
        {
            throw new ArgumentNullException(nameof(notification));
        }

        return PublishNotification(notification, cancellationToken);
    }

    /// <inheritdoc />
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        if (notification == null)
        {
            throw new ArgumentNullException(nameof(notification));
        }

        if (notification is INotification instance)
        {
            return PublishNotification(instance, cancellationToken);
        }

        throw new ArgumentException($"{nameof(notification)} does not implement {nameof(INotification)}", nameof(notification));
    }

    /// <summary>
    /// Override in a derived class to control how the notification handlers are invoked,
    /// for example to swallow exceptions or to fan out onto the thread pool.
    /// </summary>
    /// <param name="handlerExecutors">The resolved handlers</param>
    /// <param name="notification">The notification being published</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected virtual Task PublishCore(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
        => _publisher.Publish(handlerExecutors, notification, cancellationToken);

    private Task PublishNotification(INotification notification, CancellationToken cancellationToken)
    {
        var handler = NotificationHandlers.GetOrAdd(notification.GetType(), static notificationType =>
        {
            var wrapper = Activator.CreateInstance(typeof(NotificationHandlerWrapperImpl<>).MakeGenericType(notificationType))
                          ?? throw new InvalidOperationException($"Could not create wrapper for type {notificationType}");

            return (NotificationHandlerWrapper)wrapper;
        });

        return handler.Handle(notification, _serviceProvider, PublishCore, cancellationToken);
    }
}
