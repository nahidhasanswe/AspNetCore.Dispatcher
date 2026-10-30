# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0-preview.1] - 2026-10-30

First public preview. The API is complete; the prerelease label is there so it can still change in
response to feedback before `1.0.0`.

### Added

**Requests**

- `IRequest`, `IRequest<TResponse>`, `IBaseRequest` and the `ICommand`, `ICommand<TResponse>`,
  `IQuery<TResponse>` marker aliases.
- `IRequestHandler<TRequest, TResponse>` and `IRequestHandler<TRequest>` for void requests, which
  returns plain `Task`.
- Synchronous base classes `RequestHandler<TRequest, TResponse>` and `RequestHandler<TRequest>`.
- `Unit` as the response type of a void request.
- `ISender.Send` in three forms: generic over the response, generic over a void request, and
  type-erased over `object`.

**Notifications**

- `INotification`, `IEvent` and `INotificationHandler<TNotification>`.
- Synchronous base class `NotificationHandler<TNotification>`.
- `IPublisher.Publish`, generic and type-erased.
- `INotificationPublisher` with `ForeachAwaitPublisher` (default) and `TaskWhenAllPublisher`, plus
  an overridable `Dispatcher.PublishCore`.

**Streaming**

- `IStreamRequest<TResponse>` and `IStreamRequestHandler<TRequest, TResponse>` over
  `IAsyncEnumerable<T>`.
- `ISender.CreateStream`, generic and type-erased.

**Pipeline**

- `IPipelineBehavior<TRequest, TResponse>` and `IStreamPipelineBehavior<TRequest, TResponse>`, with
  the `RequestHandlerDelegate<TResponse>` and `StreamHandlerDelegate<TResponse>` continuations.
- `IRequestPreProcessor<TRequest>` and `IRequestPostProcessor<TRequest, TResponse>` with their
  driving behaviors.
- `IRequestExceptionHandler<TRequest, TResponse, TException>` and
  `IRequestExceptionAction<TRequest, TException>`, their four base classes each,
  `RequestExceptionHandlerState<TResponse>`, and the two processor behaviors — registered by
  default as the outermost layers of the pipeline.

**Registration**

- `AddDispatcher` in the `Microsoft.Extensions.DependencyInjection` namespace, with
  `DispatcherServiceConfiguration`: assembly scanning, `TypeEvaluator`, `Lifetime`, a swappable
  dispatcher implementation, open and closed behavior/processor registration,
  `AutoRegisterRequestProcessors`, `RegisterGenericHandlers` with its registration limits, and a
  scan timeout.
- `AddDispatcherFromAssemblyContaining<T>()` and `AddDispatcher(params Assembly[])` shorthands.
- Automatic registration of `VoidRequestHandlerAdapter<TRequest>` for every discovered void
  handler, so behaviors apply to void requests unchanged.

### Notes

- Targets `netstandard2.0`, `net8.0` and `net9.0`.
- Exception handlers and actions are de-duplicated by implementation type and ordered most-specific
  first, so a handler declared for a base exception type runs after a specific one and never runs
  twice for a single failure.
- `TaskWhenAllPublisher` converts a handler that throws before its first `await` into a faulted
  task, so one synchronous throw cannot abort the fan-out to the remaining handlers.
- Requests dispatch on their **runtime** type, so a request held in a base-typed or interface-typed
  variable reaches the handler of its concrete type. Notifications behave the same way, which also
  means an explicit type argument on `Publish` does not redirect it to a base type's handlers.
- An open generic handler, behavior or processor is registered as an open generic only when it
  closes its interface over its own type parameters, in order. Other shapes — a handler of a wrapped
  message, or a behavior with the wrong arity — are closed by unification under
  `RegisterGenericHandlers`, skipped, or rejected at registration with an explanation, and never
  left to break the resolution of unrelated messages.
- Trimming and Native AOT are not supported; see
  [docs/architecture.md](docs/architecture.md#trimming-and-native-aot).

[Unreleased]: https://github.com/nahidhasanswe/AspNetCore.Dispatcher/compare/v1.0.0-preview.1...HEAD
[1.0.0-preview.1]: https://github.com/nahidhasanswe/AspNetCore.Dispatcher/releases/tag/v1.0.0-preview.1
