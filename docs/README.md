# AspNetCore.Dispatcher documentation

Start here if you are new: **[Getting started](getting-started.md)**.

## By message type

- **[Requests](requests.md)** — one request, one handler, one response. Commands, queries, void
  requests, synchronous handlers, runtime dispatch.
- **[Notifications](notifications.md)** — one notification, many handlers, no response. Publishing
  strategies and custom publishers.
- **[Streaming](streaming.md)** — one request, a stream of responses over `IAsyncEnumerable`.

## By cross-cutting concern

- **[Pipeline behaviors](behaviors.md)** — wrap a request. Logging, validation, transactions,
  caching, retries, short-circuiting.
- **[Pre and post processors](processors.md)** — run before or after a handler without wrapping it.
- **[Exception handling](exception-handling.md)** — recover from a failure with a substitute
  response, or observe it and let it propagate.

## Reference

- **[Configuration](configuration.md)** — every `DispatcherServiceConfiguration` option, assembly
  scanning, service lifetimes, generic handlers, registration limits.
- **[ASP.NET Core integration](aspnet-core.md)** — controllers, minimal APIs, scopes, background
  services, mapping failures onto HTTP responses.
- **[Testing](testing.md)** — unit-testing handlers and behaviors, integration tests, faking
  `IDispatcher`.
- **[How it works](architecture.md)** — internals, caching, the void-handler adapter, performance
  characteristics and AOT limitations.

## Moving over

- **[Migrating from MediatR](migrating-from-mediatr.md)** — name-by-name mapping, the two
  deliberate behavioral differences, and the find-and-replace to run.

## Maintaining

- **[Publishing](../PUBLISHING.md)** — how a release is cut and pushed to nuget.org.
- **[Contributing](../CONTRIBUTING.md)** — build, test, and PR expectations.
- **[Changelog](../CHANGELOG.md)**
