# AuditSphere — Atomic Implementation Specification
## Supplied audit working process: 20 sections and 165 procedures

**Mode:** SPEC_ONLY — source review and specification only.  
**Scope identifier:** AS-AWP-EXEC (local identifier in this specification; no issue or repository task has been created).  
**Requirement aliases:** AWP-01-01 through AWP-20-10; existing AS-AUD-002 / T055, AS-AUD-003 / T056 and relevant T057–T075 owners.  
**Requested repository/ref:** `nirzaf/AuditSphere`, `master`.  
**Immutable source inspected:** `6ab7b25240acbda3bff2818739a60751bc0d4289`.  
**Source commit timestamp:** 2026-10-17T10:53:32Z.  
**Inspection date:** 17 October 2026, UTC.  
**Source resolution:** GitHub connected read of `branches/master`, followed by commit-pinned file reads. Search results were used for discovery and reconciled to the pinned revision.  
**Input:** `Audit working process - Audit Tool New (1).docx`.  
**Input SHA-256:** `034d3b5023f5b498f0953ef6fea2af8439d72cc107075bd3336e53405091d987`.  
**Verified extraction:** 20 numbered sections; 165 procedure paragraphs; 185 nonempty paragraphs in total, with no Word tables.  
**Deliverable:** Eleven dependency-ordered atomic tasks, a current-source gap matrix, a 20-section execution design and a 165-procedure traceability appendix.
**Agent reading scope:** load the header/gap index, the selected atomic task, its prerequisites and relevant verification rows. Consult the 165-row appendix and evidence register on demand; do not load the entire execution ledger or spawn a separate task/agent per procedure.

> This is an implementation specification, not a professional audit opinion, a certification of standards compliance, or evidence that the software passed tests. No source, database, infrastructure or Git changes were made. No application builds, tests, installs, generators, services, provider operations, CI dispatches, pushes, merges or deployments were executed. Every future verification command in this document is **NOT RUN — SPEC_ONLY**.

> **Implementation tracking (2026-10-17):** The owner subsequently authorized implementation. Atomic Task 01 is implemented in the repository; its focused verification and commit are recorded in `docs/execution/status.json` and `docs/execution/auditsphere-execution-current-slice.md`. This note supersedes the quoted no-changes statement for the current checkout only. Tasks 02–11 remain open unless a later dated handoff records evidence. Professional methodology acceptance and live/production acceptance remain separate gates.

# A. Requirement, baseline and delivery contract

## A1. What is already present, and what this change means

The supplied document is **already represented by the controlled programme catalogue**. `AuditProgramCatalog` contains the AWP identifier sequence and source wording, and its `SourceHash` equals the SHA-256 of this exact upload. Its `IsComplete` currently checks counts and distinct identifiers/sections; that is not an independent wording-regression test. Do not reseed a second catalogue, import duplicate procedures, rename the AWP identifiers or create another audit engine. [E06]

Publication, adoption, applicability, tailoring, procedure-result submission, independent result review, sampling, evidence links and workpaper records already exist. The current native fieldwork page, however, does not provide a complete generic procedure submission/review journey; its applicable-procedure row says “Execute from source workflow.” Existing accounting evidence actions link/review accounting evidence, rather than replacing the missing procedure-result editor. [E07, E11–E13, E18, E25]

Current professional completion **already** uses the batched current-procedure-evidence predicate. The older stale risk-unlinked review defect is not specified again as missing. Programme/area/display counters still use weaker raw statuses and need alignment. [E14–E17, E21]

**Target:** an authorized audit team can adopt one exact controlled version; navigate all 20 sections; retain every source procedure; tailor through reviewed history; record performed work and actual evidence; submit, return, resubmit and independently review exact results; and complete the final audit checklist without circular report dependencies. Human auditors perform the work and make judgments. Software records and validates scope, provenance, state, arithmetic in already-supported tools, and approvals.

**Not required by the Word source:** a separate screen, calculator, external integration or automated test for every procedure. Obtaining confirmations, attending inventory counts, assessing ECL assumptions, interpreting contracts and deciding an audit opinion are not automatically performed by registering checklist text.

## A2. Authority and evidence vocabulary

The Word document supplies the **procedure wording, order and professional activities**. Current repository architecture, security, records and approval rules govern their software implementation. T055/T056 provide existing task ownership and tailoring requirements; their historical command names and Blazor UI descriptions are not current APIs or route authority. Preserve the actual static services, domain statuses and Angular owners instead. [E01–E05, E31–E33]

| Evidence label | Meaning |
|---|---|
| VERIFIED_SOURCE | A concrete source path/contract was inspected. It does not mean executed or professionally accepted. |
| INFERENCE | A consequence derived from the identified source paths; a future targeted reproduction is required. |
| RECOMMENDATION | A bounded implementation/design choice in this specification. |
| NEW_REQUIREMENT | An operational detail not prescribed by the Word source; explicitly proposed and subject to the stated owner gate. |

Capability states are `IMPLEMENTED`, `PARTIAL`, `MISSING`, `UNVERIFIED`, or `NOT_APPLICABLE`. Task/acceptance status is `SPECIFIED`, `UNVERIFIED`, `BLOCKED_CONTEXT`, `BLOCKED_DECISION`, or `BLOCKED_EXTERNAL`. Never promote a source finding or a future checklist to PASS.

The top-level ledger fields inspected identify `f0ed61c9c0531aa8c45e6336a0232ae4441bf207` as `headCommit`/`verifiedCommit`, with `verifiedAt` 2026-10-17T10:13:43Z; `remoteHeadVerified=false`, `verifiedCommitWasRemoteHead=false`, active acceptance PARTIAL and `productionEnabled=false`. Those are **recorded earlier evidence**, not verification of `6ab7b25240acbda3bff2818739a60751bc0d4289`. No current-head hosted test result is claimed in this review. [E34–E35]

The indexed instruction-file search returned the root `AGENTS.md` at this same source revision; `CLAUDE.md` imports it. Before future implementation, reread these and any newly introduced nested instructions in the actual checkout. Preserve all unrelated working changes.

## A3. Verified stack and owners

| Owner | Responsibility retained |
|---|---|
| `src/AuditSphereOps.Domain/` | Procedure/programme/result records and invariant types. |
| `src/AuditSphereOps.Application/` | Static capability services, authorization, review/currentness, deterministic calculations and durable contracts. |
| `src/AuditSphereOps.Infrastructure/` | EF configuration, PostgreSQL constraints, additive migrations, provider adapters. |
| `src/AuditSphereOps.Api/` | Trusted actor, authenticated/antiforgery-protected HTTP and exact response contracts. |
| `src/AuditSphereOps.Ui/` | Canonical Angular workspaces, same-origin API, presentation and bounded local UI state. |
| `src/AuditSphereOps.Worker/` | Existing durable jobs/provider operations; no new worker is needed for ordinary procedure decisions. |
| `src/AuditSphereOps.Web/` | Retained rollback/reference host. Keep it; do not claim retirement readiness or silently break shared callers. |

Source pins are .NET SDK **10.0.300**, target **net10.0**, language **C# 14**, EF Core **10.0.12**, Npgsql **10.0.0**, and Angular/Material/CDK **22.2.1**. PostgreSQL **18.6** is the declared project target; a running database engine was not inspected. Follow the existing tool and package lock files rather than introducing version upgrades. [E01, E40–E43]

The native route is `/app/engagements/:id/audit-fieldwork`, not an invented `/ui/app/...` route. Existing library, workpaper, audit-plan, confirmations and completion routes remain authoritative. [E24]

## A4. Actors, permissions and boundaries

A current **firm-wide Partner/Administrator** controls the firm-owned programme library. Engagement-scoped users may obtain an authorized minimal adoption option projection; that does not grant library-administration access. The engagement preparer uses current permitted grants/staffing and the existing risk-rank rules. An independent reviewer reviews a frozen result. Under T056, **N/A approval is an independent Partner decision**, not an automatic consequence of a blank balance or an input dropdown. The currently authorized Engagement Partner retains opinion/signing/clearance authority. [E07, E22, E32]

Client users provide only authorized PBC or management evidence through existing portal capabilities. They do not obtain the internal audit programme, fraud-risk notes, other client records or professional review controls. Existing task/rework queues are navigation and coordination, never approval authority.

Enforce current `FIRM_WIDE`, `CLIENT`, `ENGAGEMENT` and, where an existing linked read actually requires it, `GROUP` grants at Application/API boundaries. Reauthorize after lock waits and before returning sensitive results. Preserve session epochs, professional-work holds, freeze guards, maker/checker separation, exact evidence revisions and redaction. `can*` flags do not authorize commands.

Firm accounting, client books and group consolidation remain separate. Audit tests and audit differences do not silently post journals, process payroll, initiate payments or change component books. Approved existing adjustment/source-reflection workflows remain separate from recording that an audit procedure was performed.

## A5. Scope, exclusions and implementation assumptions

In scope are the control gaps G01–G11 below, additive history/receipt contracts where needed, the reusable native procedure workspace, truthful completion views and the evidence needed to accept this supplied programme.

Out of scope are another ERP, a new catalogue, 165 bespoke forms, a general form-builder or workflow engine, a new assignment engine, automated audit opinions, new tax/payroll/inventory operations, reimplementation of existing sampling/financial engines, broad FSLI/consolidation/CRM/billing rewrites, new Graph consent or provider architecture, antivirus/ClamAV, brokers, generic repositories/UnitOfWork, MediatR, package upgrades, and removal of Web/MudBlazor. Full-product production closure remains separate.

**Assumptions:** one engagement adopts one controlled programme version for this increment; new engagements start with undecided applicability and no copied reviews. Automatic upgrades of an in-progress engagement are not requested. Existing ad-hoc procedures remain additional, auditable work and do not replace any of the 165 baseline IDs. Current authorization permits eligible-user execution; a new exclusive pre-work ownership policy is not inferred from this Word document.

New code identifiers and files below are **PROPOSED**, never claimed to exist. EF assigns migration timestamps during future generation. The logical migration names are specified, but exact generated timestamp filenames must be recorded before review; no historical migration is edited.

## A6. Decisions and blockers

| ID | Required disposition | Scope and default |
|---|---|---|
| D1 — final-stage dependency policy | **BLOCKED_DECISION** for Task 10 gate activation: a qualified methodology owner and independent reviewer must approve the exact source-ID-to-prerequisite/terminal-attestation mapping. | Proposed direction: signed-output and review attestations consume upstream evidence without becoming prerequisites of themselves. Until approved, expose the blocker; never mark such rows N/A to escape a cycle. |
| D2 — professional programme acceptance | **BLOCKED_EXTERNAL** for production methodology acceptance. Name the qualified owner/reviewer and record the supplied programme/version, applicable framework/profile and any permitted tailoring/evidence rules. | Source identity and the extracted manifest are verified; professional adequacy and runtime catalogue acceptance are not. T003/T014/T055 dependency completion is not inferred from this specification. Technical safety fixes may be separately authorized maintenance work, but cannot close those cards without their existing gates. |
| D3 — real evidence and release acceptance | **BLOCKED_EXTERNAL** for real tenant/document permissions, confirmation-channel evidence, trusted production signing/custody, and final operational acceptance. | Reuse existing gates and providers. Local synthetic work demonstrates software behavior only. |

**Question for D1 (to be answered in the implementation handoff, not guessed):** does the methodology owner approve a versioned acyclic completion policy in which AWP-20-07/09/10 and any other output attestations reference the exact completed upstream decisions/documents, while final programme completion still requires every applicable source procedure or independently approved N/A disposition?

The old T056 wording names Partner approval for N/A, while `DecideApplicabilityAsync` currently permits broader review roles and direct `NA_APPROVED`. This specification chooses the existing T056 Partner rule plus separation; a different policy requires a recorded owner decision, not a silent weakening.

The testing strategy still contains an older paragraph saying release lacks a full-regression precondition. Current workflow source now includes `verify-release-source`; its comments say retained full-regression evidence is consumed before release. Do not recreate the prior missing-gate task. Task 11 reconciles this narrow handoff contradiction and preserves existing CI separation; enforcement correctness and deployment are not re-audited here. [E36, E38–E39]

## A7. Measurable acceptance

| Acceptance | Required observable outcome |
|---|---|
| AC01 Source fidelity | All 20 titles, 165 IDs, per-section ordinals and source wordings match the supplied manifest; source hash matches; original text is immutable. |
| AC02 Publication/adoption | Current authorized publication and exact version adoption succeed once; denied/stale/partial/different-source requests create no unintended library or engagement rows. |
| AC03 Tailoring/N/A | Original wording and each effective definition/disposition remain readable; N/A has real rationale and independent current Partner approval; stale decisions never close a row. |
| AC04 Execution | Any applicable AWP procedure opens the shared native editor, records actual work/conclusion/evidence and produces its exact submitted result. Saving a draft is not submission. |
| AC05 Review/rework | Reviewer sees the frozen result and evidence; self, wrong-rank/scope, stale and open-note approvals are denied; return requires comments and retains history. |
| AC06 Recovery | Duplicate/concurrent exact command creates one outcome; a lost reply resolves to its original identity; changed intent conflicts; a generic refreshed list is not a receipt. |
| AC07 Evidence | Submitted evidence is a scope-checked immutable snapshot, distinct from later links or provider changes. External/manual references never masquerade as verified electronic evidence. |
| AC08 Coverage | All displays use current exact evidence and distinct submitted/reviewed/N/A/stale counts; missing baseline rows, unsupported states and omitted pages cannot appear complete. |
| AC09 Tool reuse | Each source procedure has the common evidence-recording route; relevant existing tools open the exact authorized context, with manual/unsupported boundaries disclosed. |
| AC10 Final sequencing | A realistic programme reaches review, report signing and final checklist completion without prematurely completing terminal rows or invalidating a report merely by attesting to it. Real underlying changes still stale dependent decisions. |
| AC11 Isolation/history | Client/engagement/session/holds/freeze and row-level concurrency are preserved; historical issued evidence is not rewritten or backfilled with guessed approval. |
| AC12 Verification/handoff | Relevant focused checks execute on the implementation revision; full regression is obtained only through CI/CD for that source; professional/live gates are independently recorded. |

# B. Gap matrix and dependency-ordered task index

Only gaps and explicit changes are scheduled. An existing class, route or test file is not a completion percentage. `PARTIAL` often means a specific connection/control is missing, not that the underlying engine must be recreated.

| Gap | Required | Existing behavior | Evidence | State | Remaining delta | Task |
|---|---|---|---|---|---|---|
| G00 | 165 original procedures and 20 sections | Catalogue and stable AWP IDs exist; exact upload hash is declared. | E06 | IMPLEMENTED | No catalogue reimplementation; independent source regression only. | 01, 11 |
| G01 | Current firm-wide, attributable controlled publication | PublishAsync relies on ActorContext role and immediately records the same creator/approver; library reads use current firm-scope authorization. | E07, E22 | PARTIAL | Enforce current publication authority and reviewed source/version provenance. | 01 |
| G02 | Exact, complete, side-effect-safe engagement adoption | Combined publish/adopt first publishes; adoption checks template count, not exact full manifest; a second version conflicts with source-ID uniqueness. | E07, E09, E11–E12 | PARTIAL | Separate publication from adoption; exact membership and explicit version conflict. | 02 |
| G03 | Original text and immutable tailoring revisions | TailorProcedureAsync overwrites engagement SourceWording; immutable library text survives separately. Existing ad-hoc history must be reused/preserved. | E07–E09, E18 | PARTIAL | Separate effective definition from original text, retain revision lineage and fence writes. | 03 |
| G04 | Justified, independently approved current N/A | Native API supplies a fixed reason and cannot approve N/A; Application accepts NA_APPROVED without binding an independent prior proposal. | E07, E12, E32 | PARTIAL | Real rationale, exact proposal/definition, Partner review, immutable decisions and recovery. | 04 |
| G05 | Exact submit/review intent and original outcome recovery | Scoped, generation/rank-guarded result/review commands exist; requests lack an original command identity and expected definition/result revision pair. | E07, E12 | PARTIAL | Add bounded reviewed request/receipt and compare-and-swap semantics; preserve existing guards. | 05 |
| G06 | Result-owned evidence, not a changing current link list | PBC hashes and physical links exist; procedure review exposes current links separately from submitted free-text reference JSON. | E07–E08, E11, E18 | PARTIAL | Freeze selected actual evidence identities and distinguish manual references from verified stored sources. | 06 |
| G07 | End-to-end native procedure editor/reviewer | Native programme table offers no complete result authoring/review action; generic workpapers and accounting evidence actions are different operations. | E11–E13, E19, E23, E25 | MISSING | One reusable procedure workspace with exact snapshots, draft protection and recovery. | 07 |
| G08 | Truthful source completeness and current progress | Core completion predicate is fixed; programme Reviewed includes Submitted/InReview, area counts use raw history/status, and zero/nonzero procedure count is not full manifest proof. | E14–E17 | PARTIAL | Reuse current predicate and exact adoption manifest for all relevant projections. | 08 |
| G09 | Procedure-to-existing-tool navigation and documented manual execution | Sampling, evidence, confirmations and accounting workspaces exist; the source checklist does not supply an approved per-procedure automation schema. | E13, E18, E24–E25 | PARTIAL | Connect explicit contextual assistance without inventing new engines or claiming automated judgments. | 09 |
| G10 | Non-circular final work programme and document attestation | All applicable procedures participate in completion/FactsDigest; Partner clearance/report currentness depend on that evidence, including procedures that attest to those outputs. | E15, E21; AWP-20-07/09/10 | PARTIAL | INFERENCE: a literal truthful terminal completion can block or stale its own prerequisite; approve and implement an acyclic stage policy. | 10 |
| G11 | Current source and professional acceptance evidence | Test sources and historical ledger evidence exist; this review executed no product tests and established no live/professional acceptance. | E26–E30, E33–E39 | UNVERIFIED | Focused regression, current-source CI and independently accepted programme evidence. | 11 |

