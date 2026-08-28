# Versioning (GitVersion)

Application version is calculated from **Git tags** and **commits** — do not edit `<Version>` in the Web/API `.csproj` files.

## Tags and releases

- Tags use the format **`v2.1.4`** (see `tag-prefix` in `GitVersion.yml`).
- On every push to **`main`**, the **Release** workflow:
  1. Calculates the semver with GitVersion
  2. Creates tag `v{x.y.z}` if it does not already exist
  3. Creates a GitHub release titled `{x.y.z} - {commit subject}`

## Bumping the version

On `main`, each merge without a matching tag increments the **patch** by default.

Use commit message hints to bump minor or major:

| Intent | Commit message contains |
|--------|-------------------------|
| Patch (default) | normal merge / fix commits |
| Minor | `+semver: minor` or `+semver: feature` |
| Major | `+semver: major` or `+semver: breaking` |

Example:

```text
Add contributor lookup improvements +semver: minor

(%release-note:Admins can look up contributors by email.%)
```

## Release notes

Add notes to the merge commit body using the same pattern as Api.Client packs:

```text
(%release-note:Describe what changed for operators and developers.%)
```

If omitted, the commit body (or “No release notes provided.”) is used.

## Local builds

- Local `dotnet build` uses **`0.0.0-local`** unless you pass `-p:Version=x.y.z`.
- Docker builds use `APP_VERSION` (CI sets this from the GitVersion workflow). Local docker defaults to `0.0.0-local`.

## First-time setup

Versioning is driven by existing **`v*`** git tags. Tag the first release manually if needed (e.g. `v2.1.4`), then merges to `main` increment patch automatically.
