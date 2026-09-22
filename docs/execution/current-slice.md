# AuditSphereOps — current state and active slice

This file records observed repository state only. The authoritative build contract is [`docs/SPECIFICATION.md`](../SPECIFICATION.md) v5.0. The public repository intentionally excludes tenant identifiers, user principals, credentials, tokens, secret values, and private-provider URLs.

## Current verified baseline

| Item | Observed value |
|---|---|
| Source implementation checkpoint | `master@70f4a59` |
| Remote | `origin/master` includes source checkpoint `70f4a59` |
| SDK | .NET 10; repository solution targets `net10.0` |
| Database | PostgreSQL 18.6 on loopback port 5433 for development only |
| Build | Web Release build at `70f4a59` — passed, 0 warnings/errors |
| Tests | 224/224 passed, 0 skipped against PostgreSQL 18.6 at `70f4a59` |
| Migrations | 83 applied; latest `20260922083103_EnableLiveMailProviderAuthority` |
| Model drift | `dotnet ef migrations has-pending-model-changes` — no changes |
| Restore drill | `scripts/db/restore-drill.sh` — passed; 83 migrations, accounting/group manifests and release-delivery identities reconciled |
| Production effects | Disabled locally; no production acceptance claimed |

## Implemented local capability

- Firm/client/engagement scope authorization, durable operations, trial-balance intake, audit planning, evidence submissions, package generation, records/archive lineage, recovery quarantine, and provider safety fences.
- Durable operations expose redacted administrator recovery state and explicit cancellation dispositions; queued work without a lease can be cancelled atomically, is excluded from worker claims, and cannot publish a partial result. Active work remains subject to lease expiry and reconciliation because cancellation cannot prove an already-started effect did not occur.
- Client accounting profiles, periods, books, chart-of-accounts mappings, versioned trial-balance profiles, signed-net/debit-credit normalization, and atomic multi-entity batches.
- Capability profiles accept only ENTITY_REPORTING/AUDIT_ONLY client scopes or GROUP_REPORTING group scopes; mismatched or unknown service kinds are rejected.
- Client ENTITY_REPORTING and AUDIT_ONLY capability profiles now require an explicit approved service route and affirmative firm acceptance decision; staffing roles alone cannot bypass a missing or prohibited service decision. External component packs remain usable without client-bookkeeping migration.
- Client accounting setup/reporting objects default blank currency input to QAR; raw source/import currencies and FX policy currencies remain explicit.
- Analytical reviews default blank reporting currency to QAR only within the selected client reporting period, retain a deterministic input snapshot and replay hash, disclose negative/seasonal movement flags, and retain journal-risk sample selection, management explanation and corroboration fields.
- Secured analytical-review aggregate summaries enforce client/engagement or explicit group scope, honor effective group membership dates, and return only period/currency totals and counts without client or component identifiers.
- ECL persists an explicit booked amount and calculates the difference against it; ECL and inventory assessments can link only to a non-void proposed adjustment in the same scoped engagement, with legacy ECL booked amounts backfilled from management amounts.
- Analytical-review approval requires and persists a reviewer conclusion alongside the exact replay snapshot/hash; missing conclusions fail closed after source/generation freshness checks.
- Direct and streaming GL imports preserve optional service dates through canonical digests and archive lineage.
- GL completeness bridges optionally bind the approved prior-period TB, persist per-account opening-plus-movement residuals and disclose missing opening or malformed journal evidence.
- Zero-adjustment plans preserve the validated source exactly; posted journals apply only when NOT_REFLECTED, REFLECTED applies zero, and UNKNOWN/PARTIALLY_REFLECTED or changed source decisions block finalization.
- Source-bound reconciliation approvals re-check the exact dataset/batch digest and client input generation, marking changed reconciliations STALE before approval.
- Client-scoped accounting dimension definitions cover branch, cost centre, department, project and intercompany counterparty codes; nonblank GL dimension values are rejected unless defined for the client, including bounded streaming imports.
- Client chart hierarchy parent lookups are restricted to the target chart version; cross-version parent references and posting-account parents are rejected.
- Draft taxonomy nodes can be added incrementally with parents from the same taxonomy version; cross-version parents and cycles are rejected.
- Trial-balance CSV/XLSX imports require and persist the selected reporting period, optional reporting book and normalized basis; import validation checks period currency/basis/book scope, and completeness/TB reconciliation rejects sources with mismatched stored context. Legacy direct fixtures remain nullable for additive migration compatibility.
- Typed GL import, bounded paged reads, account-by-account trial-balance-to-GL completeness bridges, reconciliation workbenches, generation-bound ECL/inventory/specialist/analytical/journal-risk workbenches, and typed asset/payroll/loan/equity/related-party/tax/going-concern forecast schedules.
- Equity, notes, comparatives, closed-period restatement lineage, restricted same-currency consolidation, journal lineage, and close checks.
- Immutable package-review decisions for management, accounting, and partner stages. Decisions are bound to the exact package revision, generation, package hash and persisted rendered-artifact hash/version; evidence modes remain separated; append-only database protection is enforced. Review fails closed when the exact framework/template artifact has not been rendered.
- Consolidation perimeter approval requires an independently accepted group capability profile for the selected method; capability-preparer self-approval is rejected. Component approval requires current management, accounting and partner decisions for the exact package.
- Consolidation component submissions must also match the package's period basis, taxonomy version and mapping-version ID; the deterministic run manifest preserves that exact component lineage.
- Mapping versions bind the exact approved client chart version whenever a client chart exists; missing, out-of-scope or period-ineffective chart applicability is rejected, shown in the mapping workbench, and preserved in records export.
- Externally prepared component packs use typed raw/normalized digests, line-level reconciliation, independent approval, immutable submitted/returned/resubmitted/approved history, and an explicit approved compatibility bridge when dates or bases differ. Only approved, reconciled packs can enter consolidation, and records exports preserve pack provenance/history.
- Consolidation run manifests include component package hashes; approval recomputes current package, intercompany and group-journal inputs and blocks stale runs until a new run is built.
- The bounded foreign-operation profile pins an approved rate set and translation policy to the scope, requires maker/checker approval for each foreign component translation, preserves per-line source/FX lineage in the deterministic manifest, and blocks missing or stale rates/packages. Full FX remeasurement/reserve, NCI, acquisition, ownership-change, nested-group and complex-elimination methods remain disabled pending approved method-specific fixtures.
- The enabled FX profile accepts only explicit `DIRECT` rates; unsupported inverse semantics are rejected rather than silently multiplied with the wrong direction.
- Exchange-rate sets retain immutable version metadata, source, approval and effective date ranges; each observation retains its direction, rate date and rate type, observations outside a declared range are rejected, and scope/translation approval accepts only rate types declared by the approved policy.
- Currency remeasurement, foreign-operation translation and display-only conversion have distinct deterministic calculators. Persisted foreign-operation results retain separate foreign-exchange and rounding adjustments, and approval recomputes both from the exact source amount and approved rate; translation reserve rollforward math is available as a separate method-specific calculation core but is not enabled as a production profile without approved fixtures.
- Advanced consolidation calculation helpers cover acquisition goodwill/bargain purchase with fair-value adjustments, NCI rollforward, ownership changes/disposals, nested double-count detection and asset-transfer/tax elimination math. The current enabled consolidation profiles remain fail-closed until method-owner fixtures and complete statement rollforwards are approved.
- FX rate observations and translation-policy codes return controlled idempotency conflicts when repeated, rather than relying on raw database exceptions.
- Shared reporting taxonomy overlays accept only generic industry/group scopes; a client-scoped overlay is rejected so client-specific context cannot enter a firm master template.
- Group membership changes advance a durable group revision; consolidation scopes pin that revision and reject approval/calculation after a perimeter change while preserving historical memberships, scopes and runs.
- Component, translation, intercompany-match and group-journal commands recheck the pinned group revision and fail closed instead of writing stale-scope evidence after a perimeter change.
- Approved ownership edges are retained as evidence, but consolidation scope approval rejects intermediate/nested hierarchies for the enabled calculators until a method-specific calculation and fixture exist; this prevents double counting by construction.
- Structured records exports preserve the related group perimeter, membership, scope, component, ownership/intercompany, consolidation journal/run, exchange-rate and translation lineage without copying unrelated client workpapers.
- Approved-prior-scope group roll-forward preserves the exact prior run hash, approved FX lineage hash/reserve and recurring-elimination manifest; prior journals are lineage only and are not auto-applied.
- Accounting evidence links are typed, scope-checked and bound to reviewed audit procedure results; the staff evidence queue exposes only the actor's explicit client scope, and records export preserves typed accounting lineage.
- Period close runs under a row lock, blocks matching financial packages without current management/accounting/partner approval, and authorized reopen creates an immutable append-only amendment record with the new working revision.
- Controlled roll-forward creates a new draft period, copies prior reporting books as drafts, and creates a hash/evidence-bound opening bridge without copying prior approvals.
- The accounting evidence queue links reviewed accounting evidence to the exact associated audit workpaper, and the accounting dashboard exposes scoped period status plus roll-forward/restatement handoffs.
- The accounting evidence queue treats firm-wide, direct-client and exact-engagement grants distinctly, filters every listed evidence source accordingly, and shows the engagement context beside the client and period; engagement-only access cannot widen to sibling engagements.
- The accounting workspace applies the same direct-client versus exact-engagement boundary to engagement labels, package summaries, imports and sealed-import counts, so a selected context cannot silently widen to sibling engagements.
- The explicit Development/Test-only browser harness seeds a separate `auditsphere_browser` database with a QAR SME, unrelated clients whose identical account code remains client-scoped, a basic same-currency group and an enabled foreign-operation group. Built-in-browser journeys verified client switching, QAR/USD context, exact group scope versions and approved FX policy/rate status; the fixed development identity refuses Production startup, cannot coexist with OIDC and accepts only local return URLs.
- Portfolio projections apply the same active firm-wide, direct-client or exact-engagement grant boundary to counters, holds, durable operations, release candidates and financial-package lists; the client index adds scope-bound search and CSV export, engagement-only grants cannot expand to sibling engagements, and actors without an explicit scope see no firm data and receive actionable guidance.
- The shared layout exposes current-route navigation for the implemented accounting, evidence, package-review, roll-forward, restatement, consolidation and journal workbenches; unsupported workbenches are not presented as links.
- The accounting workspace pins a selected visible period's firm, group, legal entity, engagement, period, book, currency and package version in a draft-restored context header; the selection is display-only and does not broaden authorization.
- The accounting workspace exposes exact links to the selected package and mapping version when those records exist, alongside scoped TB/GL evidence and package-review routes; missing records remain non-clickable rather than becoming guessed links.
- Work tasks can persist an optional exact client reporting-period link and optional `DueDate`; the practice-time page creates these tasks with the same explicit grant boundary, and the accounting dashboard/period detail show the persisted owner, status and deadline before falling back to PBC values. Cross-client period links are rejected by the service and covered by PostgreSQL regression tests.
- Accounting dashboard, period roll-forward and restatement data loaders fail closed on an empty grant set and include only clients covered by an explicit firm-wide, client or engagement grant; the dashboard surfaces the active scope description and missing-scope guidance. Period-linked workflow tasks use the same exact period scope and do not broaden authorization.
- Client-safe validated-package view and signed-in management acknowledgement are available at the restricted client portal route. The portal exposes statement totals and package metadata only; internal review history and workpapers remain staff-only.
- Assigned staff can create file requests only for active, authorized engagements, select the client portal owner and reviewer, and queue description-rich email notifications containing an authenticated portal upload link. Staff and client replies are retained in one append-only timeline beside upload events; the client can upload through the existing request-bound capability, while verified staged files are downloadable only through a fresh staff scope check. An isolated Acceptance worker can deliver queued notifications through mailbox-scoped Microsoft Graph, SMTP or Resend configuration; secrets remain external, unknown outcomes stop without an automatic duplicate send, and local request creation never claims external delivery.
- An internal package-review queue lists only current validated packages in the actor's authorized client/engagement scopes and routes reviewers to the exact-version package surface.
- A validated financial package can now become a release candidate only through the existing guarded approval/release path; the candidate records `FINANCIAL_PACKAGE`, exact package revision/generation/hash, current management/accounting/partner decisions, and the normal checkpoint gate.
- The completion screen exposes package-candidate preparation only to partner/administrator actors after all three current package reviews are approved; repeated preparation reuses the exact candidate.
- ECL and inventory evidence records capture the reconciliation source hash and client input generation; review blocks when either source lineage or generation is stale.
- The enabled ECL and inventory valuation profiles have PostgreSQL golden fixtures and boundary coverage: expected loss, NRV/cost valuation, zero-input behavior, unsupported ECL methods and negative inventory inputs are verified without enabling unapproved methods.
- Bounded GL chunk intake with canonical content digests, transactional batch locking, idempotent retries, contiguous finalization and persisted accepted-count reconciliation.
- Source-bound GL reconciliations reject a sealed batch whose reporting period or currency differs from the selected period; PostgreSQL regression coverage passes.
- GL-backed bank-ledger schedules require a sealed, scoped, hash-matching GL import batch with complete selected-account coverage; the persisted GL control total is derived from source lines and caller mismatches fail closed. PostgreSQL regression coverage and migration `20260921212202_ResolveScheduleControlSource` pass.
- Reconciliation items are bound to the exact source currency, reject future item dates and missing dispositions, normalize accepted currency codes, and retain explicit receivable/payable ageing basis, rule version, bucket, credit treatment and paired settlement evidence; PostgreSQL regression coverage passes.
- Scoped audit-difference summaries preserve gross absolute totals and signed/net totals by currency, with corrected and unadjusted subtotals so offsetting differences remain visible; PostgreSQL regression coverage passes.
- Bounded journal-risk analysis returns deterministic, criteria-versioned review indicators for manual, year-end, high-value, reversal and missing-source-origin journals, with scoped source-origin and debit-amount evidence; it does not make an automatic fraud finding.
- Linked audit differences now retain a `journal-impact.v2` payload from the exact journal revision and approved mapping lineage, including account/statement effects, profit and equity totals, disclosure buckets, explicit unmapped status and a payload hash; PostgreSQL regression coverage passes.
- Difference correction governance now retains optional materiality and qualitative concerns, supports proposed/agreed/rejected/applied-in-reporting/reported-posted-external states with reviewer reasons, and rejects stale, unsupported or hash-mismatched `journal-impact.v2` evidence before verified-reflected status; PostgreSQL regression coverage passes.
- Bank reconciliation now binds an independently approved ledger schedule and bank statement schedule, persists typed ledger/statement/timing/proposed-correction items, computes the unexplained residual without netting proposed corrections, requires same-engagement draft-journal lineage for proposed items, and blocks approval or completion while unreconciled; PostgreSQL regression coverage passes.
- GL completeness calculation can be enqueued as a local durable operation, with sealed-source revision fencing, operation completion lineage and repeat-enqueue idempotency; PostgreSQL regression coverage passes.
- Financial-package builds can be enqueued as local durable calculations, fenced to the approved mapping revision and finalized plan, committed atomically with operation completion, and re-enqueued idempotently; PostgreSQL regression coverage passes.
- Financial-package records inherit the source dataset's selected reporting period, optional book and normalized basis, validate period dates and book/basis/currency lineage, persist the context with scope FKs, and include it in the deterministic package hash; legacy direct fixtures remain nullable for additive compatibility.
- Context-bound adjustment journals inherit and persist the validated source dataset's period, book, basis and currency, reject a book from another period, and preserve that reporting context through management decisions, posting, reversal and source-reflection lineage; PostgreSQL regression coverage passes.
- Financial-package rendering can be enqueued as a local durable calculation, fenced to the exact package revision, and records the deterministic artifact digest for later byte verification; PostgreSQL regression coverage passes.
- Exact canonical package artifacts can be exported through pinned deterministic XLSX, DOCX and PDF renderer versions. Formula-shaped client text remains literal in Office exports; no formula, macro, external-link or active PDF content is created. The PDFsharp/MigraDoc profile embeds its OFL font, canonicalizes generated metadata and subset identifiers, preserves Unicode text, paginates on A4 and was visually checked after rasterization. Repeated rendering is byte-identical, and every format is persisted against the exact package revision/generation/hash/framework/template. The staff package page exposes tooltip-guided downloads with non-disruptive status handling. Approved formula-bearing templates remain pending, and live provider/signing/records gates remain independent.
- Financial-package mappings now require the approved taxonomy statement section; package validation records separate statement cross-cast, accounting-equation, equity/profit, comparative-consistency and note-to-face outcomes, and rendered artifacts include adjusted-snapshot, mapping-version and adjustment-plan lineage identifiers. Exact UTF-8 artifact bytes are persisted with framework/template versions and SHA-256 lineage, and package-review decisions reference that artifact; PostgreSQL financial-statement regressions pass.
- Legacy financial packages retain their original template, calculation-engine version and calculation hash beside newly versioned canonical packages; the zero-adjustment compatibility regression verifies both identities remain readable without mutation.
- Legacy trial-balance period links and mapping chart links are backfilled only from a single exact candidate. Zero or multiple matching periods/charts leave the nullable legacy field unchanged and create append-only `AccountingBackfillQuarantine` evidence; the migration-from-previous-schema PostgreSQL fixture verifies both the resolved and quarantined paths.
- The accounting workspace exposes scoped COA/mapping, adjustment-journal and difference queues with exact-record links, removes the invalid unscoped journal route, and shows a period workflow dashboard with required/complete/stale/blocked counts, role-based next-owner guidance, persisted period-linked work-task or PBC owner/due-date values when available, an existing-PBC handoff link, and an explicit `Not recorded` due-date state when no persisted task/PBC due date exists. Its no-package/no-mapping fallback opens a grant-checked read-only period detail with exact task/PBC handoffs. Release build and the 220-test PostgreSQL suite pass at `5074abc`.
- Group consolidation exposes perimeter, component-pack, FX, intercompany and elimination tabs with persisted scope counts/statuses; same-currency and unsupported-method boundaries remain explicit. Release build and the 220-test PostgreSQL suite pass at `5074abc`.
- Package-review selection is draft-retained through the shared storage fallback layer and offers a non-mutating preview that separates eligible stages from partner-role blockers; it never records a partial approval. Release build and the 220-test PostgreSQL suite pass at `5074abc`.
- The PostgreSQL-backed accounting benchmark exercises four clients, 2,000 transactions, 8,000 GL lines, parallel enqueueing, two concurrent durable workers, a 32-line group calculation, six-decimal/high-magnitude amounts and paged reads; one observed run measured enqueue 147.8 ms, worker processing 134.4 ms, first page 43.8 ms and group calculation 3.2 ms.
- Blazor status surfaces for the implemented workflows, including period restatement and truthful release/package gate state.
- Shared Blazor form UX covers contextual action/field tooltips derived from labels/placeholders/IDs, required and optional markers, accessible guidance, stable form names/autocomplete metadata, a keyboard skip link, visible focus-visible states, non-disruptive invalid-field status, and localStorage draft autosave/restore across card and standalone forms. Drafts flush on input/change, tab backgrounding, pagehide and beforeunload through one lifecycle handler; generated scopes avoid repeated-heading collisions, including dynamically added cards; checkbox/radio values restore correctly; restored values raise both native input and Blazor binding change events; and autosave includes controls disabled during an in-flight action. When localStorage is blocked or full, sessionStorage is tried before the in-memory page-session fallback, and reduced durability is reported without interrupting the workflow. Server-backed workpaper drafts remain authoritative; browser file bytes and release keys are intentionally excluded from local storage.
- Adjustment-journal instructions are exported only after exact client/engagement authorization, an accepted source with raw and normalized digests, matching period/book basis and currency, the exact journal revision, and accepted or partial management evidence are re-read. The controlled CSV is formula-neutralized and explicitly marked as not proof of external posting; the journal page exposes tooltip-guided download and non-disruptive status/error handling.
- The staff financial-package page rechecks exact client/engagement authorization and displays/downloads only the persisted artifact bound to the current package revision, generation, hash and template; release and delivery remain separate controls.
- Period roll-forward and restatement selection loads use a generation guard and a visible loading state, lock dependent selectors during the request, and prevent older async responses from replacing a newer client/period selection; the existing draft autosave remains the source of unsaved form resilience.
- The mapping workbench shows immutable current-vs-prior allocation changes, exact-dataset/chart/taxonomy applicability, and bounded token suggestions for unmapped accounts; candidates remain review-only, ambiguous matches are labeled, and no suggestion mutates allocations.
- Consolidation automatic matches now require an explicit enabled elimination nature (receivable/payable, revenue/expense, dividend or investment/equity); outside-perimeter reviews remain review-only, approved group-only journals remain distinct, and the selected nature participates in the deterministic run manifest. Unsupported legacy natures fail closed.
- The loopback restore drill now reconciles accounting package/artifact, consolidation scope/run/line and external component-pack manifests, and fails closed on duplicate release-delivery identities; the current rehearsal restored 81 migrations through `20260922071325_QuarantineLegacyAccountingBackfill`; external checkpoint custody and production RPO/RTO remain separate gates.