| Task | Concrete outcome | Depends on | Principal files/owner | Acceptance |
|---|---|---|---|---|
| 01 | Register and publish the controlled source under current firm authority | None; source and rules first | Programme library / source fidelity; exact paths in task | AC01, AC02, AC11 |
| 02 | Adopt one exact complete programme without hidden publication | 01 | Engagement programme / version selection; exact paths in task | AC01, AC02, AC08 |
| 03 | Preserve original wording and version every tailored definition | 02 | Procedure definition / provenance; exact paths in task | AC01, AC03, AC11 |
| 04 | Complete independent N/A proposal and Partner disposition | 03 | Applicability / professional exclusion; exact paths in task | AC03, AC05, AC11 |
| 05 | Make procedure submission and review exact, atomic and recoverable | 03 | Procedure commands / receipts; exact paths in task | AC04, AC05, AC06, AC11 |
| 06 | Freeze the evidence actually considered for each submitted result | 05 | Result evidence / provenance; exact paths in task | AC04, AC05, AC07 |
| 07 | Provide the reusable native procedure execution and review workspace | 02, 03, 04, 05, 06 | Angular / API / draft lifetime; exact paths in task | AC04, AC05, AC06, AC11 |
| 08 | Unify complete-programme and current-evidence progress | 02, 03, 04, 05, 06 | Programme / area / completion projections; exact paths in task | AC01, AC08, AC11 |
| 09 | Connect all 20 sections to existing tools and explicit manual work | 06, 07, 08 | Contextual workflow integration; exact paths in task | AC07, AC09 |
| 10 | Complete terminal audit procedures without circular signing gates | 04, 05, 06, 08 | Final programme / report dependency policy; exact paths in task | AC03, AC05, AC08, AC10 |
| 11 | Accept the exact programme through focused checks and current-source CI | 01, 02, 03, 04, 05, 06, 07, 08, 09, 10 | Acceptance / traceability / handoff; exact paths in task | AC01–AC12 |

Task 10 additionally requires D1. Formal T055/T056 and dependent T-card completion retains the repository's existing dependency and independent-review rules; these local task numbers do not bypass them. Tasks 07 and 08 can follow the same shared contracts without waiting for each other's unrelated presentation details. Do not create parallel agents or extra branches merely because the dependency graph permits independent work.

# C. Atomic implementation tasks

The recipes below are **future work only**. Every proposed path/identifier is labelled. Shared verification references V0–V7 are defined in Section D; they are not commands already executed. When a task changes a contract or model, its relevant V3/V4 checks apply; other unrelated checks do not become mandatory local reruns.

## Atomic Task 01 — Register and publish the controlled source under current firm authority

### 1. Objective

Close G01 and protect G00/AC01 without rebuilding the catalogue. Current `PublishAsync` immediately publishes from a raw actor role and sets creator/approver to the same identity. Target: a controlled version has exact source/content provenance, current firm-wide authorization and an explicit reviewed publication. D2 remains separate professional production acceptance. No arbitrary Word upload importer, editable methodology designer, second procedure catalogue or assertion taxonomy is added.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Application/Audit/AuditProgramService.cs`; `src/AuditSphereOps.Application/Audit/AuditProgramLibraryQuery.cs`; `src/AuditSphereOps.Domain/Audit/Audit.cs`; `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs`; `src/AuditSphereOps.Api/Ui/UiEndpoints.AuditRecords.cs`; `src/AuditSphereOps.Ui/src/app/features/audit/library.ts`; `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`.

**CREATE — PROPOSED:** `tests/AuditSphereOps.Domain.Tests/AuditProgramCatalogSourceTests.cs`; `tests/AuditSphereOps.Domain.Tests/Fixtures/audit-working-process-source.json`; `tests/AuditSphereOps.Api.Tests/AuditProgramCommandsApiTests.cs`. Modify `tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj` only to embed/copy the independent fixture when its current fixture convention requires it.

**GENERATE — PROPOSED, only for missing retained publication metadata:** additive migration with logical name `AuditProgramPublicationEvidence` under `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`, its EF-generated designer, and the existing `src/AuditSphereOps.Infrastructure/Persistence/Migrations/AuditSphereDbContextModelSnapshot.cs`. EF determines the timestamp; record actual paths before review.

**READ-ONLY:** catalogue E06, upload and hash, E01–E05, E08–E10, E22–E24, E31, existing lock/authorization owners. Do not change source wording, IDs, old migrations, package versions or unrelated library UI.

### 3. Anchors

Existing: `PublishAuditProgramRequest`, `AuditProgramResult`, `AuditProgramService.PublishAsync`, `AuditProgramVersion` with `DRAFT/PUBLISHED/RETIRED`, creator/approver fields, `AuditProgramCatalog.Items/SourceHash/IsComplete`, `AuditProgramLibraryQuery.GetLibraryAsync/GetSectionAsync`, and `/api/ui/audit/library` reads. [E06–E10, E22–E23]

**PROPOSED:** `PrepareControlledAuditProgramAsync` and `ReviewControlledAuditProgramPublicationAsync`, with exact version ID, source hash, normalized content digest and bounded review reference. These are small static capability operations, not new handler infrastructure. Additive routes are **PROPOSED**: POST `/api/ui/audit/library/versions/prepare` for the exact controlled version/source and POST `/api/ui/audit/library/versions/{versionId:guid}/publish` for independent publication of that exact draft. Bind publication to the displayed content digest and current draft state; name request/response records in the existing service/HTTP owner and include both routes in OpenAPI.

### 4. Recipe

1. Build the independent JSON fixture from the supplied Word paragraphs, not from `AuditProgramCatalog.Items`. Retain the upload SHA-256, exact titles/wording and section/ordinal/ID sequence. Do not require the application runtime to parse DOCX. Compare all rows, not only counts; preserve punctuation and source semantics.
2. Replace raw-role-only publication authorization with current `AuthorizationDecision` checks for the firm-owned target. An engagement-only Partner grant is not firm library ownership. Recheck after the existing firm/actor lock sequence and before returning a replay.
3. Reuse `AuditProgramVersion` statuses and fields: prepare a controlled draft from the fixed source, show its complete source summary, and permit independent authorized publication of that exact draft. No public input can substitute caller-supplied procedure wording for the controlled source.
4. Store a content digest distinct from the binary document hash where needed, plus the reviewed publication reference. Source identity, programme version and publication approval must remain distinct concepts. An application metadata approval is not proof of a professional's qualifications or legal adequacy.
5. Serialize on the existing firm/program/version uniqueness boundary. A concurrent same-version/same-content request returns the retained version; differing content/hash conflicts and leaves no partial catalogue. Never mutate a published programme's source content.
6. Expose publication only to authorized firm administrators/Partners on the existing library page, with explicit source/version review. A normal engagement adopt action cannot call this mutation implicitly. Keep version reads accessible under their existing policy.
7. Preserve old published versions and their existing creator/approver attribution as historical records. Do not fabricate independent approvals for legacy publications. New professional adoption must disclose legacy-unverified publication evidence until the defined human review is recorded.
8. Keep optional applicability/evidence guidance separate from source wording. The Word source does not provide assertion tags; do not silently manufacture them from `ApplicabilityCondition` or a section name.

### 5. Tests

Use one database-free source-fixture comparison in the **PROPOSED** `AuditProgramCatalogSourceTests.cs`: given the independent source manifest, every ordered row and per-section count matches; altered wording/ID/ordinal is detected. Use one database-free assertion loop over the independent manifest, not 165 separate xUnit theory rows, browser journeys or fixtures.

Extend `AuditProgramWorkflowTests.cs` using the existing `PgTestSchema`/seed conventions: current firm owner prepares, distinct authorized owner publishes, exact replay returns one version; scope-limited/revoked/self-review or changed-source attempts fail without catalogue mutations. Put HTTP authentication/antiforgery/response-shape assertions in the shared **PROPOSED** `AuditProgramCommandsApiTests.cs`; do not duplicate all domain permutations there.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V1 with the existing `AuditProgramWorkflowTests` class and the proposed source-test class after it exists; V2 only for the affected library/fieldwork component tests; V3 for new endpoint shapes; V4 only if publication persistence changes. Expected PASS: exact 20/165 manifest, complete atomic publication, current authorization and source-safe replay. Prerequisites: current locked dependencies, pinned SDK, and an isolated PostgreSQL test target for the database cohort. Full suites remain V6/CI only.

### 7. Future Done

Source catalogue unchanged; one exact reviewed version is publishable under current firm authority; legacy attribution remains honest; new contracts and migration are reviewed; focused evidence names the actual source revision. D2 and T003/T055 professional/dependency gates remain open until independently satisfied. Application rollback must retain new evidence and refuse unsafe downgrades; do not remove approval/history columns to restore an old UI.

## Atomic Task 02 — Adopt one exact complete programme without hidden publication

### 1. Objective

Close G02/AC02. Replace combined publication/adoption with explicit adoption of an already published, eligible version. Preserve the 165 controlled procedures and any separately created ad-hoc work. No automatic in-progress programme upgrade, prior-year review copying or silent re-adoption is introduced. Depends on Task 01.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Application/Audit/AuditProgramService.cs`; `src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs`; `src/AuditSphereOps.Application/Audit/EngagementAuditProgramQuery.cs`; `src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs`; `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts`; `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.spec.ts`; `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`; the proposed `tests/AuditSphereOps.Api.Tests/AuditProgramCommandsApiTests.cs`.

**READ-ONLY:** catalogue, `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs` existing programme/source uniqueness, `src/AuditSphereOps.Ui/src/app/app.routes.ts`, T014/T055/T056 handoff. `src/AuditSphereOps.Web/Components/Pages/AuditFieldwork.razor` is an existing caller: update only its affected adoption call if shared request shapes change, otherwise keep it read-only.

**CREATE/GENERATE:** none by default. Add a constraint migration only if the observed existing keys cannot enforce the selected one-active-programme rule; such a migration is PROPOSED and must preserve all historical adoptions.

### 3. Anchors

`AdoptAuditProgramRequest(EngagementId, ProgramVersionId)`, `AdoptAsync`, `EngagementAuditProgram`, `AuditProcedure`, source uniqueness `ux_audit_procedure_scope_source`, `AuditFieldworkWorkspaceQuery.PublishAndAdoptAsync`, and POST `/api/ui/engagements/{id}/fieldwork/program` are existing. [E07, E09, E11–E12]

**PROPOSED:** GET `/api/ui/engagements/{id:guid}/fieldwork/program-options` returns only the published-version identity, source/content digest and adoption eligibility needed in that engagement. Extend the existing POST `/api/ui/engagements/{id:guid}/fieldwork/program` input to carry the exact `ProgramVersionId`, source/content identity and expected existing adoption shown to the actor; it performs adoption only. Do not widen firm-wide library authorization to implement the option read.

### 4. Recipe

1. Authorize the exact engagement, current session and professional-work status before any mutation. Present its existing adoption, source identity, baseline count and explicit eligibility blockers.
2. Remove hidden publication from the native adoption handler. Resolve only a previously published version in the actor's firm; show its reviewed source identity. Failed target authorization must not create a firm version first.
3. Compare the selected version's complete source manifest—IDs, sections, ordinals and wording/content digest—not merely `Count == 165`. Corrupt, incomplete, duplicate or wrong-source versions fail closed.
4. Under the existing engagement/adoption lock and scoped keys, create the adoption and all baseline procedure instances in one transaction. Every row starts with undecided applicability, no result, no copied decision, and exact immutable source linkage.
5. Same engagement + exact same adopted version returns the original adoption only after checking completeness. A different version in an in-progress engagement returns an explicit controlled-version-conflict response, not an unhandled duplicate-key exception or partial second adoption.
6. Keep baseline membership separate from ad-hoc additions. Return `baselineProcedureCount`, `adHocProcedureCount` and the exact adoption ID; “165 baseline” must not mean “ignore additional required work.”
7. On historical inconsistent/partial adoption, expose a diagnostic blocker for authorized staff. Do not repair by deleting existing results or filling missing procedures with invented applicability/reviews. A future explicit repair/cutover requires its own reviewed intent.
8. Preserve the native route and allow reload to recover the same adoption. A user who can adopt an engagement need not be able to publish firm methodology.

### 5. Tests

Extend `ControlledCatalog_UsesScopedAppendOnlyWorkflow` in `AuditProgramWorkflowTests.cs`: exact adoption creates all 165 once, second identical request recovers the same identity, a different version fails without changes, and an authorized engagement with an invalid/partial source version cannot adopt. Reuse the allowed/sibling-scope fixture rather than constructing another tenant framework.

Extend `fieldwork.spec.ts` for a non-publisher engagement user selecting an eligible version and for a denied/unknown adoption response that does not display a new programme as adopted. One API assertion checks that a denied engagement request never causes library publication. Synthetic same-count/wrong-ID data should be created only in a fixture state that the real persistence rules permit, not by disabling guards.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V1 focused programme/adoption cohort; V2 `fieldwork.spec.ts`; V3 for changed adoption/option schemas; V4 only for an actually required new constraint. Expected PASS: one complete exact baseline, stable replay, no unintended global effect, and separate ad-hoc counts. V6 supplies full regression in CI; no local complete suite.

### 7. Future Done

Publication and adoption are distinct; scope-limited users cannot publish indirectly; an exact baseline is persisted atomically and remains source-traceable; no historical adoption/result is deleted. UI and legacy caller compatibility are accounted for. Existing T014/T055 prerequisites remain formal task-completion gates, not automatically completed by this code.

## Atomic Task 03 — Preserve original wording and version every tailored definition

### 1. Objective

Close G03/AC03. The original Word procedure must remain visible even when an engagement needs additional instructions. Current `TailorProcedureAsync` rewrites the engagement copy of `SourceWording` and uses `CurrentResultRevision` to invalidate work. Target: immutable original source plus an explicit versioned effective definition, with compare-and-swap updates and precise downstream staleness. Depends on Task 02; no arbitrary method-level rewrite or baseline deletion.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Application/Operations/Contracts.cs` for the new capability DbSets; `src/AuditSphereOps.Domain/Audit/Audit.cs`; `src/AuditSphereOps.Application/Audit/AuditProgramService.cs`; `src/AuditSphereOps.Application/Audit/EngagementAuditProgramQuery.cs`; `src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs`; `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs`; `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs`; `src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs`; `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`.

**CREATE — PROPOSED:** `src/AuditSphereOps.Domain/Audit/AuditProcedureDefinitionRevision.cs`; `src/AuditSphereOps.Application/Audit/AuditProgramService.Definitions.cs`. Make the existing service partial only as needed for this cohesive addition; do not move unrelated services.

**GENERATE — PROPOSED:** additive logical migration `AuditProcedureDefinitionHistory`, its timestamped/designer files in `src/AuditSphereOps.Infrastructure/Persistence/Migrations/`, and the existing model snapshot.

**READ-ONLY:** immutable `AuditProgramProcedure` catalogue; existing `AdHocProcedureInsertion`/`AdHocProcedureRevision` behavior in `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Connections.cs`; E19–E20 lock/receipt examples. Existing ad-hoc histories are not replaced. Update only actual shared callers of the changed tailoring request.

### 3. Anchors

Existing `TailorProcedureRequest`, `TailorProcedureAsync`, `AuditProcedure.ProgramProcedureId/SourceWording/CurrentResultRevision`, and `AuditProcedureResult.Revision/InputGeneration` must retain their different meanings. `EditAdHocProcedureAsync` already retains ad-hoc wording revisions and refuses editing after submission; reuse that contract where applicable. [E07–E09, E18]

**PROPOSED:** `AuditProcedureDefinitionRevision` with exact scope/procedure, revision, original programme-procedure reference, effective title/wording, rationale, predecessor, actor/time and request identity; `AuditProcedure.CurrentDefinitionRevision`; and a result's captured definition-revision identity. Definition revision is not a result sequence number.

### 4. Recipe

1. Resolve original wording from the exact immutable `ProgramProcedureId`/version. Show it separately from engagement-specific effective instructions. For ad-hoc work, use its existing insertion/wording history rather than pretending it came from the Word source.
2. Require a current expected definition revision, bounded changed text and a meaningful rationale. Reauthorize and apply existing holds/freeze checks. Take firm/client/actor locks before the procedure lock; never acquire a procedure lock then enter the global safety lock order.
3. Append one immutable revision and atomically update the procedure's current-definition pointer/cache. Same request and exact content returns the same revision; stale/changed intent conflicts. No direct update/delete path exists on retained revisions.
4. A definition change must make an old submitted result ineligible for new approval/completion without modifying its work/conclusion. Capture the definition ID/revision in new results; extend the existing current-evidence predicate to compare it.
5. Stop using a synthetic increment of `CurrentResultRevision` as the only definition-change signal. Preserve existing historical values, but new result sequencing advances only when a new result is submitted. Do not renumber old results to remove gaps.
6. Reuse the same revision source for tailoring previews, result submission and review. A concurrent tailor versus submit/review must either commit against the exact reviewed definition or return stale; never silently review changed instructions.
7. Reconstruct original source only from the existing immutable catalogue reference. If historical engagement text was already tailored, retain it as an explicitly labelled legacy effective snapshot; do not invent who approved it or when. A migration snapshot timestamp is not a historical authoring timestamp.
8. Existing issued artifacts remain readable. Unproven legacy definition-to-result binding must be disclosed and resolved through an explicit reviewed continuation before a new readiness claim; never backfill every old result with today's definition.
9. Protect definition IDs and scope with restricted FKs/uniqueness and immutable-history constraints. Additive migrations must not edit the original `AuditProgramExecutionWorkflow` migration.

### 5. Tests

Extend `AuditProgramWorkflowTests.cs` with a compact theory/fixture: tailoring preserves the exact original text and prior effective revision; stale concurrent editor loses without overwrite; a result tied to revision 1 cannot be approved against revision 2; fresh submission on revision 2 restores eligibility. Include one PostgreSQL direct mutation/delete refusal for the new immutable history, rather than mirroring every assertion through UI and E2E.

Reuse existing ad-hoc tests to confirm their source identity/history is unchanged. Migration evidence must show that an already tailored legacy row remains readable with honest provenance, not silently restored to baseline.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V1 programme/definition cohort and the existing current-evidence regression affected by the new definition pin; V3 for definition fields; V4 for migration/model drift. V2 is required only when the corresponding UI is changed. PASS means preserved original/history, exact revision fencing, immutable database history and truthful stale results. No full local .NET suite.

### 7. Future Done

Original and effective wording are unambiguous; new result/review eligibility binds the exact definition; all history survives; old result numbers and issued bytes remain unchanged. Upgrade/rollback notes explain legacy binding and which older writer must be disabled rather than allowed to overwrite new history. Broader catalogue authoring is excluded.

## Atomic Task 04 — Complete independent N/A proposal and Partner disposition

### 1. Objective

Close G04. A user can propose non-applicability with a real engagement-specific reason; a different current Partner accepts or returns that exact proposal. A status string must not remove professional work without evidence. Existing `APPLICABLE`, `NA_PENDING_REVIEW`, `NA_APPROVED` and pending semantics are retained. Depends on Task 03.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Application/Operations/Contracts.cs` for the new capability DbSets; `src/AuditSphereOps.Domain/Audit/Audit.cs`; `src/AuditSphereOps.Application/Audit/AuditProgramService.cs`; `src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs`; `src/AuditSphereOps.Application/Audit/EngagementAuditProgramQuery.cs`; `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs`; `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs`; `src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs`; `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`; the proposed shared `tests/AuditSphereOps.Api.Tests/AuditProgramCommandsApiTests.cs`.

