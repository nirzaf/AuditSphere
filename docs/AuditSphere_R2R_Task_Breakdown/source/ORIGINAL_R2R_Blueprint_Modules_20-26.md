# STEAuditSphere — Record-to-Report Implementation Blueprint
## Modules 20–26 · Principal architecture review draft

**Document ID:** STE-R2R-ARCH-001  
**Version:** 1.0 — proposed for review, not approved for execution  
**Prepared:** 24 September 2026  
**Production repository:** `nirzaf/AuditSphere`  
**Solution:** `AuditSphereOps.slnx`  
**Inspected baseline:** `master@ba1a3ec23335b40667b679e4f0cd5f66b9e723b9`  
**Baseline commit time:** 24 September 2026, 07:42:51 UTC  
**Functional requirements:** supplied `Progress_Tracker.md`, primarily VP-034–VP-046; the latest user directive governs the production architecture.

> This is a design and execution contract, not implementation code, a current completion report, or a certification of accounting compliance. New type, command, component, migration and test names below are **proposed** unless explicitly marked **EXISTING**. Existing accounting behavior must be characterized and reused, not rebuilt from a prototype. No application test, migration, tenant operation or repository write was performed while preparing this blueprint.

## Navigation

1. [Scope and source precedence](#scope)
2. [Verified repository baseline and architecture transition](#baseline)
3. [Standards and comparable-product research](#research)
4. [Shared domain, persistence and CQRS contract](#shared)
5. [End-to-end lifecycle and dependency contract](#lifecycle)
6. [Module 20 — Accounting](#module-20)
7. [Module 21 — Trial Balance & GL](#module-21)
8. [Module 22 — Adjustments & Journals](#module-22)
9. [Module 23 — Reconciliations](#module-23)
10. [Module 24 — Financial Statements](#module-24)
11. [Module 25 — Financial Packages](#module-25)
12. [Module 26 — Consolidation](#module-26)
13. [Sequential delivery and acceptance gates](#execution)
14. [Integrated numerical fixtures and failure journeys](#fixtures)
15. [Production operations, approvals and agent handoff](#handoff)
16. [Source register](#sources)

<a id="scope"></a>
## 1. Scope and source precedence

### 1.1 In scope

Build the **production R2R subsystem** for the seven named modules. Account mapping is owned by Module 21 in this blueprint, consumes Module 20's approved chart/taxonomy identities, and supplies Module 24. This is an explicit ownership assignment; the earlier tracker associated VP-037 with Module 20. It does not introduce a second mapping engine.

The production application remains an **import-first accounting preparation and reporting platform**: receive client source books, validate them, record proposed corrections and reporting adjustments, reconcile, produce statements, assemble reviewed packages, and consolidate eligible component results. It does not become the source operational general ledger merely because it imports GL data.

Modules outside 20–26 supply defined contracts: client/engagement/acceptance, identity and grants, documents/PBC/evidence, audit findings/review, completion/release, archive, and firm administration. They are dependencies, not seven additional implementations hidden inside accounting.

Consolidation is a configurable capability. A standalone entity must complete R2R without a group. Once a group-reporting profile is selected, the relevant component, rate, elimination and review gates are mandatory. Optional capability does not mean optional controls within an enabled capability.

### 1.2 Explicit exclusions and preserved boundaries

No AI, semantic search, native mobile application, online payments, eSignature provider, tax-return/payroll service, recurring business tasks, automatic reminders, automatic journal approval, automatic intercompany matching, bank-feed connector, or non-M365 business integration. No Microsoft Purview integration, certification project or bespoke encryption-management project. Preserve ordinary authentication, authorization, platform protection, SHA-256 identities and immutable evidence.

Imported salary/tax accounts and professionally prepared financial-statement tax balances are not payroll execution or tax-return preparation. Preserve their data. Do not fabricate tax balances; unsupported accounting treatment remains an explicit policy/input dependency.

Keep three ledgers of responsibility distinct:

| Boundary | Owner | Must never be used as a substitute |
|---|---|---|
| Firm's own commercial books | Existing Practice/firm-ledger services | Client TB, client reporting journals or group elimination ledger |
| Client source and reporting adjustments | Modules 20–25 | Posting back to a client's external ERP or bank |
| Group-only calculations and eliminations | Module 26 | Mutating a component package or firm invoice/ledger |

The supplied tracker describes browser simulation. Retain its **business requirements**, but replace localStorage, synthetic persona authority and browser-only artifact persistence with production identity, PostgreSQL transactions and approved storage. A successful prototype test is not a production test result. [F1][R2]

### 1.3 Approval and uncertainty boundaries

An implementation plan cannot establish defect-free or “100% interoperable” software by declaration. Here, full acceptance means **every approved requirement has executable evidence at one identified build**, all required cross-module/rework cases pass, and no unresolved blocking dependency is hidden. The final gate is measurable; the guarantee is not assumed.

The firm's accounting/methodology owner must approve the applicable framework, edition, jurisdictional overlay, accounting treatments, disclosures, materiality policy and golden financial fixtures. Software checks must not choose these professional conclusions. This document does not assert that all clients use full IFRS or that every industry/accounting method is supported.

### 1.4 Evidence labels

- **EXISTING:** symbol or behavior directly inspected in the pinned source.
- **EXTEND:** retain that identity/table/service and add the stated contract.
- **NEW:** proposed only; search the current repository for an equivalent before creation.
- **DECISION:** architecture or methodology approval required; a coding agent cannot silently decide.

Resolve instructions in this order: approved scope decision for this implementation → current repository instructions and preserved invariants → exact business criteria → approved standards/policy edition → this proposed design. A conflict stops the affected slice for a recorded decision; it never authorizes deleting evidence or bypassing a guard.

<a id="baseline"></a>
## 2. Verified baseline and architecture transition

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

Sources: [R1]–[R9]. Presence does not prove every new criterion is satisfied; the first delivery gate is a current-code-to-contract inventory.

### 2.2 Target stack versus current stack

The repository centrally pins EF Core 10.0.12, Npgsql EF 10.0.0, FluentValidation 12.1.1, CsvHelper 33.1.0, OpenXML 3.5.1, PDFsharp-MigraDoc 6.2.4, xUnit and Playwright. **MediatR and bUnit are not in the inspected central package list.** Add them as approved dependencies, not as though already configured. Do not upgrade unrelated libraries as part of R2R. [R3]

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

### 2.3 MediatR migration and transaction ownership

MediatR provides in-process request/notification dispatch; it is not a persistent message broker. Its selected release and licensing configuration require approval before package changes. bUnit must likewise be selected and pinned for the current .NET/xUnit setup. [T1]

Implement a request-to-transaction-owner registry:

| Request implementation | Transaction owner | Forbidden |
|---|---|---|
| Compatibility handler delegates existing service | Existing service | Wrapping `BeginTransactionAsync` around a service that opens another transaction |
| Migrated transactional core accepts a provided unit of work | One command pipeline/unit of work | Inner handlers independently committing or starting nested transactions |
| Pure read handler | Read-only per-operation context; explicit consistent-read transaction where necessary | Saving tracked UI entities |
| Long-running import/render/group operation | Existing durable worker; short staging and publication transactions | One database transaction held during upload, rendering or human review |

Current `ClientAccountingService.RollForwardPeriodAsync` and consolidation membership operations already own transactions. Introduce facade requests without changing those semantics; then migrate each core and update the registry/test. An architecture test must fail if a command has two or no declared transaction owners. [R7][R9]

Pipeline order: correlation/diagnostics → authenticated actor resolution → structural `ValidateAsync` → coarse capability check → appropriate transaction owner → fresh in-transaction scope/state/version checks → domain change → atomic evidence/outbox write → commit → response. Database-dependent validation is advisory until rechecked inside the authoritative transaction. Run validators sharing a context sequentially, not concurrently.

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

The inspected `CreateGroupAsync` currently adds a Partner group grant for its creator. Characterize and explicitly reconcile that behavior against the required separation of configuration and professional approval; do not carry it forward unnoticed. [R9]

<a id="research"></a>
## 3. Standards and comparable-product research

### 3.1 Standards-informed design, with edition control

| Primary source | Relevant principle | Proposed implementation consequence |
|---|---|---|
| IFRS 18 official overview [S1] | Effective for annual periods beginning on/after 1 January 2027, early adoption permitted; replaces IAS 1 and changes presentation. | `ReportingPolicyVersion` pins framework/edition/effective period/early-adoption decision. Old reports never silently use a new layout or subtotal policy. |
| IAS 7 official overview [S2] | Cash-flow classification, cash reconciliation and separately disclosed noncash activity; IFRS 18 consequential changes matter. | Explicit cash/noncash movement inputs and edition-specific indirect starting point/classification; no cash-flow plug from two TB totals. |
| IAS 8 official overview [S3] | Distinguish prior-period errors, accounting-policy changes and estimate changes. | Typed change reason, affected periods and approved treatment; preserve as-issued and restated comparatives separately. |
| IAS 21 [S4] | Distinguish functional/presentation currency, monetary remeasurement and foreign-operation translation. | Separate rate-rule profiles, historical equity/movement lineage and explained translation reserve. Missing rates cannot become 1. |
| IFRS 10 [S5] | Consolidation follows control; combine like items and eliminate intragroup effects, including investment/equity. | Evidence-based perimeter and compatible component policies; simple balance addition is not complete consolidation. |
| IAS 1 and IAS 10 overviews [S6][S7] | Complete statement set/comparatives and classification of subsequent events. | Include OCI where applicable, opening comparative position where required, authorization date and human subsequent-event disposition. |

The detailed IAS 21/IFRS 10 HTML references are **2024 issued editions**, used for the cited stable principles, not represented as an exhaustive 2026 consolidated standards database. Before enabling a production policy pack, the methodology owner checks all effective amendments and local requirements using its licensed current material. Do not embed copyrighted standard text or a vendor's template library without appropriate permission.

### 3.2 Lessons from comparable systems—not copied product scope

| Comparable system / official documentation | Adopt | Deliberately do not copy |
|---|---|---|
| Dynamics 365 Finance period-end close [P1] | A sequenced, accountable close process, reporting checks and controlled period restrictions. | Its operational ERP, automatic allocations, batch close or recurring task engine. |
| Oracle Account Reconciliation [P2] | Preparer submission, independent review, required evidence and reasoned return/reopen. | Optional auto-close, automatic notifications or weaker no-review paths. |
| Caseware trial-balance mapping [P3] | Separate client account codes from the firm's standardized reporting presentation; trace outputs to mappings. | Tax codes/filing workflows, proprietary template contents, automatic mapping acceptance. |

These products inform workflow usability. They are not authorities for accounting recognition policy, and their feature lists do not expand this scope.

<a id="shared"></a>
## 4. Shared domain, persistence and CQRS contract

### 4.1 Identity and common value objects

Use existing UUID identities, immutable source/revision references and explicit firm/client/engagement scopes. Introduce these **NEW or EXTEND equivalents**, after locating duplicates:

| Contract | Required content and invariant |
|---|---|
| `ReportingContextRevision` | Immutable record binding existing firm, client, legal-entity identity/key, engagement, period, book decision, basis, functional/reporting currency, chart, dimensions and policy revisions. No lookup by display name. |
| `MoneyAmount` | `decimal Value` + approved `CurrencyCode`; at most six fractional digits at storage; checked arithmetic and compatible currency. |
| `AccountIdentity` | Existing client account's stable identity plus chart revision; source code remains a string, preserving leading zeros. |
| `DateRange` | Inclusive start/end with start <= end; actual calendar validation; effective periods do not use local timestamp comparisons. |
| `ArtifactRef` | Persisted ID, kind, semantic revision, content hash, storage identity, MIME/size when applicable. A URL alone is not identity. |
| `EvidenceRef` | Existing document/snapshot ID, exact version/hash and scoped classification; never a free-text assertion of authorization. |
| `DependencyManifest` | Canonical ordered exact inputs, collection-membership revisions, policy/calculator versions and hash. Created on the server. |
| `ApprovalDecision` | Subject identity/revision/manifest, stage, actual decision person, recorder where different, rationale/evidence, UTC time, role/scope at decision. |
| `Currentness` | `Current`, `Stale`, `Unavailable`, `UnverifiedLegacy`; separate from business approval and historical issue status. |

Do not invent a second legal-entity table just to fit a diagram. Reuse existing client/entity relationships and `LegalEntityKey`; introduce a stable entity record only if the baseline mapping establishes that one client represents several distinct entities. Ambiguous historical identities must be quarantined, not guessed.

### 4.2 Monetary policy

EXISTING money uses **numeric(19,6)** and `decimal.Round(..., MidpointRounding.ToEven)`. Retain it. A wholesale conversion to integer cents would lose source/FX allocation precision. [R5][R8]

Input amounts must fit 13 integer digits and six fractional digits; reject excess precision/range before persistence instead of silently letting PostgreSQL round user entries. Derived calculations normalize at documented boundaries. Use checked decimal arithmetic and reject aggregate overflow. Define separate presentation precision by currency and report; do not infer accounting accuracy from two displayed decimals.

A journal line has nonnegative debit/credit, exactly one positive side, a valid account and currency. Submission requires at least two valid lines and **sum(debits) = sum(credits)** exactly at storage precision. Materiality is not a rounding tolerance for unbalanced journals.

Keep signed canonical balances debit-positive, credit-negative. Reporting display sign is a separate approved layout property. Fractions must sum exactly to 1; retain the existing documented `LAST_DESTINATION` residual policy with deterministic destination ordering. Currency conversion uses approved rate direction. Propose numeric(28,12) for new rate fields only after inspecting existing rate mappings and approving the migration; constrain rate/amount products to the representable result range.

Never sum currencies without an explicit translation result. Missing, zero, not applicable and unknown are different states. An explicit display-rounding reconciliation is allowed; an unexplained balancing journal or cash-flow amount is not.

### 4.3 Persistence conventions

Existing database identity remains `AuditSphereDbContext`. Implement or extract `IEntityTypeConfiguration<T>` classes in the existing Infrastructure persistence area. Initial extraction must produce **no EF model diff**; future schema changes have additive migrations. Do not configure the same entity twice with conflicting rules.

For each scoped header use a primary UUID key and alternate key needed for scoped children, typically `(FirmId, ClientId, EngagementId, Id)`. Financial children use a composite FK to that header or an equivalent enforced scope chain. Client master records use `(FirmId, ClientId, Id)` as appropriate. Group records use `(FirmId, GroupId, ScopeVersionId, Id)`; group ownership does not imply component-file access.

All business/history/source/evidence references use **Restrict/NoAction**, not cascading deletion. Retire configuration through lifecycle fields. No soft-delete filter may silently hide evidence from an issued historical package. Disposable rejected staging chunks may be removed through a separately scoped cleanup after evidence has been retained; they are not accepted accounting records.

Use unique constraints for normalized codes within their owner/version, source-line identity within batch, exact journal identity/revision, artifact identity within package/renderer, approval stage/subject constraints, and idempotency keys. Row CHECK constraints validate ranges/enum values and debit/credit exclusivity. Cross-row journal balance, hierarchy cycles and completeness need guarded transactions and existing/deferred database constraint mechanisms; a CHECK cannot prove a sum across children.

**Concurrency:** use an explicit mapped `long Revision` as an EF concurrency token where a mutable draft/header exists, preserving current generation fences. PostgreSQL `xmin` can be an additional provider token but is not a durable business revision; do not introduce SQL Server-style rowversion assumptions. [T3][T4]

Use migration suffixes such as `M20_ReportingContextPins`, `M21_ImportManifestConstraints`; EF generates the real timestamp prefix at creation. These are proposed names, not claimed existing migration IDs. Never rewrite old migrations. Test empty schema, prior-schema upgrade, ambiguity quarantine, trigger preservation, model drift and rollback/restore strategy. Evidence-bearing downgrades may intentionally refuse destructive reversal.

### 4.4 Request, result and DTO notation

All command/query classes listed below are **NEW public application facades**, including those that reuse existing services. Each implements `IRequest<CommandResult<TResponse>>`. Every row explicitly names `TResponse`. Every class has a correspondingly named `AbstractValidator<TRequest>`; shared rules are composed, not omitted.

Contract tables use the following fully defined common types:

| Type / alias | Definition |
|---|---|
| `Meta` (`R2RCommandMetadata`) | `OperationId: Guid`, `ExpectedRevision: long?`, `ExpectedInputGeneration: long`, `ExpectedPolicyGeneration: long`, `Reason: string?`. Server resolves actor/firm; no caller-selected approver. Create uses null expected object revision; updates/reviews require an exact positive revision. |
| `Context` | `ReportingContextRevisionId: Guid`; server resolves/reauthorizes all underlying identities. For pre-context setup, explicit client/period IDs are allowed only with equivalent scoped authorization. |
| `Ref` (`VersionReferenceDto`) | `Id: Guid`, `Revision: long`, `Hash: string`; hash mandatory for sealed/calculated inputs. Draft editing uses ID/revision without falsely asserting a content hash. |
| `MutationReceiptDto` | `Id`, `Revision`, optional `ContentHash`, resulting business state, currentness, current input/policy generations, `OperationId`. No entity navigation properties. |
| `OperationTicketDto` | Durable operation ID, correlation ID, state, target identity/revision, status-query identity. Enqueue is not successful completion. |
| `ValidationReportDto` | Rule version, checked input manifest, typed errors/warnings `(code,fieldPath,rowKey,message)`, totals/counts, `CanAdvance`. |
| `PageRequest` | `Cursor: string?`, `PageSize: int` (1–200), allowlisted `Sort`, typed filters. No arbitrary SQL/expression strings. |
| `PageDto<T>` | Immutable items, next cursor, read-snapshot token, optional count calculated from the same filter/snapshot. |
| `DecisionInputDto` | `Decision`, `Rationale`, list of exact evidence refs; optional actual management-contact identity for an authorized offline record. Authenticated recorder is server-owned. |
| `DomainEventEnvelope` | Event ID/type/schema version, owner aggregate/revision, scope, old/new manifests, operation/correlation/causation IDs, UTC occurrence, authenticated actor identity. No source file bytes or financial text in general logs. |

All ID fields reject Guid.Empty. Codes and required labels are trimmed, length-bounded and not used as authorization keys. Unless an existing stricter limit applies: codes <=100, display labels <=300, rationale <=4,000, references <=2,000 characters. Multiline professional notes get a separate approved limit. These are **proposed defaults**, not silently changed existing API limits.

Validators use `ValidateAsync`, including synchronous rules. A database lookup in a validator cannot replace in-transaction checks. Expose field failures using the existing error catalogue plus a typed validation-exception/error-adapter boundary; retain `CommandResult<T>` for domain outcomes. Web maps validation failures to `ValidationMessageStore`; APIs map them to field-based problem details. Do not assume FluentValidation automatically integrates with Blazor `EditForm`. [T2][T5]

### 4.5 Transaction, idempotency and lock discipline

Before any state change: resolve current actor and session epoch; authorize exact scope; lock the existing safety/context rows; recheck grants, period state, expected revision/generation and dependency manifest; perform the domain change; append evidence/invalidation/outbox in the same transaction; commit once.

Publish a lock-order ADR before adding cross-client operations. Retain the existing **firm → client → engagement/target** order; group operations must lock participant client IDs in a deterministic sorted order and fit the same global order before taking group/target locks. Audit every participating existing writer—do not merely document an order while old writers invert it. Avoid a long-lived transaction across rendering, files, network calls or human decisions.

An idempotency key includes firm, command kind, scope/target and OperationId. Persist canonical input hash and result. Same key/same body returns the original result **after current authorization**. Same key/different body returns `idempotency.conflict`. An uncertain timeout queries the operation before retrying. A unique constraint handles concurrent identical submissions; a UI disabled button is insufficient.

Handle concurrency conflicts without automatically replaying a professional approval against newer data. PostgreSQL uniqueness/FK failures are not all `DbUpdateConcurrencyException`. Map known provider errors to safe outcomes; retry deadlock/serialization failures only for the complete idempotent transaction, bounded and without repeated external effects.

### 4.6 Shared Blazor interaction standard

A new/existing page uses a per-circuit **scoped UI state service** containing selected IDs, immutable query DTOs, filters and unsaved draft state—never `DbContext`, tracked entities or cross-user static state. Database contexts live per request/operation through `IDbContextFactory`/approved unit of work. This avoids the circuit lifetime and thread-safety pitfalls documented by Microsoft. [T6]

Each page follows the same component contract:

```text
R2RPage (route parameter + authorized Context)
  ContextBanner / ScopeUnavailable
  CurrentnessBanner + RevisionHistoryLink
  FilterBar + paged Register
  Editor (EditForm + EditContext)
    typed input children and ValidationMessage fields
    line/grid rows keyed by stable draft line IDs
    ValidationSummary + server-error adapter
  ReviewDialog (exact revision/manifest, rationale/evidence)
  DurableOperationPanel / ConflictDialog
```

Child components receive DTOs and `EventCallback<TIntent>`; they do not call EF or change another module's state. Cascade a read-only context accessor/selection token, not a mutable authority object. Query capabilities describe current allowed actions but the server reauthorizes all commands.

Use `EditForm` with an explicit asynchronous `OnSubmit` path. A lightweight DTO validator provides field feedback; the MediatR pipeline remains the authoritative async validation path. Do not rely on synchronous `OnValidSubmit` or DataAnnotations alone to invoke FluentValidation. Forms use a new `EditContext` for each loaded revision, `InputText` for identifiers, `InputNumber<decimal>` for amounts, date inputs for `DateOnly`, field errors and summary. Async submit validates, awaits the server, then reloads the committed projection. No success toast for a queued/unknown/failed action. Disable duplicate submission but keep OperationId stable for a retry of the same intent.

Use `OnParametersSetAsync` with cancellation plus a monotonically increasing load sequence; clear old protected content **before** loading a new context. Discard late results. Reauthorization failure clears rows, counts, draft PII and download references. Same-document route changes and grant revocation are explicit tests.

Dirty tracking compares normalized draft to acknowledged original; changing filters alone is not a business edit. `NavigationLock` protects internal navigation and supported browser unload prompts. Save/discard/cancel are explicit. Conflict dialogs offer reload or a separately preserved draft; they never overwrite a current approved revision. Unsubscribe listeners and dispose cancellation sources.

Optimistic UI is limited to reversible local draft row edits and filters. Approval, posting eligibility, source acceptance, sealing, period close and release-related decisions are **pessimistic until confirmed**. In-app notices announce committed changes; they do not send business emails or trigger reminders. [T5][T7]

<a id="lifecycle"></a>
## 5. End-to-end lifecycle and dependency contract

### 5.1 Normal entity cycle

```text
Approved client/engagement eligibility + current user grants
  → M20 approved reporting context / chart / period / book / policy
  → M21 receive and validate TB/GL → seal source → accept source
  → M21 independently reviewed reporting mapping + completeness proof
  → M22 proposed journals → technical review → management disposition
       → source-reflection classification → sealed adjustment plan
  ↔ M23 manual reconciliations → evidence → independent review
  → M24 statements + notes + cash/equity schedules + comparatives
       → validation → reviewed exact statement-set revision
  → M25 package definition → render artifacts → verify bytes → seal
       → applicable accounting/management/partner decisions
  → handoff to M37 controlled release, then M38 archive
  → M20 authorized period close; explicit amendment/next-period action
```

Reconciliation may discover an AJE: M23 **requests a user-initiated M22 command**, then binds the reviewed resulting journal/adjusted basis and recalculates. It does not mutate ledger balances. A source or adjustment change can require another reconciliation pass. This is a controlled dependency loop in the human workflow, not a cyclic object graph or an automated posting loop.

### 5.2 Group cycle

```text
Reviewed eligible entity packages from M25
  → M26 approved effective perimeter and component pins
  → policy alignment + approved rate set/translation
  → manual intercompany review + balanced approved eliminations
  → deterministic consolidated result + independent group review
  → M25 shared artifact renderer for GroupResultRef (not entity recalculation)
  → group package approval → M37 release → M38 archive
```

M25 supplies reusable rendering/sealing infrastructure; it does not invoke M26 recursively while computing an entity. The entity and group artifact inputs are a discriminated contract. Nested consolidation is only enabled by an approved acyclic method profile, not by allowing a group to include itself.

### 5.3 Exact input manifest

For an entity calculation, capture context/profile/chart/dimension/taxonomy/policy revisions; selected raw TB/GL IDs and normalized hashes; opening source and completeness proof; effective adjustment-set membership plus journal decisions/reflection plan; required reconciliation-set membership; current/prior statement context; layout/note/cash/equity schedule versions; accounting input and policy generations; calculator version.

For a package, additionally capture selected ordered sections, reviewed statement set, template/renderer versions, language/rounding policy and expected output formats. For a group add perimeter/ownership/method, component package **and accounting-input** hashes, rate/policy versions, alignment/elimination-set membership, prior-group result and group-review basis.

A list of existing journal IDs is insufficient: a newly accepted journal must change the adjustment-set revision. The same applies to a newly required disclosure/reconciliation. Record both **item revisions and membership revisions**.

### 5.4 Invalidation rules

| Upstream change | Must lose current applicability | Must remain unchanged |
|---|---|---|
| Accepted TB pointer or source revision | Mapping applicability, reflection decisions for that basis, completeness, affected reconciliations, statements, unissued package/reviews and consumer group runs | Old sealed TB/GL, old decisions, already issued bytes/manifests |
| GL replacement/opening-source change | Completeness and GL-dependent reconciliations/evidence; dependent statements/packages if required inputs changed | Unrelated clients and snapshots not using that source |
| Chart/dimension/policy revision | Any context/mapping/calculation that depended on changed definitions | Published earlier chart/policy and historic reports |
| AJE acceptance/rejection/reflection change | Effective adjustment plan, affected reconciliations, statements/packages and downstream group consumers | Raw TB, prior journal revisions/decisions |
| Mapping allocation revision | Dependent statement/equity/note amounts, packages and group component compatibility | Source rows and unrelated reconciliations that do not depend on mapping |
| Note/cash-flow/equity input edit | Relevant statement validation, package composition and exact reviews | Raw balances and prior published output |
| Render template/version changes | Replacement rendered artifact set and its approvals | Existing approved artifact bytes; never regenerate behind an old hash |
| Group perimeter/component/rate/elimination change | Current group run, group reviews, group artifacts | Component books/packages and prior group output |
| Scope revocation | Current reads/actions/downloads and cached projections for that actor | Accounting history; never delete an audit record because access ended |

Apply the immediate generation fence and an append-only invalidation record in the originating transaction. A worker can update derived projections afterward, but `Approve`, `Seal`, `Close` and `PrepareRelease` **recompute the manifest before committing**, so a delayed event cannot permit stale output. Keep conservative existing client-wide invalidation until narrower propagation has direct safety tests.

Applicability is stored in a separate current-state projection/sidecar or computed from manifests; it must not require updating an immutable approved snapshot or bypassing its database trigger. Historical approval stays an immutable decision. “Stale” means it no longer authorizes the current working result. An issued package stays a historical issue, with amendment lineage where needed; never rewrite it as though it were never approved.

<a id="module-20"></a>
## Module 20 — Accounting: profiles, contexts, chart, periods and books

**Business outcome:** every source, journal, reconciliation and report has one defensible client/entity/period/book/currency/policy context.  
**Requirement coverage:** VP-034; supplies chart/taxonomy prerequisites for VP-037.  
**Sequence:** M20.1 profiles → M20.2 calendars/books → M20.3 chart/dimensions → M20.4 context activation. Close/amendment completion follows Modules 24–25.

### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND aggregate candidates:** `ClientAccountingProfile`, `ClientReportingPeriod`, `ClientReportingBook`, `ClientChartVersion`, `ClientAccount`, `SourceAccountAlias`, `ReportingTaxonomyVersion` and nodes. Preserve their IDs and existing persisted wire states. Encapsulate changes behind domain methods incrementally; do not just add an `AggregateRoot` base class and call existing mutable public setters safe.

**NEW where no equivalent exists:** immutable `ReportingContextRevision`, versioned `AccountingDimensionSchema`, profile-change evidence and a typed `PeriodCloseChecklist`. Reuse `ClientPeriodAmendment` and existing close controls instead of a second close history.

Core fields:

| Record | Required fields / relationships |
|---|---|
| Profile revision | Client/entity identity, jurisdiction, functional currency, fiscal calendar, source-system label, framework-policy revision, creator/reason, previous revision. |
| Period/book | Existing owner IDs, code, start/end, basis, currency, prior-period reference, state and amendment lineage. A book selects an approved inclusion rule, not executable code. |
| Chart revision | Client, stable account identity, account code/name, type, normal balance, parent stable identity, posting/active flags, publication authority/time. |
| Dimension schema | Allowed dimension types and values with stable IDs/codes, required/optional rules, active/effective dates and schema revision. |
| Reporting context | Exact profile, chart, dimension, policy, engagement, period/book and legal-entity references; functional/presentation currencies and currentness. |

**Value objects/enums proposed:** `AccountingBasisCode`, `FiscalCalendarDefinition`, `CurrencyCode`, `AccountCode`, `AccountType`, `NormalBalance`, `PeriodState`, `ConfigurationLifecycle`, `BookSelection` (explicit book or approved no-separate-book decision).

**Invariants:** chart parents belong to the exact chart revision; no cycles, duplicate normalized codes, posting parent or orphan. Preserve leading-zero codes and case-normalization policy. A non-posting account cannot receive GL or AJE lines. An archived account remains readable through historical charts. A dimension value belongs to the selected schema; descriptions do not substitute for codes.

Fiscal start dates must be real dates, including leap-year rules. Distinguish reporting periods (annual/interim) from mutually exclusive ordinary posting periods: an annual report may cover monthly periods, but two active ordinary periods in the same book/calendar cannot ambiguously accept the same posting date. Any adjustment period uses an explicit classification and inclusion policy. No arbitrary “all fiscal years are 365 days” assumption.

Functional currency is a documented professional determination, not the user's display locale. A changed functional currency needs a dated approved transition, not retroactive rewriting of old periods. Default QAR may prefill a form but must be confirmed and saved; an unknown historical currency is not automatically QAR. [S4]

**Lifecycle:** configuration `Draft → Validated → independently Published`; edits to published content create successor revisions. Context `Draft → Active`; period `Open → Closing → Closed`, with `Closing → Open` by reasoned cancellation. A closed period can only enter an authorized amendment revision; previously sealed output remains immutable. Model new logical states through a reviewed compatibility mapping to current strings.

**Domain events proposed:** `AccountingProfileRevised`, `ChartPublished`, `DimensionSchemaPublished`, `ReportingContextActivated`, `PeriodClosingStarted`, `PeriodClosed`, `PeriodAmendmentOpened`. Events report facts; they do not automatically approve new downstream versions.

### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend `ClientAccountingProfileConfiguration`, `ClientReportingPeriodConfiguration`, `ClientReportingBookConfiguration`, `ClientChartVersionConfiguration`, `ClientAccountConfiguration`, `ReportingTaxonomyVersionConfiguration`. Add equivalent configuration classes only when absent for context/dimension/close records.

Required constraints:

- Unique client profile head per firm/client; immutable profile revisions unique by logical profile/version.
- Period code unique within firm/client/basis/calendar; book code unique within period; prior-period FK same client/basis or an explicit approved basis bridge.
- Chart code uniqueness `(ChartVersionId, NormalizedAccountCode)`; stable account identity unique within chart; composite parent FK includes chart identity.
- Context composite FKs prove profile/chart/period/book belong to the same client and correct engagement/entity. Nullable legacy book links remain explicitly unbound until resolved; do not fabricate a default book during migration.
- Index contexts by `(FirmId, ClientId, EngagementId, PeriodId, Currentness)` and close records by period/revision; published configuration cannot be updated/deleted.
- Schema/value uniqueness by owner revision; dimension history uses Restrict deletion.

Use existing locks for cross-row cycle/overlap and publication checks. A recursive domain check alone is insufficient under two concurrent parent edits; serialize the chart-revision update, then validate before commit. Add a database-enforced safeguard or equivalent publication-time locked validation with negative integration tests.

Proposed additive migration suffixes: `M20_ReportingContextPins`, `M20_DimensionRevisionConstraints`, `M20_PeriodCloseEvidence`. First run backfill analysis. Only a single exact historical match may be linked; ambiguous profile/period/chart/book mappings stay quarantined using the existing mechanism. No downgrade may erase published versions or period-amendment evidence.

### 3. Application & CQRS Contracts (`.Application`)

**Input DTOs:**

- `ProfileDraftDto`: ClientId, Jurisdiction, FunctionalCurrency, FiscalYearStartMonth/Day, SourceSystem/Identifier, FrameworkPolicyId, optional predecessor profile Ref.
- `PeriodDraftDto`: ClientId, Code, StartDate, EndDate, Basis, Currency, PeriodKind, CalendarId, optional PriorPeriodId.
- `BookDraftDto`: ClientId, PeriodId, Code, Basis, Currency, InclusionPolicyId.
- `AccountDraftDto`: DraftLineId, StableIdentity, Code, Name, Type, NormalBalance, ParentStableIdentity?, IsPosting, IsActive.
- `ChartDraftDto`: ClientId, ChartId?, BaseChartRef?, EffectiveFrom/To?, Accounts[].
- `DimensionSchemaDraftDto`: ClientId, BaseRevisionRef?, Definitions[type,code,name,required,values[code,name,effectiveFrom,effectiveTo,active]].
- `ContextBindingDto`: ClientId, EngagementId, LegalEntityKey/verified entity identity, PeriodId, BookSelection, ProfileRef, ChartRef, DimensionSchemaRef, TaxonomyRef, PolicyRef, FunctionalCurrency, PresentationCurrency.

**Requests:** all have the MediatR interface/result convention in §4.4.

| Command / query and input | TResponse | Key validator + handler check |
|---|---|---|
| `CreateAccountingProfileCommand(ProfileDraftDto, Meta)` | `MutationReceiptDto` | Valid actual fiscal date, currency and client; reject duplicate profile; authorized creator. |
| `ReviseAccountingProfileCommand(ProfileId, ProfileDraftDto, Meta)` | `MutationReceiptDto` | Exact predecessor/revision; changed accounting basis requires reason and explicit impact preview. |
| `DefineReportingPeriodCommand(PeriodDraftDto, Meta)` | `MutationReceiptDto` | Inclusive dates, same-owner prior period, approved calendar/period kind, no ambiguous overlap. |
| `CreateReportingBookCommand(BookDraftDto, Meta)` | `MutationReceiptDto` | Period open, same basis/currency/client; unique code. |
| `SaveChartDraftCommand(ChartDraftDto, Meta)` | `MutationReceiptDto` | Full hierarchy/account validations; stale revision cannot overwrite. |
| `PublishChartCommand(ChartId, DecisionInputDto, Meta)` | `MutationReceiptDto` | Locked complete chart validation; distinct approved publisher where policy requires; immutable publication. |
| `PublishDimensionSchemaCommand(DimensionSchemaDraftDto, DecisionInputDto, Meta)` | `MutationReceiptDto` | Unique codes/effective values; used definitions preserved; impact acknowledged. |
| `BindReportingContextCommand(ContextBindingDto, Meta)` | `MutationReceiptDto` | All pins approved, compatible, current and in scope; no unbound/ambiguous legacy context. |
| `StartPeriodCloseCommand(Context, Meta)` | `MutationReceiptDto` | Freeze new source acceptance while evaluating latest close basis; no outstanding modifying operation. |
| `CancelPeriodCloseCommand(Context, Meta)` | `MutationReceiptDto` | Closing only, mandatory reason; changes close attempt rather than discarding evidence. |
| `CloseReportingPeriodCommand(Context, ExpectedCloseManifest: Ref, DecisionInputDto, Meta)` | `MutationReceiptDto` | Current approved package, resolved required reconciliations/differences and eligible policy; recompute manifest atomically. |
| `OpenPeriodAmendmentCommand(PeriodId, OriginalPackage: Ref, DecisionInputDto, Meta)` | `MutationReceiptDto` | Closed period only; new revision, reason, independent authority; original output retained. |
| `GetAccountingWorkspaceQuery(ClientId, PageRequest)` | `AccountingWorkspaceDto` | Authorized profile summary, available periods/books/charts and capabilities; no sibling data. |
| `GetChartRevisionQuery(ChartId, PageRequest)` | `PageDto<AccountViewDto>` | Exact chart, hierarchy counts and account state; not all client charts by default. |
| `GetReportingContextQuery(Context)` | `ReportingContextDto` | Full exact bindings/currentness/capabilities, not mutable EF entities. |
| `GetPeriodCloseReadinessQuery(Context)` | `ReadinessDto` | Named blockers, owner, exact target and safe link; quantities derived from same manifest. |

`AccountingWorkspaceDto` contains scoped profile summary, paged period/book/chart headers and allowed actions. `AccountViewDto` projects AccountDraftDto plus persisted ID/revision and applicability. `ReportingContextDto` projects ContextBindingDto plus context ID, state/generations. `ReadinessDto` contains manifest, `CanAdvance`, and blockers `(code,ownerModule,targetId,reason)`.

Before writing handlers, map each request to current `ClientAccountingService` methods and extend missing behavior. `CreateProfileAsync`, `CreatePeriodAsync`, `RollForwardPeriodAsync` already exist; their presence is not permission to omit the new locked invariants. [R7]

### 4. Inter-Module Lineage & Boundaries

M20 owns configuration, not imported balances or approved statements. M21 resolves an approved context and stores exact context pins on a new source; M22–M25 reuse those pins. A changed published chart creates a new context revision rather than moving old datasets to a different chart.

Expose `GetContextForOperation`/`GetApprovedChart` read contracts through queries. No module silently creates clients, fiscal years or missing accounts. M25 returns a package-ready manifest for close; M20 records the close decision, not a release. An accounting-only close need not invent EQR or an audit release, but must satisfy its approved service profile.

Publish invalidation before downstream use can commit. A changed account **name only** may affect output artifacts but not numeric balances; a posting/classification/dimension change affects source/mapping applicability. Initially use the conservative generation fence; narrow this distinction only after tested policy rules exist.

### 5. Blazor UI Architecture (`.Web`)

Extend the existing AccountingWorkspace/AccountingPeriod routes. Proposed composition:

```text
AccountingWorkspacePage
  ClientAccountingProfilePanel
  ReportingContextSelector (period/book/basis/currency)
  ChartRevisionEditor
    AccountHierarchyGrid → AccountEditDialog
    AliasEditor / ChartComparisonPanel
  DimensionSchemaEditor
  ContextImpactPreview
  PeriodClosePanel → CloseReviewDialog / AmendmentDialog
```

`AccountingWorkspaceState` retains context IDs, filters and acknowledged draft revisions. Each editor has its own EditContext so selecting a period cannot submit a dirty chart accidentally. Context changes prompt save/discard/cancel; no automatic context substitution. `EventCallback<ContextSelected>` reloads every dependent panel using the new selection token.

Show published charts read-only with “Create revision”, show unknown legacy setup explicitly, and display blocked-close links. Only authorize permitted actions; administrator access to settings does not automatically confer management acceptance. Failed close leaves the previous period state and visible blockers; long validation shows a real operation status.

### 6. Edge Cases, Security & Verification

Proposed test classes and required cases:

| Layer | Class | Concrete tests |
|---|---|---|
| xUnit domain | `ReportingContextInvariantTests` | `RejectsCrossClientBook`; `PreservesLeadingZeroAccountCodes`; `RejectsPostingParentAndCycles`; `FiscalCalendarHandlesLeapAnd53WeekYears`; `UnknownCurrencyIsNotDefaultedDuringMigration`. |
| xUnit PostgreSQL | `ReportingContextPersistenceTests` | `ConcurrentChartPublishHasOneWinner`; `PriorChartStillResolvesIssuedPackage`; `AmbiguousLegacyLinkIsQuarantined`; `CloseRacingSourceAcceptanceCannotBothCommit`; `AmendmentPreservesClosedPackage`. |
| bUnit | `AccountingContextEditorTests` | `FieldErrorsMapToCorrectAccountRow`; `ContextSwitchPromptsForDirtyDraft`; `LatePriorContextResultIsDiscarded`; `ClosedPeriodRendersReadOnly`. |
| Playwright | `R2R20AccountingJourneys` | Create client context/chart/book; publish under distinct user; import-ready handoff; deny sibling context; change chart and observe stale downstream output; close/amend/reload under actual persisted state. |

**Exit gate:** approved context can be read by all downstream modules using the same identity and revision; none can accept an orphan, closed, cross-client or unresolved context. Existing accounting setup tests remain and pass.

<a id="module-21"></a>
## Module 21 — Trial Balance & GL: ingestion, mapping and completeness

**Business outcome:** a reviewer can explain each reported balance from accepted source data, journal movements and an approved mapping.  
**Requirement coverage:** VP-035, VP-036, VP-037.  
**Sequence:** M21.1 intake session → M21.2 parse/validate/stage → M21.3 atomic seal/accept → M21.4 mappings → M21.5 completeness and drill-down.

### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** `TrialBalanceDataset`, `TrialBalanceImportBatch`, `TrialBalanceRow`, existing source/GL import records, `MappingVersion`, `MappingAllocation`, opening/completeness records. Reuse the current import/profile/parser and normalized-digest contracts. Do not build a second TB table under a new module name. [R4][R7]

**NEW if absent:** `SourceAcceptanceDecision`, `ImportColumnMapVersion`, typed `ImportValidationIssue`, independently identified source-selection history and a mapping account-universe manifest. These are needed only when current equivalents cannot carry the specified contract.

Maintain three distinct identities: **received file hash**, **normalized source digest**, **selected accepted revision**. Identical file bytes in another context are not automatically the same business source. Parsed source rows never change after sealing. A replacement is a successor with its own receipt/profile/parser metadata.

A GL line retains journal/line IDs, posting/document/service dates, account stable identity and original code, description, original currency/amount, functional amount, debit/credit representation, counterparty and dimensions. Resolve source aliases explicitly; do not merge accounts because descriptions match.

**Lifecycle:** `Receiving → Staging → Validating → Validated → Sealed`; failures become `Rejected`/`Cancelled` with an evidence record and no accepted-source pointer change. `Sealed` means content is immutable, not independently accepted. Source acceptance is a separate authorized decision; selected source changes through an explicit command. Map these logical stages to current LOADING/SEALED records and operation status rather than rewriting historical state strings.

GL completeness state is `NotEvaluated`, `Incomplete`, `Reconciled` or `Stale`, separate from parse success. Define:

`residual(account, functional currency) = opening + included GL movements − closing TB`

The account universe is the union of opening, GL and closing accounts, including zero-closing and GL-only accounts. Missing opening coverage is **unknown**, not zero. An explicit approved zero-opening declaration for a new entity is different. Missing chunks, unbalanced journal groups, duplicate line identities, unknown dimensions and period/basis/currency mismatches prevent completeness.

Distinguish pre-close TB from a source TB where revenue/expense accounts were closed into retained earnings. An approved import/inclusion profile defines which year-end journals and period endpoints reconcile; never discard closing journals merely to force a zero residual.

**Mapping lifecycle:** `Draft → Submitted → Approved`; return/amend creates a tracked draft/revision. Each nonzero effective reporting account must map to permitted taxonomy nodes. Manual splits conserve each source amount. Note references may cross-link primary values but must not double count them in primary statement totals. Existing `LAST_DESTINATION` is deterministic rounding allocation, not a discretionary plug.

AJE-only accounts require special treatment: Module 22 may introduce a valid account from the approved chart that has zero balance or no row in the raw TB. The effective mapping universe includes those eligible AJE accounts and is hashed; approving a later AJE cannot make an unmapped account disappear. Do not insert fake zero rows into the immutable received TB.

**Domain events:** `SourceRevisionSealed`, `AcceptedSourceChanged`, `GeneralLedgerBatchFinalized`, `CompletenessProofRecorded`, `MappingRevisionApproved`.

### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend configurations for current datasets/batches/rows, mapping versions/allocations and GL transactions/lines/chunks. Add acceptance/current-source/configuration-history mappings only where missing.

Constraints and indexes:

- Source batch and dataset carry context/firm/client/engagement FKs and parser/profile versions. `RawFileSha256Hex` and normalized digest are separate nonempty hash fields when sealed.
- Chunk uniqueness `(ImportBatchId, ChunkNumber)`; idempotent identity includes chunk digest. Same chunk number/different content is a conflict, not overwrite.
- GL line identity unique by `(BatchId, StableJournalId, StableLineId)` or existing equivalent. Retain legitimate repeated account codes on different lines/dimensions.
- TB aggregation is permitted only under a declared normalization policy. Duplicate ambiguous account/dimension keys reject; exact signed detail aggregation must retain source-row references and counts.
- Index source rows by dataset/account/dimension and GL lines by batch/account/posting date/journal key for paged drill-down. Do not load 500,000 entities into the Blazor circuit.
- Children cannot be added, changed or deleted after the source is sealed. Use existing database triggers/guards plus FK discipline; a UI status flag is insufficient.
- Mapping allocations unique for mapping/source-account/target/role; reject duplicate primary allocation. Scoped FKs bind chart and taxonomy. Fraction and amount conservation are transaction-validated.
- Completeness proof persists exact opening/GL/closing refs, inclusion profile, account residuals, row/journal coverage and result hash. New proof never edits an old accepted proof.

Proposed migration suffixes: `M21_SourceSelectionEvidence`, `M21_ImportChunkIdentity`, `M21_CompletenessManifest`, `M21_MappingUniversePins`. Existing source digests must remain resolvable; new digest versions use an explicit scheme/version field rather than rewriting old hashes.

Stage bounded rows in short transactions. Finalize only when expected chunk count, row count, journal boundaries and recomputed digest agree. In one final transaction seal membership and register the immutable result; in the separate acceptance transaction update the selected pointer, increment generation and append invalidation/evidence. A rejected import leaves the prior accepted source intact.

### 3. Application & CQRS Contracts (`.Application`)

**Reuse boundary:** current `ClientAccountingService` has direct and chunked GL inputs and limits of 100,000 transactions / 500,000 lines per import, 10,000 transactions / 50,000 lines per chunk. These are current caps, not demonstrated throughput promises. Preserve or tighten them; raising them requires profiling and approval. [R7]

**Input DTOs:**

- `ImportStartDto`: Context, SourceKind(TB/GL), FileName, DeclaredSize, MediaType, Layout(SignedNet/DebitCredit), ImportProfileRef, ParserVersion; source file bytes arrive through an authenticated bounded staging endpoint, not inside a mediator JSON request.
- `ColumnMapDto`: accountCode/name, amount or debit/credit columns, optional entity/currency/dimensions; GL adds stable journal/line keys, posting/document/service dates, source user, counterparty, reversal fields; locale/date conventions explicitly selected.
- `UploadChunkDto`: SessionId, Sequence, ServerStagedBlobRef, ClaimedChunkHash; server recomputes size/hash and scope. Claimed hash is a comparison input, not trusted identity.
- `ImportFinalizeDto`: SessionId, ExpectedChunks, ExpectedRows/Transactions/Lines, ExpectedRawHash, ColumnMapRef, ProfileRef.
- `MappingDraftDto`: Context, SourceRef, ChartRef, TaxonomyRef, EffectiveAccountUniverseHash, predecessor Ref?, Allocations[StableAccountIdentity,TargetNodeId,AllocationRole,DecimalFraction,Order,Rationale].
- `CompletenessInputDto`: Context, ClosingTbRef, GeneralLedgerBatchRefs[], OpeningSourceRef? or explicitly approved zero-opening declaration, InclusionProfileRef, CutoffDate.
- `SourceFilterDto`: account, date range, journal key, counterparty, dimension predicates, source revision; every filter is typed and scope-limited.

| Command / query and input | TResponse | Key `AbstractValidator<T>` rules plus authoritative checks |
|---|---|---|
| `BeginAccountingImportCommand(ImportStartDto, Meta)` | `ImportSessionDto` | Supported actual format/profile, safe filename, size limit, open context and current upload authority. |
| `RegisterImportChunkCommand(UploadChunkDto, Meta)` | `ImportChunkReceiptDto` | Exact session/sequence, approved size, hash match, no conflicting retry, unfinished session. |
| `SaveImportColumnMapCommand(SessionId, ColumnMapDto, Meta)` | `MutationReceiptDto` | Required headers unambiguous; no one numeric column assigned incompatible meanings; explicit dates/locale. |
| `ValidateAccountingImportCommand(ImportFinalizeDto, Meta)` | `OperationTicketDto` | Complete staged upload/profile; enqueue streaming parse/validation, no source acceptance yet. |
| `SealValidatedImportCommand(SessionId, ValidationReportRef, Meta)` | `MutationReceiptDto` | All fatal errors absent, exact input unchanged, complete counts and balanced required scopes. |
| `AcceptSourceRevisionCommand(Context, SourceRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Sealed source, current validation, independent acceptance where required, accepted-source uniqueness and generation bump. |
| `CancelAccountingImportCommand(SessionId, Meta)` | `MutationReceiptDto` | Reason, only cancellable stage; no removal of an accepted source or uncertain provider effect. |
| `SaveMappingDraftCommand(MappingDraftDto, Meta)` | `MutationReceiptDto` | Account universe/targets/context valid; draft can show unresolved rows but cannot submit as complete. |
| `SubmitMappingCommand(MappingId, Meta)` | `MutationReceiptDto` | All relevant accounts covered; split totals exactly 1; no cycles/double primary counting. |
| `ReviewMappingCommand(MappingId, DecisionInputDto, Meta)` | `MutationReceiptDto` | Authorized different reviewer; exact source/chart/taxonomy/universe unchanged; return needs rationale. |
| `BuildCompletenessProofCommand(CompletenessInputDto, Meta)` | `OperationTicketDto` | Compatible accepted sources; explicit opening coverage and cutoffs; no duplicate batches. |
| `GetImportSessionQuery(SessionId)` | `ImportSessionDto` | Session owner/scope, accepted chunk counts, profile, state and operation identity. |
| `GetImportIssuesQuery(SessionId, PageRequest)` | `PageDto<ImportIssueDto>` | Row keys/error codes safe; no arbitrary source-file path disclosure. |
| `GetTrialBalanceQuery(SourceRef, SourceFilterDto, PageRequest)` | `PageDto<TrialBalanceRowDto>` | Exact source, typed row values, visible context and matching totals snapshot. |
| `GetGeneralLedgerQuery(SourceRef, SourceFilterDto, PageRequest)` | `PageDto<GeneralLedgerLineDto>` | Exact source-line/journal provenance; paged dates and amounts. |
| `GetJournalDrillDownQuery(SourceRef, StableJournalId)` | `GeneralLedgerJournalDto` | All permitted journal lines and debit/credit totals; count and completeness warning. |
| `GetMappingWorkspaceQuery(Context, MappingId?)` | `MappingWorkspaceDto` | Current/prior mapping, effective account universe, unmapped queue and capabilities. |
| `GetCompletenessProofQuery(ProofId, PageRequest)` | `CompletenessProofDto` | Exact source manifest, coverage/counts, result and paged per-account residuals. |
| `ExportSourceRowsQuery(SourceRef, SourceFilterDto, ExportFormat)` | `ExportTicketDto` | Allowlisted CSV/XLSX format and bounded export; authorization reapplied at byte retrieval. |

`ImportSessionDto` contains ID, state, context, raw/normalized hashes when known, counts, limits, profile/parser versions and validation/operation refs. `ImportChunkReceiptDto` contains sequence, accepted digest/count and retry identity. Row DTOs project the source fields above plus exact IDs; issues contain row/journal keys and field errors. `CompletenessProofDto` carries input refs, coverage state, totals and residual rows. `ExportTicketDto` is a short-lived scoped output identity, not an unrestricted filesystem URL.

**Parser rules:** inspect ZIP/package structure for genuine XLSX, reject XLSM/macros/external workbook links/formula-dependent numeric cells, bound compressed/uncompressed size and shared strings, stop decompression bombs, preserve 1900/1904 date-system semantics. Never evaluate workbook formulas or open Office automation. CSV profile explicitly defines delimiter, quotes, encoding, numeric sign/decimal and date rules. Invalid dates, NaN-like text, oversized values and duplicate ambiguous keys produce row errors, not substituted zero.

Use existing CsvHelper/OpenXML parsers and pure normalization functions. Header-name guesses may be displayed as an unconfirmed convenience only; the saved mapping must be explicit. A cancellation yields an actual cancelled operation or conflict, never an announced success.

### 4. Inter-Module Lineage & Boundaries

M21 consumes M20 context/chart/taxonomy and existing document staging/evidence services. It owns source records and mappings, not management AJE decisions or financial statement layouts. M22 reads immutable source rows and chart identities; M24 reads the selected TB plus effective adjustments and approved mappings, never a mutable current-source query during rendering.

Accepting a new TB requires fresh source-specific reflection decisions. Preserve logical AJE identity/history across bases, but do not automatically mark old journal treatment applicable to replacement data. A new mapping tied to a changed taxonomy must invalidate reviewed statements using the old selected mapping while leaving historical packages unchanged.

For GL replacement, stale only dependent completeness/reconciliation/report requirements—not unrelated records solely sharing the same account label. Until precise dependency proofs exist, the existing generation fence remains conservative. A source with no GL can support only the approved profile that explicitly permits such reporting; UI must not claim GL completeness.

### 5. Blazor UI Architecture (`.Web`)

```text
AccountingSourceWorkspacePage
  ReportingContextBanner
  SourceRevisionRegister / SelectedSourcePanel
  TbGlImportWizard
    FileSelectionStep → ContextAndProfileStep → ColumnMapStep
    PreviewAndIssuesStep → ValidationProgressStep → AcceptanceStep
  TrialBalanceGrid / GeneralLedgerGrid / JournalDrawer
  MappingWorkspace
    UnmappedQueue / AllocationGrid / SplitDialog
    PriorRevisionComparison / IndependentReviewDialog
  CompletenessWorkspace → OpeningSourceSelector / ResidualDrillDown
```

Use `AccountingSourceState` with session/source/mapping IDs, filters, import progress and draft mappings. File input streams to the existing authenticated staging boundary with explicit maximum size; never retain the entire file or parsed GL in a scoped circuit service. Large grids use paging/virtualization backed by queries.

`EditForm` surrounds the profile/column/allocation choices, not a lifetime-long database transaction. Split rows have stable IDs and field-specific fraction errors. Closing or changing context while an upload is running requires an explicit cancel/leave decision; server staging retains honest resumability. Progress distinguishes bytes received, rows validated and source accepted.

After validation, render all fatal issues and a bounded preview. “Accept revision” displays the old/new source and invalidation impact. Read-only old revisions stay accessible with clear labels. Navigation back to another module preserves the exact selected context and source, not “most recently imported anywhere”.

### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required test methods/journeys |
|---|---|---|
| xUnit domain/parser | `AccountingImportContractTests` | `SignedAndDebitCreditLayoutsNormalizeEqually`; `RepeatedAccountAcrossDimensionsRetainsLineage`; `FormulaMacroExternalLinkAndRenamedZipRejected`; `LeadingZeroCodesAnd1904DatesSurvive`; `SixDecimalAndOverflowLimitsAreExplicit`. |
| xUnit PostgreSQL | `SourceRevisionIntegrityTests` | `ConflictingChunkRetryRejected`; `MissingChunkCannotSeal`; `RejectedReplacementPreservesAcceptedPointer`; `SealedRowsRejectInsertUpdateDelete`; `SourceAcceptanceInvalidatesInSameTransaction`. |
| xUnit calculations | `CompletenessAndMappingTests` | `OpeningPlusMovementEqualsClosing`; `MissingOpeningIsIncomplete`; `GlOnlyAccountIsNotOmitted`; `PreCloseVsPostCloseProfileIsExplicit`; `SplitConservesMicroUnits`; `AjeOnlyAccountRequiresMapping`. |
| bUnit | `AccountingImportWizardTests`, `MappingEditorTests` | Required headers/errors, cancel/back/resume, stale acceptance button, invalid split row, no automatic approval, exact source context after navigation. |
| Playwright | `R2R21SourceJourneys` | Genuine CSV and XLSX intake; malformed/oversize/browser validation failures; GL chunk resume/reload; independent mapping review; opening/GL/TB drill-down and export reconciliation; wrong-tenant source denial. |

**Exit gate:** a complete accepted source/mapping/completeness chain is immutable, replayable and scoped; its exact identity can be consumed by M22–M25. Source acceptance cannot occur on a partial upload or a fabricated zero residual.

<a id="module-22"></a>
## Module 22 — Adjustments & Journals

**Business outcome:** every proposed adjustment has an exact accounting basis, balanced lines, independent technical review, attributable management disposition and explicit source-reflection treatment.  
**Requirement coverage:** VP-038.  
**Sequence:** M22.1 journal drafts → M22.2 technical decisions → M22.3 management/reflection decisions → M22.4 adjustment plans and immutable adjusted snapshots.

### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** `AdjustmentJournal`, `AdjustmentLine`, `AdjustmentJournalManagementDecision`, `JournalSourceReconciliation`, `AdjustmentPlan` and plan lines, `AdjustedTrialBalanceSnapshot`/rows. Current journal purpose/origin/supersession/reversal fields must remain. [R4]

Journal identity is logical `(firm, engagement, journal number)` with distinct persisted base/revision identity. A source replacement never silently changes `BaseDatasetId` of an already reviewed journal. A revised journal states predecessor, new basis and reason. An authorized reversal is a new balanced journal linked to the original—not deletion or negation of a posted row in place.

Separate three dimensions of state:

| Dimension | Proposed explicit states | Meaning |
|---|---|---|
| Technical | Draft, Submitted, Returned, TechnicallyApproved, Voided | Correct form, balanced lines and reviewer conclusion for the exact revision. |
| Management disposition | Pending, Accepted, Rejected, PartiallyAccepted, NotRequiredByApprovedPolicy | Attributable decision; never inferred from technical approval. |
| Source reflection | Unassessed, NotReflected, Reflected, PartiallyReflected, Unknown | Whether this exact adjustment is already present in the selected received source. |

Current `Posted` and `ReflectedInSource` values require an explicit compatibility map; do not reinterpret them as external ERP posting or replace all historical strings blindly. New immutable decision records may supply richer state without rewriting old journals.

**Invariants:** at least two lines; valid posting accounts in the approved chart; one positive side per line; exact debit/credit equality; same reporting context/functional currency; dates within the eligible period or authorized amendment; evidence/rationale; no self-review. A client's management decision must be within the correct client/engagement authority, not an application administrator impersonating management.

Every line retains account stable identity, source code, dimension tuple, debit/credit, description and optional finding/evidence relationship. A legitimate zero-origin chart account may be used, but then Module 21's mapping universe must include it before statements are valid.

**Reporting eligibility:** technically approved, management disposition satisfying the approved purpose policy, current source-specific reflection decision, and not superseded/void. Only **NotReflected** contributes the journal delta. **Reflected** contributes zero because it is already in source. **Unknown/PartiallyReflected** blocks final treatment until resolved. Keep rejected/uncorrected amounts visible in the findings/evaluation layer.

Partially accepted management decisions do **not** authorize arbitrary selected journal lines or multiplication by a percentage. Create explicitly linked replacement journals whose accepted portion is itself balanced, record the rejected remainder and obtain fresh required review. Likewise, partial reflection requires account/line-level proof and a balanced approved residual, not double counting.

**Formula:** `Adjusted(account) = Raw(account) + sum(eligible, not-reflected adjustment contributions)` at the preserved numeric precision. The effective contribution set is hashed and each logical purpose/source revision can contribute once. The snapshot stores the plan, not merely the final totals.

**Events:** `AdjustmentSubmitted`, `AdjustmentTechnicalDecisionRecorded`, `AdjustmentManagementDecisionRecorded`, `JournalReflectionAssessed`, `AdjustmentPlanSealed`, `AdjustmentSuperseded`.

### 2. Persistence & Migrations (`.Infrastructure`)

Extend current journal/line/decision/plan/snapshot `IEntityTypeConfiguration<T>` mappings. Add separate technical-decision or source-reflection history tables only if existing review/reconciliation entities cannot represent them.

Use context/base-dataset composite FKs; line account references resolve to a valid same-client chart identity without requiring a preexisting raw TB row. Preserve string source codes for auditability. A reporting journal with `GroupOnlyElimination` purpose must be routed to the group-owned contract or explicitly rejected on an entity basis; it cannot leak into entity results.

Unique `(FirmId,EngagementId,JournalNumber,BaseDatasetId,Revision)` or the current equivalent; unique plan membership for the same journal revision/purpose; unique snapshot per immutable plan/calculator/input hash. Enforce nonnegative amounts and mutually exclusive positive debit/credit at row level. Balance is checked at submission/approval under header lock and protected against later child inserts/edits.

Technical, management and reflection decisions are append-only with actor/evidence/time and exact target revision. Index decisions by journal/revision/stage and selected base. Superseded states never physically remove prior snapshots. Foreign keys to findings/evidence use Restrict and exact scope.

Proposed migration suffixes: `M22_JournalDecisionHistory`, `M22_SourceReflectionPins`, `M22_EffectiveAdjustmentSet`. Migrate legacy `Posted` records only when real technical and management evidence supports each derived status; otherwise mark `UnverifiedLegacy` and block new finalization rather than manufacture approvals.

### 3. Application & CQRS Contracts (`.Application`)

**Input DTOs:**

- `AdjustmentLineDto`: LineId, AccountStableIdentity, Debit, Credit, DimensionValues[], Description, EvidenceRefs[], FindingId?.
- `AdjustmentDraftDto`: Context, BaseSourceRef, JournalNumber, Purpose, Origin, AccountingDate, Currency, Reason, Lines[], EvidenceRefs[], SupersedesRef?, ReversalOfRef?.
- `JournalReflectionDto`: JournalRef, SelectedBaseRef, ReflectionState, ObservedSourceJournal/LineReferences[], EvidenceRefs[], Rationale. Actual effects are recomputed; no caller-provided adjusted balance.
- `AdjustmentPlanInputDto`: Context, BaseSourceRef, JournalRevisionRefs[], ReflectionDecisionRefs[], ExpectedAdjustmentSetRevision, CalculatorVersion.
- `ManagementJournalDecisionDto`: JournalRef, Decision, EvidenceMode(SignedIn/Offline), EvidenceRefs[], Rationale, ActualManagementContactId? for permitted offline recording.

| Command / query and input | TResponse | Key validator + handler gate |
|---|---|---|
| `CreateAdjustmentDraftCommand(AdjustmentDraftDto, Meta)` | `MutationReceiptDto` | Valid current base/context, allowed purpose, valid line structure; incomplete draft clearly labelled. |
| `UpdateAdjustmentDraftCommand(JournalId, AdjustmentDraftDto, Meta)` | `MutationReceiptDto` | Draft/returned only; exact revision; reviewed content creates a new revision instead. |
| `SubmitAdjustmentCommand(JournalId, Meta)` | `MutationReceiptDto` | All lines/accounts/evidence complete; debit=credit; no unsupported currency/period. |
| `ReviewAdjustmentCommand(JournalId, DecisionInputDto, Meta)` | `MutationReceiptDto` | Exact submitted revision, independent technical reviewer; return/void requires reason. |
| `RecordAdjustmentManagementDecisionCommand(ManagementJournalDecisionDto, Meta)` | `MutationReceiptDto` | Technical eligibility, actual client-management authority or explicitly permitted offline recorder; exact decision evidence. |
| `AssessJournalReflectionCommand(JournalReflectionDto, Meta)` | `MutationReceiptDto` | Base and journal match; source refs valid; ambiguous/partial remains blocked for final inclusion. |
| `ReviseAdjustmentCommand(OriginalJournal: Ref, AdjustmentDraftDto, Meta)` | `MutationReceiptDto` | Original retained; new base/version and reason; no inherited approval/reflection. |
| `ReverseAdjustmentCommand(OriginalJournal: Ref, NewJournalNumber, AccountingDate, EvidenceRefs[], Meta)` | `MutationReceiptDto` | Authorized open/amendment context, balanced reversal lines derived from original; no duplicate reversal; fresh review. |
| `SealAdjustmentPlanCommand(AdjustmentPlanInputDto, Meta)` | `MutationReceiptDto` | Recompute complete eligible journal/reflection set and generation; unresolved treatment blocks. |
| `BuildAdjustedTrialBalanceCommand(PlanRef, Meta)` | `OperationTicketDto` | Current sealed plan; deterministic snapshot, idempotent key and no raw-source mutation. |
| `GetAdjustmentRegisterQuery(Context, StatusFilters, PageRequest)` | `PageDto<AdjustmentSummaryDto>` | Submitted/returned/rejected/eligible separately visible; scoped totals by currency. |
| `GetAdjustmentDetailQuery(JournalId)` | `AdjustmentDetailDto` | Header/lines, immutable decisions, source reflection and prior revisions; capabilities. |
| `GetAdjustedTrialBalanceQuery(SnapshotRef, AccountFilter, PageRequest)` | `PageDto<AdjustedBalanceRowDto>` | Raw, reporting delta, adjusted, source account, journal contributions and mapping coverage. |
| `GetAdjustmentEligibilityQuery(Context)` | `AdjustmentEligibilityDto` | Exact membership revision, eligible/excluded/blocked journals and reason codes. |

Output detail DTOs expose draft fields plus persisted refs and decision histories; summaries include number, base/revision, amounts, the three state dimensions and currentness. `AdjustedBalanceRowDto` carries RawAmount, Delta, AdjustedAmount, Currency and contribution refs. `AdjustmentEligibilityDto` is a report, not an approval command.

Use existing `AdjustmentJournalService` and `AdjustmentPlanService`. Add structural `AbstractValidator<T>` checks, but keep balanced-content/independence/source-generation checks inside authoritative handlers. Map stable errors to `journal.rejected`, `generation.stale`, `revision.stale`, `scope.denied` or approved new catalogue entries; never parse free-text error messages in UI.

### 4. Inter-Module Lineage & Boundaries

M22 reads source/COA from M20–21 and existing audit findings/evidence; it owns journal treatment and adjustment plans. M23 may link a proposed correction to this module but cannot mark it accepted. M24 consumes only a sealed effective plan and an adjusted snapshot; it does not rescan all “latest posted” journals independently. This prevents calculation engines from disagreeing on eligibility.

A new management decision or reflection correction changes adjustment-set membership and invalidates affected statements/packages even when the raw TB hash stays the same. A source replacement makes source-reflection applicability stale, not history false. A previously issued package preserves its plan and bytes; use the existing amendment/reissue path outside this module.

Management acceptance never posts to an external ledger. An uploaded replacement TB that already includes the adjustment requires explicit Reflected evidence; otherwise the system must not automatically infer that matching amounts prove identity.

### 5. Blazor UI Architecture (`.Web`)

```text
AdjustmentJournalWorkspacePage
  ContextAndBaseSourceBanner / AdjustmentRegister
  JournalEditor(EditForm)
    HeaderFields / DebitCreditGrid / AccountDimensionPicker
    EvidenceAndFindingLinks / BalanceSummary
  TechnicalReviewDialog
  ManagementDecisionPanel
  ReflectionAssessmentPanel
  AdjustmentPlanPreview → RawDeltaAdjustedGrid
```

Reuse the existing Journals surface, extending rather than creating a competing journal route. `AdjustmentEditorState` stores draft IDs, exact base and line models. A row edit updates local balance preview, but no preview is considered posted/accepted. Submit/review waits for server receipt. Render distinct technical/management/reflection badges; one green “Approved” badge is insufficient.

All form rows have stable LineId keys; deletion/reorder retains errors against the correct line. Dirty route guards protect unsaved drafts. An upstream-source notification produces a stale warning and disables final submit until rebase/revision is explicitly selected. Management view hides internal professional working notes while retaining the proposed journal and permitted supporting explanation.

### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `AdjustmentEligibilityTests` | `BalancedSixDecimalLinesRequired`; `DebitAndCreditOnSameLineRejected`; `AjeOnlyPostingAccountAllowedButMustMap`; `PartialManagementDecisionDoesNotApplyUnbalancedSubset`; `ReflectedContributionIsZero`. |
| xUnit PostgreSQL | `AdjustmentPlanLineageTests` | `SamePersonDifferentRoleCannotReview`; `CrossClientManagementDecisionDenied`; `SourceReplacementRequiresFreshReflection`; `SameJournalCannotContributeTwice`; `NewAcceptedJournalChangesSetManifest`; `PostedHistoryRejectsMutation`. |
| bUnit | `AdjustmentJournalEditorTests` | Correct row validation, balancing preview, distinct statuses, returned-draft handling, stale-source alert and no optimistic approval. |
| Playwright | `R2R22AdjustmentJourneys` | Create arbitrary multi-line AJE → independent review → management acceptance → reflection → snapshot; reject/partial/revise; replace TB containing the AJE and prove no double inclusion; revoke scope mid-form. |

**Exit gate:** raw plus eligible deltas reconciles exactly; management rejection, reflection uncertainty and source replacement are visible and cannot be bypassed by direct handler invocation.

<a id="module-23"></a>
## Module 23 — Bank & Subledger Reconciliations

**Business outcome:** explain the difference between a specified ledger basis and independently sourced supporting balances, with evidence and an independent conclusion.  
**Requirement coverage:** VP-039.  
**Sequence:** M23.1 basis/sources → M23.2 typed items → M23.3 correction handoff → M23.4 proof → M23.5 review and source-change handling.

### 1. Domain Modeling (`.Domain`)

**EXTEND:** current source-bound reconciliation and typed accounting/bank-schedule records in the accounting/fieldwork services. Locate exact existing entity/configuration names before implementing. The following are **logical proposed contracts**, not assertions that those names already exist:

`ReconciliationSchedule` aggregate owns a revision, scope, reconciliation kind, as-of date, account set, selected ledger basis, supporting-source references, item list, proof and review history. `ReconciliationItem` owns type, side/sign, amount/currency, dates, narrative, exact source/evidence refs and optional AJE reference. `ReconciliationProof` is immutable and records the complete input and formula version. Reuse existing independently reviewed schedule/evidence records where equivalent.

**Enums:** Kind=Bank/Receivables/Payables/OtherControl; Basis=RawTb/AcceptedGl/AdjustedSnapshot; ItemType=Timing/LedgerCorrection/SupportingSourceCorrection/Informational; Side=Ledger/Supporting; state=Draft/Submitted/Returned/Approved, plus separate Currentness.

**Sign convention:** normalize both ledger and supporting sources to the same **client-ledger perspective**, debit-positive. A bank statement's debit/credit convention must be explicitly transformed and recorded; never infer from the caption “balance”. For liability reconciliations use the same signed canonical basis and a separate display sign.

Define:

`LedgerExplained = SelectedLedgerBalance + eligible correction deltas NOT already contained in selected basis`

`SupportExplained = SelectedSupportingBalance + approved typed timing/supporting-source reconciliation items`

`Residual = LedgerExplained − SupportExplained`

For a schedule based on an adjusted snapshot, any journal in that snapshot's plan contributes **zero additional adjustment**. Merely linking the same AJE again must not change its explained ledger. Each item declares its side/effect; a journal's two balanced lines do not both affect the selected bank account.

Timing items explain when recorded activity appears in the other source; they do not generate ledger journals. Proposed ledger corrections link to M22 and count only under the approved eligibility rule. A supporting-source correction requires explicit evidence and review; it cannot be a generic “plug” field. Informational items have no numeric effect.

**Invariants:** account and source scope agree; required source coverage is complete; as-of dates and currency compatible; item dates fit the selected rule; no duplicate source-item inclusion; all values finite and within precision; required evidence available; unexplained residual exactly zero under the approved proof rule. Review cannot manufacture a waived balancing figure. Any allowed small presentation rounding is separately explained, bounded and policy-versioned.

For AR/AP subledgers, store source-row count, declared control totals, customer/vendor identity references, debit/credit treatment, aging date basis and cutoffs. Reconcile control account to the same as-of subledger, not to an unrelated current balance. Negative customer balances and unapplied credits remain visible rather than silently netted against another counterparty.

**Lifecycle:** Draft → Submitted → Returned or Approved; return reason mandatory. An approved schedule's change creates a successor revision and fresh independent review. A changed ledger source, included AJE, supporting document version or required evidence adequacy makes current proof stale. Manual carry-forward may copy still-outstanding item references into a new draft but never copies approval.

Oracle's documented separation of preparer/reviewer and rejection return informs this flow; automatic closure/notifications are deliberately not adopted. [P2]

### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend existing reconciliation configurations and source-bound schedule relationships. Persist schedule context, revision, exact source hashes/IDs, selected basis kind, as-of/currency, creator and versioned review. Items have unique IDs/order, typed side/effect and optional journal revision FK.

Composite FKs prevent linking another client's bank statement, GL batch, subledger or adjustment. Exact document snapshot is referenced rather than a mutable filename. Ledger basis must resolve to one valid sealed source or adjusted snapshot; do not store a free-text balance as if it were a proved ledger total.

Indexes: `(FirmId,ClientId,EngagementId,PeriodId,Kind,State)` for queues; schedule/revision for history; evidence/journal refs for invalidation; selected account/as-of for prior schedule lookup. Unique effective item/source key prevents duplicate inclusion. Approved schedules/proofs/reviews are append-only; details and linked journals use Restrict.

Use FK/row constraints for date/range/amount/kind; complete residual and freshness checks run under schedule plus dependency locks. The proof persists formula-version and input manifest, including adjustment membership. Proposed migrations: `M23_ReconciliationBasisPins`, `M23_TypedItemProofs`, `M23_ReconciliationReviewHistory`, only for actual missing fields. Existing source-proof data must not be regenerated under a new sign convention without a new version.

### 3. Application & CQRS Contracts (`.Application`)

**DTOs:**

- `ReconciliationDraftDto`: Context, Kind, AccountIdentities[], AsOfDate, LedgerBasisRef, SupportingSourceRefs[], SupportingBalance, SupportingSignConvention, Currency, ExtractionMethod(Imported/ManualTranscription), SourceLocationReferences[], PriorScheduleRef?.
- `ReconciliationItemDto`: ItemId, Type, Side, SignedEffect, Currency, TransactionDate, ExpectedClearanceDate?, Description, SourceItemRef?, EvidenceRefs[], LinkedJournalRef?, CounterpartyReference?.
- `ReconciliationEditDto`: ScheduleId, HeaderChanges, Items[]; read-only ledger balance excluded from caller-writable fields.
- `ReconciliationReviewDto`: ScheduleRef, ProofRef, Decision, ReviewerConclusion, EvidenceRefs[].

| Command / query and input | TResponse | Key validator + handler gate |
|---|---|---|
| `CreateReconciliationCommand(ReconciliationDraftDto, Meta)` | `MutationReceiptDto` | Current scoped basis, explicit sign/as-of/currency, required support reference and account coverage. |
| `SaveReconciliationDraftCommand(ReconciliationEditDto, Meta)` | `MutationReceiptDto` | Draft/returned state; valid item types/sides/dates and no duplicate item identity. |
| `LinkReconciliationCorrectionCommand(ScheduleId, ItemId, JournalRef, Meta)` | `MutationReceiptDto` | Same context/account effect, valid correction purpose; link is not approval/inclusion by itself. |
| `CalculateReconciliationProofCommand(ScheduleId, Meta)` | `ReconciliationProofDto` | Recompute basis/source/AJE treatment; no input-derived ledger total accepted blindly. |
| `SubmitReconciliationCommand(ScheduleId, ExpectedProof: Ref, Meta)` | `MutationReceiptDto` | Exact proof current, required answers/evidence and correction dispositions complete, residual zero. |
| `ReviewReconciliationCommand(ReconciliationReviewDto, Meta)` | `MutationReceiptDto` | Independent assigned reviewer; recompute proof/currentness; return/reject requires rationale. |
| `ReviseReconciliationCommand(Original: Ref, Reason, Meta)` | `MutationReceiptDto` | Preserve old schedule and review; new revision starts unapproved. |
| `CarryForwardReconciliationItemsCommand(Original: Ref, TargetContext, SelectedItemIds[], Meta)` | `MutationReceiptDto` | Explicit next-period basis; copy selected unresolved references only; no prior proof or approval. |
| `GetReconciliationRegisterQuery(Context, KindFilter, AsOfFilter, PageRequest)` | `PageDto<ReconciliationSummaryDto>` | Current and historic state, residual and responsible persons; scope applied before totals. |
| `GetReconciliationWorkspaceQuery(ScheduleId)` | `ReconciliationWorkspaceDto` | Header/items, live evidence/reflection applicability, formula, proof and review history. |
| `GetReconciliationSourceRowsQuery(ScheduleId, SourceSide, PageRequest)` | `PageDto<ReconciliationSourceRowDto>` | Exact ledger/subledger/statement rows and extraction provenance. |
| `ExportReconciliationQuery(ScheduleRef, Format)` | `ExportTicketDto` | Authorized exact revision/proof, currency, evidence/source IDs and no misleading current claim. |

`ReconciliationProofDto` includes signed source totals, included/excluded item effects, applied AJE IDs, residual, completeness, manifest and CanSubmit. Workspace DTO contains this proof plus projected header/items; summary contains kind, account, period, state/currentness and residual. SourceRow DTO preserves source identity/date/amount/currency/evidence location.

Use existing accounting-analysis/fieldwork validation; do not introduce automatic matching or bank APIs. A manually transcribed statement figure is permissible only with explicit provenance and required independent review; label it as such rather than claiming bank-system verification.

### 4. Inter-Module Lineage & Boundaries

M23 consumes immutable M21 sources and M22 eligibility/adjusted-snapshot DTOs. M22 alone creates and decides journal treatment. M23 asks M22 to create a draft through an explicit user action, then links the returned identity. No hidden journal creation occurs on “Calculate”.

M24/25 consume a reconciliation-readiness report containing required schedule IDs, manifest hashes, reviews and unresolved blockers. An irrelevant schedule is not a universal blocker; applicability must be approved in the context policy. If a required schedule becomes stale, current finalization is blocked immediately through the generation fence.

A raw-TB reconciliation and adjusted-TB reconciliation can coexist as different versions/bases; labels must make the basis clear. Never compare one module's raw figure with another's adjusted figure without a visible bridge.

### 5. Blazor UI Architecture (`.Web`)

```text
ReconciliationWorkspacePage
  ContextBanner / ScheduleRegister
  ReconciliationEditor(EditForm)
    LedgerBasisSelector / SupportingSourceSelector
    SignConventionAndAsOfPanel
    ReconciliationItemGrid → EvidencePicker / CorrectionLinkDialog
    RawExplainedResidualPanel
  ProofDetails / ReviewDialog / VersionComparison
```

Extend current accounting evidence/bank schedule surfaces. `ReconciliationState` holds schedule/proof refs, selected source and draft items. Ledger amount is server-derived/read-only. A user changes a supporting balance only in a draft with provenance. Recalculating doesn't auto-approve.

Expose exactly which correction is excluded because it already appears in the adjusted snapshot. Show unresolved source/evidence/management treatment with actionable links. A changed source puts the editor in a conflict/revision flow; no green approved residual until server review succeeds. Use field-level EditForm errors and the shared dirty/cancellation/reauthorization rules.

### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `ReconciliationProofTests` | `BankSignConventionIsExplicit`; `TimingItemDoesNotPostJournal`; `CorrectionAlreadyInSnapshotNotAddedTwice`; `UnknownReflectionBlocksProof`; `UnexplainedResidualBlocksSubmission`; `CreditsRemainVisibleByCounterparty`. |
| xUnit PostgreSQL | `ReconciliationRevisionTests` | `DifferentReviewerRequired`; `ChangedSourceOrDocumentMakesProofStale`; `ConcurrentCorrectionDecisionCannotApproveOldProof`; `ApprovedItemMutationDenied`; `CarryForwardDoesNotCopyApproval`. |
| bUnit | `ReconciliationEditorTests` | Read-only ledger source, correction-link pending state, row-date errors, visible formula/effects and reasoned return/new revision. |
| Playwright | `R2R23ReconciliationJourneys` | Bank schedule with deposit/cheque/fee bridge; accepted AJE linked once; return/rework; source replacement/reload; subledger control tie-out and denied cross-client document. |

**Exit gate:** the ledger/supporting proof is reproducible, has no unexplained residual or double-applied correction, and current independent review is required before it can satisfy a downstream gate.

<a id="module-24"></a>
## Module 24 — Financial Statements, Comparatives and Disclosures

**Business outcome:** produce a complete, policy-appropriate and source-traceable statement set, not merely a formatted trial balance.  
**Requirement coverage:** VP-040 and VP-041.  
**Sequence:** M24.1 approved layout/policy → M24.2 balances/comparatives → M24.3 cash/equity/notes → M24.4 deterministic statement set → M24.5 review/restatement.

### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** existing financial-package lines, validations, cash-flow/disclosure/equity/note records; `ReportingTaxonomyVersion`/nodes; `ClientPeriodRestatement`; existing deterministic financial-statement/calculation services. They are not missing simply because preparation and assembly currently share a `FinancialPackage` model. [R4][R5]

**NEW if necessary:** independently versioned `StatementLayoutVersion`, typed `StatementLineDefinition`, `CashFlowScheduleRevision`, `DisclosureNoteRevision`, `EquityMovementSchedule`, `StatementSetRevision` and `SubsequentEventAssessment`. Where existing records already persist these facts, extend them instead. If a StatementSet layer is introduced, it stores the one canonical calculated result and M25 references it; do not calculate a competing result in the package builder.

**Statement scope:** financial position; profit or loss and other comprehensive income in the applicable one/two-statement presentation; changes in equity; cash flows; notes; comparative information and an opening comparative financial position where the selected framework requires it. Avoid treating “BS/P&L” labels as the entire reporting obligation. [S6]

**Policy version:** framework, edition, jurisdiction/industry applicability, effective-from/to, early-adoption decision, taxonomy/layout family, required sections/disclosures, cash-flow classification policy, rounding, reviewed owner and policy hash. A user selects an approved policy; the engine does not decide which law applies.

For IFRS 18 periods, the policy must address its defined subtotals, classification/aggregation and applicable management-defined-performance-measure disclosures. A historical IAS 1 report retains its original policy. Transition comparative re-presentation is an explicit reviewed output, not an overwrite. Initial industry support must be enumerated; an unsupported bank/insurer or specialized business profile cannot inherit a general-company classification silently. [S1]

**Layout grammar:** allow Heading, MappedTaxonomyBalance, SumChildLines, Add/SubtractReferencedLines, approved Ratio with divide-by-zero behavior, and linked Schedule/Note display. Each numeric line has explicit source kind, sign, order, subtotal role and current/comparative applicability. No arbitrary C#, SQL, JavaScript, Excel expressions or free text interpreted as a calculation. Enforce acyclic references, valid same-layout targets, supported sections and no unintended inclusion of both parent totals and the same leaf values.

**Cash-flow contract:** explicit opening/closing cash and cash-equivalent membership; direct or indirect method selected from the approved supported profile; movement IDs, date, amount/currency, activity classification, cash/noncash flag, source reference, FX cash effect and rationale. First deliver one complete approved indirect profile; direct-method output is enabled only when its complete movement-input tests exist. No requirement to invent direct cash receipts from a closing TB.

Reconcile `Opening cash + operating + investing + financing + FX effect on cash = Closing cash`. Noncash investing/financing items are separately disclosed, not counted as cash. An indirect schedule needs supported movements/adjustments to distinguish working capital, acquisitions, FX, noncash changes and reclassifications. The edition controls the starting profit subtotal and interest/dividend classification. [S2]

**Equity contract:** reconcile opening equity by component + current profit/loss + OCI + owner contributions − distributions + approved prior-period adjustments = closing equity. Source books that already transfer profit to retained earnings need the approved source-closing convention, so profit is not added twice. Contributions/distributions cannot silently default to none.

**Notes:** NoteCode, PolicyRequirementId, applicability(Pending/Required/NotApplicable), text/table, linked statement lines, source/evidence, owner/reviewer, revision and status. Required notes need content; NotApplicable needs a reason and appropriate review. Include bounded related-party, going-concern and subsequent-event inputs without autonomous conclusions. Record financial-statement authorization date separately from generation date. IAS 10 adjusting/nonadjusting treatment is recorded by a qualified user. [S7]

**Comparatives/restatements:** distinguish AsIssued, Reclassified, RestatedError, PolicyTransition and ProspectiveEstimateChange. IAS 8 errors/policy changes and estimate changes have different temporal effects; the user supplies the approved treatment/evidence. Do not classify every difference as a current-year AJE. Keep original and revised package/statement references, period-by-period impact and disclosures. [S3]

**Lifecycle:** layouts/notes/schedules Draft → Submitted → independently Approved/Returned → successor revision. Statement set Calculated → ValidationBlocked or ReadyForReview → Reviewed; separate currentness. Any changed basis invalidates current review, not historical content.

### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend the current package/line/note/cash/equity/validation mappings. Add separate layout/statement revision tables only as justified by ADR-07. Keep one canonical result-to-source mapping.

Required persistence:

- Layout unique by framework/edition/family/version; line IDs stable within immutable layout, referenced lines same layout version; acyclic formula graph checked before publication.
- Note/schedule revisions scoped to context and exact period/currency; note code and line order unique in owner revision. Foreign keys bind evidential sources and policy requirement IDs.
- StatementSetRevision pins accepted raw source, adjusted snapshot/plan, mapping/universe, reconciliation readiness, current/prior context, layout/note/cash/equity/rounding/calculator refs and input manifest hash.
- Persist line-level amounts and lineage, not merely section totals. Compare line totals to canonical account contributions; normalized source records remain unchanged.
- Comparative linkage includes exact **prior revision**, selection mode and approved restatement/reclassification evidence. Do not use an unqualified “latest prior package” lookup.
- Validation facts persist rule version, inputs and outcomes. Review references exact result hash; positive validation is not a reviewer signature.
- Restrict deletion of all referenced configuration, sources, notes, revisions and reviews. Index context/period/state and reverse dependencies to find stale consumers.

Proposed migrations: `M24_StatementLayoutVersions`, `M24_SupplementarySchedulePins`, `M24_StatementSetManifest`, `M24_ComparativeRestatementEvidence`. Existing completed statement/package records get source-preserving adapters; they are not reconstructed with today's policy to populate new mandatory fields. Unprovable historical pins remain explicitly legacy.

### 3. Application & CQRS Contracts (`.Application`)

**DTOs:**

- `StatementLayoutDraftDto`: FrameworkPolicyRef, Name, VersionBaseRef?, StatementKinds[], Lines[LineId,Code,Label,Kind,Section,DisplaySign,Order,TaxonomyNodeIds[],ReferencedLineIds[],Operation,ScheduleKind?,ComparativeMode], RoundingPolicyRef.
- `ComparativeSelectionDto`: CurrentContext, PriorStatementOrPackageRef, Mode, RestatementCaseRef?, BasisBridgeRef?, Explanation.
- `CashFlowScheduleDto`: Context, Method, PolicyRef, OpeningCashSourceRef, ClosingCashSourceRef, CashAccountIdentities[], Movements[Id,Date,Amount,Currency,Activity,IsNonCash,SourceRefs[],Rationale], FxCashEffects[], StartingProfitLineRef for indirect.
- `EquityScheduleDto`: Context, OpeningEquitySourceRef, Components[], Movements[Id,ComponentId,Type,Amount,Date,SourceRefs[],Rationale], ProfitTransferConvention.
- `DisclosureDraftDto`: Context, NoteCode, PolicyRequirementId, Applicability, Text, TypedTable?, StatementLineLinks[], EvidenceRefs[], NotApplicableReason?.
- `StatementBuildInputDto`: Context, AdjustedSnapshotRef, MappingRef, LayoutRef, ComparativeSelectionRef?, CashScheduleRef, EquityScheduleRef, DisclosureRevisionRefs[], ExpectedNoteSetRevision, ReconciliationReadinessRef.
- `RestatementDraftDto`: Context, OriginalStatementOrPackageRef, ChangeType, AffectedPeriods[], ProposedJournalOrReclassificationRefs[], RevisedBasis, Reason, EvidenceRefs[], MethodologyDecisionRef.

| Command / query and input | TResponse | Validation and authoritative rule |
|---|---|---|
| `SaveStatementLayoutCommand(StatementLayoutDraftDto, Meta)` | `MutationReceiptDto` | Allowed grammar, unique lines/order, valid references/sections, no formula cycles or duplicated primary balances. |
| `PublishStatementLayoutCommand(LayoutId, DecisionInputDto, Meta)` | `MutationReceiptDto` | Complete policy-required sections, golden fixture validation, independent publication and immutable version. |
| `SelectComparativeBasisCommand(ComparativeSelectionDto, Meta)` | `MutationReceiptDto` | Same entity/compatible basis/currency; exact approved prior source; explicit restatement/reclassification. |
| `SaveCashFlowScheduleCommand(CashFlowScheduleDto, Meta)` | `MutationReceiptDto` | Defined cash population and support, policy-valid categories, dates/currencies and no duplicate movement IDs. |
| `SaveEquityMovementScheduleCommand(EquityScheduleDto, Meta)` | `MutationReceiptDto` | All equity components and owner movements explicit; no double profit transfer. |
| `SaveDisclosureNoteCommand(DisclosureDraftDto, Meta)` | `MutationReceiptDto` | Applicability/content/rationale, typed table bounds, safe markup and valid same-context line/evidence refs. |
| `ReviewSupplementaryScheduleCommand(ScheduleKind, ScheduleRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Allowlisted kind; exact current schedule, independent reviewer and documented reconciliation. |
| `ReviewDisclosureNoteCommand(NoteRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Required content/evidence, exact version, independent reviewer; NoApplicable is evidenced not a blank bypass. |
| `BuildStatementSetCommand(StatementBuildInputDto, Meta)` | `OperationTicketDto` | Complete approved inputs; capture membership revisions; worker uses immutable inputs and fenced publication. |
| `ReviewStatementSetCommand(StatementSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Every blocking validation passes, manifest remains current, appropriate independent reviewer. |
| `CreateRestatementCaseCommand(RestatementDraftDto, Meta)` | `MutationReceiptDto` | Original preserved; supported change classification and period impact; estimate change not silently retrospective. |
| `ApproveRestatementCaseCommand(CaseId, RevisedStatementSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Distinct reviewer, exact original/revised manifests, balanced impacts and complete required disclosure. |
| `GetStatementWorkspaceQuery(Context)` | `StatementWorkspaceDto` | Selected source, mapping/layout/supplementary readiness and eligible comparative options. |
| `GetStatementSetQuery(StatementSetRef)` | `StatementSetDto` | Typed lines, current/prior values, OCI/equity/cash/notes, validations and manifest. |
| `GetStatementLineageQuery(StatementSetRef, LineId, PageRequest)` | `PageDto<StatementContributionDto>` | Source accounts/journal contributions/mapping fractions plus schedule references; no hidden source access. |
| `GetComparativeDifferenceQuery(OriginalRef, RevisedRef)` | `ComparativeDifferenceDto` | Per-line original/revised/delta and treatment evidence; scoped same entity. |

StatementSetDto is the canonical immutable preparation input for M25, not a re-evaluatable expression tree. `StatementContributionDto` identifies SourceRef, AccountIdentity, JournalRef?, MappingAllocationId, Fraction, SignedAmount and currency. Workspace/ComparativeDifference DTOs contain exact refs and derived currentness. No client-supplied totals are treated as calculated results.

Reuse `FinancialStatementService` and the existing arithmetic engines. New layout operations require exhaustive pure tests; do not add a second SQL/JavaScript calculation inside Razor or a PDF renderer.

### 4. Inter-Module Lineage & Boundaries

M24 consumes M21 source/mapping, M22 adjusted snapshot and M23 required reconciliation status. M20 owns chart/taxonomy identity; M24 owns presentation semantics and layout publication. This prevents a bootstrap cycle where mappings require a statement result that itself needs mappings.

M24 returns a sealed **preparation snapshot** for M25. A new note or required applicability decision changes note-set membership and stales current output even when totals do not change. A prior-period mapping/restatement change must identify all current comparative consumers. The old as-issued comparative is still available; user selection cannot silently switch to a restated version.

Source-to-line, line-to-note and current-to-prior references form a directed acyclic dependency graph. Layout formatting edits affect artifacts; amount/classification edits additionally affect numeric validations. Both require a new reviewed output version according to their affected scope.

### 5. Blazor UI Architecture (`.Web`)

```text
FinancialStatementsWorkspacePage
  ContextAndPolicyBanner / CurrentPriorSelector
  StatementLayoutEditor → LineDefinitionDialog / LayoutValidationPanel
  StatementTabs (position, performance/OCI, equity, cash flows)
    StatementGrid → SourceContributionDrawer
  CashFlowEditor / EquityMovementEditor
  NotesRegister → DisclosureEditor / ApplicabilityReview
  ValidationSummary / IndependentStatementReview
  ComparativeRestatementWizard → AsIssuedVsRestatedComparison
```

`StatementWorkspaceState` stores exact selected versions and draft layout/schedule/note forms. Each form has its own EditContext; switching a tab preserves acknowledged context but cannot silently save another dirty form. Layout preview clearly distinguishes Draft/NotValidated from a reviewed statement set.

Unsupported or missing cash-flow movements display unavailable/required inputs, not illustrative numbers in a production report. Missing prior data displays unavailable rather than zero. Totals are formatted in UI but calculated only on the server. Source drill-down reauthorizes the underlying record; group-only access does not imply every client account is viewable.

Publication/review waits for server verification, and stale data disables review actions. A note-edit callback refreshes the statement readiness/manifest, not just the visible note text. Explicit dirty navigation and failed-generation messages preserve unsaved work without claiming a saved version.

### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `StatementLayoutAndPolicyTests` | `CyclicSubtotalRejected`; `ParentAndLeafDoubleCountDetected`; `DisplaySignDoesNotChangeCanonicalAmount`; `MissingRequiredNoteBlocks`; `IFRS18AndLegacyPoliciesRemainVersioned`. |
| xUnit numerical | `CashEquityComparativeTests` | `NonCashAcquisitionExcludedFromCashMovements`; `CashReconcilesWithSeparateFxEffect`; `ProfitTransferNotCountedTwice`; `MissingComparativeNotZero`; `PriorErrorDoesNotBecomeCurrentProfit`; `EstimateChangeUsesApprovedProspectiveTreatment`. |
| xUnit PostgreSQL | `StatementRevisionLineageTests` | `ChangedPriorMappingStalesCurrentComparative`; `NewRequiredNoteStalesSet`; `ReviewedSetRejectsMutation`; `RestatementRetainsAsIssuedReferences`; `ConcurrentUpstreamChangeBlocksReview`. |
| bUnit | `StatementDesignerTests`, `DisclosureEditorTests` | Grammar/row errors, required/N/A fields, dirty note switch, missing cash-flow state, exact lineage drawer and independent review controls. |
| Playwright | `R2R24StatementJourneys` | Create approved layout; compare current/prior; supported cash/equity/notes; independently review; change prior source and observe stale state; produce separate restatement preserving originals. |

**Exit gate:** all required statements/notes in the selected policy are supported, reconciled and reviewed for exact inputs. Unsupported methods or missing schedules block the relevant final output rather than being omitted silently.

<a id="module-25"></a>
## Module 25 — Financial Packages, Artifacts and Approval Decisions

**Business outcome:** assemble and review the exact set of files that will be handed to controlled release, with deterministic lineage and no silent regeneration.  
**Requirement coverage:** VP-042; integrates with existing reviews, completion and archive.  
**Sequence:** M25.1 composition → M25.2 render → M25.3 validate/seal → M25.4 exact-stage decisions → M25.5 release handoff.

### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** `FinancialPackage`, `FinancialPackageArtifact`, package lines/validations and `FinancialPackageReviewDecision`; existing render/build durable operations and renderer profiles. Do not classify the current OpenXML/PDFsharp-MigraDoc output as absent or replace it merely to implement a new facade. [R3][R4][R5]

**NEW only where missing:** immutable `PackageCompositionRevision`, `ArtifactManifest` and `RenderAttempt` facts, or equivalent extensions to existing package records. Separate the canonical accounting result from the ordered presentation/export definition.

A package definition contains scope kind Entity/Group, exact StatementSetRef or approved GroupResultRef, ordered section IDs, selected note refs, output formats, template/renderer profile versions, branding/locale/rounding versions, revision, predecessor, preparer and reason. Require exactly one scope branch: do not use null/Guid.Empty client IDs to fake a group context.

**Lifecycle:** DraftDefinition → ReadyToRender → Rendering → RenderFailed or ArtifactsReady → Sealed → UnderReview → Reviewed/Returned → ReadyForRelease. Currentness is separate. Existing status strings remain through a compatibility adapter; “Validated” must not suddenly mean all human decisions are complete.

Sealing proves immutable content, not professional approval. Accounting/management/partner decisions occur afterward against exact artifact identity; which stages apply comes from the approved service profile. A package returned for substantive changes gets a new definition/input revision. The old sealed package and its review remain unchanged.

**Two hashes avoid a circular definition:**

1. `ContentManifestDigest` hashes immutable source/statement/layout/note/policy/composition/template inputs **before rendering**. It may be embedded in each output.
2. `ArtifactManifestDigest` hashes the completed ordered artifact identities, byte hashes, sizes, MIME types and renderer versions. This is stored in the seal/review/release records, not inserted back into those same artifact bytes afterward.

Do not claim an artifact contains its own final content hash; that creates a self-reference. File checksum must be calculated from exact persisted bytes.

**Artifact invariants:** format genuinely matches extension/MIME, nonempty correct bytes, exact content input, renderer/template/font dependencies pinned, stable technical metadata and expected pages/sheets/sections. Every required format succeeds before sealing; partially persisted files are not a sealed package. Optional outputs are explicitly optional in the definition, not dropped after failure.

Preserve the repository's reviewed formula-free and controlled-workbook profiles. A controlled profile may contain only explicitly allowlisted application-owned formulas; importing or rendering client-supplied active formulas/macros is forbidden. Never launch Office or recalculate arbitrary uploaded spreadsheets.

**Review invariants:** actual person, scope, stage, authority, exact revision/generation/content/artifact digests and evidence are recorded. No self-approval across role switching. Management acknowledgement does not create an audit opinion, external signature or release. A changed required input invalidates current readiness even if the file still downloads as a historical artifact.

**Events:** `PackageDefinitionRevised`, `PackageRenderRequested`, `PackageArtifactsValidated`, `PackageSealed`, `PackageDecisionRecorded`, `PackageReadyForRelease`, `PackageSuperseded`. “Ready” is a derived/current eligibility fact, not an automatic release instruction.

### 2. Persistence & Migrations (`.Infrastructure`)

Extend current package/artifact/review configurations. Proposed composition rows link ordered section IDs and exact note/layout versions. Artifact records retain current storage approach, exact content bytes or immutable storage reference, MIME, byte length, SHA-256, renderer/template version and content basis.

Unique constraints: package logical ID/revision; composition position and section occurrence per revision; artifact kind/renderer/content digest per package revision; operation/deduplication identity; subject/stage/decision uniqueness rules appropriate to append-only reviews. Duplicate renderer retry returns the same stored output or an explicit deterministic conflict—never replaces earlier bytes.

Database FK scope binds every artifact and decision to the exact package and its entity or group owner. Referential integrity cannot rely on a text hash alone. Index artifact manifests by package and exact digest, review queues by permitted scope/stage/state, and dependencies by statement/package refs. DeleteBehavior.Restrict for all sealed/history data.

Existing PostgreSQL-backed artifact storage is a valid starting point. Do not introduce a new blob provider solely for this plan. Where approved storage is external to the database, write immutable staged bytes first, verify them, then atomically publish their database references. A failed metadata commit leaves a nonpublished orphan eligible only for controlled cleanup; it must not masquerade as a released file.

Proposed migrations: `M25_PackageCompositionRevision`, `M25_ArtifactManifestSeal`, `M25_StageDecisionPins`. Preserve exact old render bytes and hashes. Newly unavailable historical renderer inputs do not justify regenerating a “replacement identical” output.

### 3. Application & CQRS Contracts (`.Application`)

**DTOs:**

- `PackageInputDto`: discriminated `EntityStatementInput(Context, StatementSetRef)` or `GroupStatementInput(GroupId, ScopeVersionRef, ApprovedGroupResultRef)`; cannot contain both.
- `PackageDefinitionDraftDto`: PackageInputDto, PreviousPackageRef?, Sections[SectionId,Order,NoteRefs[]], RequiredFormats[], OptionalFormats[], TemplateProfileRefs[], Locale, BrandingRef, RoundingPolicyRef, Description.
- `PackageReviewInputDto`: PackageRef, ContentManifestDigest, ArtifactManifestDigest, Stage, Decision, EvidenceMode, EvidenceRefs[], Rationale, actual management contact where permitted offline.
- `ArtifactReadRequestDto`: ArtifactId and requested disposition inline/download; filename, storage key, MIME and authorization are server-resolved.

| Command / query and input | TResponse | Validation and handler rule |
|---|---|---|
| `CreatePackageDefinitionCommand(PackageDefinitionDraftDto, Meta)` | `MutationReceiptDto` | One scope/input kind, reviewed eligible statement/group result, unique ordered sections and approved template versions. |
| `RevisePackageDefinitionCommand(OriginalPackage: Ref, PackageDefinitionDraftDto, Meta)` | `MutationReceiptDto` | Original preserved, new revision/operation, change reason; no copied current approval. |
| `RequestPackageRenderingCommand(PackageDefinitionRef, Meta)` | `OperationTicketDto` | Complete current inputs, exact expected format manifest and durable parent operation. |
| `CancelPackageRenderCommand(OperationId, Meta)` | `MutationReceiptDto` | Existing worker cancellation discipline; do not cancel already published artifact history. |
| `ValidatePackageArtifactsCommand(PackageDefinitionRef, RenderAttemptId, Meta)` | `ValidationReportDto` | Server-read exact bytes, MIME/format, round-trip values, digests and required-file completeness. |
| `SealFinancialPackageCommand(PackageDefinitionRef, RenderAttemptId, ExpectedManifestDigest, Meta)` | `MutationReceiptDto` | Recompute all dependencies and byte manifest; all required formats valid; one atomic immutable seal. |
| `RecordFinancialPackageDecisionCommand(PackageReviewInputDto, Meta)` | `MutationReceiptDto` | Exact current sealed content, correct separate stage/person/scope, current evidence; return/reject reason. |
| `GetPackageWorkspaceQuery(PackageId)` | `PackageWorkspaceDto` | Definition/version list, readiness, operation states, artifacts and permitted stage decisions. |
| `GetPackageReviewQueueQuery(StageFilter, ScopeFilter, PageRequest)` | `PageDto<PackageReviewRowDto>` | Current actionable stages only; historical decisions shown distinctly; scope before counts. |
| `GetPackageManifestQuery(PackageRef)` | `PackageManifestDto` | Content and artifact manifests, lineage and historical/current state. |
| `GetPackageArtifactQuery(ArtifactReadRequestDto)` | `AuthorizedArtifactReadDto` | Reauthorize and verify digest before download; no client-chosen filesystem path. |
| `GetPackageReleaseReadinessQuery(PackageRef)` | `ReadinessDto` | M37-compatible exact package/artifacts/decisions with unresolved blockers; no release side effect. |

`PackageWorkspaceDto` contains definition projection, exact input refs, render attempts, validation summary, artifact metadata and review history. `PackageManifestDto` contains both hashes and ordered records. `AuthorizedArtifactReadDto` is an internal bounded stream/download descriptor with safe filename/MIME/hash, resolved only after authorization; public URLs require the approved short-lived scoped capability mechanism. No request returns raw tracked entities or an arbitrary storage path.

**Rendering execution:** capture immutable inputs in a short transaction; generate each format outside long-held DB locks using pure canonical DTOs; persist/reuse results; validate; then reauthorize/recompute manifest in the publication transaction. If a source changed while rendering, retain the artifact as a noncurrent attempt if policy permits, but fail publication with `generation.stale` and no misleading Ready state.

Use OpenXML and PDFsharp/MigraDoc already pinned. Pin render timestamps/identifiers from the package definition where included; never let `DateTime.Now`, random IDs, machine culture or changing font lookup produce unexplained file differences. Exact renderer version plus fixture determines output reproducibility. [R3]

### 4. Inter-Module Lineage & Boundaries

M24 owns accounting-result semantics; M25 owns composition/rendering/sealing. A renderer cannot fetch new GL data or choose a different mapping. Group results arrive from M26 in the same canonical statement schema with group-owned authorization, not as unscoped component rows.

M25 hands M37 an exact `PackageReleaseBasis` consisting of package revision, content/artifact digests and stage decision refs. M37 must revalidate freshness at freeze and issue. M38 archives those exact released bytes, not a freshly generated report. This blueprint does not implement M37/38 internally.

Purview and eSignature are excluded. Existing release/protection policies must be reconciled in ADR-08 for the chosen target profile; this is not permission to disable independent approval, source integrity, recovery or idempotency safeguards. A missing permitted external delivery provider blocks delivery, not local accounting calculation.

### 5. Blazor UI Architecture (`.Web`)

```text
FinancialPackageWorkspacePage
  PackageInputBanner / RevisionSelector
  PackageCompositionEditor(EditForm)
    SectionOrderList / RequiredSectionWarnings / OutputProfileSelector
  RenderOperationPanel / FailedFormatDetails
  ArtifactPreviewAndDownloads / ContentLineagePanel
  PackageSealDialog
  StageReviewPanel → ManagementPresentation / ReviewDialog
  ReleaseReadinessHandoff
```

Extend existing FinancialPackage and package-review pages. `PackageWorkspaceState` stores selected revision, definition draft and operation IDs. Section reorder is an optimistic local draft only; persisted composition requires a successful command. Preview labels distinguish draft, sealed, reviewed, historical and stale states.

Disable unsupported output selections with a reason. A generation failure displays the actual safe failure and preserves current version; it must not emit a download or success message. After an operation completes, query the durable published result—not a guessed next version. Dirty-state checks apply to composition changes, not read-only review dialogs.

Review dialog shows the exact revision/hash/format list and requires appropriate evidence/rationale; cannot approve a “current” pointer whose content changed behind the dialog. Client management presentation contains only explicitly shared allowed content; internal audit findings/costs do not leak through metadata or filenames.

### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `PackageManifestTests` | `OrderedSectionsChangeContentDigest`; `ArtifactHashDoesNotSelfReference`; `MissingRequiredFormatBlocksSeal`; `ApprovalBindsEntireArtifactSet`; `ReturnedPackageNeedsNewRevision`. |
| xUnit renderer | `FinancialArtifactRoundTripTests` | `XlsxIsValidAndValuesMatchStatementLines`; `DocxContainsExpectedTablesAndNoExternalParts`; `PdfIsReadableAndContainsExpectedTotals`; `ControlledFormulaAllowlistOnly`; `FixedInputsProduceDeterministicArtifacts`. |
| xUnit PostgreSQL | `PackagePublicationIntegrityTests` | `SourceChangeDuringRenderCannotPublish`; `SameOperationDoesNotDuplicateArtifact`; `TamperedBytesBlockSealAndDownload`; `PartialPersistenceDoesNotAdvancePackage`; `HistoricApprovedBytesRemainUnchanged`. |
| bUnit | `PackageAssemblyComponentTests` | Persisted section order, failed-format display, queued-vs-complete distinction, exact-hash review dialog and no optimistic seal. |
| Playwright | `R2R25PackageJourneys` | Create/reorder/reload/render all three formats, download and parse them; independent review; injected format/storage failure; source race; replacement revision and preserved predecessor; group-only access boundaries. |

**Exit gate:** every reviewed/release-ready package points to a complete immutable artifact set with current exact inputs, and every download returns those same authorized bytes.

<a id="module-26"></a>
## Module 26 — Consolidation, FX and Intercompany Eliminations

**Business outcome:** combine eligible component results into a separately governed group report, preserving each component's books and proving every group-only adjustment.  
**Requirement coverage:** VP-043–VP-046.  
**Sequence:** M26.1 group/perimeter → M26.2 component pins → M26.3 alignment/FX → M26.4 manual matching and journals → M26.5 calculation/review → M26.6 group package.

### 1. Domain Modeling (`.Domain`)

**EXISTING/EXTEND:** `ClientGroup`, `ClientGroupMembership`, group grants, current consolidation scope/component/package/match/journal/run records, exchange-rate/policy records and calculators. Existing `ConsolidationService` already exposes group/membership, scope, internal/external component, intercompany match, journal and report request contracts. Preserve them. [R6][R9]

Logical aggregate responsibilities:

| Aggregate / logical owner | Required records and responsibility |
|---|---|
| Group/perimeter revision | Group identity, parent, members, dates, ownership/economic interests, control assessment/evidence, method and independent acceptance. |
| Consolidation scope | Exact approved perimeter, reporting period/basis/presentation currency, policy/taxonomy/method, prior scope and rate-policy references. |
| Component acceptance | Entity, exact period/book/basis/package/statement/artifact refs, review eligibility, source revision/hash and compatibility bridges. |
| Exchange-rate set/policy | Currency pair/direction, rate date/type, precision, source/evidence, effective range, approval and versioned line-rate rules. |
| Group journal | Kind, participating entities/pairs, source matches, balanced group-currency lines, taxonomy, reason/evidence, independent decision. |
| Consolidation run | Exact membership/input manifest, component/translation/alignment/elimination rows, reconciliation and result hash, independent review. |

**Control is not ownership alone.** Store a qualified user's control conclusion and scope rationale. The first enabled acceptance profile remains one parent and one wholly-owned subsidiary with aligned reporting period/basis; ownership alone does not establish legal control. Do not produce results for Associates, minority interests, mid-period acquisition or hyperinflation merely by multiplying balances by a percentage. Existing advanced code and approved historical fixtures are preserved; each advanced production profile needs its own approved method/inputs/tests. [S5]

An effective perimeter is a dated directed graph. Reject self-membership, cycles, duplicate economic inclusion, missing parent, incompatible periods or ambiguous overlap. Where ownership changes during a period, an approved advanced profile must supply cutover data; the simple full-period profile blocks instead of inventing proration. Consolidation relationship groups remain separate from CRM contact groups.

**Component compatibility:** same reporting scope/date and approved accounting policy, or an explicit reviewed alignment bridge. Pin exact reviewed internal packages. Where current external component-pack support is used, require equivalent source reconciliation, review, taxonomy compatibility and immutable digest—never treat an uploaded unreviewed spreadsheet as an accepted component.

**FX policy:** keep transaction-to-functional remeasurement separate from translating a foreign operation to the group's presentation currency. Imported functional amounts are not silently recalculated. For the supported nonhyperinflation foreign-operation profile, assets/liabilities use the applicable closing rate; income/expenses use transaction-date rates or a documented representative average; equity uses historical/movement lineage and opening accumulated reserves. Resulting translation differences require an explained OCI/equity bridge. Never convert every statement line at one closing rate. [S4]

An average is allowed only where the approved method determines it adequately approximates transaction rates. Missing/zero/negative/wrong-direction rates block. Same-currency rate 1 is permitted because source and target are identical, not as an error fallback. Nonexchangeability, hyperinflation, acquisition accounting, NCI, disposals and nested groups require their separately supported policy; do not guess treatments.

**Eliminations:** include applicable investment/equity, intragroup receivable/payable, revenue/expense, dividends and intragroup cash-flow effects, plus supported unrealized-profit adjustments. The base golden profile must at least prove investment/equity and receivable/payable elimination. Unsupported consequential treatment is an explicit blocker, not “complete consolidation”. Group-only adjustments never post into entity books. [S5]

Manual matching records both source sides, original currency/amounts, translated amounts, matched portion and explained residual. Currency/timing differences are not automatically offset or hidden. Every journal is balanced in group currency, scoped to the exact perimeter/input version and included once. A source match cannot be consumed twice by overlapping elimination journals.

**Formula:** `Consolidated = translated compatible components + approved policy alignment + approved group adjustments/eliminations`. Each column reconciles at account/taxonomy, entity and group level. Cumulative translation reserve is a supported calculated result with a rate/equity bridge, not a general imbalance plug.

**Lifecycle:** perimeter Draft → Submitted → Approved; components Submitted/Returned → Approved; rate sets Draft → Reviewed; journals Draft → Submitted → Reviewed/Returned; run Calculated → Validated → IndependentlyApproved. Currentness is separate. A changed input requires a new run, never silently refreshing an approved result.

### 2. Persistence & Migrations (`.Infrastructure`)

Extract/extend existing configurations for groups/memberships/scopes/components, rate sets/observations/policies, matches/journals/lines and runs. A real entity FK proves membership; a label such as “Subsidiary A” is not enough. Group period representation must be documented: reuse the existing scope period only where it unambiguously models group reporting; do not borrow an unrelated client period simply to satisfy a required Guid.

Constraints:

- Unique group code within firm; membership revision/effective dates and owner scope; no overlapping active membership of the same entity in a perimeter interval.
- Unique component entity/package purpose in one scope revision. Typed component-source discriminator requires one internal package or accepted external pack, never both/neither.
- Component immutable pin includes exact accepted package hash/revision, accounting input hash, mapping/policy/period and approval refs. A caller cannot submit edited component rows as a package snapshot.
- Rate-set version unique; currency pair/type/date key; positive rate, valid effective range and immutable approval. Preserve precision consistently across .NET/PostgreSQL.
- Group journal header/lines share firm/group/scope/currency, taxonomy and counterparty identities. Balance and matched-source consumption validated under locks and protected afterward.
- Run manifest unique for scope/method/input digest; run-output rows reference immutable component/translation/journal contributions. Prior group result never overwritten.
- Restrict every component, package, match, journal, rate and historical run reference. Group-only reviewer read access does not create a raw component-artifact grant.

Proposed migrations: `M26_PerimeterRevisionPins`, `M26_ComponentCompatibilityEvidence`, `M26_RatePolicyLineage`, `M26_EliminationConsumption`, `M26_GroupResultManifest`. Existing approved advanced-method schedules/runs stay intact; no destructive simplification to the initial baseline profile.

Cross-client locking must obey ADR-05. Do not take a group lock and then acquire clients in arbitrary order while another operation does the reverse. Pin component identities and calculation input hashes; no database transaction remains open for rendering/group user review.

### 3. Application & CQRS Contracts (`.Application`)

**DTOs:**

- `GroupDraftDto`: Code, Name, ReportingPeriodRef, ReportingCurrency, Basis, FrameworkPolicyRef, MethodProfileId, ManagerUserId.
- `PerimeterDraftDto`: GroupId, PriorPerimeterRef?, Members[ClientId/EntityId,ParentEntityId?,EffectiveFrom/To?,OwnershipPercent,EconomicInterestPercent,ControlConclusion,Method,EvidenceRefs[]].
- `ComponentPinDto`: ScopeVersionRef, EntityId, SourceKind, InternalPackageRef? or ExternalPackRef?, AccountingInputDigest, PeriodRef, Basis, Currency, TaxonomyRef, MappingRef, CompatibilityBridgeRef?.
- `RateSetDraftDto`: GroupId, EffectiveRange, SourceDescription, Observations[FromCurrency,ToCurrency,Type,Date,Rate,EvidenceRef], PriorRateSetRef?.
- `TranslationPolicyDraftDto`: FrameworkPolicyRef, ProfileCode, LineRules[Taxonomy/AccountSelector,Closing/Transaction/Average/Historical/CarryForward,RateType,SourceRequirement], AverageRepresentativenessEvidence?, CtaTreatment, RoundingPolicy.
- `IntercompanyPairDto`: ScopeVersionRef, LeftEntityId, RightEntityId, LeftSourceLineRefs[], RightSourceLineRefs[], AccountNature, OriginalCurrency, OriginalAmounts, MatchedAmount, ResidualReason, EvidenceRefs[].
- `GroupJournalDraftDto`: ScopeVersionRef, Number, Kind, Currency, SourceMatchRefs[], Lines[LineId,EntityId?,TaxonomyNodeId,Debit,Credit,Description], EvidenceRefs[], Reason, PredecessorRef?.
- `ConsolidationRunInputDto`: ScopeVersionRef, ComponentPinRefs[], RateSetRef?, TranslationPolicyRef?, AlignmentRefs[], ApprovedJournalRefs[], ExpectedComponentSetRevision, ExpectedEliminationSetRevision, PriorGroupRunRef?.

| Command / query and input | TResponse | Validation + authoritative gate |
|---|---|---|
| `CreateConsolidationGroupCommand(GroupDraftDto, Meta)` | `MutationReceiptDto` | Explicit group-creation authority; approved method/profile; no automatic professional-role promotion. |
| `SavePerimeterDraftCommand(PerimeterDraftDto, Meta)` | `MutationReceiptDto` | Same-firm entities, valid dates/control evidence, no cycles/duplicates/unsupported ownership. |
| `ReviewConsolidationPerimeterCommand(PerimeterRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Independent allowed reviewer, supported exact scope/method and compatible periods. |
| `PinConsolidationComponentCommand(ComponentPinDto, Meta)` | `MutationReceiptDto` | Read server-owned package/approval/hash; membership and compatibility current; no caller-invented values. |
| `ReplaceComponentPinCommand(OldPinRef, ComponentPinDto, Meta)` | `MutationReceiptDto` | Explicit reviewed replacement, same intended member/purpose; new scope/run basis, history preserved. |
| `SaveExchangeRateSetCommand(RateSetDraftDto, Meta)` | `MutationReceiptDto` | Positive bounded rates, correct pairs/types/dates, no ambiguous duplicates or implied direction. |
| `ReviewExchangeRateSetCommand(RateSetRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Independent review, source evidence, complete required date/type coverage. |
| `PublishTranslationPolicyCommand(TranslationPolicyDraftDto, DecisionInputDto, Meta)` | `MutationReceiptDto` | Approved method and golden fixtures; historical/average/closing rules complete. |
| `TranslateComponentCommand(ComponentPinRef, RateSetRef, TranslationPolicyRef, Meta)` | `OperationTicketDto` | Required current rates and compatible method; deterministic translation and reserve bridge. |
| `RecordIntercompanyPairCommand(IntercompanyPairDto, Meta)` | `MutationReceiptDto` | Both source sides scoped/eligible, supported pair, matched amount bounded, residual explained or open. |
| `SaveGroupJournalCommand(GroupJournalDraftDto, Meta)` | `MutationReceiptDto` | Valid distinct lines/accounts/currency and source-consumption identity; draft totals visible. |
| `SubmitGroupJournalCommand(JournalId, Meta)` | `MutationReceiptDto` | Exact balance, evidence, current input set and no duplicate elimination. |
| `ReviewGroupJournalCommand(JournalRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Independent reviewer and live input/currentness checks; return reason mandatory. |
| `BuildConsolidationRunCommand(ConsolidationRunInputDto, Meta)` | `OperationTicketDto` | Full effective member/component/journal set, approved policy, no unsupported profile or unexplained required mismatch. |
| `ReviewConsolidationRunCommand(RunRef, DecisionInputDto, Meta)` | `MutationReceiptDto` | Recompute current complete input manifest; validate columns/equations; independent group review. |
| `PrepareGroupPackageCommand(ApprovedRunRef, PackageDefinitionDraftDto, Meta)` | `MutationReceiptDto` | Group branch of M25 contract only; no recalculation of component sources or borrowed client scope. |
| `GetConsolidationWorkspaceQuery(GroupId, ScopeVersionId?)` | `ConsolidationWorkspaceDto` | Perimeter/components/method/rates/journals/readiness and allowed actions. |
| `GetComponentReadinessQuery(ScopeVersionRef)` | `ComponentReadinessDto` | Required members, eligible exact package choices and explicit mismatches; no “first two clients”. |
| `GetTranslationBridgeQuery(TranslationRef, PageRequest)` | `PageDto<TranslationLineDto>` | Original, rate/purpose, translated value, historical/CTA/rounding lineage. |
| `GetIntercompanyExceptionsQuery(ScopeVersionRef, PageRequest)` | `PageDto<IntercompanyExceptionDto>` | Original/translated sides, matched and unmatched amounts with scope-safe references. |
| `GetConsolidationReportQuery(RunRef, PageRequest)` | `ConsolidationReportDto` | Component/alignment/elimination/group totals, input manifest and currentness; stale current report not asserted approved. |
| `GetGroupSourceDrillDownQuery(RunRef, OutputLineId, PageRequest)` | `PageDto<GroupContributionDto>` | Group-permitted contributions; raw component download requires separate component authorization. |

DTO projections retain IDs and all numeric bridge columns; `ConsolidationReportDto` includes exact run/method/perimeter/currency and equations. `ComponentReadinessDto` reports missing inputs without exposing unauthorized package content. `TranslationLineDto` includes SourceAmount, SourceCurrency, RateRef, RateType, TranslatedAmount, PresentationCurrency and calculated reserve/rounding category. GroupContributionDto identifies the approved source pin rather than returning its entire private file.

### 4. Inter-Module Lineage & Boundaries

Components arrive only from reviewed M25 packages or the existing equivalently accepted external-pack contract. M26 must not fetch live mutable TB totals to replace pinned package values. Map compatible taxonomy and policy versions through reviewed alignment, preserving original source meaning.

M26 owns group-only balances. An elimination never becomes an M22 entity journal automatically. A user who discovers an actual source error creates a separate entity AJE through M22, obtains a new reviewed component package, then explicitly repins and rebuilds group output.

Rate/perimeter/component/journal changes stale the **group** result and group approvals, not the entity's original accounting approval merely because it was translated. A component becomes stale on its own inputs according to entity policy; current group finalization cannot continue consuming that component without its required reacceptance.

Entity close and group close are distinct: do not require “all groups closed” before an entity can supply a reviewed component while simultaneously requiring “all entities closed” before a group can start. Define reviewed eligible component packages as the input gate; separate close decisions follow each owner's output policy.

### 5. Blazor UI Architecture (`.Web`)

```text
ConsolidationWorkspacePage
  GroupContextSelector / SupportedMethodBanner
  PerimeterEditor(EditForm) → OwnershipAndControlDialog
  ComponentIntakeGrid → PackagePinAndCompatibilityDialog
  RateSetEditor / TranslationPolicyReview / TranslationBridge
  IntercompanyReviewGrid → ManualMatchDialog
  GroupJournalEditor → IndependentJournalReview
  ConsolidatedColumnsGrid / ValidationPanel / RunHistory
  GroupReviewDialog → GroupPackageHandoff
```

Extend the existing Consolidation/AdvancedConsolidation routes; do not hide existing advanced history or make every profile available by a dropdown alone. `ConsolidationWorkspaceState` contains one selected group/scope/run, immutable DTOs and drafts. Changing group clears prior group/entity data first. Client-context cascade is not reused as group authority.

Clearly display reporting and source currencies, historical/average/closing rate kinds, unresolved pairs and input freshness. Manual matching has a preview and explicit confirm; no auto-match button. Elimination journal preview is group-only. Review cannot proceed on missing components or calculated artifacts still rendering.

Every editor has independent dirty tracking and exact expected revision. A component pin dialog displays eligibility from server data; users cannot paste arbitrary rows/hashes as an internal package. Source drill-down shows only allowed detail; denied deep links must not reveal hidden client names in errors.

### 6. Edge Cases, Security & Verification

| Layer | Proposed class | Required tests |
|---|---|---|
| xUnit domain | `ConsolidationPerimeterTests` | `ControlConclusionNotInferredFromPercentage`; `SelfCycleAndDuplicateEntityRejected`; `UnsupportedMinorityOrMidPeriodProfileBlocked`; `CreatingGroupDoesNotGrantPartnerAuthority`. |
| xUnit numerical | `ConsolidationCurrencyAndEliminationTests` | `DifferentRateTypesProduceExplainedCta`; `ClosingRateNotAppliedToAllEquity`; `MissingRateCannotDefaultToOne`; `InvestmentEquityAndReceivablePayableEliminateOnce`; `UnmatchedDifferenceNotPlugged`; `SourceEntitiesRemainUnchanged`. |
| xUnit PostgreSQL | `ConsolidationInputManifestTests` | `ForgedComponentRowsRejected`; `NewComponentOrJournalChangesMembershipDigest`; `ChangedRateOrPerimeterStalesReview`; `ConcurrentComponentReplacementCannotApproveOldRun`; `GroupOnlyReviewerDeniedRawArtifact`; `PriorRunRetained`. |
| bUnit | `ConsolidationWorkspaceTests` | Explicit currency/rate labels, unsupported profile blocking, component mismatch messages, manual matching confirmation and dirty group switch. |
| Playwright | `R2R26GroupJourneys` | Parent/subsidiary same-currency full elimination; approved FX profile; missing/invalid rate; changed component/rebuild; independent run review; group package export; deny unrelated group and component artifact. |

**Exit gate:** component columns plus approved alignment/eliminations reconcile exactly to the reviewed group result; sources are unchanged, missing methods/rates remain blocked, and M25 can render a group package with the exact same reviewed manifest.

<a id="execution"></a>
## 6. Sequential implementation plan and coordination gates

### 6.1 Ordered work packages

Each row is a bounded vertical slice: domain rule → persistence → handler/validator → existing Blazor route → tests → evidence. Do not mark an entire module complete after its entity classes or a screenshot exist.

| Step | Work package | Depends on | Required exit evidence |
|---|---|---|---|
| R2R-00 | Pin current SHA; inventory existing models/services/migrations/UI/tests; reconcile ADRs and scope conflicts | Owner review of this draft | Existing/new symbol ledger, policy approvals, preserved baseline tests and dependency licence decisions. |
| R2R-01 | MediatR facade, async validation adapter, explicit DTO mapping, one transaction-owner registry, bUnit test project | R2R-00 | One representative existing command/query migrated without behavior change or nested transaction. |
| R2R-02 | Scope/currentness/manifest/idempotency contracts and shared EditForm/state/dirty/conflict components | R2R-01 | Direct-handler denial, concurrency, same-document stale-route and atomic invalidation tests. |
| R2R-03 | M20 profiles, approved chart/taxonomy/dimension versions, periods/books and context activation | R2R-02 | Complete valid/invalid context lifecycle with historical preservation and source handoff. |
| R2R-04 | M21 bounded TB CSV/XLSX receipt, profile/column mapping, staging, validation and acceptance | R2R-03 | Real formats, parser attacks/limits, rejected replacement and sealed-membership tests. |
| R2R-05 | M21 resumable GL intake, opening/completeness, mapping/splits, paged drill-down and exports | R2R-04 | Opening+movement=closing; partial batches cannot pass; approved mapping/source lineage. |
| R2R-06 | M22 journals, independent technical/management decisions, reflection and adjusted snapshots | R2R-05 | Accepted/rejected/partial/reflected/unknown and replacement-base tests; no double application. |
| R2R-07 | M23 bank/subledger source schedules, typed items, correction links, proof/review/rework | R2R-06 | Raw and adjusted-basis proofs agree without duplicate corrections; zero unexplained residual. |
| R2R-08 | M24 versioned layout, current/prior balances, line drill-down and statement-set snapshots | R2R-07 | Approved mapping → exact current/prior lines; no parent/child double count; missing prior visible. |
| R2R-09 | M24 cash/equity schedules, notes, policy-edition rules, restatements and review | R2R-08 | Complete supported statement set including required disclosures; subsequent-event/treatment decisions recorded. |
| R2R-10 | M25 composition, renderer reuse, genuine outputs, validation, sealing and exact decisions | R2R-09 | All expected artifacts parse and reconcile; partial generation/tamper/source races fail closed. |
| R2R-11 | M20 close/amendment and entity package→review→release/archive boundary integration | R2R-10 | End-to-end entity lifecycle, fresh amendment and prior-period opening bridge without historical mutation. |
| R2R-12 | M26 approved group/perimeter and exact component acceptance | R2R-10 | Control/membership/scoping/currentness; no implicit Partner grant; no invented component values. |
| R2R-13 | M26 versioned rates/policy, translation bridges and manual intercompany review | R2R-12 | Same-currency and defined FX profiles; missing-rate/unsupported-method denial. |
| R2R-14 | M26 elimination journals, deterministic group runs, independent review and group packages | R2R-13 | Investment/equity and intercompany golden results, source nonmutation, group-only artifact scope. |
| R2R-15 | Cross-module replay, historical/currentness, permission revocation, concurrency, fault and recovery acceptance | R2R-11, R2R-14 | All approved criterion/test mappings executed on one release candidate with zero unresolved blocking failures. |
| R2R-16 | Staged deployment/migration rehearsal, operator training and production acceptance | R2R-15 | Approved environment, backups/restore, configuration and live Microsoft evidence where the enabled workflow requires it. |

The ordering is intentional: M20 declares close contracts early, but a complete close cannot be accepted until M24/25 exist. M25 entity artifacts precede M26 components; M26 later reuses the same artifact kernel for group output. This removes artificial module dependency cycles.

### 6.2 Cross-module API/port register

These are **proposed application interfaces or query/command contracts**, implemented by existing owning modules where possible. They must not be duplicate databases or direct foreign-aggregate repositories.

| Consumer → owner | Contract and minimum returned content | Boundary rule |
|---|---|---|
| R2R → Identity/Access (19) | Current actor/session epoch, explicit capabilities and firm/client/engagement/group scopes | Server authority only; no role/caller-supplied approver shortcuts. |
| M20/R2R → Engagements/Acceptance (4/27) | Client/entity, service profile, period/team scope, acceptance/hold/lifecycle and revision | Commercial acceptance alone does not permit professional work. |
| M21/M23/M24 → Documents/PBC (9/10/33) | Authorized exact document snapshot, version/hash/classification, availability and evidence status | A current working URL is not a reviewed immutable source. |
| M21–25 → M20 | Approved immutable reporting context/chart/dimension/taxonomy/policy and period state | Resolve IDs, never infer from selected screen labels. |
| M22–24 → M21 | Accepted source snapshots, approved mapping, completeness and source-set revision | Read-only; preserve raw rows. |
| M23/24 → M22 | Effective adjustment plan, contributions, eligibility/reflection and membership hash | Never independently count all posted journals in UI. |
| M24/25 → M23 | Required reconciliation readiness, exact proof/review refs and blockers | Reconciled is distinct from audit conclusion. |
| M25 → M24 | Canonical reviewed statement-set DTO with all source/supplementary/lineage refs | Renderer cannot recalculate or fetch newer balances. |
| M26 → M25 | Approved component accounting/package manifest and scoped canonical values | No component mutation or ungranted raw-file read. |
| M25 → M26 | Approved GroupResultRef and canonical group statement DTO | Discriminated group scope; no fake client ID. |
| M22/24/25 → Audit/Review (28/34/35/36) | Materiality/context, current human conclusion and permitted decision refs | Numeric threshold cannot auto-create professional opinion. |
| M25 → Completion/Release (37) | Exact content/artifact manifest plus applicable stage decisions/currentness | Freeze/issue remain separate rechecked commands. |
| Release → Records (38) | Immutable release/artifact IDs and predecessor/amendment lineage | Archive exact bytes; no Purview or deletion guarantee. |
| Reporting (16) → R2R | Scoped immutable result/operation summaries and exact-revision drill-down contracts | Currency/date totals reconcile; no unscoped cross-client cache. |

### 6.3 Agent coordination protocol

A principal/coordinator owns the contract registry, accounting-policy decisions, shared kernel, migration order and integration acceptance. Each module has one accountable implementation owner. A coding agent may parallelize isolated UI/tests after shared contracts are accepted; it may not independently change a shared DTO, taxonomy meaning, money policy or migration baseline.

For each slice produce a short implementation record containing:

1. Current inspected SHA, existing symbols reused, exact requirements and approved ADRs.
2. Command/query DTO fields, authority, state transition, transaction owner and source/output manifests.
3. Files to change; additive migration and prior-schema preservation plan.
4. Named unit/integration/component/browser assertions and fixed expected outcomes.
5. Observed build/test results, code commit, evidence links, limitations and reviewer decision.

Before every PR rebase/reconcile the current repository; do not assume this pinned snapshot is still head. Never fabricate an API or class because its name appears in this proposal. Search first, adapt the existing equivalent, or record the proposed new type explicitly.

Serialize migrations and shared-contract changes. Parallel agents must not generate competing snapshots of `AuditSphereDbContext` or add duplicate config classes. Do not suppress a failed financial assertion, weaken a permission condition or replace a expected financial result with the implementation's own output to make tests green.

Use branch-per-slice and reviewed PRs, preserving the project's merge authorization process; the user-established AuditSphere merge codeword is `AUDITSPHERE-MERGE-APPROVED`. This documentation request does not authorize any commit, push, merge, deployment or production data change.

### 6.4 Requirement traceability to the supplied tracker

| Original story | Production module ownership | Required production adaptation |
|---|---|---|
| VP-034 | M20 | Real profiles/charts/periods/books; server authority and database history replace demo state. |
| VP-035 | M21 | Server-bounded CSV/XLSX intake and immutable receipts; not browser-only fake source identity. |
| VP-036 | M21 | Persisted GL batches, explicit openings and completeness with source lineage. |
| VP-037 | M21, consumes M20 | One approved mapping engine; exact splits, unmapped queue and source drill-down. |
| VP-038 | M22 | Real journal decisions/reflection/adjusted snapshots; no external source-ledger posting implied. |
| VP-039 | M23 | Typed manual reconciliations and proof/review; no bank feed or automated matching. |
| VP-040 | M24 | Policy-approved complete statement layouts and comparatives. |
| VP-041 | M24 | Source-backed cash/equity movements, notes and human disclosure review. |
| VP-042 | M25 | Durable real file artifacts and exact-byte decisions; no signing provider. |
| VP-043 | M26 | Approved effective control/perimeter, not CRM relationship groups. |
| VP-044 | M26 | Approved component package pins and explicit rate policies; no external FX feed. |
| VP-045 | M26 | Balanced manual group journals, exact source pairs and visible unmatched differences. |
| VP-046 | M26 + M25 artifact kernel | Reconciled group outputs, independent review and separately scoped package/export. |

All four original criteria per story are retained in Appendix A below. Prototype “Verified” does not transfer to these production adaptations. Additional controls introduced by this blueprint have their own R2R test IDs; do not renumber or replace original VP identifiers.

<a id="fixtures"></a>
## 7. Integrated numerical fixtures and failure journeys

### 7.1 Golden accounting fixtures

The examples below are **proposed synthetic QA fixtures**, not client data, market rates, or approved professional treatments. Have the methodology owner approve exact inputs/expected outputs. Keep them separate from the repository's existing approved fixtures; preserve both with explicit names.

| Fixture | Inputs | Exact expected outcome |
|---|---|---|
| GOLD-R2R-01 — raw/adjusted TB | Signed raw rows: Cash 10,000; AR 3,000; Equipment 5,000; AP −4,000; Share capital −10,000; Revenue −8,000; Expense 4,000. AJE: Dr depreciation expense 100, Cr accumulated depreciation 100. | Raw signed total 0; delta total 0; adjusted signed total 0. Raw assets 18,000/profit 4,000; adjusted assets 17,900/profit 3,900/equity 13,900/liabilities 4,000. Raw rows unchanged. |
| GOLD-R2R-02 — mapping micro-unit | Amount 0.010000 split by 0.333333, 0.333333, 0.333334, stable target order; six-decimal ToEven and last-destination residual. | Allocated 0.003333, 0.003333, 0.003334; sum 0.010000. No unexplained account or loss of 0.000001. |
| GOLD-R2R-03 — bank proof | Ledger 10,000; supporting bank statement 10,200; deposit in transit +500; outstanding cheque −800; accepted unreflected ledger bank fee −100. | Explained ledger 9,900; explained support 9,900; residual 0. If ledger basis is already adjusted to 9,900, fee is not added again. |
| GOLD-R2R-04 — cash flow | Opening cash 1,000; operating +200; investing −300; financing +100; FX cash effect +10; separate noncash lease 500. | Closing cash 1,010; noncash lease excluded from cash arithmetic and separately disclosed. |
| GOLD-R2R-05 — equity | Opening 5,000 + profit 900 + OCI 50 + contributions 400 − distributions 200 − approved opening error adjustment 100. | Closing equity 6,050; error adjustment is not current profit. |
| GOLD-R2R-06 — whole-owned group | Parent: Cash 5,000; IC AR 1,000; Investment 2,000; capital/retained equity −8,000. Subsidiary: Cash 1,500; inventory 1,500; IC AP −1,000; capital −2,000. | Combined assets 11,000. Eliminate IC AR/AP 1,000 and investment/subsidiary equity 2,000: group assets 8,000, liabilities 0, parent equity 8,000. Both source TBs unchanged. |
| GOLD-R2R-07 — FX bridge | Synthetic foreign entity: cash 100; capital −80; revenue −40; expense 20. Closing rate 4, historical capital rate 3.5, representative income/expense rate 3.6. These rates are test values, not market quotations. | Cash 400; capital −280; revenue −144; expense 72; calculated translation reserve −48; signed sum 0. Equity = capital 280 + profit 72 + reserve 48 = 400. A universal closing rate would give a different and prohibited treatment. |
| GOLD-R2R-08 — source reflection | New accepted raw TB contains GOLD-01's 100 depreciation AJE; exact reflection evidence says Reflected. | Reporting delta for that journal is 0, assets/profit remain 17,900/3,900, not 17,800/3,800. Old raw and adjusted snapshots remain available. |

### 7.2 Complete integrated acceptance journeys

| ID | Journey / fault | Required observed result |
|---|---|---|
| R2R-AT-01 | New entity context → TB/GL → mapping → AJE → reconciliation → statements → package → close | Every displayed/exported amount reconciles to the same scope and exact inputs; no draft step is silently skipped. |
| R2R-AT-02 | Same account code and period label in two clients | No shared data, authority, cache, row, mapping, export or evidence leakage. |
| R2R-AT-03 | Existing browser circuit navigates to another unauthorized context | Prior content/IDs/counts/download links clear before new load; no full-page reload needed to enforce denial. |
| R2R-AT-04 | User grant/session revoked after opening approval dialog | Handler denies and UI clears protected content; no approval persists. |
| R2R-AT-05 | Two reviewers act on same expected revision | Unique/optimistic concurrency rule yields one applicable decision or explicit conflict, not duplicate contradictory approval. |
| R2R-AT-06 | Duplicate command, same OperationId/body | One mutation/result; retry reauthorizes. Changed body with same ID conflicts. |
| R2R-AT-07 | Import interrupted, duplicate/resumed/out-of-order chunk | Consistent staged state; no source seal before full expected counts/hash; prior accepted source remains. |
| R2R-AT-08 | Macro/formula/external-link/fake-XLSX/decompression attack | Bounded failure with safe issue list; no parser execution, network request or partial acceptance. |
| R2R-AT-09 | Missing opening data or GL-only account | Completeness stays incomplete; missing is not zero; UI and export agree. |
| R2R-AT-10 | New accepted AJE after initial plan | Adjustment-set membership changes; old statement/package cannot be newly approved as current. |
| R2R-AT-11 | Partial/rejected management AJE | No unbalanced subset applied; remainder visible for human evaluation; fresh balanced revision when needed. |
| R2R-AT-12 | Replacement TB includes a former adjustment | Fresh reflection decision; no double count; old result preserved. |
| R2R-AT-13 | Approved reconciliation supporting document replaced | Old proof/review historical; currentness stale and downstream finalization blocked. |
| R2R-AT-14 | Cash-flow noncash movement included by mistake | Validation identifies classification/bridge mismatch; no balancing cash plug. |
| R2R-AT-15 | Comparative mapping/source changes after current review | Current dependent statement stale; as-issued historical output unchanged. |
| R2R-AT-16 | Error restatement versus estimate change | Approved distinct period treatment, disclosure and lineage; no universal retrospective or current-P&L shortcut. |
| R2R-AT-17 | Required disclosure removed/added after build | Set-membership hash changes even when balances do not; prior review no longer authorizes current package. |
| R2R-AT-18 | XLSX succeeds, DOCX/PDF generation or artifact publication fails | Package not sealed/advanced; successful intermediate files explicitly nonpublished; retry deterministic. |
| R2R-AT-19 | Source acceptance races package rendering | Worker cannot publish against obsolete generation; no current success notification. |
| R2R-AT-20 | Artifact bytes tampered after rendering | Digest check blocks seal/review/download/release handoff as applicable; historical identity not rewritten to match tamper. |
| R2R-AT-21 | Closed period receives import/journal command | Denied; authorized amendment creates successor with fresh required decisions. |
| R2R-AT-22 | Group-only reviewer requests component private artifact | Group result allowed, source file denied; no sensitive filename/details leak. |
| R2R-AT-23 | Missing rate, rate direction reversed, unsupported ownership | No final figures asserted; clear bounded method/input requirement. |
| R2R-AT-24 | Intercompany mismatch or overlapping journal source inclusion | Unmatched difference visible; duplicate consumption rejected; no plug. |
| R2R-AT-25 | Component package replaced during group approval | Manifest mismatch blocks old run; explicit new pin/run required; old group result retained. |
| R2R-AT-26 | Entity close/group close sequencing | No mutual prerequisite deadlock; reviewed components feed group work without inventing approvals. |
| R2R-AT-27 | Upgrade prior schema with ambiguous/null context | Data preserved, unresolved links quarantined, no fabricated approval/currency/book. |
| R2R-AT-28 | Restore DB/artifacts and resume interrupted durable work | Exact manifest/digest reconciliation; stale leases fenced; no duplicate external effects or falsely completed operations. |
| R2R-AT-29 | Keyboard-only and responsive forms | Labels, field/summary errors, focus, line add/remove, dialog cancellation and dirty navigation usable without losing context. |
| R2R-AT-30 | Entity and group package delivered to existing release/archive contracts | Exact sealed/reviewed artifacts handed off; no source recomputation, Purview requirement or signature-provider side effect introduced. |

These supplement, not replace, the original VP criteria and existing repository tests. Automated source mentioning a test ID is not evidence that the complete journey ran.

### 7.3 Verification layers and evidence ledger

Use xUnit for pure/domain calculations and actual PostgreSQL integration for FKs, uniqueness, triggers, transaction isolation and concurrency. An InMemory provider cannot establish those claims. bUnit covers component parameters, cascading context, EventCallbacks, EditForm validation and transitions; Playwright covers the actual Blazor host with seeded isolated database state, real network/API behavior and downloaded files. [T8][T9]

Use accessible role/label locators and auto-retrying assertions, not sleeps or UI force actions that conceal application defects. Keep fixture setup separate from user actions being verified. Calculation expectations must be independently derived, not `expected = implementation.Calculate(...)`.

| Evidence field | Required value |
|---|---|
| Requirement | Original VP/AC ID plus new R2R rule ID where applicable |
| Source | Exact code commit and migration schema |
| Scenario | Fixture/version, role/person, scope, input/source/artifact identities |
| Assertion | Named method/browser step; expected and observed result |
| Execution | Command, run ID, environment, timestamp, outcome; not merely a filename |
| Rework/security | Appropriate denial, stale, retry, concurrency and historical-preservation checks |
| Review | Independent reviewer and decision, blockers, deferred approved scope |

An assembly/module can be declared complete only when its own positive/negative/rework criteria and consuming-module contract tests pass. Compilation, lines of code, test count, screenshot count or a manually labelled “Verified” badge is insufficient.

<a id="handoff"></a>
## 8. Production readiness, operator handoff and execution boundaries

### 8.1 Release-candidate verification sequence

The implementation team must execute, not merely document: exact SDK/pinned locked restore; full Release build; pure and PostgreSQL tests; bUnit tests; real Blazor Playwright journeys; migration/model-drift check; representative import/render/group performance profile; dependency/secret/output-safety checks; restore rehearsal; complete original-criterion crosswalk. Use the repository's current approved commands and update evidence with observed results only.

Suggested command intent, not an executed result in this document: locked solution restore; `dotnet build AuditSphereOps.slnx --configuration Release`; `dotnet test AuditSphereOps.slnx --configuration Release`; EF `has-pending-model-changes`; approved restore-drill script. Ensure test configuration/build flags agree—do not run stale Debug binaries while claiming a Release candidate.

Performance acceptance must set measured targets for upload size, rows, memory, request latency, group members, artifact size and concurrent sessions in an ADR. Preserve the inspected GL caps until benchmarks justify changes. Cancellation, progress and bounded paging are required even where latency targets are not yet approved. No throughput number is claimed from this blueprint.

Telemetry tracks operation ID, duration, safe rule code, row counts and retries—not client financial narratives, raw files, secrets or full account details. Background services use approved service identities; no current browser circuit is their security context. User-requested operation identity is retained, and authority is freshly checked at enqueue/publication according to policy.

### 8.2 Deployment and external integrations

This is a production architecture, so actual identity and document integrations eventually require real approved environment evidence. However local R2R calculations, DB invariants and file rendering must be verifiable without accessing a production tenant. Separate local correctness, approved methodology, environment readiness and provider acceptance.

Use existing Entra/Graph/SharePoint interfaces, selected-resource authorization and credential custody. Optional mail/OneDrive failure must not masquerade as successful setup or corrupt accounting work. No new provider, live exchange-rate feed, Purview adapter or eSignature requirement is added. Basic stored accounting artifact integrity is not a promise of legal retention or regulator certification.

Do not disable existing safety fences just to unblock a test. Where older repository scope requires Purview/signature evidence, the owner must approve the scoped policy change and its regression matrix. Continue preserving human reviews, exact manifests, archive history and safe recovery.

### 8.3 Acceptance decision checklist

- [ ] Module ownership/DTO/transaction/migration ledger reconciles to current source.
- [ ] All approved accounting policy/profile decisions are explicit and versioned.
- [ ] Domain remains free of EF/MediatR/UI/provider dependencies.
- [ ] MediatR migration preserves existing guarded transactions and current errors.
- [ ] Every planned command/query has a validator, capability check and exact scoped DTO contract.
- [ ] Every financial row and artifact traces to preserved accepted input versions.
- [ ] Immediate generation fences plus manifest rechecks prevent stale approval/publication.
- [ ] All agreed entity/group golden numbers and failure/rework journeys pass.
- [ ] Database upgrade, immutable history, concurrency and restore evidence are recorded.
- [ ] Forms, scoped UI state, dirty tracking and live circuit revocation are accepted.
- [ ] Downloads are genuine approved-format outputs with verified exact byte identity.
- [ ] Required source/schema/provider/production checks are not replaced by mocks or old test runs.
- [ ] Scope exclusions remain exclusions; no fake zero values, hidden unresolved items or automatic professional decisions.
- [ ] Independent code/financial review approves the exact release candidate.

### 8.4 Coding-agent handoff instruction

Implement only the next approved work package in §6.1. Before changing code, inspect the current commit, AGENTS.md and relevant existing models/services/tests; produce a reuse/new-symbol list. Preserve the separation of firm books, client reporting and consolidation. Use the defined CQRS contracts, metadata and validator rules, not invented endpoints or duplicate models. Keep one transaction owner and one canonical calculation owner. Stop the affected feature and report a concrete unresolved policy or contract contradiction rather than inventing an accounting treatment or weakening a gate. Add and execute the required positive, failure, concurrency, scope and rework tests. Update progress only from observed evidence. Do not merge or perform tenant/production operations without the separate authorization required by the project.

<a id="sources"></a>
## 9. Source register and provenance

### 9.1 Supplied requirements

**[F1]** `Progress_Tracker.md`, supplied by the user. It contains the original 39-module/64-story scope, including VP-034–VP-046 and the prior prototype progress narrative. This blueprint uses the requirements, **not its prototype completion statuses**, as the functional basis. The latest user instruction explicitly requests the production .NET/CQRS/Blazor implementation design.

### 9.2 Inspected repository sources

All repository URLs below pin `ba1a3ec23335b40667b679e4f0cd5f66b9e723b9`. The architecture review read selected source sections, not every method in the solution. No application execution or exhaustive repository audit is asserted.

- **[R1]** [Pinned production commit](https://github.com/nirzaf/AuditSphere/commit/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9).
- **[R2]** [AGENTS.md](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/AGENTS.md): three financial boundaries, architecture, immutable records, scope and durable operations; historical Purview/signing references require the scope ADR.
- **[R3]** [Directory.Packages.props](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/Directory.Packages.props): actual central dependencies; MediatR/bUnit additions are proposed.
- **[R4]** [Domain Accounting.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Domain/Accounting/Accounting.cs): sources, mappings, journals, plans and financial-package identities.
- **[R5]** [AuditSphereDbContext.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.cs): current authoritative context, application interfaces, DbSets and persistence conventions.
- **[R6]** [Domain ClientAccounting.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Domain/Accounting/ClientAccounting.cs): profiles, periods/books, charts, groups, capability profiles and restatements.
- **[R7]** [ClientAccountingService.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Application/Accounting/ClientAccountingService.cs): existing request shapes, GL limits and transaction-owning period operations.
- **[R8]** [Domain Shared Kernel.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Domain/Shared/Kernel.cs): existing CommandResult/error contracts, six-decimal midpoint-to-even MoneyPolicy and SHA-256 helpers.
- **[R9]** [ConsolidationService.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Application/Accounting/ConsolidationService.cs): component, rate/scope/match/journal/report contracts and current group-creation grant behavior.
- **[R10]** [FinancialStatementService.cs](https://github.com/nirzaf/AuditSphere/blob/ba1a3ec23335b40667b679e4f0cd5f66b9e723b9/src/AuditSphereOps.Application/Accounting/FinancialStatementService.cs): confirmed existing owner/service seam; inspect its complete relevant methods before implementation.

### 9.3 Primary standards research

Consulted 24 September 2026. Official overviews are design research, not a substitute for licensed complete standards, local law or professional sign-off.

- **[S1]** [IFRS Foundation — IFRS 18](https://www.ifrs.org/issued-standards/list-of-standards/ifrs-18-presentation-and-disclosure-in-financial-statements/).
- **[S2]** [IFRS Foundation — IAS 7](https://www.ifrs.org/issued-standards/list-of-standards/ias-7-statement-of-cash-flows/).
- **[S3]** [IFRS Foundation — IAS 8](https://www.ifrs.org/issued-standards/list-of-standards/ias-8-basis-of-preparation-of-financial-statements/). Use the edition effective for the selected period.
- **[S4]** [IAS 21 official overview](https://www.ifrs.org/issued-standards/list-of-standards/ias-21-the-effects-of-changes-in-foreign-exchange-rates/) and [2024 issued HTML, particularly paragraphs 21–23, 35–40](https://www.ifrs.org/content/dam/ifrs/publications/html-standards/english/2024/issued/ias21.html). Historical edition explicitly identified; current applicability reviewed before policy activation.
- **[S5]** [IFRS 10 official overview](https://www.ifrs.org/issued-standards/list-of-standards/ifrs-10-consolidated-financial-statements/) and [2024 issued HTML, particularly control requirements and B86–B87](https://www.ifrs.org/content/dam/ifrs/publications/html-standards/english/2024/issued/ifrs10.html).
- **[S6]** [IFRS Foundation — IAS 1](https://www.ifrs.org/issued-standards/list-of-standards/ias-1-presentation-of-financial-statements/).
- **[S7]** [IFRS Foundation — IAS 10](https://www.ifrs.org/issued-standards/list-of-standards/ias-10-events-after-the-reporting-period/).

### 9.4 Comparable-system research

- **[P1]** [Microsoft Dynamics 365 Finance — Close the general ledger at period end](https://learn.microsoft.com/en-us/dynamics365/finance/general-ledger/close-general-ledger-at-period-end).
- **[P2]** [Oracle Account Reconciliation — Reviewing Reconciliations](https://docs.oracle.com/en/cloud/saas/account-reconcile-cloud/raarc/reconcile_user_review_114xd902a65f.html).
- **[P3]** [Caseware — Mapping the Trial Balance](https://www.caseware.com/docs/en/desktop/jazzit/jazzit-fundamentals/implementing-the-jazzit-templates/mapping-the-trial-balance).

### 9.5 Primary engineering documentation

- **[T1]** [MediatR official repository and registration/licensing documentation](https://github.com/LuckyPennySoftware/MediatR).
- **[T2]** [FluentValidation — asynchronous validation](https://docs.fluentvalidation.net/en/latest/async.html).
- **[T3]** [EF Core — handling concurrency conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
- **[T4]** [Npgsql EF Core — concurrency tokens](https://www.npgsql.org/efcore/modeling/concurrency.html).
- **[T5]** [Blazor form validation](https://learn.microsoft.com/en-us/aspnet/core/blazor/forms/validation?view=aspnetcore-10.0) and [input components](https://learn.microsoft.com/en-us/aspnet/core/blazor/forms/input-components?view=aspnetcore-10.0).
- **[T6]** [Blazor with EF Core and per-operation context lifetimes](https://learn.microsoft.com/en-us/aspnet/core/blazor/blazor-ef-core?view=aspnetcore-10.0).
- **[T7]** [Blazor routing/navigation and NavigationLock](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing?view=aspnetcore-10.0).
- **[T8]** [bUnit — writing component tests](https://bunit.dev/docs/getting-started/writing-tests.html).
- **[T9]** [Playwright .NET — assertions](https://playwright.dev/dotnet/docs/test-assertions).

## 10. Contract clarifications for implementation review

**Filter and enum shorthand used in request tables:**

| Shorthand | Required typed meaning |
|---|---|
| `StatusFilters` | Allowlisted TechnicalState[], ManagementState[], ReflectionState[], Currentness[], optional date range; never a raw query expression. |
| `ScopeFilter` | Optional ClientId/EngagementId or GroupId, validated as a subset of server-granted scope; no client/group wildcard expansion. |
| `KindFilter` / `SourceSide` | ReconciliationKind enum / Ledger or Supporting enum. |
| `AsOfFilter` | Explicit DateOnly for time-sensitive queries; not a hidden switch to today's balances. |
| `AccountFilter` | Stable account IDs/codes and approved dimension-value IDs belonging to the selected chart/context. |
| `StageFilter` | Explicit existing/approved accounting, management, partner or group-review stages, not caller-defined role names. |
| `ScheduleKind` | CashFlow or Equity for the shared supplementary-schedule review request; unknown values rejected. |
| `Format` / `ExportFormat` | An allowlisted CSV/XLSX/DOCX/PDF enum, further restricted by each query and output profile. |

**Transaction adapter condition:** a facade is not accepted merely because it calls an old method. Operation identity, current authority, input fences and invalidation/evidence must be committed with the business mutation inside that method's transaction. Do not add an idempotency record outside the commit or declare success before the original service commits. Extract the smallest transactional core where necessary to satisfy this condition.

**Close versus input-set selection:** client/period locking must prevent a new required adjustment/reconciliation/disclosure from being added between readiness calculation and close. Capturing references to existing rows without a collection-membership revision does not satisfy that requirement.

**No implicit functional expansion:** the complete R2R lifecycle includes handling unsupported inputs honestly. It does not promise every financial-industry, GAAP, business-combination, hedge, tax measurement or statutory filing engine. Each supported framework/method profile must be explicitly approved, fully implemented and tested; unsupported profiles cannot yield claimed-compliant output.

**Document-maintenance rule:** update the exact contract, affected source mapping, validator, migration and consuming-module tests together. A renamed DTO or changed enum without consumer/test changes is an incomplete implementation, even when its individual project compiles.


### 10.1 Bounded resource-policy proposal

Create or reuse an approved versioned resource policy; every intake/layout operation reports the applicable limits. The following are **proposed initial upper bounds**, not observed capacity results. A stricter existing repository limit wins until a reviewed change replaces it. Do not increase current limits implicitly.

| Resource | Proposed initial bound / treatment |
|---|---|
| Interactive page | 200 rows; source exports through bounded streaming rather than circuit materialization |
| TB source | 50 MiB received file and 100,000 normalized rows, subject to stricter existing parser caps |
| GL source | Preserve current 100,000 transaction / 500,000 line ceiling and 10,000 transaction / 50,000 line chunk ceiling |
| Upload transport | Reuse current authenticated chunk limit; total compressed and expanded limits both mandatory |
| XLSX expansion | 200 MiB maximum expanded input, bounded entry count/ratio/shared strings; reject before unbounded allocation; stricter existing limit wins |
| Journal or reconciliation draft | 1,000 editable lines/items per revision; larger workflows require a separately reviewed bulk path |
| Statement layout | 1,000 line definitions, dependency depth 32; cycle detection required independently of the depth bound |
| Note table | 5,000 cells per note revision, bounded text per cell; no executable formulas |
| Text | Codes 100, labels 300, standard rationale 4,000 characters unless a documented existing stricter limit applies |

Acceptance must benchmark these agreed bounds and tune them downward where appropriate. Limit changes are configuration revisions with tests, not arbitrary constants altered by an agent to make an oversized fixture pass.


<a id="appendix-a"></a>
## Appendix A — Preserved original acceptance criteria, with production interpretation

These are the **52 original criteria** for VP-034–VP-046 from the supplied file. They are reproduced for traceability, not marked passed. Prototype-only implementation details are explicitly translated below; this blueprint's corresponding production contracts and tests govern the implementation.

### VP-034 — Add accounting profiles, periods, books, charts and dimensions

**Production interpretation:** Retain the business criteria. Real PostgreSQL/identity/Blazor context replaces local demo state; configuration changes use reviewed immutable revisions.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-034-AC01 | Given a client/engagement, when setting up a period/book, then subsequent imports inherit a visible explicit context and cannot attach to a sibling client by accident. | Planned; exact production test/run evidence required |
| VP-034-AC02 | Duplicate account codes, invalid date ranges, hierarchy cycles and posting accounts used as parents are rejected. | Planned; exact production test/run evidence required |
| VP-034-AC03 | Used/approved charts and period settings are revised rather than destructively overwritten; affected packages show staleness. | Planned; exact production test/run evidence required |
| VP-034-AC04 | New screens create neither client operational transactions nor tax/payroll configurations; empty setup provides a clear manual starting action. | Planned; exact production test/run evidence required |

### VP-035 — Complete bounded CSV and genuine XLSX trial-balance intake

**Production interpretation:** Retain real-format, precision, scope and replacement invariants. The original in-session-only source-byte rule is prototype-specific: production uses approved server-side bounded receipt/staging/storage with authorization and retention policy.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-035-AC01 | Given valid balanced CSV/XLSX data, when committed, then source rows, normalized totals, file metadata/hash and reporting context are retained as one revision. | Planned; exact production test/run evidence required |
| VP-035-AC02 | Unbalanced totals, duplicate ambiguous accounts, missing headers, unknown dimensions, formula-dependent numeric cells and exceeded limits produce a non-committed error preview. | Planned; exact production test/run evidence required |
| VP-035-AC03 | A rejected import leaves the previous accepted source untouched; a successful replacement preserves it and stales dependent calculations/approvals. | Planned; exact production test/run evidence required |
| VP-035-AC04 | XLSX means an actual workbook format, not CSV renamed to .xlsx; source bytes remain in-session only and exported/imported formats are verified. | Planned; exact production test/run evidence required |

### VP-036 — Add GL intake, transaction browsing and TB completeness

**Production interpretation:** Retain file-based GL and non-posting boundary. Use real durable import, completeness records and PostgreSQL-backed filters/exports, not synthetic success.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-036-AC01 | Given coherent opening balances, GL movements and closing TB, when completeness is calculated, then per-account residuals and source references reconcile. | Planned; exact production test/run evidence required |
| VP-036-AC02 | Missing opening data, partial journal batches, duplicate line keys, unmatched accounts, unbalanced journals and wrong periods/currencies are exposed rather than marked complete. | Planned; exact production test/run evidence required |
| VP-036-AC03 | Importing/replacing GL creates a new source revision and invalidates affected reconciliations/packages without changing the original source rows. | Planned; exact production test/run evidence required |
| VP-036-AC04 | Filters, source counts, drill-down and CSV export agree; the module never posts to client or firm books and handles the documented fixture size without freezing navigation. | Planned; exact production test/run evidence required |

### VP-037 — Extend account mappings and reporting validation

**Production interpretation:** Retain all mapping invariants. Module 21 owns mappings in this plan, consuming Module 20 chart/taxonomy and feeding Module 24.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-037-AC01 | Given a source with unmapped material balances, when preparing a package, then validation flags the rows and blocks a misleading complete result. | Planned; exact production test/run evidence required |
| VP-037-AC02 | A mapping revision requires independent review; editing an approved mapping preserves prior version and stales its dependent output. | Planned; exact production test/run evidence required |
| VP-037-AC03 | Where splits are used, allocations reconcile exactly to each source balance and cannot double count; invalid/mismatched chart or note targets are rejected. | Planned; exact production test/run evidence required |
| VP-037-AC04 | Every generated line exposes its mapping/source references; no unrecognized account is silently assigned a zero balance or miscellaneous category. | Planned; exact production test/run evidence required |

### VP-038 — Generalize adjustment journals and source-reflection decisions

**Production interpretation:** Use real current-person decisions, persisted exact source-reflection records and immutable adjusted snapshots. Reporting inclusion never asserts external ledger posting.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-038-AC01 | Given a balanced journal, when independently reviewed and management-accepted, then its effect is included once in the selected reporting layer. | Planned; exact production test/run evidence required |
| VP-038-AC02 | Given a replacement TB already containing that journal, when marked reflected with evidence, then additional effect is zero and no double counting occurs. | Planned; exact production test/run evidence required |
| VP-038-AC03 | Unknown/partial reflection blocks final reporting inclusion until resolved; changed source or journal revision stales the relevant decision. | Planned; exact production test/run evidence required |
| VP-038-AC04 | Unbalanced/mixed-context lines, same-person approval and duplicate inclusion are rejected; amendments preserve prior versions and do not alter source or firm ledgers. | Planned; exact production test/run evidence required |

### VP-039 — Implement editable manual reconciliation schedules

**Production interpretation:** Retain manual proof and independent review; use real scoped evidence/sources and reject ambiguous sign/currency/as-of treatment.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-039-AC01 | Given valid schedule items, when recalculated, then opening/source/supporting totals and unexplained residual are reproducible from displayed inputs. | Planned; exact production test/run evidence required |
| VP-039-AC02 | An unexplained nonzero residual or missing required evidence blocks approval; proposed corrections cannot masquerade as already cleared timing items. | Planned; exact production test/run evidence required |
| VP-039-AC03 | An accepted source/evidence replacement makes current reconciliation review stale; the previous approved snapshot remains viewable. | Planned; exact production test/run evidence required |
| VP-039-AC04 | Item currency/date/scope validation and independent reviewer checks work; no bank feed, automated matching, payment initiation or tax integration is introduced. | Planned; exact production test/run evidence required |

### VP-040 — Build configurable financial statements and comparatives

**Production interpretation:** Replace bounded demo-only output with explicitly approved production policy profiles and real versioned statement sets. Do not claim universal accounting-method coverage.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-040-AC01 | Given valid mapped current/prior periods, when statements are built, then each column and subtotal reconciles to its selected source; a missing prior period shows unavailable, not zero. | Planned; exact production test/run evidence required |
| VP-040-AC02 | Assets, liabilities/equity and period-result movements reconcile in the supported fixture; invalid totals display blocking validation. | Planned; exact production test/run evidence required |
| VP-040-AC03 | Changing source, mapping, layout or comparative selection creates a new output revision and stales the previous current review. | Planned; exact production test/run evidence required |
| VP-040-AC04 | The preview provides complete visible structure, editing and drill-down for the supported demonstration; unsupported calculations never render invented balanced figures. | Planned; exact production test/run evidence required |

### VP-041 — Complete notes, cash-flow support and disclosure review

**Production interpretation:** Provide real source-backed cash/equity schedules and notes. Human professional decisions replace any simulation marker; no fabricated cash-flow balances.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-041-AC01 | Given closing TB data alone, when cash flows lack required movement support, then the screen reports incomplete support instead of inventing movements. | Planned; exact production test/run evidence required |
| VP-041-AC02 | Not-applicable notes need a reason and reviewer decision; a blank note is not an approved exemption. | Planned; exact production test/run evidence required |
| VP-041-AC03 | Approved note/support edits preserve prior revision and invalidate current package review; totals tie to current statement context. | Planned; exact production test/run evidence required |
| VP-041-AC04 | Client previews expose only deliberately shared note content; internal reviewer comments remain internal and no professional conclusion is autogenerated. | Planned; exact production test/run evidence required |

### VP-042 — Complete financial-package assembly and genuine exports

**Production interpretation:** Render genuine server-side artifacts with exact persistent identities. Browser-local generation wording is prototype-specific; content, authorization and history requirements remain.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-042-AC01 | Given a valid supported package, when exported, then XLSX/DOCX/PDF files open as their actual formats and contain the displayed totals, entity, period and demo watermark. | Planned; exact production test/run evidence required |
| VP-042-AC02 | Export generation failure or unsupported content blocks that output and reports the reason; no renamed CSV, empty PDF or fake success is accepted. | Planned; exact production test/run evidence required |
| VP-042-AC03 | When package content changes, prior artifacts and decisions remain historical and a new artifact revision must be reviewed. | Planned; exact production test/run evidence required |
| VP-042-AC04 | External sharing remains explicit and scope-bound; internal workpapers/comments are excluded from management/client outputs by default. | Planned; exact production test/run evidence required |

### VP-043 — Create consolidation groups and effective perimeters

**Production interpretation:** Keep the bounded whole-owned baseline and explicit unsupported methods. Preserve existing separately approved advanced method implementations without treating them as globally activated.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-043-AC01 | Given a valid group/perimeter, when saved, then the group has its own scope, revision and component links and no source client balances are changed. | Planned; exact production test/run evidence required |
| VP-043-AC02 | Duplicate components, cycles, invalid ownership percentages and incompatible period/entity assignments are rejected. | Planned; exact production test/run evidence required |
| VP-043-AC03 | Adding a component does not expand the operator’s access to its unrelated engagements; narrow group access exposes only approved component projections. | Planned; exact production test/run evidence required |
| VP-043-AC04 | The selected calculation profile and limitations are visible; unsupported ownership/accounting methods cannot silently fall back to full consolidation. | Planned; exact production test/run evidence required |

### VP-044 — Select component packages and demonstrate currency translation

**Production interpretation:** Use approved manually maintained rates and exact accepted component identities. Synthetic fixture language is replaced by approved production policy plus separate QA fixtures; no online rate feed.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-044-AC01 | Given eligible component packages, when selected, then exact revisions are pinned and a subsequent replacement produces a stale-component warning rather than silent refresh. | Planned; exact production test/run evidence required |
| VP-044-AC02 | Missing rates, incompatible basis/period or unreviewed component packages block group output; missing amounts never default to zero. | Planned; exact production test/run evidence required |
| VP-044-AC03 | The fixture’s translated values and rounding reconcile to published test expectations; every rate and translation difference is traceable. | Planned; exact production test/run evidence required |
| VP-044-AC04 | An unapproved/unsupported translation rule shows a limitation and no fabricated consolidation result; component client packages remain unchanged. | Planned; exact production test/run evidence required |

### VP-045 — Implement manual eliminations and group adjustment review

**Production interpretation:** Retain every balancing, independence, nonmutation and unmatched-difference criterion. No automated matching or component-ledger posting is introduced.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-045-AC01 | Given a balanced supported elimination, when independently approved, then it affects group output once and neither component book/package is modified. | Planned; exact production test/run evidence required |
| VP-045-AC02 | Unbalanced lines, unsupported counterparties, mixed contexts and duplicate source inclusion are rejected. | Planned; exact production test/run evidence required |
| VP-045-AC03 | Unmatched intercompany amounts remain visible for human resolution; approval does not hide the difference by netting an unexplained plug. | Planned; exact production test/run evidence required |
| VP-045-AC04 | A component/rate/perimeter change stales dependent elimination approval and preserves the previous decision and journal revision. | Planned; exact production test/run evidence required |

### VP-046 — Produce, review and export consolidated output

**Production interpretation:** Use actual group-run/package DTOs, database lineage, independent review and exports. No group operation writes into component or firm ledgers.

| Criterion | Preserved original wording | Production acceptance |
|---|---|---|
| VP-046-AC01 | Given compatible reviewed components and approved adjustments, when the supported fixture is consolidated, then consolidated = translated components + approved group adjustments/eliminations. | Planned; exact production test/run evidence required |
| VP-046-AC02 | Statement equations and reconciliation columns agree with fixed expected fixture values; unresolved required inputs prevent a ready-for-review state. | Planned; exact production test/run evidence required |
| VP-046-AC03 | Group output review binds to the exact perimeter/component/rate/elimination revisions; edits require fresh review. | Planned; exact production test/run evidence required |
| VP-046-AC04 | Exported group demo artifacts preserve these references and exclude unrelated client information; no group action posts into component or firm ledgers. | Planned; exact production test/run evidence required |