## Remaining local implementation work

These are product gaps, not claims of production readiness:

- [x] Complete the service-level release handoff from exact package-review decisions to a package-bound release candidate; client management acknowledgement and the staff review queue remain available.
- [x] Bind ECL and inventory review applicability to the exact reconciliation source and client generation.
- [x] Require explicit depreciation method/useful life and persist the calculated closing balance for asset schedules.
- [x] Add typed payroll, loan, equity, related-party, tax and going-concern forecast inputs with evidence-bound approval.
- [x] Link typed accounting evidence to reviewed audit procedure results and expose a scoped evidence queue; preserve the links in records exports.
- [x] Govern package-aware period close and immutable authorized reopen/amendment lineage.
- [x] Add controlled entity-period roll-forward with draft book copies and explicit opening-balance evidence.
- [x] Add the bounded approved foreign-operation translation profile with pinned rate/policy inputs, maker/checker review, source/FX lineage and stale-input blocking.
- [x] Add deterministic method-specific calculation cores for currency remeasurement/translation separation, acquisition/NCI, ownership changes, nested double-count rejection and asset-transfer/tax elimination.
- [ ] Enable advanced accounting methods only after approved golden fixtures and complete statement rollforwards; the enabled first profile remains deliberately fail-closed.
- [x] Link accounting evidence to reviewed audit workpapers and expose account-area UI for the typed specialist schedules.
- [x] Add the accounting dashboard and cross-workflow navigation for accounting, roll-forward and release handoffs.
- [x] Include accounting/group dependencies in structured records exports while retaining the existing records-profile and legal-hold gates.
- [x] Carry approved group opening consolidation lineage across scope versions without duplicating prior journals.
- [x] Reject component, translation, intercompany-match and group-journal writes against a changed group revision.
- [x] Bind GL reconciliation sources to the selected reporting period and currency; reject cross-period or cross-currency batches.
- [x] Bind reconciliation items to the selected source currency; reject future dates and missing dispositions while preserving explicit as-of date, date basis, bucket rule/bucket, credit treatment and paired settlement links for receivable/payable ageing.
- [x] Route GL completeness calculation through the existing durable operation infrastructure with source revision fencing and idempotent retries.
- [x] Route financial-package builds through the existing durable operation infrastructure with mapping/plan fencing and idempotent retries.
- [x] Bind context-bound financial packages to the selected client reporting period, optional book, basis and currency, including the package hash and period-date validation.
- [x] Bind context-bound adjustment journals to the validated client reporting period, optional book, basis and currency; reject cross-period books and preserve the context through journal lineage.
- [x] Validate nonblank GL dimension values against client-scoped definitions and default accounting setup/reporting currency to QAR without defaulting source evidence.
- [x] Run seeded built-in-browser journeys for the SME, unrelated colliding-code clients, basic group and enabled foreign-operation profile without changing production authentication.
- [x] Route financial-package rendering through the existing durable operation infrastructure with exact package-revision fencing and deterministic artifact-digest verification.
- [x] Export controlled adjustment instructions with exact source, period/book, account, journal revision and management-evidence lineage; keep external posting and final artifact gates independent.
- [x] Export exact-package deterministic XLSX and DOCX artifacts with formula-shaped values retained as literal text, no macro/external-link/formula parts, SHA-256 persistence and scoped Blazor downloads.
- [x] Export an exact-package deterministic, inactive PDF through PDFsharp/MigraDoc with an embedded OFL font, A4 pagination, SHA-256 persistence, structural tests and rasterized visual verification.
- [ ] Add approved formula-bearing template fixtures; keep provider, signing and records acceptance independent.
- [x] Benchmark representative accounting workloads before production acceptance; the current local workload evidence is recorded above and does not establish production capacity or RPO/RTO.
- [x] Re-run focused tests, full tests, build, migration drift and restore drill for the current coherent slice; repeat this checklist for the next slice.