**CREATE — PROPOSED:** `src/AuditSphereOps.Domain/Audit/AuditProcedureApplicabilityDecision.cs`; `src/AuditSphereOps.Application/Audit/AuditProgramService.Applicability.cs`.

**GENERATE — PROPOSED:** additive logical migration `AuditProcedureApplicabilityHistory`, timestamped/designer files and model snapshot under the existing Infrastructure persistence owner.

**READ-ONLY:** T056, current Partner authorization/rank owners, programme source, existing review-note and rework services. Task 07 supplies the common UI; do not add a separate N/A application or approval engine.

### 3. Anchors

Existing `DecideProcedureApplicabilityRequest(Guid AuditProcedureId, string Decision, string? Rationale)`, `DecideApplicabilityAsync`, `AuditApplicabilityStatuses`, and POST `/api/ui/procedures/{id}/applicability`. Current native `ApplicabilityInput` carries only `Decision` and supplies the fixed N/A reason. [E07, E12, E32]

**PROPOSED:** append-only applicability decisions with procedure/definition revision, client input basis, request ID, action/rationale, proposal/predecessor reference, actor/session/time and exact outcome. Proposed request additions include expected applicability/definition revision and the specific N/A proposal being reviewed. A returned proposal remains unfinished work; do not invent a new completed state.

### 4. Recipe

1. Remove the hard-coded rationale. Require the preparer to enter a bounded actual reason for N/A. “No data uploaded,” zero balance, a failed provider, or lack of permission cannot automatically establish non-applicability.
2. Enforce exact engagement/current role/session/professional-work/freeze checks for proposal and decision. The proposal is visible to eligible independent Partners even when the procedure has no submitted result; the current reviewer-only result filter must not hide this queue.
3. Append proposal evidence for the exact definition and input basis, then update its current display cache atomically. A proposal does not count as accepted N/A.
4. Approval requires an existing latest pending proposal, its exact expected revision/definition/basis, a different actor and the current Partner authority required by T056. A direct `NA_APPROVED` request without that proof is denied at Application, regardless of UI visibility.
5. Returning/rejecting a proposal requires a reason and retains the original rationale and both actors. The preparer can revise/re-propose or make the procedure applicable; new decisions supersede, never overwrite history.
6. Changing the definition or a materially relevant recorded basis invalidates the N/A decision for new completion. Use the explicit captured basis; do not indiscriminately rewrite every historical decision. No N/A decision suppresses separate engagement-level materiality, critical-confirmation, signed-output or freeze requirements.
7. Serialize decisions per procedure after the existing safety locks. Persist command identity with the decision so duplicate/concurrent approvals recover one decision; changed intent under the same identity conflicts.
8. Preserve compatibility status columns as projections only. Database rules should prevent an approved cache without the corresponding current scoped immutable decision and independent reviewer. Legacy `NA_APPROVED` without a demonstrable proposal remains historical/unverified, not retroactively approved.
9. Return named DTOs with original rationale, pending proposal, decision history and exact `canPropose/canApprove/canReturn` values. Authoritative checks remain in Application, including calls from the retained Web host.

### 5. Tests

Extend existing programme tests with one approval-lifecycle fixture: real proposal → different Partner approval; return/re-propose; switching back to applicable. Parameterize denial for same proposer, wrong role, missing proposal, stale definition/basis, and sibling procedure. Assert no new decision/cache change after denial.

One persistence check rejects forged `NA_APPROVED` without retained decision evidence; one shared API check verifies reason transport and response shape. An N/A row without result data must appear to the authorized Partner. Do not create 165 applicability tests.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V1 selected programme/applicability cases; V3 for the revised endpoint/DTO; V4 for additive history and guard migration. V2 applies when Task 07 connects the editor. Expected PASS: independent exact N/A decision and history; pending/stale/returned N/A remains a blocker. Full suites are V6/CI only.

### 7. Future Done

Real rationale reaches storage and reviewer; N/A approval cannot self-approve, use a stale proposal or bypass a mandatory separate gate; history/replay and legacy treatment are explicit. T056 completion remains dependent on its existing reviewed handoffs. Any choice to allow a role other than Partner requires an explicit approved policy amendment.

## Atomic Task 05 — Make procedure submission and review exact, atomic and recoverable

### 1. Objective

Close G05/AC04–AC06. Preserve the current generation/risk/rank/freeze/independence checks while adding exact definition/result intent and recoverable acknowledgments. A repeated HTTP POST must not create another result or lose proof of a successful review. Depends on Task 03. This is one procedure capability, not a generic command bus.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Application/Audit/AuditProgramService.cs`; `src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs`; `src/AuditSphereOps.Domain/Audit/Audit.cs`; `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs`; `src/AuditSphereOps.Application/Operations/Contracts.cs` only for genuinely new DbSets; `src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs`; `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`; the proposed `tests/AuditSphereOps.Api.Tests/AuditProgramCommandsApiTests.cs`.

**CREATE — PROPOSED:** `src/AuditSphereOps.Domain/Audit/AuditProcedureCommandReceipt.cs`; `src/AuditSphereOps.Application/Audit/AuditProcedureCommandContracts.cs`; `src/AuditSphereOps.Application/Audit/AuditProcedureCommandWorkspace.cs`.

**GENERATE — PROPOSED:** additive logical migration `AuditProcedureCommandReceipts`, its timestamped/designer files and model snapshot in the existing migration directory.

**READ-ONLY/reuse:** `src/AuditSphereOps.Application/Audit/AuditPlanningCommandWorkspace.cs`, `src/AuditSphereOps.Application/Audit/WorkpaperCommandWorkspace.cs`, `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs`, `src/AuditSphereOps.Application/Audit/ProcedureRiskBandEvaluator.cs`, `src/AuditSphereOps.Application/Practice/PracticeTimeService.cs`, current actor/authorization/freeze owners. Inspect and minimally adapt actual legacy/API/test callers when request contracts change; do not leave an unfenced alternate write path.

### 3. Anchors

Existing `SubmitProcedureResultRequest`, `ReviewProcedureResultRequest`, `ProcedureResultValue`, `ProcedureReviewValue`, `SubmitResultAsync`, `ReviewResultAsync`; POST `/api/ui/procedures/{id}/results` and `/api/ui/procedure-results/{id}/review`. The former currently substitutes generation 1 when input is absent. Existing successful submission creates an `AuditProcedureResult`, frozen `Workpaper` and `WorkpaperSubmission`; review creates `AuditProcedureReview` and optional existing rework task. [E07–E09, E12]

**PROPOSED** capability-local `StateAsync`, `PreviewAsync`, `ExecuteAsync`, `LookupAsync` and typed request/receipt records, following the existing reviewed-command pattern. The exact additive routes are **PROPOSED**: GET `/api/ui/procedures/{id:guid}/commands/state`, POST `/api/ui/procedures/{id:guid}/commands/preview`, POST `/api/ui/procedures/{id:guid}/commands/actions`, and GET `/api/ui/procedures/{id:guid}/commands/receipts/{requestId:guid}?requestHash=...`. They are not existing APIs. State exposes the actual current definition/result/input basis; preview exposes the canonical request hash; actions requires fresh assent over that exact preview; lookup returns an original retained receipt or an explicit unresolved/absent result.

### 4. Recipe

1. Define a bounded action contract for `SUBMIT_RESULT` and `REVIEW_RESULT`, not arbitrary property writes. Require exact procedure ID, current definition revision, expected current result revision, current input generation, reviewed payload and request identity. Review additionally pins result ID/revision and the latest decision seen. Never default an omitted generation or revision to 1 or today's value.
2. Canonicalize and hash the actual allowed payload on the server, including firm/client/engagement/procedure, actor, session epoch, request ID, definition/result/input basis, work/conclusion, evidence snapshot and action. Reject malformed JSON/reserved server evidence, over-bound text, unsupported states and numerical overflow before publication.
3. Acquire existing safety locks in the established order: firm safety, client generation/safety, actor, then engagement/procedure and dependent rows. `AuditPlanningCommandWorkspace.LockAsync` provides the firm/client/actor sequence. Use shared client/engagement locks for ordinary independent-procedure submission where compatible with existing source guards; do not serialize unrelated work by taking a whole-engagement exclusive lock unnecessarily.
4. Reuse the existing per-actor/request advisory-barrier pattern with a procedure-specific namespace. Lookup uses the corresponding shared barrier. Reauthorize after waits. Never acquire a procedure row first and then invert the firm/client ordering.
5. Refactor only transaction ownership necessary for composition: existing service operations must join an owned ambient transaction, rather than start/commit a nested transaction. When a working draft is used, pin its exact workpaper ID, owned draft revision and acknowledged save ID as well as its content; consume that draft only when the exact final result commits. Reuse the existing result's frozen workpaper/submission creation, or attach its exact newly frozen submission through the same transaction—never call both generic workpaper-submit and procedure-submit as two independent writes. Commit result/workpaper/review/rework, any draft consumption and the immutable original receipt together. A failure rolls back the publication as a unit; preserve any separately designed refusal-audit behavior.
6. Before submit, recheck applicability, exact definition/result sequence, current planning/risk/rank, freeze and evidence. Before review, recheck latest result/definition, input generation/risk, independent reviewer, staffing rank and zero applicable open notes. Do not replace the existing predicates with weaker receipt logic.
7. Same request ID and exact hash returns the original result/review receipt without another row or rework task. Same ID with different procedure/values conflicts. A historical committed receipt is an acknowledgment of that past action, not a current approval of later inputs.
8. An absent receipt lookup is not proof that an in-flight request cannot commit. Retain the original identity and permit only safe reconciliation or identical-intent replay through the same barrier; never mint a fresh ID merely because a list refresh or first lookup returned no item.
9. Return full typed acknowledgment: original actor/epoch, request ID/hash, procedure/source ID, definition and result IDs/revisions, action, decision, committed UTC timestamp and relevant workpaper/review IDs. Preserve exact revision values as decimal strings across JavaScript.
10. Route old native mutation endpoints through the same guard/receipt path or require the exact new inputs with a clear fail-closed response. Keep public service signatures stable where a compatible overload suffices, but no compatibility overload may bypass required freshness. Adapt actual retained-host callers; do not quietly declare rollback acceptance.
11. Preserve feature-enabled existing rework task creation and its unassigned/ineligible-preparer fallback. Do not create a second workflow queue or allow task completion to approve an audit result.

### 5. Tests

Reuse `AuditProgramWorkflowTests.cs` for one successful submit/independent-review sequence plus a parameterized stale/denial cohort. Add a deterministic two-context PostgreSQL same-request race and lost-response lookup: one result/workpaper/receipt, changed payload conflict, exact original result recovered.

Extend the same cohort for returned result → fresh revision → independent re-review, with old content readable. Verify transaction rollback leaves no orphan result/receipt/rework. API tests check actual serialized acknowledgment and opaque denied lookup, not all domain combinations. A no-tests-found run is not a pass.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V1 the affected programme/receipt cases; V5 the proposed API cohort once created; V3 new OpenAPI shapes; V4 receipt migration/drift. Build only the affected project graph if needed. PASS requires one original committed outcome, valid compare-and-swap, denied self/stale/foreign actions, and no partial publication. Full regression is CI-only V6.

### 7. Future Done

The original result/review outcome can be established without resending a new intent or guessing from current state; existing financial/security gates remain enforced. Every public caller is accounted for, receipt/definition history is append-only, migration/rollback and pending-request handling are documented, and executed evidence is attributed to the actual source snapshot.

## Atomic Task 06 — Freeze the evidence actually considered for each submitted result

### 1. Objective

Close G06/AC07 without creating another document store. Existing PBC/physical links are useful and already carry evidence identifiers/hashes. A reviewer must see the particular evidence considered for the submitted result, not an unrelated later link list. Manual audit work remains supported honestly. Depends on Task 05.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Application/Audit/AuditProgramService.cs`; `src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs`; `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Connections.cs`; the proposed `src/AuditSphereOps.Application/Audit/AuditProcedureCommandContracts.cs`/`src/AuditSphereOps.Application/Audit/AuditProcedureCommandWorkspace.cs`; `src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs`; `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`; the proposed shared `tests/AuditSphereOps.Api.Tests/AuditProgramCommandsApiTests.cs`.

**CREATE — PROPOSED:** `src/AuditSphereOps.Application/Audit/AuditProgramService.Evidence.cs` for the small scoped evidence projection/normalizer.

**READ-ONLY:** `AuditProcedureResult.StructuredResultJson/EvidenceReferencesJson`, `ProcedureEvidenceLink`, `ProcedurePhysicalLink`, existing upload/physical item records, current accounting evidence actions and provider handlers. No Graph/provider call is added to a database transaction.

**GENERATE:** none by default. Reuse the existing immutable result JSON and receipt snapshot; no new upload table/provider or independent evidence repository. A schema change requires showing why those existing snapshot fields cannot represent the bounded contract.

### 3. Anchors

`EvidenceCandidate`, `ProcedureEvidenceView`, `LinkClientEvidenceAsync`, `ProcedureEvidenceAsync`, `RegisterPhysicalItemAsync`, `LinkPhysicalItemAsync`, `PhysicalItemsAsync`; `ProcedureEvidenceLink` already captures upload/request/file/hash/note/actor/time. Existing `SubmitProcedureResultRequest` accepts a string reference array, while `ProcedureAsync` separately returns current linked evidence. [E07–E08, E11, E18]

**PROPOSED:** a versioned server-owned evidence block inside the submitted structured-result envelope, with selected existing link IDs, exact source identity/hash/revision where available, attribution and a clear evidence kind. The caller submits references to resolve, not a trusted `verified=true` value.

### 4. Recipe

1. Preserve all existing PBC/physical linking and duplicate-link semantics. At procedure preview, let the auditor choose which linked items were actually considered; do not automatically include every later/current link in a past submission.
2. Resolve each chosen link in the same firm/client/engagement/procedure scope. Validate existing received/accepted/source-state conditions through the existing owner. Capture the stored upload/request/link identity and exact digest; do not treat a filename, URL or caller-provided digest as proof.
3. For physical evidence capture the registered item/link plus its observed index and relevant custody record, not an assertion that the physical item was electronically verified. A later location movement does not rewrite the old evidence snapshot.
4. For management explanations, attendance/inspection notes and externally performed recalculations, retain a bounded manual reference, what was observed, who recorded it and the auditor's conclusion. Distinguish `MANUAL_REFERENCE` from stored electronic evidence. Do not require a fake PDF for every professional judgment or accept an empty unexplained conclusion.
5. For internal calculations/schedules later connected by Task 09, bind exact authorized source/result ID, revision and digest. The helper's execution is not a procedure review; the auditor still records how it supports the conclusion.
6. Serialize this normalized block with a versioned server-reserved property in `StructuredResultJson`, while preserving the current risk-basis envelope. Reject caller attempts to forge either server-owned block. Bound the total bytes using existing result limits; errors must be actionable and never truncate evidence silently.
7. Pin the entire block into Task 05's request/receipt hash and immutable result. Re-resolve currentness at commit after locks without performing network I/O. A relevant source change between preview and commit returns stale.
8. Return the submitted snapshot and current linked evidence as explicitly different projections. A new upload/link or movement does not change an old review's content. Superseded evidence can remain readable as historical; whether it supports new work is decided by the current source/generation rules.
9. Preserve old plain-text arrays as `LEGACY_REFERENCE` evidence. Never infer a provider receipt, hash, author or approval that was not recorded. Prevent arbitrary URL fetching, Graph calls from Angular, bearer-token storage or client disclosure of internal work.

### 5. Tests

One existing programme result fixture covers stored PBC link + physical reference + manual observation. Submit it, add a later link/move the physical item, and assert the submitted snapshot/receipt remains unchanged. Parameterize denial for foreign link, altered digest/source revision and malformed reserved evidence. An empty electronic-reference list is not inherently wrong when an approved manual-observation procedure has complete recorded evidence; test that distinction rather than blanket “attachment required.”

Use `FieldworkConnectionsJourneyTests.cs` only for the additional native link-to-submitted-result handoff not established by domain tests. Do not recreate provider acceptance tests or call a real tenant locally.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V1 relevant programme/evidence cases; V5 changed API receipt/projection; V3 contract drift. No migration/drift run is required if storage shape remains inside existing immutable JSON. V2 applies with Task 07. PASS requires exact historical evidence, scope denial and no false provider verification. Live evidence/custody remains D3/V7.

### 7. Future Done

A reviewer can identify exactly what supported a result and distinguish current, historical, physical, manual and provider-backed evidence. Old results never gain invented proof, no external I/O occurs inside publication transactions, and no new document-management system or provider permission is introduced.

## Atomic Task 07 — Provide the reusable native procedure execution and review workspace

### 1. Objective

