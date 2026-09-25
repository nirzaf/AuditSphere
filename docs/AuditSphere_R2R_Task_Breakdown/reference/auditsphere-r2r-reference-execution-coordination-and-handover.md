# Original work packages, agent coordination and handover

[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Unchanged source blueprint](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md)

**Reference, not an execution task.** The excerpts below retain the source contract. Baseline facts and research are historical to the supplied document; this breakdown does not re-audit the repository or re-verify external standards.

<!-- SOURCE-LINES: 1221-1248 -->
## 6. Sequential implementation plan and coordination gates

<a id="section-6-1"></a>
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


<!-- SOURCE-LINES: 1270-1307 -->
<a id="section-6-3"></a>
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

<a id="section-6-4"></a>
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


<!-- SOURCE-LINES: 1382-1422 -->
## 8. Production readiness, operator handoff and execution boundaries

<a id="section-8-1"></a>
### 8.1 Release-candidate verification sequence

The implementation team must execute, not merely document: exact SDK/pinned locked restore; full Release build; pure and PostgreSQL tests; bUnit tests; real Blazor Playwright journeys; migration/model-drift check; representative import/render/group performance profile; dependency/secret/output-safety checks; restore rehearsal; complete original-criterion crosswalk. Use the repository's current approved commands and update evidence with observed results only.

Suggested command intent, not an executed result in this document: locked solution restore; `dotnet build AuditSphereOps.slnx --configuration Release`; `dotnet test AuditSphereOps.slnx --configuration Release`; EF `has-pending-model-changes`; approved restore-drill script. Ensure test configuration/build flags agree—do not run stale Debug binaries while claiming a Release candidate.

Performance acceptance must set measured targets for upload size, rows, memory, request latency, group members, artifact size and concurrent sessions in an ADR. Preserve the inspected GL caps until benchmarks justify changes. Cancellation, progress and bounded paging are required even where latency targets are not yet approved. No throughput number is claimed from this blueprint.

Telemetry tracks operation ID, duration, safe rule code, row counts and retries—not client financial narratives, raw files, secrets or full account details. Background services use approved service identities; no current browser circuit is their security context. User-requested operation identity is retained, and authority is freshly checked at enqueue/publication according to policy.

<a id="section-8-2"></a>
### 8.2 Deployment and external integrations

This is a production architecture, so actual identity and document integrations eventually require real approved environment evidence. However local R2R calculations, DB invariants and file rendering must be verifiable without accessing a production tenant. Separate local correctness, approved methodology, environment readiness and provider acceptance.

Use existing Entra/Graph/SharePoint interfaces, selected-resource authorization and credential custody. Optional mail/OneDrive failure must not masquerade as successful setup or corrupt accounting work. No new provider, live exchange-rate feed, Purview adapter or eSignature requirement is added. Basic stored accounting artifact integrity is not a promise of legal retention or regulator certification.

Do not disable existing safety fences just to unblock a test. Where older repository scope requires Purview/signature evidence, the owner must approve the scoped policy change and its regression matrix. Continue preserving human reviews, exact manifests, archive history and safe recovery.

<a id="section-8-3"></a>
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

<a id="section-8-4"></a>
### 8.4 Coding-agent handoff instruction

Implement only the next approved work package in §6.1. Before changing code, inspect the current commit, AGENTS.md and relevant existing models/services/tests; produce a reuse/new-symbol list. Preserve the separation of firm books, client reporting and consolidation. Use the defined CQRS contracts, metadata and validator rules, not invented endpoints or duplicate models. Keep one transaction owner and one canonical calculation owner. Stop the affected feature and report a concrete unresolved policy or contract contradiction rather than inventing an accounting treatment or weakening a gate. Add and execute the required positive, failure, concurrency, scope and rework tests. Update progress only from observed evidence. Do not merge or perform tenant/production operations without the separate authorization required by the project.
