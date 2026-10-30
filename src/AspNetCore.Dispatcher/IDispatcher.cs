namespace AspNetCore.Dispatcher;

/// <summary>
/// Defines a dispatcher to encapsulate request/response and publishing interaction patterns.
/// </summary>
public interface IDispatcher : ISender, IPublisher
{
}
