namespace AspNetCore.Dispatcher;

/// <summary>
/// Allows for generic type constraints of objects implementing IRequest or IRequest{TResponse}.
/// </summary>
public interface IBaseRequest
{
}

/// <summary>
/// Marker interface to represent a request with a void response.
/// </summary>
public interface IRequest : IRequest<Unit>
{
}

/// <summary>
/// Marker interface to represent a request with a response.
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
public interface IRequest<out TResponse> : IBaseRequest
{
}

/// <summary>
/// Marker interface to represent a command with a void response.
/// </summary>
public interface ICommand : IRequest
{
}

/// <summary>
/// Marker interface to represent a command with a response.
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>
{
}

/// <summary>
/// Marker interface to represent a query with a response.
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}