Close G07: replace the “Execute from source workflow” dead end with one usable, scope-safe workspace for any applicable baseline or ad-hoc procedure. Preserve existing sampling, evidence, review notes and accounting tools. Depends on Tasks 02–06. No 165 bespoke pages, generic form-builder, new state-management dependency or automatic professional conclusion.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts`; `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.spec.ts`; `src/AuditSphereOps.Ui/src/app/app.routes.ts`; `src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs`; the proposed `src/AuditSphereOps.Application/Audit/AuditProcedureCommandContracts.cs`/`src/AuditSphereOps.Application/Audit/AuditProcedureCommandWorkspace.cs`; `src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs`; the proposed shared `tests/AuditSphereOps.Api.Tests/AuditProgramCommandsApiTests.cs`; `tests/AuditSphereOps.E2E.Tests/AngularAuditFieldworkParityJourneyTests.cs`.

**CREATE — PROPOSED:** `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace.ts`; `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace.html`; `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace-contracts.ts`; `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace.spec.ts` in that same directory. Keep command/lifetime state inside this component unless an existing compatible helper genuinely reduces duplication.

**READ-ONLY/reuse:** `src/AuditSphereOps.Ui/src/app/core/api.ts`, `src/AuditSphereOps.Ui/src/app/core/session.ts`, `src/AuditSphereOps.Ui/src/app/core/tab-drafts.ts`, `src/AuditSphereOps.Ui/src/app/core/unsaved-changes.ts`, `src/AuditSphereOps.Ui/src/app/core/unsaved-changes.guard.ts`, existing workpaper editor/command state and E19/E25 patterns. No new app-wide polling or draft subsystem. No new SPA route is necessary.

### 3. Anchors

Existing route `/app/engagements/:id/audit-fieldwork`; existing `AuditFieldwork`, `decodeFieldwork`, `decodeProcedureReview`; server `FieldworkProcedure`/`FieldworkResultView`/`ProcedureReviewWorkspace`; result/tailoring/applicability routes and Task 05's **PROPOSED** command routes. The existing `/app/audit/workpapers/:id` editor owns workpaper drafts, not proof that a procedure result was submitted. [E11–E13, E19, E23–E25]

**PROPOSED:** a selected procedure panel within the existing fieldwork route, optionally restored by an allowlisted `procedureId` query parameter. It consumes Task 05 state/preview/receipt and Task 06 submitted evidence. Its DTOs must declare actual result status, definition/source IDs, actor attribution, structured results and review history omitted from the old minimal projection.

### 4. Recipe

1. Keep the 20-section navigator and existing programme list. Add a clear “Open procedure”/“Review submitted result” action, source ID and current evidence state. Default navigation must never silently exclude pending N/A or rework from authorized reviewers.
2. Display immutable source wording, effective tailored instructions, definition revision, applicability/rationale/history, source programme version/hash and existing relevant controls. Show human-editable work performed, conclusion and references; never prefill “completed” or a professional conclusion from the procedure text.
3. Use ordinary typed Angular forms for the common inputs. Specialized structured evidence is composed from existing supported tools or bounded manual fields; do not ask ordinary auditors to author arbitrary JSON.
4. Reuse the existing owned workpaper draft lifecycle where a working draft is saved. Keep draft-save identity separate from final result identity. A final action must invoke `AuditProgramService.SubmitResultAsync` through the new exact command boundary and consume/freeze the actual reviewed draft in the same transaction when used. A draft save or generic workpaper submission cannot mark the procedure Reviewed.
5. Before dispatch, show the exact procedure, effective definition, work/conclusion, selected evidence and existing risk/input basis. Editing any material field, selecting another procedure or observing a newer basis invalidates preview and assent.
6. Reviewer mode shows the frozen submitted result, preparer/time, evidence snapshot, actual result/definition revision and review history. Offer REVIEWED/CHANGES_REQUIRED only from current server capabilities; return requires actual comments. Connect real N/A proposal/Partner controls from Task 04.
7. Bind every load, preview, command, recovery and `finally` callback to firm/user/session epoch, engagement, procedure, selected revision and a monotonically new component/visit lifetime. A delayed A response cannot overwrite B, and A→B→A is not the same visit.
8. Keep the active editor outside a parent loading branch that destroys children on list/summary reload. A successful summary refresh must not discard drafts or pending requests. Actual session/scope denial still clears protected content immediately when detected; stale protected content is not kept merely to preserve edits.
9. Add `unsavedChangesGuard` to the existing audit-fieldwork route, composing with the selected panel. Internal procedure/section changes use the same save/discard/cancel protection. Never persist file bytes, tokens or assent. Existing allowed tab-draft metadata and bounded pending request references are sufficient; storage failure must be explicit before an irreversible-looking dispatch.
10. Unknown result stays unknown until its exact receipt is decoded and matches the owning request/procedure/actor. A generic HTTP 200, a matching conclusion string or a reloaded Reviewed status cannot resolve it. Lost/denied callbacks do not recreate cleared state.
11. Use semantic labels, keyboard focus, error summaries, readable text and responsive layout. Section/list filtering changes what is visible, not the completion denominator or authorization. Reuse Angular Material/CDK already present.
12. Expose the actual existing result/review history through named API DTOs; return exact revision strings and documented nullability. Regenerate only the canonical OpenAPI contract. Do not claim that route availability proves a performed professional procedure.

### 5. Tests

Extend `fieldwork.spec.ts` for selecting source sections/IDs, current counters and the N/A/no-result reviewer case. In the **PROPOSED** `procedure-workspace.spec.ts`, use a small component fixture covering draft→preview→submit, reviewer return, changed-input assent reset and lost-response recovery. Parameterize A→B, A→B→A, destruction and session invalidation to exercise one lifetime rule rather than duplicate entire suites.

Extend `AdoptedProgramReviewedFieldworkAndFrozenWorkpaperSurviveReload` in `AngularAuditFieldworkParityJourneyTests.cs`: perform the missing native work/submit/review actions through the UI, not by calling the service to manufacture the state being asserted. Add one lost-response branch only if not already proved at the required boundary.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V2 only `fieldwork.spec.ts` and the proposed procedure spec; V5 for changed real response shapes; V3 OpenAPI. Run a focused Angular build only when necessary for new template/compilation integration. A bounded existing browser scenario may be run with its verified exact selector when that boundary cannot be proved otherwise; full automated browser suites remain CI-only V6. Record manual browser evidence separately.

PASS: any authorized selected procedure can be performed and independently reviewed through native UI; drafts/requests do not cross visits; revocation remains effective; result identity is exact. A screenshot or mocked API-only state is not the full journey.

### 7. Future Done

The native execution/review gap is closed without 165 screens. A generic workpaper remains distinguishable from a submitted procedure result; real comments and evidence reach the frozen record. Focused behavioral and API evidence exists, new routes/contracts are documented, and no new frontend dependency, source deletion or retirement claim is introduced.

## Atomic Task 08 — Unify complete-programme and current-evidence progress

### 1. Objective

Close G08 while preserving the already corrected completion predicate. All programme/area/native views must agree about current independently reviewed work and approved N/A. Missing source rows or filtered pages cannot imply completion. Depends on Tasks 02–06; Task 10 subsequently adds approved evaluation contexts without weakening this evidence rule.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Application/Audit/EngagementAuditProgramQuery.cs`; `src/AuditSphereOps.Application/Audit/AuditAreaSummaryQuery.cs`; `src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs`; `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs`; `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs`; `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts`; `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.spec.ts`; `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`; `tests/AuditSphereOps.Domain.Tests/AuditAreaSummaryTests.cs`.

**READ-ONLY, MODIFY only the affected consumption:** `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs` already uses current evidence; preserve its exact result/review identities. `UiEndpoints.Fieldwork.cs` owns changed projection contracts; update named response metadata there. Reuse existing query/paging owners. No independent metrics store, new worker, migration or caching service by default.

### 3. Anchors

`EvaluateProcedureEvidenceBatchForAuthorizedScopeAsync` already returns current result/review IDs and states such as `STALE_GENERATION`, `STALE_RESULT`, `STALE_REVIEW`, `OPEN_REVIEW_NOTES`, `SELF_REVIEW`, `REVIEWED_CURRENT`. `EvaluateCompletionForAuthorizedScopeAsync` uses it. `GetProgramAsync` incorrectly includes Submitted/InReview in Reviewed; `GetAreaSummaryAsync` uses raw Reviewed and historical item-test counts. [E14–E17]

**PROPOSED:** one small programme coverage projection reusing the batch evaluator and Task 04's current N/A predicate, with distinct baseline/ad-hoc totals and mutually interpretable current states. Do not introduce a second professional-completion evaluator.

### 4. Recipe

1. Resolve exactly one active adoption and compare its baseline source references against the full immutable catalogue. A nonzero procedure count, matching total alone, or one ad-hoc procedure is insufficient. Missing/duplicate/wrong-version IDs produce an explicit programme-integrity blocker.
2. Keep all 165 baseline rows accounted for, including pending applicability and approved N/A. Ad-hoc work has its own count and joins applicable review gates; it never supplies a missing baseline ID.
3. Obtain current result/review eligibility once through the existing batch helper, with Task 03's definition pin. Obtain current N/A eligibility from exact retained decisions. Do not call a database query independently for each of 165 rows or recompute from UI status strings.
4. Distinguish pending applicability, N/A pending/returned, applicable needs work, submitted awaiting review, changes required, stale evidence, current independently reviewed and current approved N/A. A raw `SUBMITTED`, `IN_REVIEW` or historical `REVIEWED` flag is not current reviewed completion.
5. Define totals explicitly. Current reviewed applicable work and approved N/A are separate figures. A filtered page's row count is not the engagement denominator; zero applicable rows does not manufacture 100% readiness or waive mandatory separate gates.
6. Fix area summary semantics. Latest reviewed current item evidence counts once per selection item; multiple historical tests cannot inflate coverage. Do not label all assessments as “workpapers reviewed.” Either expose accurate separate fields or correct the existing name/contract with coordinated consumers.
7. Reuse existing item-test/assessment/confirmation predicates when currentness is required, rather than implementing weaker parallel checks. Area-specific facts are not a complete engagement-release verdict.
8. Reauthorize after composing sensitive projections and ensure counts, rows and evidence refer to one coherent snapshot. Opaque foreign IDs remain indistinguishable from missing unauthorized objects.
9. Preserve old review history as history; do not fix counters by rewriting statuses or removing stale records. Current SRM facts continue using the exact existing current-result/review IDs, not a count-only digest.

### 5. Tests

Extend `EngagementProgramQuery_ReportsTailoredCoverage` in `AuditProgramWorkflowTests.cs` and `AreaSummary_ReportsCoveragePerArea` in `AuditAreaSummaryTests.cs`. One shared fixture moves a procedure through submitted, returned, current reviewed, generation/definition stale and approved N/A, asserting agreement between programme, area, fieldwork and completion outputs.

Add a missing-baseline/same-count-wrong-ID integrity case, and a historical two-test-revisions case that counts one current selected-item result. Use legal source changes to cause staleness, not mutation of immutable historical evidence or disabled database triggers. Angular checks verify the displayed semantics, not repeat all domain guard combinations.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V1 programme/area/current-evidence cohort; V2 `fieldwork.spec.ts`; V3 only changed output schemas. No V4 without a model change. PASS requires exact baseline integrity and consistent current-state counts, without extra per-procedure database round trips. Performance improvement is not claimed without measurement. Full suites remain V6/CI.

### 7. Future Done

No view credits submitted/stale work as reviewed or silently changes a denominator. Programme completeness includes every source identity, current N/A and ad-hoc obligations. Existing current-evidence security fixes remain intact, and the diff contains no unrelated metric engine, status rewrite or test deletion.

## Atomic Task 09 — Connect all 20 sections to existing tools and explicit manual work

### 1. Objective

Close G09/AC09. Every source procedure has a practical common evidence-recording path and, where relevant, a contextual link to an existing verified capability. The source's verb “recalculate” does not authorize an unapproved accounting methodology or require a new calculator. Depends on Tasks 06–08.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs`; the proposed `src/AuditSphereOps.Application/Audit/AuditProgramService.Evidence.cs` and `src/AuditSphereOps.Application/Audit/AuditProcedureCommandContracts.cs`; the proposed `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace.ts`; `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace.html`; `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace-contracts.ts`; `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace.spec.ts`; `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts`; `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`.

**CREATE — PROPOSED:** `src/AuditSphereOps.Application/Audit/AuditProcedureSupportMap.cs` only for a small explicit source-ID/section-to-existing-tool key map. This is navigation metadata, not applicability, authorization or an audit methodology engine.

**READ-ONLY/reuse:** `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Connections.cs`, `src/AuditSphereOps.Application/Audit/AuditAreaSummaryQuery.cs`, `src/AuditSphereOps.Application/Audit/AuditPlanningCommandWorkspace.cs`, `src/AuditSphereOps.Application/Audit/AuditProgramLibraryQuery.cs`, `src/AuditSphereOps.Ui/src/app/app.routes.ts`, `src/AuditSphereOps.Ui/src/app/features/accounting/evidence-actions.ts`, and the existing audit-plan, confirmations, statements, general-ledger, TB-intake, workpaper and completion owners. Touch a destination component only for a missing exact-context return handoff demonstrated by this integration; record its actual path before editing.

**GENERATE:** no migration or new calculator/provider by default. API/OpenAPI changes are limited to the support projection or newly captured exact references.

### 3. Anchors

Existing `PreviewSamplingAsync`/`RunSamplingAsync`, `EvidenceCandidate`, `ProcedureEvidenceView`, physical evidence methods and ad-hoc procedure methods are real. Current routes include `/app/engagements/:id/audit-plan`, `/confirmations`, `/general-ledger`, `/tb-intake`, `/statements`, `/financial-package`, plus `/app/audit/workpapers/:id`, `/app/accounting/evidence/:kind/:id/actions` and completion. The accounting evidence action screen accepts ECL, INVENTORY, SPECIALIST, ANALYTICAL and JOURNAL_RISK kinds. [E18, E24–E25]

**PROPOSED:** bounded contextual support links containing an allowlisted tool kind, exact authorized target identity/revision where available, explanation and availability/blocker. Section titles or substring matches are not exact FSLI/evidence relationships.

### 4. Recipe

1. Use the 20-section design and the complete procedure appendix below as the requested source coverage. Provide the common work/conclusion/evidence/review path for every ID, even where no specialized automated helper is approved.
2. Offer only known current tools; make their purpose precise: obtaining/inspecting a source, recording an observation, checking a supported calculation, recording a confirmation, or reaching an existing professional decision. Opening a tool never auto-completes the procedure.
3. Resolve target client/engagement/period and actual stored source/result on the server. Do not select “first package,” latest unrelated annual engagement, a similarly named FSLI or arbitrary user-entered GUID. A suggested match is a suggestion requiring review, not audited coverage.
4. Preserve exact return context to the selected procedure and Task 06 evidence snapshot. A selected result can be linked only after the source owner's current scope/revision checks. Changing the source or procedure while the tool is open must invalidate an old selection.
5. Reuse existing sampling and confirmations screens for Cash/AR/AP/Inventory/Revenue and other eligible tests. Keep sample selection, selected-item testing, evaluation of confirmation differences and final procedure conclusion separate.
6. Reuse existing accounting evidence/workspaces where their supported profile applies. For unsupported depreciation, interest, payroll, ECL assumption, tax or other methodology, permit a clearly identified manual/external workpaper with calculation basis and independent review; do not invent a rate, legal rule, forecast or plug.
7. Retain all conditional terms from the Word document: “where applicable,” “where appropriate,” “selected,” “significant,” and “where available.” The software cannot decide these judgments from a source title or zero balance. Task 04 governs explicit exclusion.
8. Final-report and signed-representation helpers must return the actual exact existing evidence, not merely a link to a screen. Task 10 owns their non-circular prerequisite policy and final decision gates.
9. Do not implement another payroll/inventory/sales/purchases system, automate confirmations without approved channels, duplicate statement generation, or introduce a new provider. Existing feature/profile limitations remain visible and independently accepted under D2/D3.

### 5. Tests

Use one parameterized support-map test for all 20 section mappings, checking that all 165 IDs retain the common route and unsupported tools are not represented as available. Reuse `procedure-workspace.spec.ts` for one exact tool-return binding and changed-procedure/source rejection.

Extend one existing `FieldworkConnectionsJourneyTests.cs` or native fieldwork journey for a cash/sampling/evidence handoff only where that end-to-end connection is not already protected. Purely manual procedure wording requires no invented calculator test. Specialized numerical tests remain with the existing calculator owners and approved expected values.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V1 support-map/context cohort; V2 procedure/fieldwork specs; V3 changed support/reference contract. Check only affected destination interactions, not a broad browser sweep. PASS means all source IDs remain executable/documentable, links preserve exact context, unsupported automation is disclosed, and manual/automated evidence is distinguished. D2/V7 supplies professional adequacy, not UI route existence.

### 7. Future Done

All 20 sections are connected to the common workflow, relevant existing tools are reused, and no unexplained “execute elsewhere” dead end remains. No new calculation engine or professional policy was inferred from a checklist verb. The acceptance record clearly separates general execution support, specialized automation and actual performed audit work.

## Atomic Task 10 — Complete terminal audit procedures without circular signing gates

### 1. Objective

Close G10/AC10. **Source-derived inference to reproduce:** AWP-20-10 asks the auditor to finalize the report/signed financial statements; the current all-applicable-procedure completion and `FactsDigest` feed SRM, Partner clearance and report currentness. Requiring that terminal attestation beforehand, or adding it afterward to the same signed-source digest, creates an unsound cycle. Similar checks are needed for signed representation and combined Manager/Partner review attestations.

Target: an approved acyclic evidence dependency policy, not looser sign-off. Depends on Tasks 04–06 and 08; **D1 must be approved before changing gate semantics**. The Word source is not silently reinterpreted by the coding agent.

### 2. Files

**MODIFY:** `src/AuditSphereOps.Application/Operations/Contracts.cs` for the new capability DbSets; `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs`; `src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs`; `src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs`; `src/AuditSphereOps.Application/Audit/AuditProgramService.cs`; `src/AuditSphereOps.Application/Audit/EngagementAuditProgramQuery.cs`; `src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs`; `src/AuditSphereOps.Domain/Audit/Audit.cs`; `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs`; the proposed `src/AuditSphereOps.Application/Audit/AuditProcedureCommandContracts.cs`, `src/AuditSphereOps.Application/Audit/AuditProcedureCommandWorkspace.cs`, `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace.ts` and `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace-contracts.ts`; `src/AuditSphereOps.Ui/src/app/features/audit/completion.ts`; `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`; the proposed `tests/AuditSphereOps.Api.Tests/AuditProgramCommandsApiTests.cs` and `src/AuditSphereOps.Ui/src/app/features/audit/procedure-workspace.spec.ts`.

**CREATE — PROPOSED:** `src/AuditSphereOps.Domain/Audit/AuditProgramCompletionPolicy.cs`; `src/AuditSphereOps.Application/Audit/AuditProgramCompletionPolicyEvaluator.cs`. The Domain file retains policy identity/invariants; the Application file evaluates the bounded approved policy. No general workflow engine.

**GENERATE — PROPOSED:** additive logical migration `AuditProgramCompletionPolicyBinding`, its timestamped/designer files and model snapshot, only after D1 determines the exact accepted policy contract.

**READ-ONLY/reuse:** existing `AuditWorkprogramManagerApprovals`, signed-representation, opinion, signing, financial-package review and release owners. Inspect their exact current callers before modifying a consumed digest. No changes to certificate trust, Graph grants, commercial activation or the eleven canonical lifecycle states.

### 3. Anchors

