# Baseline, architecture transition and decisions







[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Unchanged source blueprint](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md)







**Status:** HISTORICAL_REFERENCE



**Authority:** Preserved contract excerpts and design research. Does not override `AGENTS.md` or `docs/architecture/auditsphere-architecture-current-architecture.md`. Current implementation uses static capability services and modular monolith architecture (see [`auditsphere-r2r-reference-baseline-architecture-and-adrs.md` §2.3.1](#section-2-3-1)).







**Reference, not an execution task.** The excerpts below retain the source contract. Baseline facts and research are historical to the supplied document; this breakdown does not re-audit the repository or re-verify external standards.







<!-- SOURCE-LINES: 79-149 -->



## 2. Verified baseline and architecture transition







<a id="section-2-1"></a>



### 2.1 What already exists







| Area | EXISTING source | Reuse/extension rule |



|---|---|---|



| Financial kernel | `Domain/Shared/Kernel.cs`: `CommandResult<T>`, error catalogue, `MoneyPolicy`, `Hashing` | Preserve current wire error values and six-decimal, midpoint-to-even normalization. |



| Accounting context | `Domain/Accounting/ClientAccounting.cs`: `ClientAccountingProfile`, `ClientReportingPeriod`, `ClientReportingBook`, `ClientChartVersion`, `ClientAccount`, `SourceAccountAlias`, taxonomy versions/nodes | Add immutable context pins and missing invariants without duplicating clients/entities/books. |



| Sources and mapping | `Domain/Accounting/Accounting.cs`: `TrialBalanceDataset`, `TrialBalanceRow`, `TrialBalanceImportBatch`, `MappingVersion`, `MappingAllocation` | Retain immutable source membership, raw/normalized digests and mapping identity. |



| Adjustment lineage | `AdjustmentJournal`, `AdjustmentLine`, `AdjustmentJournalManagementDecision`, `AdjustmentPlan`, `AdjustedTrialBalanceSnapshot` | Preserve exact base/source/reflection semantics. Legacy `Posted` is not proof of external ledger posting. |



| Statements/packages | `FinancialPackage`, package lines/validations/cash-flow/disclosure/equity/note records, `FinancialPackageArtifact`, package-review decisions | Separate preparation and assembly responsibilities by references, not duplicate arithmetic engines. |



| Application | `ClientAccountingService`, `AdjustmentJournalService`, `AdjustmentPlanService`, `FinancialStatementService`, `AccountingAnalysisService`, `ConsolidationService`, existing currency/consolidation calculators | Add MediatR facades first; extract transaction-safe cores one vertical slice at a time. |



| Group reporting | `ClientGroup`, membership/grants; current consolidation request/component/journal/report contracts and calculators | Preserve component package identity, group scope and approved bounded methods. |



| Persistence | `AuditSphereDbContext`; existing application DB interfaces; scoped DbSets, decimal(19,6), PostgreSQL invariants | One authoritative database model; additive migrations, no alternate R2R database. |



| Presentation | Blazor Interactive Server accounting, mapping, package, consolidation and review pages | Extend existing pages/routes before adding a parallel workspace. New UI must use CQRS DTOs. |







Sources: [R1](auditsphere-r2r-reference-standards-and-source-register.md#source-r1)–[R9](auditsphere-r2r-reference-standards-and-source-register.md#source-r9). Presence does not prove every new criterion is satisfied; the first delivery gate is a current-code-to-contract inventory.







<a id="section-2-2"></a>



### 2.2 Target stack versus current stack







The repository centrally pins EF Core 10.0.12, Npgsql EF 10.0.0, FluentValidation 12.1.1, CsvHelper 33.1.0, OpenXML 3.5.1, PDFsharp-MigraDoc 6.2.4, xUnit and Playwright. **MediatR and bUnit are not in the inspected central package list.** Add them as approved dependencies, not as though already configured. Do not upgrade unrelated libraries as part of R2R. [R3](auditsphere-r2r-reference-standards-and-source-register.md#source-r3)







Use the user's requested target:







```text



Razor page / reusable components



    → ISender.Send(command or query)



    → actor resolution + validation + scoped authorization



    → handler / existing transactional accounting service



    → domain operations + PostgreSQL persistence + durable operation record



    → mapped, immutable response DTO



```







Domain has no MediatR, EF, FluentValidation, ASP.NET, OpenXML or PDF dependency. Application owns request/response DTOs, validators, handlers, orchestration and interfaces. Infrastructure implements EF mappings, repository/transaction abstractions and artifact access. Web/Worker compose approved dependencies. Pure financial calculations may remain in the current Application calculator files initially; purity matters more than moving files.







**Do not perform a whole-application “clean architecture” rewrite.** Existing Application interfaces expose EF concepts. Keep that compatibility boundary during migration; new domain aggregates stay persistence-agnostic and new UI stops depending on tracked entities. Explicit DTO mapping functions are sufficient; no AutoMapper dependency is required.







<a id="section-2-3"></a>



### 2.3 MediatR migration and transaction ownership







MediatR provides in-process request/notification dispatch; it is not a persistent message broker. Its selected release and licensing configuration require approval before package changes. bUnit must likewise be selected and pinned for the current .NET/xUnit setup. [T1](auditsphere-r2r-reference-standards-and-source-register.md#source-t1)







Implement a request-to-transaction-owner registry:







| Request implementation | Transaction owner | Forbidden |



|---|---|---|



| Compatibility handler delegates existing service | Existing service | Wrapping `BeginTransactionAsync` around a service that opens another transaction |



| Migrated transactional core accepts a provided unit of work | One command pipeline/unit of work | Inner handlers independently committing or starting nested transactions |



| Pure read handler | Read-only per-operation context; explicit consistent-read transaction where necessary | Saving tracked UI entities |



| Long-running import/render/group operation | Existing durable worker; short staging and publication transactions | One database transaction held during upload, rendering or human review |







Current `ClientAccountingService.RollForwardPeriodAsync` and consolidation membership operations already own transactions. Introduce facade requests without changing those semantics; then migrate each core and update the registry/test. An architecture test must fail if a command has two or no declared transaction owners. [R7](auditsphere-r2r-reference-standards-and-source-register.md#source-r7)[R9](auditsphere-r2r-reference-standards-and-source-register.md#source-r9)







Pipeline order: correlation/diagnostics → authenticated actor resolution → structural `ValidateAsync` → coarse capability check → appropriate transaction owner → fresh in-transaction scope/state/version checks → domain change → atomic evidence/outbox write → commit → response. Database-dependent validation is advisory until rechecked inside the authoritative transaction. Run validators sharing a context sequentially, not concurrently.







<a id="section-2-3-1"></a>



### 2.3.1 Recorded architecture variation: static application services, no bUnit project







Recorded 2026-09-25 as the decision against [R2R-ADR-02](#section-2-4). The shipped application layer resolves commands and queries through static service classes returning `CommandResult`/`CommandResult<T>` (for example `AuditSphereOps.Application.Accounting.ClientAccountingService`, `CurrencyTranslationService`, `ConsolidationService`), not MediatR handlers, and the solution has no component-test project: `Directory.Packages.props` pins neither MediatR nor bUnit. Component behaviour is verified by the PostgreSQL-backed xUnit suites (`tests/AuditSphereOps.Domain.Tests`) and the Playwright journeys (`tests/AuditSphereOps.E2E.Tests`).







This is an explicit recorded variation, not a silent omission. The transaction-ownership registry rules in §2.3 apply unchanged to these static services: each command that writes opens exactly one transaction and no nested service opens another, and long-running work stays in the local durable operation workers. Introducing MediatR or bUnit later is a separate reviewed package change under R2R-ADR-02, not a prerequisite for the delivered slices. Task cards keep the original `XxxCommand`/`XxxQuery` contract rows as preserved source wording; each implementation maps those requests to these services by behaviour and evidence.







<a id="section-2-4"></a>



### 2.4 Required architecture decisions before feature construction







| ADR | Decision to record | Proposed position |



|---|---|---|



| R2R-ADR-01 | Module ownership and prototype-to-production translation | This document's seven-module scope; no production React replacement. |



| R2R-ADR-02 | MediatR/bUnit versions, licence and transaction ownership | Add pinned compatible dependencies; facade then migrate; no generic nested transaction wrapper. |



| R2R-ADR-03 | Monetary/FX precision and rounding | Keep money numeric(19,6), `decimal`, ToEven; explicit rate precision and numeric bounds. |



| R2R-ADR-04 | Framework editions and supported methods | Versioned approved policy packs; IFRS-era choices explicit; no universal compliance claim. |



| R2R-ADR-05 | Context identity, dependency locks and stale propagation | Existing firm/client generation fences retained; precise dependency manifests layered on top. |



| R2R-ADR-06 | Source and artifact storage/retention | Reuse approved current stores, exact bytes/hashes, no browser business authority or new provider. |



| R2R-ADR-07 | Presentation/package split | One calculation result owner; independently versioned layout/assembly references; no second balance engine. |



| R2R-ADR-08 | Release/records exclusions | Remove Purview/eSignature dependencies from the approved target profile without weakening ordinary evidence gates. |



| R2R-ADR-09 | Consolidation methods | Wholly-owned baseline first; preserve advanced code, activate only approved method-specific fixtures. |



| R2R-ADR-10 | Capability-to-role mappings | Explicit professional grants and person-based independence; no implicit Partner promotion on group creation. |







The inspected `CreateGroupAsync` currently adds a Partner group grant for its creator. Characterize and explicitly reconcile that behavior against the required separation of configuration and professional approval; do not carry it forward unnoticed. [R9](auditsphere-r2r-reference-standards-and-source-register.md#source-r9)