## External acceptance gates

Local code and PostgreSQL evidence cannot close these gates:

| Gate | Required evidence | Status |
|---|---|---|
| P1 | Live Entra OIDC, runtime identity fixtures, wrong-tenant/disabled denial | `BLOCKED_EXTERNAL` |
| P2 | Selected-resource SharePoint/Graph upload, download, versioning and reconciliation | `BLOCKED_EXTERNAL` |
| P3 | Independently administered release checkpoint store and capability evidence | `BLOCKED_EXTERNAL` |
| P4 | Approved Purview records profile, reviewer fixtures and observed protection behavior | `BLOCKED_EXTERNAL` |
| P5 | Approved signing methodology and exact-byte signature lineage | `BLOCKED_EXTERNAL` |
| P7 | Custodially separate restore rehearsal with measured production RPO/RTO | `BLOCKED_EXTERNAL` |
| P8 | Production secret custody, telemetry, capacity and no-sensitive-log evidence | `BLOCKED_EXTERNAL` |
| P9 | Independent human review and protected-merge evidence | `BLOCKED_EXTERNAL` |
| P10 | Full §47 real-tenant acceptance cycle and professional sign-off | `BLOCKED_EXTERNAL` |

No fixture, local adapter, documentation statement, or browser login is treated as a substitute for the required external evidence.

## Verification commands

```text
dotnet build AuditSphereOps.slnx --no-restore
dotnet test AuditSphereOps.slnx --no-build
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore --filter 'FullyQualifiedName~AccountingBenchmarkTests'
dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web
scripts/db/restore-drill.sh
```

The restore evidence is written to [`docs/evidence/restore-drill-latest.json`](../evidence/restore-drill-latest.json). The latest rehearsal restored 81 migrations through `20260922071325_QuarantineLegacyAccountingBackfill`, reconciled accounting/group manifests and found zero duplicate release-delivery keys; it is loopback-only and explicitly reports that external checkpoint custody and production RPO/RTO were not run.

## Resume rule

Before starting another slice, re-read `AGENTS.md`, this file, `docs/execution/status.json`, the Git worktree and current remote head. Preserve unrelated dirty work. Implement the smallest coherent local slice, verify it on PostgreSQL, commit and push that slice, then refresh the observed evidence. Never rewrite history or claim an external gate from local evidence.