`EvaluateCompletionForAuthorizedScopeAsync`; `FactsAsync`/`FactsDigest`; `CompileAfterFinalReviewAsync`; `GenerateSummaryReviewMemorandumAsync`; `PartnerClearAsync`; `DecideOpinionAsync`; `IsCurrentAsync`; `SignIndependentReportAsync`. Partner clearance refuses non-confirmation completion blockers; signing separately refuses critical unreturned/unevaluated confirmations and requires current opinion/report. [E15, E21]

**PROPOSED:** explicit evaluation contexts such as manager handoff, Partner clearance, signing and final programme completion; these are read/evidence contexts, **not new writable engagement stages**. An immutable approved completion-policy identity is pinned to the adopted programme/engagement.

### 4. Recipe

1. Reproduce the literal sequence with actual relevant catalogue IDs: complete ordinary work, leave terminal signed-output attestation truthfully pending, attempt SRM/clearance/signing, then attest to the output. Record where the current implementation blocks or invalidates the source. Do not seed the terminal procedure as already performed to make a test pass.
2. Obtain D1: a finite reviewed mapping identifies each procedure's actual prerequisite context and any terminal output attestation. Preserve every source ID and wording. No blanket exclusion of Section 20, automatic N/A, or “all but inconvenient procedures” exception is allowed.
3. Persist the exact approved policy revision/hash and reviewer evidence as an immutable companion to the controlled programme. Bind it explicitly at adoption or a separately reviewed first policy-binding handoff; do not silently update all active engagements. Missing/unknown policy remains a visible blocker for the new completion claim.
4. Separate **upstream audit facts that determine a document/decision** from **downstream checklist evidence attesting to that exact output**. The latter pins the output/decision's identity and digest; it must not be recursively included in the document's own defining digest.
5. For AWP-20-07, require the existing verified signed management representation identity under its actual current gate. For AWP-20-09, reference actual required senior/manager/Partner decision identities, not a generic “reviewed” comment. For AWP-20-10, reference the actual final report and signed financial-statement evidence applicable to the approved profile. The auditor's attestation remains a human result and independent review.
6. AWP-20-01 and similar roll-up attestations must evaluate their approved upstream set without counting themselves as their own prerequisite. Final programme completion still enumerates all 165 baseline procedures and additional applicable work with a current reviewed result or valid independently approved N/A.
7. Keep actual Manager approval, current SRM/Partner clearance, opinion authority, Red-risk clearance, critical confirmation return/evaluation, required representation and financial-package review gates intact. An output attestation cannot replace the actual domain decision or sign a report.
8. Changes to underlying audit evidence, definitions, accepted sources, actual professional decisions or referenced artifact identities stale the relevant dependent outputs and attestations. Merely recording a truthful downstream attestation does not alter already signed bytes or require a fictitious endless re-sign cycle.
9. Include the approved policy/hash and exact evaluation context in currentness/receipt evidence. Do not generate a second mutable lifecycle or let a task flag assign readiness. User-facing output distinguishes “ready for Partner review,” “signed,” and “all audit-programme completion evidence reviewed.”
10. Preserve old signed artifacts, old source digests and historical adoptions. Apply a versioned currentness algorithm to new policy-bound outputs; never rewrite old digest history or grandfather missing terminal evidence as accepted. Existing engagements require an explicit compatibility/continuation handoff before new-policy readiness is claimed.
11. Keep declaration of final audit-programme completion distinct from actual live release/archive success. D3 and existing release/provider gates remain separate, even after every procedure has valid local evidence.

### 5. Tests

Add a focused source-backed sequence in `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs` (PROPOSED method `TerminalProcedureAttestation_DoesNotCreateASigningDependencyCycle`), using the existing approved planning and completion fixture conventions: ordinary current work → actual Manager approval/SRM/Partner clearance → legitimate report/signature/representation evidence → terminal procedure attestation/review → final programme completion. Test both before and after the attestation; a stable signed-source digest must not cycle.

Parameterize rejection for an uncompleted ordinary procedure, wrong/missing signed artifact, stale actual approval and unapproved/unknown policy. A material underlying source change must still stale readiness. Reuse existing critical-confirmation, independent-review and signing tests; do not weaken them or use direct database status assignments in place of genuine domain operations.

### 6. Verification

**NOT RUN — SPEC_ONLY.** First record D1. Then V1 focused programme/deliverable sequence, V5 affected API state contract, V2 procedure/completion UI checks, V3 consumed contract changes and V4 policy binding migration. V6 full source-pinned regression is required in CI before release claims. V7 covers real professional/signing acceptance.

PASS requires a truthful non-circular sequence, all mandatory upstream gates retained, and all final source rows accounted for. Until the policy is approved and this evidence exists, this task remains blocked/unverified, not bypassed.

### 7. Future Done

The reviewed policy and exact source/output dependency graph are recorded; terminal procedures can be completed truthfully without self-invalidating their evidence; actual stale work still blocks; all historical digests/artifacts remain intact. No new audit opinion automation, broad provider permission or lifecycle-state override exists. Production acceptance remains dependent on D2/D3.

## Atomic Task 11 — Accept the exact programme through focused checks and current-source CI

### 1. Objective

Close G11 and reconcile AC01–AC12 with real evidence. Prove the shared execution contract, all source content and the accepted specialized/terminal boundaries without creating a test case for every checklist sentence. Depends on the applicable completed implementation tasks, D1 for final-stage behavior, and separate D2/D3 acceptance. A test file or this specification is never a PASS.

### 2. Files

**MODIFY only from observed evidence:** `docs/task_breakdown/tracking/auditsphere-audit-tracker-workflow-traceability.md`; `docs/execution/status.json` (targeted fields only); `docs/execution/auditsphere-execution-current-slice.md` (relevant newest handoff); `docs/migration/auditsphere-migration-blazor-audit-planning-fieldwork-source-review.md`; `docs/architecture/auditsphere-architecture-code-map.md` only for changed owners; `docs/testing/auditsphere-testing-strategy-definition-of-done-current.md` only for the stale release-gate statement confirmed by current workflow source.

**MODIFY as necessary for the exact new behavior:** existing `tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs`, `tests/AuditSphereOps.Domain.Tests/AuditAreaSummaryTests.cs`, `src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.spec.ts`, `tests/AuditSphereOps.E2E.Tests/AngularAuditFieldworkParityJourneyTests.cs`, `tests/AuditSphereOps.E2E.Tests/FieldworkConnectionsJourneyTests.cs`, and the focused source/API/procedure tests proposed in Tasks 01/07. No additional test project or framework.

**READ-ONLY:** `.github/workflows/ci.yml`, `.github/workflows/release-verification.yml`, `.github/workflows/release.yml`, existing task-status/board tooling and applicable task cards. Do not hand-edit a generated board or change CI triggers, branch protections or environments as incidental work.

### 3. Anchors

Existing source and workflow tests, `PgTestSchema`, `PlanningBasisSeed`, `OwnedHost`, existing task traceability and V0–V7 commands. `AdoptedProgramReviewedFieldworkAndFrozenWorkpaperSurviveReload` is an existing native journey to extend, not proof of the newly requested end-to-end path. The full-regression workflow is nightly/manual; the current release workflow includes a full-source precondition. [E26–E39]

**PROPOSED:** one bounded AS-AWP-EXEC evidence entry in the existing ledger/traceability, carrying exact source/test snapshot, selected/omitted checks, result counts/durations, artifact identity and independent review. Do not introduce a competing completion ledger.

### 4. Recipe

1. Rebase the investigation, not the branch history: compare the actual implementation checkout with this pinned source and reverify changed anchors before editing. Do not redo work already implemented after this specification.
2. Independently reconcile the fixture/appendix with the Word source: every title, wording, section and ordinal; preserve conditions and the distinction between professional activity and software assistance. One data-driven source test covers all 165 entries.
3. Use a small shared workflow fixture to prove applicable→draft→submitted→returned→resubmitted→independently reviewed and justified N/A. Add only material distinct scope, freshness, history, replay and race dimensions not already protected.
4. Exercise one representative native business journey across programme adoption, Cash & Bank work/evidence, actual reviewer return and original receipt recovery. Separately exercise the D1-approved terminal sequence where the general journey cannot prove it. Do not run a browser journey for each of 165 procedures.
5. For existing specialized calculators, reuse their approved boundary tests and actual stored output contracts. A generic procedure test does not certify ECL, tax, useful lives, sampling methods, forecast assumptions or complete financial-statement compliance.
6. Run only the affected local cohorts after each coherent slice; reuse successful checks unless relevant code/test/configuration changed. On failure rerun the failing/affected cohort. Do not delete, skip or weaken protections to reduce totals, and do not run the unfiltered suite locally.
7. Obtain fast PR CI on the implementation revision. Obtain complete unfiltered full regression through the existing nightly/manual CI workflow for the exact release candidate, retaining its resolved source and aggregate result. An earlier/nightly different SHA or fast-CI-only success is insufficient.
8. Obtain professional acceptance from a qualified methodology owner and independent reviewer across all 20 source sections and the selected supported profile. Record which procedures use manual evidence, which use accepted tools, which are conditional and how final attestations are sequenced. Professional acceptance cannot be authored by the coding agent.
9. Record live provider/document/signing/operational evidence only in an authorized environment with appropriate human approval. Existing synthetic evidence does not close D3. Actual credentials, client data and private source documents remain in approved private evidence stores.
10. Update the existing source/action review with observed native counterparts and explicit remaining limitations. Do not infer Web retirement or whole-product production readiness from this programme acceptance. Use existing task-status tooling only after its dependencies and independent review conditions are genuinely met.
11. Reconcile the single outdated testing-strategy paragraph with current `verify-release-source`; preserve the fast/local versus full-CI distinction. This is documentation alignment, not a request to implement another pipeline or change required PR checks.
12. Final handoff reports the minimal diff, actual command/results, deferred checks, approvals, migration/rollback disposition and remaining blockers. Measured timing can be recorded; no speedup or completion percentage is invented.

### 5. Tests

The minimum **kinds of evidence**, not a numeric quota, are: one independent catalogue comparison; one reusable programme lifecycle/permission/history/replay fixture; the affected API/Angular contract tests; one representative native journey; and the approved terminal dependency sequence. Combine or reuse them where each distinct invariant remains observable.

For every procedure in the appendix, preserve content and identify its common workflow and any accepted tool/manual boundary. This traceability is not 165 claims that its professional activity has been executed in a live engagement. The professional review record supplies that acceptance distinction.

### 6. Verification

**NOT RUN — SPEC_ONLY.** V0 targeted documentation/diff checks; affected V1/V2/V5 cohorts only; V3/V4 only when impacted; V6 complete regression through CI/CD; V7 independently authorized professional/live evidence.

PASS must state the exact source/test snapshot and actual executed counts/results. Zero discovered tests, skipped required cases, unfinished/cancelled CI, old SHA evidence, unavailable PostgreSQL and a successful build alone are not sufficient. Omitted checks remain explicitly NOT RUN/UNVERIFIED.

### 7. Future Done

Each acceptance row has an exact evidence reference or an explicit unresolved blocker; all 165 source IDs are represented without fabricated professional completion; current CI and necessary professional/live acceptance are retained. Delivery uses the PR/approval handoff in Section F. No direct master push, merge, deployment or irreversible action is authorized by completing this checklist.

# D. Verification and acceptance traceability

## D1. Execution rules

**Every command below is NOT RUN — SPEC_ONLY.** Commands are future implementation checks, not a script to execute wholesale. Run from the repository root in the supported shell. Choose the smallest cohort that can detect the change; do not repeat already-sufficient checks merely to collect more counts.

Use the pinned SDK and existing locked dependencies. Restore tools/packages or run `npm ci` only for a fresh environment, a changed relevant lockfile/toolchain, or demonstrated missing assets—not every task or edit. A focused `dotnet test` without `--no-build` compiles its affected test-project graph; avoid separately rebuilding that same unchanged graph. Use `--no-build` only when the exact edited source and tests have already been built in the same configuration.

Database-free source/algorithm checks need no database. Database-backed programme, API and browser checks must use the existing owned synthetic PostgreSQL fixtures, the approved `auditsphere_tests` test target and disposable fixture databases, never the development or production database. Default local port is 5433; the existing CI service configuration is a different isolated target. Missing SDK/dependencies/database/browser prerequisites are a blocked verification environment, not an instruction to install packages, start services or reset a database without permission.

The command forms below are supported by current repository instructions/project scripts [E01, E36, E40–E43]. Existing class/file selectors are source-backed [E26–E30]. Selectors explicitly marked **PROPOSED** become valid only after their task creates that test. For the underlying filter syntax, the external primary references are the official .NET selective-unit-test documentation and Angular CLI `test` documentation in the source register.

A PASS requires that the selected tests actually execute, all required assertions succeed, no required scenario is silently skipped, and the report identifies the source/test snapshot. Discovery-only, zero-test, stale binary, cancelled, timeout, incomplete CI, malformed report and unrelated earlier PASS are not acceptance. A passing working-tree check describes the base commit plus the exact recorded changes, not the commit alone.

## D2. Bounded verification catalogue

### V0 — Diff and relevant documentation only

**NOT RUN — SPEC_ONLY.** Use for the actual edited files; documentation-only work needs no application tests.

```bash
git diff --check

# Select only validators relevant to changed canonical documentation.
python3 scripts/docs/validate-markdown-documentation.py
python3 scripts/docs/validate-markdown-filenames.py
python3 scripts/docs/validate-narrative-metrics.py
```

PASS: no whitespace errors, broken required references, prohibited filenames or unsupported volatile-metric claims in the changed documentation. Generated boards/inventories are updated only through their existing tool when that specific output actually changes. Do not rerun every repository validator after a procedure wording comment.

### V1 — Affected programme, evidence or coverage cohort

**NOT RUN — SPEC_ONLY.** The existing database-backed test class is a verified selector:

```bash
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore --configuration Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~AuditProgramWorkflowTests'
```

For an isolated area-summary change, select `FullyQualifiedName~AuditAreaSummaryTests` instead. Narrow further to the actual changed method once its name is known. The class is not an invitation to rerun all Domain tests.

The following selector is **PROPOSED** for Task 01's single independent source-manifest test and must exist before it is used:

```bash
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore --configuration Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~AuditProgramCatalogSourceTests'
```

For Task 10, the **PROPOSED** method selector is `FullyQualifiedName~TerminalProcedureAttestation_DoesNotCreateASigningDependencyCycle`, in the existing programme test class. Do not credit that sequence before D1 approval and genuine upstream domain operations.

PASS: only the selected contract/invariant is established. Report permitted, denied, stale, replay and rollback observations actually exercised; do not describe one passing method as all 165 professional activities having been completed.

### V2 — Affected Angular spec and compilation

**NOT RUN — SPEC_ONLY.** The existing fieldwork spec is a verified path:

```bash
npm --prefix src/AuditSphereOps.Ui run test:ci -- \
  --include 'src/app/features/audit/fieldwork.spec.ts'
```

Task 07's **PROPOSED** component spec, after creation:

```bash
npm --prefix src/AuditSphereOps.Ui run test:ci -- \
  --include 'src/app/features/audit/procedure-workspace.spec.ts'
```

The `--include` path is relative to the Angular project root. Include only the affected spec(s), not all Angular specs. A full Angular build is a compilation check, not a full test suite; use it only when changed templates/routes/types make it necessary:

```bash
npm --prefix src/AuditSphereOps.Ui run build
```

PASS: real source responses decode; exact owner/draft/assent/recovery behavior holds; malformed or stale replies never become a false success; current template compilation succeeds when checked. A visual observation must state its actual role, target and steps; it does not substitute for stored-result or receipt assertions.

### V3 — Changed HTTP/OpenAPI contract only

**NOT RUN — SPEC_ONLY.** Reuse the current canonical emitter and drift gate. Read `scripts/contracts/verify-openapi.sh` at the implementation checkout before running it; its Release API/emitter inputs must reflect the edited source. Build only missing/stale required outputs, not the solution by habit:

```bash
dotnet build src/AuditSphereOps.Api \
  --no-restore --configuration Release --maxcpucount:1

dotnet build tools/AuditSphereOps.OpenApiEmitter \
  --no-restore --configuration Release --maxcpucount:1

bash scripts/contracts/verify-openapi.sh
```

These directory targets refer to the existing projects. Do not repeat a build already satisfied by the same current test-project build. If generation is required, follow the existing emitter command and commit only the updated `contracts/auditsphere-openapi.json`; the verification script is a drift check, not permission to invent a second contract.

PASS: declared success/error shapes match the real endpoint envelopes, money/revision strings and nullability; changed procedure responses decode in the affected Angular spec; no stale canonical contract remains. A generic HTTP 200 without validated original receipt is not a passed command.

### V4 — Additive model and migration integrity

**NOT RUN — SPEC_ONLY.** Only after a model/persistence change, with current Release API/Infrastructure outputs and the pinned EF tool available:

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/AuditSphereOps.Infrastructure \
  --startup-project src/AuditSphereOps.Api \
  --no-build --configuration Release
```

Future generation must assign actual timestamped migration/designer paths and update only the existing model snapshot. Each generated migration is reviewed for scope FKs, uniqueness, append-only history, exact-current pointers, legacy treatment and downgrade safety. Do not alter an existing migration, automatically migrate a development/production database, or backfill human approvals.

PASS also requires the focused PostgreSQL fixture to demonstrate the new constraint and a permitted lifecycle—not EF drift alone. Where downgrade would discard populated professional evidence, refuse it; application rollback must retain the additive schema and fail closed rather than delete history.

### V5 — Affected API or one representative browser boundary

**NOT RUN — SPEC_ONLY.** After Task 01 creates the **PROPOSED** shared API test class:

```bash
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore --configuration Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~AuditProgramCommandsApiTests'
```

The existing native journey selector below is source-verified [E29]. Extend its meaningful UI actions rather than seeding a completed result and claiming the editor was exercised:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj \
  --no-restore --configuration Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~AngularAuditFieldworkParityJourneyTests.AdoptedProgramReviewedFieldworkAndFrozenWorkpaperSurviveReload'
```

Use this one browser boundary only when needed to prove actual authoring/review/navigation; it is not a requirement after every backend edit. Prerequisites include the existing compatible Playwright browser, current Angular bundle, owned API host and isolated PostgreSQL fixtures. This specification installs/starts none of them. Full E2E stays in CI/CD. The existing `FieldworkConnectionsJourneyTests` is a reuse owner for evidence/physical-link cases; select the specific changed method after inspecting it, rather than running the entire project.

