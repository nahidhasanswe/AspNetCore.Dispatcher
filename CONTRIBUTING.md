# Contributing

Thanks for taking a look. Issues and pull requests are both welcome.

## Build and test

Requires the .NET 8 SDK or later. The library multi-targets `netstandard2.0`, `net8.0` and
`net9.0`; the test project runs on `net8.0`.

```shell
git clone https://github.com/nahidhasanswe/AspNetCore.Dispatcher.git
cd AspNetCore.Dispatcher

dotnet build
dotnet test
```

The build treats nullable-reference warnings as errors and generates XML documentation for every
public member, so a missing `<summary>` fails the build. Keep it clean.

## Repository layout

```
src/AspNetCore.Dispatcher/          the library
tests/AspNetCore.Dispatcher.Tests/  the test suite
docs/                               user documentation
```

## Reporting a bug

Please include:

- The version of the package.
- The registration code — your `AddDispatcher` call, including any behaviors.
- The message and handler declarations involved, trimmed to the shape that reproduces it.
- What you expected and what happened, with the exception and stack trace if there is one.

Registration and resolution problems are usually reproducible in a handful of lines; a small
repro is worth more than a description.

## Pull requests

- **Add a test.** Every feature and fix in this repository has one. Tests live beside the others in
  `tests/AspNetCore.Dispatcher.Tests`, grouped by feature.
- **Isolate handlers in tests.** Scanning sees the whole test assembly, so declare messages and
  handlers as nested types and build the provider with the `TestServices` helper, which filters
  scanning down to the declaring test class.
- **Match the surrounding style.** Single-line comments only, and only where the code does not
  already say it. Explicit `using` directives, no implicit usings.
- **Mind the public surface.** Adding a member to a public interface breaks every implementor.
  Prefer a new type or an optional parameter, and say so in the PR description.
- **Keep `netstandard2.0` compiling.** It is easy to reach for an API that only exists on modern
  .NET. `dotnet build` covers all three targets, so the build will tell you.
- **Update the docs.** A change in behavior means a change in `docs/`, and an entry under
  `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md).

## Design principles

A few things the library deliberately does, worth knowing before proposing a change:

- **Handlers are discovered, behaviors are declared.** Scanning registers handlers automatically; a
  pipeline behavior applies to every request, so turning one on stays an explicit call.
- **`TryAdd` everywhere.** A consumer's own registration always wins over the scan.
- **No dependency beyond `Microsoft.Extensions.DependencyInjection.Abstractions`.** A proposal that
  adds a dependency needs a strong reason.
- **Reflection at startup, not per call.** Per-request work is container resolution and delegate
  composition. See [docs/architecture.md](docs/architecture.md).

## Releasing

Maintainer-only; see [PUBLISHING.md](PUBLISHING.md).
