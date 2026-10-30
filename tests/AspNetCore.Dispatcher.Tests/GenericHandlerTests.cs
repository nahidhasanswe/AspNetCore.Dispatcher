using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

public class GenericHandlerTests
{
    public class Wrapped<TPayload> : IRequest<string>
    {
        public TPayload Payload { get; set; } = default!;
    }

    public class WrappedHandler<TPayload> : IRequestHandler<Wrapped<TPayload>, string>
    {
        public Task<string> Handle(Wrapped<TPayload> request, CancellationToken cancellationToken)
            => Task.FromResult($"{typeof(TPayload).Name}:{request.Payload}");
    }

    public class Payload
    {
        public override string ToString() => "payload";
    }

    public class EchoRequest : IRequest<string>
    {
        public string Message { get; set; } = string.Empty;
    }

    public class EchoHandlerBase<TRequest> : IRequestHandler<TRequest, string>
        where TRequest : EchoRequest
    {
        public Task<string> Handle(TRequest request, CancellationToken cancellationToken)
            => Task.FromResult("echo:" + request.Message);
    }

    public class EchoHandler : EchoHandlerBase<EchoRequest>
    {
    }

    [Fact]
    public async Task Closes_generic_handlers_over_discovered_type_arguments()
    {
        using var provider = TestServices.Build<GenericHandlerTests>(configuration =>
        {
            configuration.TypeEvaluator = type => type == typeof(WrappedHandler<>);
            configuration.RegisterGenericHandlers = true;
            configuration.MaxTypesClosing = 0;
        });

        var response = await provider.GetRequiredService<IDispatcher>()
            .Send(new Wrapped<Payload> { Payload = new Payload() });

        Assert.Equal("Payload:payload", response);
    }

    [Fact]
    public void Generic_handlers_are_not_registered_by_default()
    {
        using var provider = TestServices.Build<GenericHandlerTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(WrappedHandler<>));

        Assert.Null(provider.GetService<IRequestHandler<Wrapped<Payload>, string>>());
    }

    [Fact]
    public async Task Handlers_inheriting_from_a_generic_base_are_registered()
    {
        using var provider = TestServices.Build<GenericHandlerTests>(configuration =>
            configuration.TypeEvaluator = type => type == typeof(EchoHandler));

        var response = await provider.GetRequiredService<IDispatcher>()
            .Send(new EchoRequest { Message = "hi" });

        Assert.Equal("echo:hi", response);
    }
}