PASS: the user-visible path actually invokes the intended Application command, the database retains the exact result/review/receipt, no scope or stale callback crosses targets, and only one committed effect occurs after a lost response.

### V6 — Full regression through existing CI/CD only

**NOT RUN — SPEC_ONLY.** No workflow was dispatched or rerun in this review.

Use `.github/workflows/ci.yml` for its existing fast PR checks. Use `.github/workflows/release-verification.yml` for complete unfiltered Domain, API, Playwright/PostgreSQL and Angular regression on its one resolved immutable source. It remains nightly/manual, not an added full-regression check after every local edit or every PR. Never run the equivalent full suites locally.

Keep current `release.yml` exact-source full-verification consumption; this review does not change branch protections, required PR checks, environments or release permissions. Inspect the actual implementation SHA, resolved test SHA, aggregate conclusion and retained evidence. Fast CI PASS, a passed job within a failing aggregate, or a nightly PASS for another SHA cannot establish full regression.

PASS: all required full-verification jobs succeed for the exact candidate and retained evidence is attributable to that source/artifact set. Full synthetic regression is not professional or live-provider acceptance. Do not build or dispatch another pipeline to produce duplicate evidence.

### V7 — Professional and authorized external acceptance

**NOT RUN — SPEC_ONLY.** A qualified methodology owner and independent reviewer accept the source/programme/profile, N/A interpretation, evidence expectations and D1 final sequencing. Human auditors validate realistic work and review in each of the 20 sections, with actual manual/external assistance disclosed. They decide conclusions; the software records and enforces the accepted workflow.

A separate authorized operator establishes required real document permissions, confirmation-channel observations, signed-representation/report identities and production custody/release evidence using the existing gates. Observing a file name, simulated provider, generated signature image, queued request or capability setting is not proof that the professional or external activity occurred.

PASS: named role/owner, environment, exact source/artifact/decision identity, expected and observed results, private evidence reference and independent disposition are retained. Missing D1 stays `BLOCKED_DECISION`; missing professional/live evidence stays `BLOCKED_EXTERNAL`. No production action, tenant consent, external confirmation dispatch or professional sign-off is authorized by this document.

## D3. Acceptance → gap → task → file → evidence

The table records **specification coverage**, not execution. Refer to task Files sections for all exact existing and PROPOSED paths; the abbreviated owner names below are navigation labels, not invented paths.

| Acceptance | Gaps | Tasks | Concrete change/evidence owner | Focused proof and separate gate | Current disposition |
|---|---|---|---|---|---|
| AC01 | G00, G02, G03, G08 | 01–03, 08, 11 | AuditProgramCatalog.cs (read-only); AuditProgramService.cs; PROPOSED independent source JSON / AuditProgramCatalogSourceTests.cs; EngagementAuditProgramQuery.cs | V1 ordered manifest equality plus exact adoption-membership regression; appendix contains all source rows | SPECIFIED; runtime UNVERIFIED |
| AC02 | G01, G02 | 01, 02 | AuditProgramService.cs; AuditProgramLibraryQuery.cs; UiEndpoints.AuditRecords.cs / Fieldwork.cs | AuditProgramWorkflowTests; PROPOSED AuditProgramCommandsApiTests; exact permitted/denied publication/adoption/replay | SPECIFIED; runtime UNVERIFIED; D2 professional gate |
| AC03 | G03, G04, G10 | 03, 04, 10 | PROPOSED AuditProcedureDefinitionRevision.cs / AuditProcedureApplicabilityDecision.cs; AuditProgramService.Definitions.cs / Applicability.cs; completion-policy binding | V1 definition/N/A lifecycle; V4 direct-DB denial; terminal policy V7 | SPECIFIED; runtime UNVERIFIED; Task 10 BLOCKED_DECISION |
| AC04 | G05, G06, G07 | 05–07 | AuditProgramService.cs; PROPOSED AuditProcedureCommandWorkspace.cs / procedure-workspace.ts | V1 exact submitted result/draft consumption; V2 component; V5 real native authoring | SPECIFIED; runtime UNVERIFIED |
| AC05 | G04, G05, G06, G07, G10 | 04–07, 10 | Existing review service/current-evidence owner; PROPOSED applicability/result receipt and evidence contracts | Current independent review, latest result, wrong rank/scope, open-note and return evidence; V1/V5; D1 terminal context | SPECIFIED; runtime UNVERIFIED; D1 conditional |
| AC06 | G05, G07 | 05, 07 | PROPOSED AuditProcedureCommandReceipt.cs / Workspace.cs; procedure-workspace.ts | Original-ID replay, concurrent same request, changed intent, delayed A→B→A callback and lost-response lookup | SPECIFIED; runtime UNVERIFIED |
| AC07 | G06, G09 | 06, 09 | AuditProgramService.Evidence.cs (PROPOSED); AuditFieldworkService.Connections.cs; existing result JSON and source owners | Snapshot versus later link difference, scope/stale source denial, physical/manual classification; V1/V5; D3 real provider | SPECIFIED; runtime UNVERIFIED; live proof BLOCKED_EXTERNAL |
| AC08 | G02, G04, G08, G10 | 02, 04, 08, 10 | EngagementAuditProgramQuery.cs; AuditAreaSummaryQuery.cs; ProcedureEvidence.cs / Completion.cs | Missing manifest member, submitted≠reviewed, stale result/review, N/A proposal and ad-hoc inclusion; V1/V2; D1 final context | SPECIFIED; runtime UNVERIFIED; D1 conditional |
| AC09 | G07, G09 | 07, 09 | PROPOSED AuditProcedureSupportMap.cs; procedure-workspace.ts; existing route/source owners | All 165 can use the common route; exact authorized contextual handoff and manual boundary; one representative V5 plus V7 section review | SPECIFIED; runtime UNVERIFIED |
| AC10 | G10 | 10 | AuditDeliverableService.cs; Completion.cs; PROPOSED AuditProgramCompletionPolicy.cs / Evaluator.cs | PROPOSED terminal-sequence method; true upstream change stales, downstream attestation does not self-invalidate; V7 policy approval | SPECIFIED direction; BLOCKED_DECISION / UNVERIFIED |
| AC11 | G01–G08, G10 | 01–08, 10 | Operations/Contracts.cs; existing safety/authorization owners; additive history constraints; Angular lifetime boundaries | V1/V4 scoped FKs, current grants/freeze/races/rollback; V2/V5 no stale protected UI | SPECIFIED; runtime UNVERIFIED |
| AC12 | G11 | 11 | Existing traceability, status fields, source/action review and workflows | V0 relevant docs; affected V1–V5; V6 current-source full CI; V7 independent evidence | SPECIFIED; current HEAD execution UNVERIFIED; D2/D3 BLOCKED_EXTERNAL |

No `VERIFIED` implementation-acceptance verdict is assigned by this specification. Source facts labelled `VERIFIED_SOURCE` describe inspected code or the supplied document only.

# E. Complete source-to-execution design

## E1. Common execution contract

For **every** source procedure, Task 07 provides one common native path: choose the exact engagement and procedure → inspect original wording and effective definition → record actual work performed and conclusion → select or describe the evidence actually considered → save an owned draft where needed → preview and submit the exact result → independent review or return → retain the original result/review/receipt and currentness status.

This is the target shared software workflow, **not a claim that it already exists end to end**. Tasks 01–06 provide the source, version, applicability, command and evidence controls; Task 08 provides truthful coverage; Task 09 supplies only proven contextual helpers; Task 10 governs approved terminal sequencing; Task 11 provides acceptance. No procedure is marked complete merely because it has a row, an attachment, a calculation, a selected sample or an external request.

The following table preserves the source's 20-section organization. The helper column names existing capability families identified in source/architecture, not proof that every specialized screen or calculation is complete. The common manual/evidence path covers a procedure when no approved dedicated helper exists. Expanding a specialist method requires its own approved requirement, not speculative implementation within this task.

## E2. Section design and acceptance boundaries

| Section | Count | Source activities to support | Existing assistance to reuse where applicable | Required boundary |
|---|---:|---|---|---|
| 01 — Planning & Risk Assessment | 9 | Planning sources, prior/current TB and financial statements; business understanding; documented risk/materiality/strategy. | Existing audit-plan, TB-intake and GL/statement workspaces; existing risk/materiality commands. Upload or reference original company/prior-year documents through authorized evidence routes. | Document the understanding, selected risk/materiality basis and strategy. Do not infer risk conclusions or prior-year agreement from a successful upload. |
| 02 — Cash & Bank | 8 | Year-end reconciliations/statements/confirmations, ledger agreement, outstanding items, selected transactions and subsequent statements. | Existing bank-reconciliation/schedule and sampling/evidence mechanisms; confirmations workspace. No automatic bank-document procurement. | Record exact balances/source references, tested reconciling items and exceptions. Direct confirmation means actual authorized confirmation evidence, not a generated request. |
| 03 — Trade Receivables | 9 | Ageing and GL/TB reconciliation, balances/confirmations, alternatives, invoices, receipts, ECL and cutoff. | Existing receivable schedules, confirmations, sampling/item tests, subsequent matches and supported ECL accounting evidence. | Keep the ECL method/assumptions and audited conclusion human-reviewed; manual recalculation is valid as explicitly identified work, not an invented universal model. |
| 04 — Inventory | 8 | Inventory listing/count evidence, test counts, costs, obsolescence, NRV and cutoff. | Existing inventory/schedule/item-test and supported inventory valuation evidence; physical-reference records. | Record attendance/count observations and source differences. The software cannot claim physical attendance, perform stock operations, or decide NRV from absent inputs. |
| 05 — Revenue / Sales | 8 | Sales population/GL reconciliation, analytics, risk/value samples, invoices/delivery, price/quantity, receipts, credit notes and cutoff. | Existing revenue schedules, sampling/item testing, analytical review, subsequent matches and source evidence. | Retain the actual selected items and recalculation/work performed; a selected sample is not a tested sample. |
| 06 — Purchases & Trade Payables | 8 | Payable ageing, supplier balances/confirmations/statements, purchases, subsequent payments, unrecorded liabilities and cutoff. | Existing payable schedules, confirmations, item/subsequent testing and documentary evidence. | Record the population/search period and actual supporting records. No purchase-order processing or payment initiation is introduced. |
| 07 — Fixed Assets | 9 | Asset register/openings, additions/capitalization/physical checks, disposals, depreciation, useful lives and impairment. | Existing specialist accounting evidence and workpaper/schedule mechanisms; digital and physical sources. | Record selected asset calculations with supplied method/life/dates and approvals. An asset-by-asset depreciation or impairment engine is not inferred from the checklist. |
| 08 — Expenses | 9 | Expense populations/reconciliation, analytics, risk samples, documents, approval/payment, classification, capital items and cutoff. | Existing GL/schedule, analytical review and item/evidence routes. | Document classification and cut-off conclusions; never change client books merely because an expense was audited. |
| 09 — Payroll | 8 | Payroll summaries/reconciliation, selected employees, contracts, recomputation, bank evidence, joiners/leavers and trends. | Existing supported specialist payroll reconciliation plus restricted manual/digital/physical workpapers. | Handle employee evidence under existing access rules. The programme audits payroll; it does not execute payroll, determine legal benefits or initiate salary payments. |
| 10 — Loans & Borrowings | 9 | Borrowing schedules/openings, confirmations/agreements, receipts/repayments, interest, classification, terms/covenants and subsequent payments. | Existing confirmation, specialist loan/schedule and workpaper/evidence mechanisms. | Record supplied contract terms and calculation basis; no universal effective-interest/covenant/legal engine is assumed. |
| 11 — Equity / Share Capital | 7 | Equity movements/openings, company records, shareholding changes, dividends, retained earnings and closing statements. | Existing statement/equity accounting evidence and source-backed workpapers. | Reconcile the actual movement schedule and document human classification; no equity issuance, dividend execution or group consolidation mutation. |
| 12 — Related Parties | 7 | Management related-party listing, directors/shareholders/KMP, transactions/balances, confirmations, recording and disclosures. | Existing risk/confirmation/statement evidence and common workpaper path. | Record the actual listing, relationships, selected transactions and disclosure conclusion. Do not infer exhaustive related-party identification from a CRM contact list. |
| 13 — Tax & Statutory Liabilities | 7 | Tax returns/computations/balances/payments/liabilities/penalties/correspondence/provisions/disclosures. | Existing supported tax specialist evidence and statement/source workpapers. | Use supplied jurisdiction/method inputs with professional review. No tax filing, automatic tax-law selection or legal-sufficiency claim. |
| 14 — Journal Entries & Fraud | 8 | Journal population, unusual/manual/high-value and year-end entries, risk samples, support/authorization, reversals and fraud indicators. | Existing GL, journal-risk accounting evidence, sampling/item tests and audit risk/finding owners. | Record actual selections and indicators; the software does not conclude that fraud is present or absent and does not add an unrequested Benford engine. |
| 15 — Analytical Review | 8 | Year-on-year/monthly analysis, margins, movements, ratios/days, unusual fluctuations, explanations and support. | Existing analytical-review investigation and supported statement/GL evidence. | Missing comparatives or inputs remain unavailable, not zero. Record chosen ratios/bases and corroboration rather than auto-accepting management explanations. |
| 16 — Going Concern | 9 | Management assessment, position/working capital, forecasts, cashflows, financing, adverse indicators, assumptions, trading and auditor conclusion. | Existing going-concern assessment/forecast evidence and common procedure records. | Keep assumptions, scenarios and final conclusion human-owned; a checked list or forecast upload is not going-concern approval. |
| 17 — Subsequent Events | 8 | Post-year-end transactions/minutes, loans/investments/assets, litigation, discussions, adjustment/disclosure and statements. | Existing subsequent-event/area-assessment and evidence/workpaper mechanisms; existing adjustment/disclosure owners. | Record the review period and exact event evidence; route adjustments through approved separate commands. No automated legal-event determination. |
| 18 — Financial Statements & Disclosures | 9 | Final FS-to-audited-TB agreement, face statements, cash/equity, policies/estimates, comparatives/notes and mathematical consistency. | Existing statement/presentation/package review and disclosure evidence, with exact source/package identities. | Record the actual reviewed package/version and remaining defects. Rendered output is not an audit opinion or complete professionally accepted financial statements. |
| 19 — Audit Differences & Adjustments | 7 | Differences, management responses, financial impact, adjusted/unadjusted schedule, materiality and reflected adjustments. | Existing audit differences/aggregate assessment and approved adjustment/source-reflection workflows. | Keep proposed/agreed/rejected/reported-posted/verified-reflected distinctions; audit completion must not automatically post or mark an adjustment reflected. |
| 20 — Final Completion & Audit Report | 10 | Section/review/risk completion, final materiality, GC/subsequent/disclosures, signed representation, final analytics, reviews and signed output. | Existing completion/SRM/Partner/opinion/representation/signing/financial-package owners, connected under the D1-approved terminal policy. | Do not complete a terminal row before its real output exists, include it recursively in its own prerequisite, waive it automatically, or substitute a task flag for actual approval. |

### What the attachment does not decide

The Word programme does not specify software field schemas, calculation algorithms, monetary thresholds/rates, recognition policies, allowed confirmation channels, professional qualifications, per-procedure automation, or the database/HTTP design. The common fields, exact receipts, source bindings and UI behavior in these tasks are explicit engineering recommendations implementing its work/document/review purpose under current repository rules.

Retain the source's conditions—such as “where applicable,” “selected,” “significant,” “where appropriate” and “where available.” Do not reinterpret an absence of data as a concluded N/A. A human approved N/A decision is separate from a helper being unavailable. Automatic conclusion text, bulk approval and “complete all” controls are not authorized.

## E3. All 165 source procedures

**Provenance:** extracted in order from the supplied DOCX, whose SHA-256 is in the header. The `AWP-SS-PP` identifiers use the existing catalogue convention. Each row below requires the common execution contract above; the evidence row does not certify that the audit was performed. Per-section assistance refers to E2 and is a recommendation, not a new professional requirement.

**Task key:** all rows use the common controls/workspace/coverage in Tasks 01–08, contextual assistance in Task 09, and acceptance in Task 11. The final-stage policy is additionally Task 10. This is a traceability map, not 165 new development stories or 165 software tests.

### Source section 01 — Planning & Risk Assessment (9 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-01-01` | Obtain company registration documents and basic company information. | Common procedure workflow; E2 section 01 assistance; Task 11. |
| `AWP-01-02` | Obtain prior-year audited financial statements and audit report. | Common procedure workflow; E2 section 01 assistance; Task 11. |
| `AWP-01-03` | Obtain current-year Trial Balance and draft financial statements. | Common procedure workflow; E2 section 01 assistance; Task 11. |
| `AWP-01-04` | Agree opening balances with prior-year audited balances. | Common procedure workflow; E2 section 01 assistance; Task 11. |
| `AWP-01-05` | Understand the nature of business, major revenue streams and accounting system. | Common procedure workflow; E2 section 01 assistance; Task 11. |
| `AWP-01-06` | Identify significant account balances and transaction classes. | Common procedure workflow; E2 section 01 assistance; Task 11. |
| `AWP-01-07` | Identify significant audit risks, fraud risks and areas involving management judgement. | Common procedure workflow; E2 section 01 assistance; Task 11. |
| `AWP-01-08` | Determine materiality and performance materiality. | Common procedure workflow; E2 section 01 assistance; Task 11. |
| `AWP-01-09` | Prepare the audit strategy and audit program. | Common procedure workflow; E2 section 01 assistance; Task 11. |

### Source section 02 — Cash & Bank (8 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-02-01` | Obtain bank reconciliation for all bank accounts at year-end. | Common procedure workflow; E2 section 02 assistance; Task 11. |
| `AWP-02-02` | Agree bank ledger balances with the Trial Balance. | Common procedure workflow; E2 section 02 assistance; Task 11. |
| `AWP-02-03` | Agree bank reconciliation balances with year-end bank statements. | Common procedure workflow; E2 section 02 assistance; Task 11. |
| `AWP-02-04` | Obtain direct bank confirmations and reconcile confirmed balances. | Common procedure workflow; E2 section 02 assistance; Task 11. |
| `AWP-02-05` | Check outstanding cheques, deposits and other reconciling items. | Common procedure workflow; E2 section 02 assistance; Task 11. |
| `AWP-02-06` | Investigate old or unusual outstanding items. | Common procedure workflow; E2 section 02 assistance; Task 11. |
| `AWP-02-07` | Test selected bank transactions to supporting documents. | Common procedure workflow; E2 section 02 assistance; Task 11. |
| `AWP-02-08` | Review subsequent bank statements for unusual transactions. | Common procedure workflow; E2 section 02 assistance; Task 11. |

