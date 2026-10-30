# Publishing

How a release of `AspNetCore.Dispatcher` is cut and pushed to [nuget.org](https://www.nuget.org).

## One-time setup

### 1. Create the GitHub repository

```shell
cd F:/Projects/Personal/AspNetCore.Dispatcher

git add .
git commit -m "Initial commit"
git branch -M main
git push -u origin main
```

The remote `origin` is already set to
`https://github.com/nahidhasanswe/AspNetCore.Dispatcher.git`. Create the repository on GitHub first
(empty, no README or licence — this repo has both).

### 2. Store your nuget.org username

Repository → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**:

- **Name**: `NUGET_USER`
- **Value**: your nuget.org **username** (the profile name, *not* your email address)

This is the only value the repository stores, and it is not a credential — there is nothing to
rotate and nothing that expires.

### 3. Create a trusted publishing policy

Publishing uses [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
rather than a long-lived API key. The workflow asks GitHub for a short-lived OIDC token, nuget.org
validates it against a policy you register here, and hands back a temporary API key valid for one
hour. No key is ever stored.

Sign in at [nuget.org](https://www.nuget.org) → click your username → **Trusted Publishing** → add
a policy:

| Field | Value |
|---|---|
| Policy name | `AspNet.Core.Dispatcher release` |
| Package owner | your individual account |
| Repository Owner | `nahidhasanswe` |
| Repository | `AspNetCore.Dispatcher` |
| Workflow File | `release.yml` |
| Environment | *leave empty* |
| Scopes | **Push new packages and package versions** |
| Glob pattern | `AspNet.Core.Dispatcher*` |

Three details decide whether this works:

- **Workflow File is the file name only.** Enter `release.yml`, never
  `.github/workflows/release.yml`. This is the most common reason a policy fails to match.
- **The scope must allow new packages.** Until the first version is live on nuget.org, a policy
  scoped only to new *versions* cannot publish anything.
- **Leave Environment empty.** The release job does not use a GitHub environment, and a policy that
  requires one will not match.

The policy is bound to that workflow file name. If you ever rename `release.yml`, edit the policy to
match or publishing stops working.

> **Private repositories**: a new policy starts *temporarily active for 7 days*. nuget.org needs
> GitHub's immutable repository and owner IDs to lock the policy down, and it only receives those
> from a real token exchange. The first successful run makes the policy permanent. If the window
> lapses first, restart it from the same page — you can do that at any time, even after it expires.

### 4. Rehearse before tagging

A mistyped policy field is the usual first-run failure, so prove the setup without publishing
anything. Push this repo to `main`, then: **Actions** → **Release** → **Run workflow**.

A manual run builds, tests, packs, and performs the real OIDC exchange, then stops — the push and
GitHub-release steps only run for a `v*` tag. A green *Authenticate to nuget.org* step means the
policy, the secret and the workflow permission are all correct.

## Cutting a release

### 1. Set the version

Version lives in `src/AspNetCore.Dispatcher/AspNetCore.Dispatcher.csproj`:

```xml
<VersionPrefix>1.0.0</VersionPrefix>
<VersionSuffix>preview.1</VersionSuffix>
```

| Release | `VersionPrefix` | `VersionSuffix` | Produces |
|---|---|---|---|
| Preview | `1.0.0` | `preview.3` | `1.0.0-preview.3` |
| Release candidate | `1.0.0` | `rc.1` | `1.0.0-rc.1` |
| Stable | `1.0.0` | *(empty)* | `1.0.0` |
| Patch | `1.0.1` | *(empty)* | `1.0.1` |

Clear `VersionSuffix` entirely for a stable release:

```xml
<VersionSuffix></VersionSuffix>
```

### 2. Update the changelog

Move the entries under `## [Unreleased]` into a new version heading in
[CHANGELOG.md](CHANGELOG.md), date it, and update the link definitions at the bottom.

### 3. Verify locally

```shell
dotnet clean
dotnet build -c Release
dotnet test -c Release
dotnet pack src/AspNetCore.Dispatcher/AspNetCore.Dispatcher.csproj -c Release -o ./artifacts
```

Inspect what you are about to publish:

```shell
# Contents — expect lib/netstandard2.0, lib/net8.0, lib/net9.0, README.md, LICENSE
unzip -l ./artifacts/AspNet.Core.Dispatcher.1.0.0-preview.1.nupkg

# Metadata
unzip -p ./artifacts/AspNet.Core.Dispatcher.1.0.0-preview.1.nupkg AspNet.Core.Dispatcher.nuspec
```

Checklist before pushing — a published version can never be replaced:

- [ ] `dotnet test` passes
- [ ] The version is what you intend, in the file and in the `.nupkg` name
- [ ] A `.snupkg` was produced alongside the `.nupkg`
- [ ] `lib/` has all three target frameworks, each with its `.xml` documentation file
- [ ] `README.md` and `LICENSE` are in the package root
- [ ] The README renders correctly — paste it into a markdown preview; every link in it must be
      absolute, since nuget.org cannot resolve relative paths
- [ ] `CHANGELOG.md` describes this version
- [ ] The trusted publishing policy shows as **active** on nuget.org

### 4. Tag and push

```shell
git tag v1.0.0-preview.1
git push origin v1.0.0-preview.1
```

The tag triggers `.github/workflows/release.yml`, which verifies the tag against the project
version, builds, tests, packs, exchanges an OIDC token for a one-hour nuget.org key, pushes, and
opens a GitHub release.

The push is strict on purpose. If nuget.org already has that version the run **fails**, because a
release that published nothing must not report success. Versions are immutable once claimed, so the
fix is always to bump the version and tag again — never to re-run the release.

A version can be claimed without being visible: nuget.org holds a newly pushed package in
*Validating* for a few minutes, during which the public package page and search both return nothing
while a second push of it is already rejected as a duplicate. Your own packages are listed either
way under **Manage Packages**.

### 5. Confirm

The package appears at
`https://www.nuget.org/packages/AspNet.Core.Dispatcher` within a few minutes, and becomes
installable once indexing finishes — usually under 15 minutes, occasionally up to an hour.

```shell
dotnet new console -o /tmp/smoke && cd /tmp/smoke
dotnet add package AspNet.Core.Dispatcher --prerelease
```

## Publishing by hand

Trusted publishing works from the workflow only — the OIDC token comes from the GitHub Actions job,
so there is nothing to authenticate with locally. Publish through the workflow.

If you are ever genuinely stuck (Actions outage, say), the escape hatch is a manual API key:
[API keys](https://www.nuget.org/account/apikeys) → create one scoped to
*Push new packages and package versions* with glob `AspNet.Core.Dispatcher*`, set the shortest
expiry offered, push, then **delete the key immediately**.

```shell
dotnet pack src/AspNetCore.Dispatcher/AspNetCore.Dispatcher.csproj -c Release -o ./artifacts

read -s NUGET_API_KEY && export NUGET_API_KEY   # keeps it out of your shell history

dotnet nuget push "./artifacts/AspNet.Core.Dispatcher.1.0.0-preview.1.nupkg" \
  --api-key "$NUGET_API_KEY" \
  --source https://api.nuget.org/v3/index.json
```

`dotnet nuget push` pushes the matching `.snupkg` automatically when it sits beside the `.nupkg`.

Resist `--skip-duplicate` here, for the same reason the workflow does not use it: it reports success
when the version was already claimed and nothing was uploaded.

Do not store such a key as a repository secret — that would undo the point of the policy.

## Overriding the version at pack time

Handy for CI builds that should not commit a version change:

```shell
dotnet pack -c Release -p:VersionSuffix=preview.3
dotnet pack -c Release -p:Version=1.0.0-ci.$(git rev-parse --short HEAD)
```

`-p:Version` overrides both prefix and suffix.

## Versioning policy

[Semantic Versioning](https://semver.org):

- **Patch** (`1.0.1`) — bug fixes, no API change.
- **Minor** (`1.1.0`) — additions that existing code keeps compiling against.
- **Major** (`2.0.0`) — anything that breaks a consumer, including a new minimum target framework.

While on `1.0.0-preview.*` the public API may still change between previews; say so in the
changelog when it does.

Adding a member to a public interface is a breaking change for anyone who implements it. Prefer a
new interface or a default implementation over editing `IDispatcher`, `ISender`, `IPublisher` or any
handler contract.

## After publishing

A published version is permanent. You cannot replace, re-upload or delete it — the most you can do
is **unlist** it (nuget.org → the package → *Manage package* → *Listing*), which hides it from
search while leaving it restorable for anyone who already depends on it. Unlist a release that
turns out to be broken, then publish a fixed patch version.

Sign in to nuget.org after the first push to:

- Confirm the README, icon and metadata render as expected.
- Consider requesting the `AspNetCore.Dispatcher` **package ID prefix reservation** if you plan a
  family of packages. nuget.org only grants reservations for prefixes you own, and the bare
  `AspNetCore.` prefix will not be granted — ask for `AspNetCore.Dispatcher.*`.

## Adding a package icon

Optional, and a visible improvement on the nuget.org listing. Add a 128×128 PNG and two lines:

```xml
<PackageIcon>icon.png</PackageIcon>
```

```xml
<None Include="../../icon.png" Pack="true" PackagePath="/" />
```

## Troubleshooting

| Symptom | Cause |
|---|---|
| Login step: `403`, or no matching policy | A policy field does not match the workflow. Usually **Workflow File** was entered as `.github/workflows/release.yml` instead of `release.yml`, or **Environment** is set while the job uses none. Repository owner and name are case-insensitive, so those are rarely the problem. |
| Login step: no OIDC token available | `id-token: write` is missing from the workflow's `permissions` block. Note that listing any permission drops every one you did not list, so `contents: write` must stay alongside it. |
| Login step: `400` about the user | `NUGET_USER` holds an email address. It must be the nuget.org profile name. |
| Push rejected, package not found or forbidden | The policy's scope omits *push new packages*, or its glob does not match `AspNetCore.Dispatcher`. |
| Policy shows as inactive | The 7-day pending window lapsed before a successful run (private repositories only). Restart it from the Trusted Publishing page and re-run. |
| Push fails after a long job | The temporary key expired; it is valid for one hour. Keep the login step immediately before the push. |
| `429` from the login step | nuget.org issues at most one key per 30 seconds per user. Wait and re-run. |
| `409 Conflict` / "already exists" on push | That version is already claimed — including by a package still validating, or one later unlisted or deleted. The run fails on purpose: nothing was published. Bump the version and tag again. |
| Workflow green but the package is nowhere on nuget.org | It is probably still *Validating* — the public page and search stay empty for a few minutes after a push. Check **Manage Packages** on your account, which shows it immediately. |
| Package installs but the README is blank on nuget.org | `README.md` was not packed, or `PackageReadmeFile` does not match the packed path. |
| Images missing from the README on nuget.org | nuget.org only renders images from allow-listed hosts. `img.shields.io` and `raw.githubusercontent.com` are fine. |
| Symbols not resolving in a debugger | The `.snupkg` was not pushed, or the consumer has not enabled the NuGet symbol server. |
| `warning NU5048` or similar on pack | Metadata mismatch; read the message, it names the property. |
