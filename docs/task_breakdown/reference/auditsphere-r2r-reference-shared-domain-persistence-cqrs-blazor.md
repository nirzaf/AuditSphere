# Shared domain, persistence, CQRS and Blazor rules







[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Unchanged source blueprint](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md)







**Status:** HISTORICAL_REFERENCE



**Authority:** Preserved contract excerpts and design research. Does not override `AGENTS.md` or `docs/architecture/auditsphere-architecture-current-architecture.md`. Current implementation uses static capability services and modular monolith architecture (see [`auditsphere-r2r-reference-baseline-architecture-and-adrs.md` §2.3.1](auditsphere-r2r-reference-baseline-architecture-and-adrs.md#section-2-3-1)).







**Reference, not an execution task.** The excerpts below retain the source contract. Baseline facts and research are historical to the supplied document; this breakdown does not re-audit the repository or re-verify external standards.







<!-- SOURCE-LINES: 177-284 -->



## 4. Shared domain, persistence and CQRS contract







<a id="section-4-1"></a>



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







<a id="section-4-2"></a>



### 4.2 Monetary policy







EXISTING money uses **numeric(19,6)** and `decimal.Round(..., MidpointRounding.ToEven)`. Retain it. A wholesale conversion to integer cents would lose source/FX allocation precision. [R5](auditsphere-r2r-reference-standards-and-source-register.md#source-r5)[R8](auditsphere-r2r-reference-standards-and-source-register.md#source-r8)







Input amounts must fit 13 integer digits and six fractional digits; reject excess precision/range before persistence instead of silently letting PostgreSQL round user entries. Derived calculations normalize at documented boundaries. Use checked decimal arithmetic and reject aggregate overflow. Define separate presentation precision by currency and report; do not infer accounting accuracy from two displayed decimals.







A journal line has nonnegative debit/credit, exactly one positive side, a valid account and currency. Submission requires at least two valid lines and **sum(debits) = sum(credits)** exactly at storage precision. Materiality is not a rounding tolerance for unbalanced journals.







Keep signed canonical balances debit-positive, credit-negative. Reporting display sign is a separate approved layout property. Fractions must sum exactly to 1; retain the existing documented `LAST_DESTINATION` residual policy with deterministic destination ordering. Currency conversion uses approved rate direction. Propose numeric(28,12) for new rate fields only after inspecting existing rate mappings and approving the migration; constrain rate/amount products to the representable result range.







Never sum currencies without an explicit translation result. Missing, zero, not applicable and unknown are different states. An explicit display-rounding reconciliation is allowed; an unexplained balancing journal or cash-flow amount is not.







<a id="section-4-3"></a>



### 4.3 Persistence conventions







Existing database identity remains `AuditSphereDbContext`. Implement or extract `IEntityTypeConfiguration<T>` classes in the existing Infrastructure persistence area. Initial extraction must produce **no EF model diff**; future schema changes have additive migrations. Do not configure the same entity twice with conflicting rules.







For each scoped header use a primary UUID key and alternate key needed for scoped children, typically `(FirmId, ClientId, EngagementId, Id)`. Financial children use a composite FK to that header or an equivalent enforced scope chain. Client master records use `(FirmId, ClientId, Id)` as appropriate. Group records use `(FirmId, GroupId, ScopeVersionId, Id)`; group ownership does not imply component-file access.







All business/history/source/evidence references use **Restrict/NoAction**, not cascading deletion. Retire configuration through lifecycle fields. No soft-delete filter may silently hide evidence from an issued historical package. Disposable rejected staging chunks may be removed through a separately scoped cleanup after evidence has been retained; they are not accepted accounting records.







Use unique constraints for normalized codes within their owner/version, source-line identity within batch, exact journal identity/revision, artifact identity within package/renderer, approval stage/subject constraints, and idempotency keys. Row CHECK constraints validate ranges/enum values and debit/credit exclusivity. Cross-row journal balance, hierarchy cycles and completeness need guarded transactions and existing/deferred database constraint mechanisms; a CHECK cannot prove a sum across children.







**Concurrency:** use an explicit mapped `long Revision` as an EF concurrency token where a mutable draft/header exists, preserving current generation fences. PostgreSQL `xmin` can be an additional provider token but is not a durable business revision; do not introduce SQL Server-style rowversion assumptions. [T3](auditsphere-r2r-reference-standards-and-source-register.md#source-t3)[T4](auditsphere-r2r-reference-standards-and-source-register.md#source-t4)







Use migration suffixes such as `M20_ReportingContextPins`, `M21_ImportManifestConstraints`; EF generates the real timestamp prefix at creation. These are proposed names, not claimed existing migration IDs. Never rewrite old migrations. Test empty schema, prior-schema upgrade, ambiguity quarantine, trigger preservation, model drift and rollback/restore strategy. Evidence-bearing downgrades may intentionally refuse destructive reversal.







<a id="section-4-4"></a>



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







Validators use `ValidateAsync`, including synchronous rules. A database lookup in a validator cannot replace in-transaction checks. Expose field failures using the existing error catalogue plus a typed validation-exception/error-adapter boundary; retain `CommandResult<T>` for domain outcomes. Web maps validation failures to `ValidationMessageStore`; APIs map them to field-based problem details. Do not assume FluentValidation automatically integrates with Blazor `EditForm`. [T2](auditsphere-r2r-reference-standards-and-source-register.md#source-t2)[T5](auditsphere-r2r-reference-standards-and-source-register.md#source-t5)







<a id="section-4-5"></a>



### 4.5 Transaction, idempotency and lock discipline







Before any state change: resolve current actor and session epoch; authorize exact scope; lock the existing safety/context rows; recheck grants, period state, expected revision/generation and dependency manifest; perform the domain change; append evidence/invalidation/outbox in the same transaction; commit once.







Publish a lock-order ADR before adding cross-client operations. Retain the existing **firm → client → engagement/target** order; group operations must lock participant client IDs in a deterministic sorted order and fit the same global order before taking group/target locks. Audit every participating existing writer—do not merely document an order while old writers invert it. Avoid a long-lived transaction across rendering, files, network calls or human decisions.







An idempotency key includes firm, command kind, scope/target and OperationId. Persist canonical input hash and result. Same key/same body returns the original result **after current authorization**. Same key/different body returns `idempotency.conflict`. An uncertain timeout queries the operation before retrying. A unique constraint handles concurrent identical submissions; a UI disabled button is insufficient.







Handle concurrency conflicts without automatically replaying a professional approval against newer data. PostgreSQL uniqueness/FK failures are not all `DbUpdateConcurrencyException`. Map known provider errors to safe outcomes; retry deadlock/serialization failures only for the complete idempotent transaction, bounded and without repeated external effects.







<a id="section-4-6"></a>



### 4.6 Shared Blazor interaction standard







A new/existing page uses a per-circuit **scoped UI state service** containing selected IDs, immutable query DTOs, filters and unsaved draft state—never `DbContext`, tracked entities or cross-user static state. Database contexts live per request/operation through `IDbContextFactory`/approved unit of work. This avoids the circuit lifetime and thread-safety pitfalls documented by Microsoft. [T6](auditsphere-r2r-reference-standards-and-source-register.md#source-t6)







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







Optimistic UI is limited to reversible local draft row edits and filters. Approval, posting eligibility, source acceptance, sealing, period close and release-related decisions are **pessimistic until confirmed**. In-app notices announce committed changes; they do not send business emails or trigger reminders. [T5](auditsphere-r2r-reference-standards-and-source-register.md#source-t5)[T7](auditsphere-r2r-reference-standards-and-source-register.md#source-t7)