### Source section 03 — Trade Receivables (9 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-03-01` | Obtain the year-end receivable ageing report. | Common procedure workflow; E2 section 03 assistance; Task 11. |
| `AWP-03-02` | Agree total receivables and ageing to the GL/TB. | Common procedure workflow; E2 section 03 assistance; Task 11. |
| `AWP-03-03` | Review significant and overdue customer balances. | Common procedure workflow; E2 section 03 assistance; Task 11. |
| `AWP-03-04` | Send customer balance confirmations and investigate differences. | Common procedure workflow; E2 section 03 assistance; Task 11. |
| `AWP-03-05` | Perform alternative procedures for non-confirmed balances. | Common procedure workflow; E2 section 03 assistance; Task 11. |
| `AWP-03-06` | Test selected sales invoices to delivery documents and ledger. | Common procedure workflow; E2 section 03 assistance; Task 11. |
| `AWP-03-07` | Check subsequent customer collections through bank statements. | Common procedure workflow; E2 section 03 assistance; Task 11. |
| `AWP-03-08` | Perform/recalculate ECL and assess adequacy of provision. | Common procedure workflow; E2 section 03 assistance; Task 11. |
| `AWP-03-09` | Test sales and receivable cut-off around year-end. | Common procedure workflow; E2 section 03 assistance; Task 11. |

### Source section 04 — Inventory (8 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-04-01` | Obtain the year-end inventory listing and agree it to the GL. | Common procedure workflow; E2 section 04 assistance; Task 11. |
| `AWP-04-02` | Attend/observe physical inventory count where applicable. | Common procedure workflow; E2 section 04 assistance; Task 11. |
| `AWP-04-03` | Perform auditor test counts and reconcile differences. | Common procedure workflow; E2 section 04 assistance; Task 11. |
| `AWP-04-04` | Check inventory quantities against count sheets/final listing. | Common procedure workflow; E2 section 04 assistance; Task 11. |
| `AWP-04-05` | Test inventory costs to purchase invoices or supporting records. | Common procedure workflow; E2 section 04 assistance; Task 11. |
| `AWP-04-06` | Review slow-moving, damaged and obsolete inventory. | Common procedure workflow; E2 section 04 assistance; Task 11. |
| `AWP-04-07` | Compare cost with NRV where applicable. | Common procedure workflow; E2 section 04 assistance; Task 11. |
| `AWP-04-08` | Test purchases and goods received around year-end for cut-off. | Common procedure workflow; E2 section 04 assistance; Task 11. |

### Source section 05 — Revenue / Sales (8 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-05-01` | Obtain sales listing and reconcile total revenue to GL/TB. | Common procedure workflow; E2 section 05 assistance; Task 11. |
| `AWP-05-02` | Perform analytical review of monthly and annual sales. | Common procedure workflow; E2 section 05 assistance; Task 11. |
| `AWP-05-03` | Select sales samples based on value and risk. | Common procedure workflow; E2 section 05 assistance; Task 11. |
| `AWP-05-04` | Check invoices to customer orders/delivery documents. | Common procedure workflow; E2 section 05 assistance; Task 11. |
| `AWP-05-05` | Verify quantity, price, calculation and accounting entry. | Common procedure workflow; E2 section 05 assistance; Task 11. |
| `AWP-05-06` | Check selected subsequent receipts where relevant. | Common procedure workflow; E2 section 05 assistance; Task 11. |
| `AWP-05-07` | Review significant credit notes after year-end. | Common procedure workflow; E2 section 05 assistance; Task 11. |
| `AWP-05-08` | Perform revenue cut-off testing before and after year-end. | Common procedure workflow; E2 section 05 assistance; Task 11. |

### Source section 06 — Purchases & Trade Payables (8 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-06-01` | Obtain supplier ageing and reconcile it to the GL/TB. | Common procedure workflow; E2 section 06 assistance; Task 11. |
| `AWP-06-02` | Review significant, old and unusual supplier balances. | Common procedure workflow; E2 section 06 assistance; Task 11. |
| `AWP-06-03` | Obtain supplier confirmations and investigate differences. | Common procedure workflow; E2 section 06 assistance; Task 11. |
| `AWP-06-04` | Compare supplier statements with the company's payable ledger. | Common procedure workflow; E2 section 06 assistance; Task 11. |
| `AWP-06-05` | Test selected purchases to invoices, GRNs and purchase orders. | Common procedure workflow; E2 section 06 assistance; Task 11. |
| `AWP-06-06` | Check subsequent payments to identify outstanding liabilities. | Common procedure workflow; E2 section 06 assistance; Task 11. |
| `AWP-06-07` | Perform search for unrecorded liabilities. | Common procedure workflow; E2 section 06 assistance; Task 11. |
| `AWP-06-08` | Test purchase and payable cut-off around year-end. | Common procedure workflow; E2 section 06 assistance; Task 11. |

### Source section 07 — Fixed Assets (9 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-07-01` | Obtain the fixed asset register and reconcile it to the GL. | Common procedure workflow; E2 section 07 assistance; Task 11. |
| `AWP-07-02` | Agree opening balances with prior-year audited balances. | Common procedure workflow; E2 section 07 assistance; Task 11. |
| `AWP-07-03` | Test additions to invoices, payment records and approvals. | Common procedure workflow; E2 section 07 assistance; Task 11. |
| `AWP-07-04` | Determine whether expenditure is correctly capitalized. | Common procedure workflow; E2 section 07 assistance; Task 11. |
| `AWP-07-05` | Physically verify significant additions/assets where appropriate. | Common procedure workflow; E2 section 07 assistance; Task 11. |
| `AWP-07-06` | Test disposals to disposal documents and sale proceeds. | Common procedure workflow; E2 section 07 assistance; Task 11. |
| `AWP-07-07` | Recalculate depreciation for selected assets. | Common procedure workflow; E2 section 07 assistance; Task 11. |
| `AWP-07-08` | Check useful lives and depreciation method. | Common procedure workflow; E2 section 07 assistance; Task 11. |
| `AWP-07-09` | Review assets for impairment, damage or obsolescence. | Common procedure workflow; E2 section 07 assistance; Task 11. |

### Source section 08 — Expenses (9 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-08-01` | Obtain detailed expense listing and reconcile to GL/TB. | Common procedure workflow; E2 section 08 assistance; Task 11. |
| `AWP-08-02` | Perform analytical review against prior year and budget where available. | Common procedure workflow; E2 section 08 assistance; Task 11. |
| `AWP-08-03` | Identify significant and unusual expense movements. | Common procedure workflow; E2 section 08 assistance; Task 11. |
| `AWP-08-04` | Select samples based on value and risk. | Common procedure workflow; E2 section 08 assistance; Task 11. |
| `AWP-08-05` | Check invoices and supporting documentation. | Common procedure workflow; E2 section 08 assistance; Task 11. |
| `AWP-08-06` | Check management approval and payment evidence. | Common procedure workflow; E2 section 08 assistance; Task 11. |
| `AWP-08-07` | Verify correct accounting classification. | Common procedure workflow; E2 section 08 assistance; Task 11. |
| `AWP-08-08` | Check whether any capital expenditure has been incorrectly expensed. | Common procedure workflow; E2 section 08 assistance; Task 11. |
| `AWP-08-09` | Perform expense cut-off testing. | Common procedure workflow; E2 section 08 assistance; Task 11. |

### Source section 09 — Payroll (8 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-09-01` | Obtain annual/monthly payroll summary and reconcile to GL. | Common procedure workflow; E2 section 09 assistance; Task 11. |
| `AWP-09-02` | Select employees for detailed testing. | Common procedure workflow; E2 section 09 assistance; Task 11. |
| `AWP-09-03` | Check employment contracts and salary details. | Common procedure workflow; E2 section 09 assistance; Task 11. |
| `AWP-09-04` | Recalculate gross salary, allowances and deductions. | Common procedure workflow; E2 section 09 assistance; Task 11. |
| `AWP-09-05` | Agree selected salary payments to bank statements. | Common procedure workflow; E2 section 09 assistance; Task 11. |
| `AWP-09-06` | Test new employees and supporting employment documents. | Common procedure workflow; E2 section 09 assistance; Task 11. |
| `AWP-09-07` | Check terminated employees and final payments. | Common procedure workflow; E2 section 09 assistance; Task 11. |
| `AWP-09-08` | Review unusual changes in payroll or employee numbers. | Common procedure workflow; E2 section 09 assistance; Task 11. |

### Source section 10 — Loans & Borrowings (9 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-10-01` | Obtain loan/borrowing schedule and agree it to GL. | Common procedure workflow; E2 section 10 assistance; Task 11. |
| `AWP-10-02` | Agree opening balances to prior-year financial statements. | Common procedure workflow; E2 section 10 assistance; Task 11. |
| `AWP-10-03` | Obtain bank/financier confirmation. | Common procedure workflow; E2 section 10 assistance; Task 11. |
| `AWP-10-04` | Check new loans against agreements and bank receipts. | Common procedure workflow; E2 section 10 assistance; Task 11. |
| `AWP-10-05` | Check repayments against bank statements. | Common procedure workflow; E2 section 10 assistance; Task 11. |
| `AWP-10-06` | Recalculate interest expense and accrued interest. | Common procedure workflow; E2 section 10 assistance; Task 11. |
| `AWP-10-07` | Check current and non-current classification. | Common procedure workflow; E2 section 10 assistance; Task 11. |
| `AWP-10-08` | Review loan terms, security and covenant requirements where applicable. | Common procedure workflow; E2 section 10 assistance; Task 11. |
| `AWP-10-09` | Check subsequent repayments. | Common procedure workflow; E2 section 10 assistance; Task 11. |

### Source section 11 — Equity / Share Capital (7 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-11-01` | Obtain share capital and equity movement schedule. | Common procedure workflow; E2 section 11 assistance; Task 11. |
| `AWP-11-02` | Agree opening balances with prior-year audited FS. | Common procedure workflow; E2 section 11 assistance; Task 11. |
| `AWP-11-03` | Agree share capital to company/CR records. | Common procedure workflow; E2 section 11 assistance; Task 11. |
| `AWP-11-04` | Check additions, transfers or changes in shareholding. | Common procedure workflow; E2 section 11 assistance; Task 11. |
| `AWP-11-05` | Check dividend declarations and payments. | Common procedure workflow; E2 section 11 assistance; Task 11. |
| `AWP-11-06` | Reconcile retained earnings movement with profit/loss. | Common procedure workflow; E2 section 11 assistance; Task 11. |
| `AWP-11-07` | Agree closing equity balances to financial statements. | Common procedure workflow; E2 section 11 assistance; Task 11. |

### Source section 12 — Related Parties (7 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-12-01` | Obtain management's related-party listing. | Common procedure workflow; E2 section 12 assistance; Task 11. |
| `AWP-12-02` | Check directors, shareholders and key management records. | Common procedure workflow; E2 section 12 assistance; Task 11. |
| `AWP-12-03` | Review related-party transactions during the year. | Common procedure workflow; E2 section 12 assistance; Task 11. |
| `AWP-12-04` | Review significant related-party balances. | Common procedure workflow; E2 section 12 assistance; Task 11. |
| `AWP-12-05` | Confirm significant balances where appropriate. | Common procedure workflow; E2 section 12 assistance; Task 11. |
| `AWP-12-06` | Check whether transactions are properly recorded. | Common procedure workflow; E2 section 12 assistance; Task 11. |
| `AWP-12-07` | Verify required related-party disclosures in the financial statements. | Common procedure workflow; E2 section 12 assistance; Task 11. |

### Source section 13 — Tax & Statutory Liabilities (7 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-13-01` | Obtain tax returns and tax computations. | Common procedure workflow; E2 section 13 assistance; Task 11. |
| `AWP-13-02` | Reconcile tax balances with the GL/TB. | Common procedure workflow; E2 section 13 assistance; Task 11. |
| `AWP-13-03` | Check tax payments against bank statements. | Common procedure workflow; E2 section 13 assistance; Task 11. |
| `AWP-13-04` | Review outstanding tax liabilities and penalties. | Common procedure workflow; E2 section 13 assistance; Task 11. |
| `AWP-13-05` | Review correspondence with tax authorities. | Common procedure workflow; E2 section 13 assistance; Task 11. |
| `AWP-13-06` | Check tax provisions and current-year tax expense. | Common procedure workflow; E2 section 13 assistance; Task 11. |
| `AWP-13-07` | Check relevant tax disclosures in the financial statements. | Common procedure workflow; E2 section 13 assistance; Task 11. |

### Source section 14 — Journal Entries & Fraud (8 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-14-01` | Obtain journal entry listing for the audit period. | Common procedure workflow; E2 section 14 assistance; Task 11. |
| `AWP-14-02` | Identify unusual, manual and high-value journal entries. | Common procedure workflow; E2 section 14 assistance; Task 11. |
| `AWP-14-03` | Focus on journals posted close to year-end. | Common procedure workflow; E2 section 14 assistance; Task 11. |
| `AWP-14-04` | Select samples based on risk. | Common procedure workflow; E2 section 14 assistance; Task 11. |
| `AWP-14-05` | Check supporting documents and authorization. | Common procedure workflow; E2 section 14 assistance; Task 11. |
| `AWP-14-06` | Review unusual journals affecting revenue, expenses or reserves. | Common procedure workflow; E2 section 14 assistance; Task 11. |
| `AWP-14-07` | Check reversed or unusual post-year-end journals. | Common procedure workflow; E2 section 14 assistance; Task 11. |
| `AWP-14-08` | Document any fraud indicators or management override concerns. | Common procedure workflow; E2 section 14 assistance; Task 11. |

### Source section 15 — Analytical Review (8 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-15-01` | Compare current-year results with prior year. | Common procedure workflow; E2 section 15 assistance; Task 11. |
| `AWP-15-02` | Analyse monthly revenue and expense trends. | Common procedure workflow; E2 section 15 assistance; Task 11. |
| `AWP-15-03` | Compare gross profit and net profit margins. | Common procedure workflow; E2 section 15 assistance; Task 11. |
| `AWP-15-04` | Analyse significant movements in major accounts. | Common procedure workflow; E2 section 15 assistance; Task 11. |
| `AWP-15-05` | Calculate relevant ratios and key performance indicators. | Common procedure workflow; E2 section 15 assistance; Task 11. |
| `AWP-15-06` | Review receivable, payable and inventory days where applicable. | Common procedure workflow; E2 section 15 assistance; Task 11. |
| `AWP-15-07` | Investigate significant or unexpected fluctuations. | Common procedure workflow; E2 section 15 assistance; Task 11. |
| `AWP-15-08` | Obtain management explanations and supporting evidence. | Common procedure workflow; E2 section 15 assistance; Task 11. |

### Source section 16 — Going Concern (9 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-16-01` | Obtain management's going-concern assessment. | Common procedure workflow; E2 section 16 assistance; Task 11. |
| `AWP-16-02` | Review current financial position and working capital. | Common procedure workflow; E2 section 16 assistance; Task 11. |
| `AWP-16-03` | Review cash flow forecasts. | Common procedure workflow; E2 section 16 assistance; Task 11. |
| `AWP-16-04` | Check expected cash inflows and major payments. | Common procedure workflow; E2 section 16 assistance; Task 11. |
| `AWP-16-05` | Review loan repayments and financing facilities. | Common procedure workflow; E2 section 16 assistance; Task 11. |
| `AWP-16-06` | Review losses, negative cash flows and overdue liabilities. | Common procedure workflow; E2 section 16 assistance; Task 11. |
| `AWP-16-07` | Assess significant assumptions used in forecasts. | Common procedure workflow; E2 section 16 assistance; Task 11. |
| `AWP-16-08` | Consider subsequent trading performance. | Common procedure workflow; E2 section 16 assistance; Task 11. |
| `AWP-16-09` | Document the auditor's conclusion. | Common procedure workflow; E2 section 16 assistance; Task 11. |

### Source section 17 — Subsequent Events (8 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-17-01` | Review post-year-end bank statements and transactions. | Common procedure workflow; E2 section 17 assistance; Task 11. |
| `AWP-17-02` | Review significant sales, purchases and payments after year-end. | Common procedure workflow; E2 section 17 assistance; Task 11. |
| `AWP-17-03` | Review board/management meeting minutes. | Common procedure workflow; E2 section 17 assistance; Task 11. |
| `AWP-17-04` | Check new loans, investments or major asset purchases. | Common procedure workflow; E2 section 17 assistance; Task 11. |
| `AWP-17-05` | Review litigation and significant legal developments. | Common procedure workflow; E2 section 17 assistance; Task 11. |
| `AWP-17-06` | Discuss significant events with management. | Common procedure workflow; E2 section 17 assistance; Task 11. |
| `AWP-17-07` | Determine whether events require adjustment or disclosure. | Common procedure workflow; E2 section 17 assistance; Task 11. |
| `AWP-17-08` | Ensure relevant events are reflected in the financial statements. | Common procedure workflow; E2 section 17 assistance; Task 11. |

### Source section 18 — Financial Statements & Disclosures (9 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-18-01` | Agree final financial statements to the audited Trial Balance. | Common procedure workflow; E2 section 18 assistance; Task 11. |
| `AWP-18-02` | Check statement of financial position and profit/loss. | Common procedure workflow; E2 section 18 assistance; Task 11. |
| `AWP-18-03` | Check cash flow statement and statement of changes in equity. | Common procedure workflow; E2 section 18 assistance; Task 11. |
| `AWP-18-04` | Review accounting policies and significant estimates. | Common procedure workflow; E2 section 18 assistance; Task 11. |
| `AWP-18-05` | Check comparative figures with prior-year audited FS. | Common procedure workflow; E2 section 18 assistance; Task 11. |
| `AWP-18-06` | Check note disclosures and supporting schedules. | Common procedure workflow; E2 section 18 assistance; Task 11. |
| `AWP-18-07` | Check related-party, tax and going-concern disclosures. | Common procedure workflow; E2 section 18 assistance; Task 11. |
| `AWP-18-08` | Check subsequent-event disclosures. | Common procedure workflow; E2 section 18 assistance; Task 11. |
| `AWP-18-09` | Perform final financial statement consistency and mathematical review. | Common procedure workflow; E2 section 18 assistance; Task 11. |

