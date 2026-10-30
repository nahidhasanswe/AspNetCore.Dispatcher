namespace AspNetCore.Dispatcher.Pipeline;

/// <summary>
/// Carries the outcome of exception handling back to the pipeline.
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
public class RequestExceptionHandlerState<TResponse>
{
    /// <summary>
    /// Indicates whether the exception has been handled and a response produced.
    /// </summary>
    public bool Handled { get; private set; }

    /// <summary>
    /// The response to return in place of the thrown exception.
    /// </summary>
    public TResponse? Response { get; private set; }

    /// <summary>
    /// Marks the exception as handled and supplies the response to return.
    /// </summary>
    /// <param name="response">Response to return to the caller</param>
    public void SetHandled(TResponse response)
    {
        Handled = true;
        Response = response;
    }
}
