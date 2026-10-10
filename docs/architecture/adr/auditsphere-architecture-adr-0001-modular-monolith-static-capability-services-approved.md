# ADR-0001: One modular monolith with static capability services

**Status: APPROVED** (owner-delegated approval recorded 2026-10-10) · Date recorded: 2026-10-08 · Decider: repository owner (authority delegated 2026-10-10)

## Context
R2R-ADR-02 planned MediatR facades and bUnit component tests. The shipped code instead resolves every command and query through static service classes that return `CommandResult` / `CommandResult<T>` (for example `ClientAccountingService`, `ConsolidationService`, `MaterialityEngineService`). The variation was recorded on 2026-09-25 in §2.3.1 of the R2R baseline document and in the current architecture document.

## Decision
- One deployable modular monolith: Domain, Application, Infrastructure, Api, Worker, plus the Web rollback host and the Angular UI project.
- Commands and queries are static methods on capability services, taking `IAuditSphereDbContext`, `ActorContext` and a request record, returning `CommandResult`.
- Each writing command opens exactly one transaction; no nested service opens another. Long-running work goes to local durable operations.
- One `AuditSphereDbContext`, split into `AuditSphereDbContext.<Module>.cs` partials.

## Alternatives considered
- MediatR or Wolverine handler layer: adds indirection without solving navigability; licence and version risk.
- Microservices or DbContext-per-module: breaks single-transaction guarantees the financial gates depend on.
- `Features/` vertical-slice rewrite: large churn for no behavioural gain.

## Consequences
- Agents find behaviour by capability file name (`<Service>.<Capability>.cs`) via the code map.
- No mediator pipeline: cross-cutting checks (scope, session epoch) must be called explicitly in each command; missing a call is the main defect risk, so reviews look for it.
- Introducing MediatR, Wolverine, MassTransit, AutoMapper or generic repositories requires a new ADR, not a feature task.