### Source section 19 — Audit Differences & Adjustments (7 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-19-01` | Record all identified audit differences. | Common procedure workflow; E2 section 19 assistance; Task 11. |
| `AWP-19-02` | Obtain management's response to proposed adjustments. | Common procedure workflow; E2 section 19 assistance; Task 11. |
| `AWP-19-03` | Recalculate the financial statement impact of each difference. | Common procedure workflow; E2 section 19 assistance; Task 11. |
| `AWP-19-04` | Update the adjusted and unadjusted misstatement schedule. | Common procedure workflow; E2 section 19 assistance; Task 11. |
| `AWP-19-05` | Compare total unadjusted differences with materiality. | Common procedure workflow; E2 section 19 assistance; Task 11. |
| `AWP-19-06` | Assess whether remaining differences affect the audit conclusion. | Common procedure workflow; E2 section 19 assistance; Task 11. |
| `AWP-19-07` | Ensure all agreed adjustments are posted and reflected in the final TB. | Common procedure workflow; E2 section 19 assistance; Task 11. |

### Source section 20 — Final Completion & Audit Report (10 procedures)

| Procedure ID | Exact source procedure | Execution/acceptance mapping |
|---|---|---|
| `AWP-20-01` | Ensure all audit sections are completed and cross-referenced. | Exact roll-up of required upstream work, not a self-prerequisite; Task 10/11. |
| `AWP-20-02` | Ensure all review points have been cleared. | Common procedure workflow; Task 10 approved final-stage policy; Task 11. |
| `AWP-20-03` | Confirm all significant risks have been addressed. | Common procedure workflow; Task 10 approved final-stage policy; Task 11. |
| `AWP-20-04` | Review audit differences and final materiality assessment. | Common procedure workflow; Task 10 approved final-stage policy; Task 11. |
| `AWP-20-05` | Complete going-concern and subsequent-event procedures. | Common procedure workflow; Task 10 approved final-stage policy; Task 11. |
| `AWP-20-06` | Complete financial statement disclosure checklist. | Common procedure workflow; Task 10 approved final-stage policy; Task 11. |
| `AWP-20-07` | Obtain signed management representation letter. | Exact signed-output/review evidence plus Task 10 (D1 required); Task 11. |
| `AWP-20-08` | Perform final analytical review. | Common procedure workflow; Task 10 approved final-stage policy; Task 11. |
| `AWP-20-09` | Complete senior/manager/partner review. | Exact signed-output/review evidence plus Task 10 (D1 required); Task 11. |
| `AWP-20-10` | Finalize the auditor's report and signed financial statements. | Exact signed-output/review evidence plus Task 10 (D1 required); Task 11. |

**Source inventory total: 20 sections and 165 ordered procedures.** No source wording was replaced with inferred professional instructions in this appendix.

# F. Handoff, rollout and completion boundary

## F1. Delivery workflow

**Future delivery is explicitly Issue → feature branch → dependency-ordered atomic tasks → focused checks → fast PR CI → review → owner-approved merge/release → applicable exact-source full regression and external gates.** The release candidate must have its required full regression evidence before release; a review/merge approval cannot substitute for a release gate.

This specification selects **pull requests**, overriding the repository's current default of direct `master` delivery for this work. No issue, branch, commit, PR, push, merge, workflow run or deployment was created during SPEC_ONLY.

Before implementation, reread the actual checkout's `AGENTS.md`, `CLAUDE.md`, nested instructions and the named current sources. Record its exact HEAD and existing working changes. Compare affected anchors to this specification; skip a task already correctly implemented and verified after this review rather than reimplementing it. Preserve unrelated changes. Do not reset, stash, overwrite or move another contributor's work merely to make the tree convenient.

Use one reviewable feature branch and cohesive commits/PRs as the actual dependency boundaries warrant; do not create 165 branches or a separate service per section. A task touching multiple layers remains one coherent behavior. Avoid unrelated formatting, dependencies or framework changes. Changes to definitions, applicability or result receipts must not expose half-migrated contracts to the native or retained caller.

For AuditSphere PR merges, require the owner's explicit confirmation **`AUDITSPHERE-MERGE-APPROVED`** for the specific merge action. Merely including that token in this specification is not authorization. Other irreversible production, data, permission or history actions need the owner's separately designated authorization/confirmation; never infer it from a green build, a past merge or this request for a specification.

## F2. Safe rollout sequence

Introduce additive provenance/definition/applicability/receipt storage and its guards before enabling the new write contracts. Apply migrations only through the owner-approved deployment process; generation or an isolated test fixture does not mean a migration has been applied to the running system.

Expose one current controlled publication/adoption route, then guarded procedure operations and the native workspace. Keep old routes/callers compatible only where they enter the same current guard contract; do not preserve a bypass as a “legacy” convenience. If compatibility cannot safely preserve missing revision/request semantics, fail closed with clear guidance and coordinate the actual caller change in the same release.

Existing published programmes, results, decisions, signed artifacts and workpapers retain their exact identities and bytes. Legacy fields missing definition, proposal, evidence or policy binding remain explicitly unverified historical evidence. Never manufacture a past independent approval, link old results to today's latest definition, delete an inconvenient adoption, renumber historical result revisions, or mark earlier N/A as proven from its cache string alone.

Bind the D1-approved terminal policy explicitly and prospectively. An in-progress engagement needs a reviewed compatibility/continuation decision before new-policy final readiness is claimed. Until D1 is accepted, ship only separable safe maintenance/UI work whose behavior does not weaken current gates; retain the final-sequence blocker.

## F3. Rollback and recovery

An application rollback is not permission to drop additive history/receipt tables, reverse a data-bearing migration or rewrite signed-source digests. Retain committed new evidence, prevent an older writer from bypassing new invariants, and expose read-only or blocked operation where the older code cannot safely interpret it. The implementation PR must show the exact version-compatibility boundary and approved recovery approach.

Use the existing record/checkpoint/provider recovery architecture. No new external store, database reset, provider retry storm or broad tenant permission is authorized. Unknown command outcomes are reconciled by their original request; unknown provider outcomes stay under their existing durable provider gates.

Do not remove `AuditSphereOps.Web`, MudBlazor, source/action evidence or old migration files. This programme spec cannot establish whole-application parity or an accepted operational rollback path.

## F4. What counts as finished

A task is done only when its applicable acceptance, focused executed evidence, compatible contract/migration, minimal diff and required approvals are available. “SPECIFIED” means the work has been described; it is not “IMPLEMENTED.” “IMPLEMENTED in source” means a path exists; it is not runtime or professional acceptance.

The overall supplied-programme increment is accepted only when all 165 source IDs and all 20 sections have a complete source-preserving software route; each required shared security/history/currentness/recovery invariant has current evidence; D1 has an approved tested non-circular policy; and the relevant professional/live acceptance has been independently recorded. This is not a claim that any real engagement's 165 activities have already been performed.

The specification does **not** certify AuditSphere's complete practice-management lifecycle, financial-statement methodology, current standards compliance, live provider custody or production readiness. Unrelated CRM, billing, group consolidation and hosting backlog stays with its existing owners.

## F5. Suggested implementation handoff record

For each task record: requirement/source IDs; exact reviewed commit plus working changes; affected file/symbols; expected versus observed behavior; current actor/scope and input identities; command and actual result; omitted/blocked checks and reason; migration state; relevant receipt/artifact digest; independent reviewer; and unresolved professional/external gates. Keep sensitive evidence in the approved private store and link to it through a non-sensitive reference.

Do not use percentages or test totals as substitutes for acceptance. New test count is not a delivery goal. Prefer the minimum tests that protect independently material behavior, and full regression only through the existing CI/CD workflow.

# G. Evidence register and review limits

## G1. Source register

All repository links below are pinned to `6ab7b25240acbda3bff2818739a60751bc0d4289`. Ranges are shown only where the inspected range is known; otherwise the file and named symbols are the evidence anchor. Source search is discovery, not test execution or proof that no alternative path could exist. The input document and previous broader gap reports do not override current executable behavior.

| Evidence | Exact source | Observation / scope |
|---|---|---|
| E01 | [AGENTS.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/AGENTS.md) | Current agent rules: boundaries, lean local testing, and repository-default Git delivery. |
| E02 | [CLAUDE.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/CLAUDE.md) | Imports AGENTS.md. |
| E03 | [docs/auditsphere-docs-index.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/docs/auditsphere-docs-index.md#L1-L135) | Documentation authority and source-status hierarchy. |
| E04 | [docs/architecture/auditsphere-architecture-current-architecture.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/docs/architecture/auditsphere-architecture-current-architecture.md) | Static capability services, modular monolith, Angular canonical, Web rollback/reference. |
| E05 | [docs/architecture/auditsphere-architecture-code-map.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/docs/architecture/auditsphere-architecture-code-map.md#L171-L245) | Audit, review, completion and workflow owners. |
| E06 | [src/AuditSphereOps.Application/Audit/AuditProgramCatalog.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/AuditProgramCatalog.cs) | Controlled AWP identifiers, source wording, SourceHash and count-based IsComplete. |
| E07 | [src/AuditSphereOps.Application/Audit/AuditProgramService.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/AuditProgramService.cs) | PublishAsync, AdoptAsync, applicability, tailoring, workpapers, SubmitResultAsync and ReviewResultAsync. |
| E08 | [src/AuditSphereOps.Domain/Audit/Audit.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Domain/Audit/Audit.cs#L113-L340) | Existing programme, procedure, workpaper, draft, result and review records. |
| E09 | [src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.Audit.cs#L105-L305) | Scope foreign keys, source uniqueness, result revision uniqueness and field bounds. |
| E10 | [src/AuditSphereOps.Infrastructure/Persistence/Migrations/20260920140141_AuditProgramExecutionWorkflow.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Infrastructure/Persistence/Migrations/20260920140141_AuditProgramExecutionWorkflow.cs#L300-L564) | Original programme/result keys and constraints; historical migration, never edit. |
| E11 | [src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/AuditFieldworkWorkspaceQuery.cs) | Fieldwork and procedure-review projections; combined publication/adoption wrapper. |
| E12 | [src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Api/Ui/UiEndpoints.Fieldwork.cs) | Current fieldwork HTTP routes, inputs, fixed N/A rationale and result/review commands. |
| E13 | [src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.ts) | Native programme list, sampling/evidence/notes, missing procedure-execution action. |
| E14 | [src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.ProcedureEvidence.cs) | Existing batched current-result/current-review predicate; preserve and reuse. |
| E15 | [src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Completion.cs) | Current completion already reuses the predicate; all-procedure and adoption-presence boundaries. |
| E16 | [src/AuditSphereOps.Application/Audit/EngagementAuditProgramQuery.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/EngagementAuditProgramQuery.cs) | Paged programme reads; submitted/in-review incorrectly included in Reviewed counter. |
| E17 | [src/AuditSphereOps.Application/Audit/AuditAreaSummaryQuery.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/AuditAreaSummaryQuery.cs) | Reusable area summary; raw status/history counters are not current evidence. |
| E18 | [src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Connections.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/Fieldwork/AuditFieldworkService.Connections.cs) | Existing sampling, PBC/physical links, ad-hoc histories and standard-form blockers. |
| E19 | [src/AuditSphereOps.Application/Audit/WorkpaperCommandWorkspace.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/WorkpaperCommandWorkspace.cs#L1-L135) | Existing owned-draft, exact-preview and original-receipt recovery pattern. |
| E20 | [src/AuditSphereOps.Application/Audit/AuditPlanningCommandWorkspace.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/AuditPlanningCommandWorkspace.cs#L1-L125) | Existing firm/client/actor lock order, scoped authorization and request barrier pattern. |
| E21 | [src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs) | FactsAsync/FactsDigest, SRM, PartnerClearAsync, opinion and signed-report currentness. |
| E22 | [src/AuditSphereOps.Application/Audit/AuditProgramLibraryQuery.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Audit/AuditProgramLibraryQuery.cs) | Firm-wide current authorization on reads; version/section/procedure DTOs. |
| E23 | [src/AuditSphereOps.Api/Ui/UiEndpoints.AuditRecords.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Api/Ui/UiEndpoints.AuditRecords.cs#L1-L110) | Existing library, workpaper, review-point and release routes. |
| E24 | [src/AuditSphereOps.Ui/src/app/app.routes.ts](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Ui/src/app/app.routes.ts) | Canonical /app routes and existing unsaved-change guard. |
| E25 | [src/AuditSphereOps.Ui/src/app/features/accounting/evidence-actions.ts](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Ui/src/app/features/accounting/evidence-actions.ts) | Already implemented accounting-evidence linking/review; not procedure-result submission. |
| E26 | [tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/tests/AuditSphereOps.Domain.Tests/AuditProgramWorkflowTests.cs) | Existing publication/adoption/result/review and library/coverage tests; source only, not executed. |
| E27 | [tests/AuditSphereOps.Domain.Tests/AuditAreaSummaryTests.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/tests/AuditSphereOps.Domain.Tests/AuditAreaSummaryTests.cs) | Existing parameterized area-summary tests discovered in current source index. |
| E28 | [src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.spec.ts](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Ui/src/app/features/audit/fieldwork.spec.ts) | Existing Angular fieldwork contracts and component checks. |
| E29 | [tests/AuditSphereOps.E2E.Tests/AngularAuditFieldworkParityJourneyTests.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/tests/AuditSphereOps.E2E.Tests/AngularAuditFieldworkParityJourneyTests.cs#L1-L125) | Existing owned-host programme and aggregate-difference journeys. |
| E30 | [tests/AuditSphereOps.E2E.Tests/FieldworkConnectionsJourneyTests.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/tests/AuditSphereOps.E2E.Tests/FieldworkConnectionsJourneyTests.cs) | Existing evidence, physical-reference and revoked-session journey owner. |
| E31 | [docs/task_breakdown/tasks/17_Audit_Foundation/auditsphere-audit-task-t055-audit-program-library.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/docs/task_breakdown/tasks/17_Audit_Foundation/auditsphere-audit-task-t055-audit-program-library.md) | Existing T055 ownership; depends on professional T003 handoff, not evidence of missing catalogue. |
| E32 | [docs/task_breakdown/tasks/17_Audit_Foundation/auditsphere-audit-task-t056-engagement-audit-programs.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/docs/task_breakdown/tasks/17_Audit_Foundation/auditsphere-audit-task-t056-engagement-audit-programs.md) | T056: preserved source, tailoring history, justified N/A with Partner approval; dependencies T014/T055. |
| E33 | [docs/execution/auditsphere-execution-index-task-board-current.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/docs/execution/auditsphere-execution-index-task-board-current.md#L89-L145) | T056–T075 ownership and current dependency/professional blockers. |
| E34 | [docs/execution/auditsphere-execution-current-slice.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/docs/execution/auditsphere-execution-current-slice.md#L1-L38) | Current handoff; earlier slice evidence does not verify this HEAD. |
| E35 | [docs/execution/status.json](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/docs/execution/status.json) | Only header/active source fields were consulted; no whole-ledger audit or current-head PASS claim. |
| E36 | [docs/testing/auditsphere-testing-strategy-definition-of-done-current.md](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/docs/testing/auditsphere-testing-strategy-definition-of-done-current.md) | Local focused checks, risk-based coverage and full suites only in CI/CD. |
| E37 | [.github/workflows/ci.yml](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/.github/workflows/ci.yml) | Existing fast PR/push checks; not full .NET regression. |
| E38 | [.github/workflows/release-verification.yml](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/.github/workflows/release-verification.yml#L1-L72) | Existing nightly/manual immutable-source full regression. |
| E39 | [.github/workflows/release.yml](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/.github/workflows/release.yml#L60-L130) | Current source contains verify-release-source full-regression precondition; do not rebuild old gap. |
| E40 | [global.json](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/global.json) | .NET SDK 10.0.300, rollForward disabled. |
| E41 | [Directory.Build.props](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/Directory.Build.props) | net10.0, C# 14, nullable and deterministic builds. |
| E42 | [Directory.Packages.props](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/Directory.Packages.props) | EF Core 10.0.12; Npgsql 10.0.0; existing test and document dependencies. |
| E43 | [src/AuditSphereOps.Ui/package.json](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Ui/package.json) | Angular/Material/CDK 22.2.1 and existing build/test:ci scripts. |
| E44 | [src/AuditSphereOps.Application/Operations/Contracts.cs](https://github.com/nirzaf/AuditSphere/blob/6ab7b25240acbda3bff2818739a60751bc0d4289/src/AuditSphereOps.Application/Operations/Contracts.cs) | Existing IAuditSphereDbContext declaration and DbSet contract owner, identified by current-source search; add only required capability sets. |
### Supplied source

`Audit working process - Audit Tool New (1).docx`, SHA-256 `034d3b5023f5b498f0953ef6fea2af8439d72cc107075bd3336e53405091d987`. The appendix is an ordered extraction of this upload. The catalogue's source hash is the same; runtime publication/adoption and independent professional acceptance remain separate checks.

### External command-syntax references

- [Microsoft Learn — Run selected unit tests](https://learn.microsoft.com/en-us/dotnet/core/testing/selective-unit-tests): support for the `dotnet test --filter` forms used in the future command catalogue. This is syntax guidance, not evidence that a repository test was executed.
- [Angular CLI — test](https://angular.dev/cli/test): `--include` spec selection relative to the project root. The repository's `test:ci` script remains the command entrypoint.

No external accounting, legal or auditing standard was used to silently enlarge or replace the supplied Word procedures. Such professional applicability and acceptance remain with the named qualified owner.

## G2. Review limits and artifact checks

Executed in this review: read-only repository/source investigation, original DOCX text extraction and binary hashing, reconciliation of the supplied programme structure, and structural checks of this Markdown output. These are not application builds, product tests, provider trials or migrations.

Not executed: local .NET/Angular/API/PostgreSQL/Playwright tests, restored-tool installs, build/contract generators, services, live tenant/confirmation/signing operations, CI dispatches, deployment, production data changes or professional sign-off. Source-derived failure scenarios require the focused reproduction specified in their task. Current master may move after the pinned inspection; future work must resolve and record its own source.

The source register and tasks identify the inspected owners. No claim is made that every helper, legacy caller or historical migration in the repository received a whole-product audit. Conditional specialist/provider work stays out of this scope unless current task evidence demonstrates a specific necessary change.

The output is one complete UTF-8 Markdown specification. Its verification checks cover ordered task numbers with seven required subsections, the 20-section/165-procedure source appendix, valid dependency ordering, essential safety/verification text and a nonempty saved file. Those checks establish artifact integrity only.
