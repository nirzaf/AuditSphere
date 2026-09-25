# AuditSphereOps — Current Architecture Authority

**Status: CURRENT.** This document and [`AGENTS.md`](../../AGENTS.md) are the architectural
implementation authority for this repository. Requirement sources preserved under
`docs/AuditSphere_R2R_Task_Breakdown/source/` are `HISTORICAL_SOURCE` and never override this
document. Verified project state (test counts, verified SHA, migration count, external blockers)
is recorded only in [`docs/execution/status.json`](../execution/status.json).

## Document authority vocabulary

Every architecture-sensitive document carries one status:

| Status | Meaning |
| --- | --- |
| `CURRENT` | The implemented architecture and its binding rules. Authoritative. |
| `APPROVED` | A human decision that binds future work (for example an accepted methodology). |
| `PROPOSED` | Not implemented; requires a reviewed change before being cited as available. |
| `HISTORICAL_SOURCE` | Preserved original requirements or blueprint text. Not implementation authority. |
| `SUPERSEDED` | Replaced by a later decision; kept for lineage only. |

When preserved source text disagrees with this document (for example "add MediatR facades"),
this document wins and the source text remains a historical requirement record.

## The architecture (unchanged)

```text
                    AuditSphereOps
                          │
              ┌───────────┴───────────┐
              │                       │
             Web                    Worker
              │                       │
              └──────────┬────────────┘
                         │
                    Application
                 capability services
                 queries / calculators
                         │
                ┌────────┴────────┐
                │                 │
             Domain        Infrastructure
                               │
                          PostgreSQL
```

- **One modular monolith**, five projects: `Domain`, `Application`, `Infrastructure`, `Web`,
  `Worker`. No microservices, no message broker, no second ERP.
- **Commands and queries are static capability services** returning `CommandResult` /
  `CommandResult<T>`. There is no MediatR/Wolverine handler layer; neither package is pinned.
  This is a recorded variation from R2R-ADR-02 (see
  `docs/AuditSphere_R2R_Task_Breakdown/reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md` §2.3.1).
- **One `AuditSphereDbContext`**, physically split into capability partial files
  (`AuditSphereDbContext.<Module>.cs`), with `OnModelCreating` calling `Configure<Module>(b)`
  methods. No DbContext-per-module.
- **Pure calculators** (`LineTranslationCalculator`, `ConsolidationCalculator`,
  `CurrencyTranslationCalculator`, `TrialBalanceCalculator`, advanced-consolidation
  calculators) contain no EF Core, network, clock or random-ID dependencies.
- **Durable work** (GL completeness, package build/render) runs through the local durable
  operation infrastructure with revision fencing and idempotent retries.

## Physical organization rules

1. **Large classes are split into capability-focused partial files** without changing public
   APIs: `ConsolidationService.<Capability>.cs`, `ClientAccountingService.<Capability>.cs`,
   `AccountingAnalysisService.<Capability>.cs`, `AuditFieldworkService.<Capability>.cs`,
   `FinancialStatementService.<Capability>.cs`, `AuditSphereDbContext.<Module>.cs`, and
   `ClientAccountingTests.<Capability>.cs`. Keep new operations in the matching capability file.
2. **File names are business-semantic.** Do not introduce generic names (`Class1.cs`,
   `UnitTest1.cs`, `R2RPass*Tests.cs`); a file name should tell an agent where to look.
3. **One fact, one authority.** Volatile project state (test counts, verified SHA, migration
   count, external blockers, verification dates) lives in `docs/execution/status.json` only.
   Do not copy exact volatile numbers into README or narrative docs.
4. **Web composes Application.** Razor components may compose Application commands/queries and
   scoped reads; do not add new business-state mutations directly through `DbContext`. When an
   existing page is substantially modified, move complex reads or business operations into a
   named Application query/service when that reduces page responsibility. Do not bulk-refactor
   unaffected pages.
5. **Authorization is scope-checked and records are append-only.** Every command, query and
   queue enforces explicit `RoleGrant` scope; sealed datasets, approved mappings, applied
   journals, issued packages and review decisions change only through new revisions. History
   must never gain an update path.

## What this architecture explicitly is not

Not introduced, and not to be introduced: MediatR, Wolverine, MassTransit, Kafka/RabbitMQ,
microservices, a repository pattern wrapping every EF operation, AutoMapper, generic
UnitOfWork abstractions, event sourcing, or a `Features/` vertical-slice rewrite. None of
these solve the navigability problem the physical splits solve.

## Where to look first

Use [`auditsphere-architecture-code-map.md`](auditsphere-architecture-code-map.md) to find the Domain/Application/Infrastructure/Web/tests files
for a business capability before searching the whole repository.
