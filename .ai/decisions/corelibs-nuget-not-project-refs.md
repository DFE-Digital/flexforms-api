# Consume DfE.CoreLibs as NuGet packages

- **Status:** Accepted
- **Provenance:** **Verified** in README “CoreLibs packages”. **Historical** pain from mixed ProjectReference graphs.

## Context

CoreLibs packages are independently GitVersioned. A local project build emits assembly version `1.0.0.0`. Downstream packages (e.g. Notifications) bind to a published Contracts version.

## Decision

FlexForms references nuget.org packages. Optional sibling project references are a **temporary** local trick and must not land on `main`.

## Consequences

Contract changes require a CoreLibs publish before API/Web/Prism can consume them. That latency is accepted to keep startup stable.
