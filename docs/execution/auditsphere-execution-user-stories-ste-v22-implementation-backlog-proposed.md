# AuditSphere — STE v2.2 implementation backlog

**Status: PROPOSED — supplied baseline backlog; implementation evidence remains in [status.json](status.json).**

## Repository integration notes

This preserves the supplied backlog below; Markdown hard line breaks are normalized to backslashes for the repository whitespace check. Its baseline claims must be checked against the current checkout before implementation. Story and decision IDs are local to this document; similarly numbered cards in other backlogs or ADRs are not automatically the same decision.

- `TASK-01` remains partial. Reviewed task dependency amendments now preserve pinned template edges, reject cycles, bind both task revisions and recover uncertain replies. Evidence and limits are recorded under `targetedVerification.workflowTaskDependencyAmendments` in [status.json](status.json). This does not complete template, automation or professional acceptance.
- Existing ADRs still govern. In particular [ADR-0018](../architecture/adr/auditsphere-architecture-adr-0018-practice-reporting-bases-approved.md) already decides several reporting bases; compare `DEC-09`/`PRACTICE-01` with it before treating those choices as missing. Its unresolved professional/commercial policies remain unresolved.
- `REL-01` remains partial. Bundle assembly now rechecks current certificate policy on replay and requires the certification credential to match the signed report; focused evidence is under `targetedVerification.releaseBundleSigningReplay` in [status.json](status.json). The retained Web journey and built-in replay-refusal check passed. Correspondence lineage, concurrent replay, inclusive row limit and stale-source checks now have focused evidence under `targetedVerification.releaseBundleCorrespondence`. Further adverse cases, canonical Angular and full release acceptance remain open. Lifecycle, professional assignment/methodology, signing policy and live-provider changes must respect their unresolved owner decisions and current authoritative services.
- Source SHA, integration revision and scoped checks are retained in [status.json](status.json). No baseline CI result in the supplied text proves the current candidate or live-provider acceptance.

## Supplied baseline backlog


**Purpose:** Agent-ready work plan for the remaining STE Audit Management Tool v2.2 requirements.\
**Repository reviewed:** [nirzaf/AuditSphere](https://github.com/nirzaf/AuditSphere)\
**Baseline:** [b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8](https://github.com/nirzaf/AuditSphere/commit/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8), branch master. The branch head matched this SHA on 2026-10-13 before backlog drafting. Recheck before any implementation.\
**Requirement source:** Library file libfile_9336996a3f4c819196c74e826e644a34, STE Audit Management Tool Functional Requirements & End-to-End System Workflow Specification v2.2, 677 lines.\
**Review source:** Library file libfile_b00418aa3bcc8191aa9b7c03c049b835, completion review and pinned requirement matrix.

## 1. Verdict and how to use this backlog

The repository is **not complete against the full v2.2 specification and is not production accepted**. The baseline has substantial domain behavior, UI, and exact-commit CI passes, but functional gaps and product decisions remain. Live identity, mail, SharePoint isolation/retention, release-checkpoint read-back, and provider records-protection acceptance are not demonstrated.

This backlog separates five kinds of work:

1. **Verified source gaps:** implement the listed behavior and add regression coverage.
2. **Verification gaps:** code exists, but the required test cohort or end-to-end evidence was not run or observed.
3. **Product decisions:** stop at the named decision. Do not silently select one interpretation or prebuild the gated implementation.
4. **Recorded deviations:** retain the approved ADR behavior; do not open a remediation story unless the owner reopens the decision.
5. **External acceptance:** run only after explicit authorization in a synthetic, explicitly approved test tenant. No production mutations, new secrets, permission expansion, or external messages are authorized by this backlog.

The exact-commit CI run [38433824802](https://github.com/nirzaf/AuditSphere/actions/runs/38433824802) succeeded. It ran Angular CI, a filtered .NET Release cohort (97 Domain, 6 API, 11 API-hosted browser journeys), EF drift/publish/OpenAPI and documentation gates. It did **not** run the full .NET solution suite or live integrations. No local AuditSphere tests were run in the prior review or while drafting this backlog. Green CI is not production acceptance.

### Priority convention

- **P0:** required to close a lifecycle/security correctness blocker before calling the workflow complete.
- **P1:** material workflow or data-integrity gap.
- **P2:** product decision, fidelity gap, or incomplete acceptance that does not alone authorize a production claim.
- **Gate:** cannot be implemented or tested until an owner decision or external authorization is recorded.

No effort estimates are assigned.

## 2. Architecture and safety constraints for every story

Follow the repository’s [AGENTS.md](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/AGENTS.md) and capability contracts:

- Keep the five modules, eleven canonical lifecycle names, and seven-phase read-only projection. UI task completion must not write a professional or financial gate.
- Use Domain → Application command/query → Infrastructure migration/provider → API/OpenAPI → Angular where applicable. No business-state writes directly through DbContext in UI/API components.
- Every command, query, inbox, and queue enforces firm/client/engagement scope and current role grants in Application. Show client-safe blocker text only.
- Preserve append-only review, acceptance, signed-document, release, archive, and task-activity evidence. Use optimistic revision fencing for changes.
- Use one guarded transaction for coupled evidence and idempotent operation keys for retries. Serialize only the aggregate whose invariant requires it; do not widen cross-client locks.
- Recheck current evidence at command time. Fail closed for stale mappings, changed terms, missing evidence, changed grants, and conflicting release/archive state.
- For schema changes, use additive migrations, deterministic backfill, expand/contract rollout, and a documented rollback that never deletes signed or historical evidence. Do not reassign historical work silently; preserve old result/reviewer identity.
- Automation may move operational tasks and reminders only. It must not clear acceptance, make professional judgments, record payments, sign reports, release packages, or freeze files except where the existing requirement-authorized scheduled/Partner command explicitly provides that action.
- Keep all production external gates marked BLOCKED_EXTERNAL until a live authorized acceptance record exists. Simulations and local checks cannot change that status.
- Preserve the accepted Microsoft sign-in decision; never email temporary passwords. Certificate-based report signing remains optional until the firm selects a policy; never make it a requirement by assumption.

## 3. Existing behavior to preserve; do not rebuild

The following source paths already exist and must be kept while fixing adjacent gaps:

- Current offer/risk gates for Engagement Letter generation and the fully paid 50% advance activation check: [CommercialDocumentService](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Practice/CommercialDocumentService.cs#L113), [EngagementLifecycleService](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs#L67).
- The seven-phase mapper already accounts for all eleven canonical state codes: [EngagementDisplayPhaseMapper](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Acceptance/EngagementDisplayPhaseMapper.cs#L18). The *actual lifecycle projection* still has gaps; the mapper itself is not a missing feature.
- Reviewer independence, current-result generation checks, mandatory “changes required” comments, and immutable review records: [AuditProgramService](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Audit/AuditProgramService.cs#L560).
- Read-only engagement workflow projection and current blocker display; versioned workflow templates, task plans, queues, annual preparation, and narrow idempotent PBC reminders already exist. Their breadth/acceptance is incomplete; do not replace them with a second workflow engine.
- Reporting already assembles a correspondence JSON audit trail into the bundle, including confirmation rows, PBC communications, and cleared queries, with the digest covering that content: [AuditDeliverableService.Bundle](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs#L31). Do **not** recreate the component; test that its contents and client/Partner review are usable.
- Local archive locking is committed and distinct from external provider immutability: [FileFreezeService](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Records/FileFreezeService.cs#L51). Keep the BLOCKED_EXTERNAL distinction.
- Preserve 50/50 fee milestones, dual-key stale-input checks, current report/release identity, exact manifest/hash lineage, scoped grants, and the approved deviations listed in Section 7.

## 4. Decision and external-gate cards

A decision card is complete only when the named owner records the answer and rationale in the authoritative ADR/decision register. Its dependent implementation story then becomes schedulable. A decision card is not authorization to perform live actions.

### DEC-01 — Accept or reject the stage 3/4 lifecycle correction
**Priority:** Gate · **Spec:** §§5, 6.1, 7.2 · **Owner:** product/engagement-process owner\
**Evidence:** [current verification register, lines 219–221 and 268](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L219). The current projection changes the baseline stage 3/4 gate evaluation; the source says this was a truthfulness correction, but §7 says the canonical lifecycle remains unchanged.\
**Decision:** Is this correction accepted as a clarification of the Section 5 gates, or must the prior behavior be restored? Record precisely which gate predicates may change.\
**Acceptance:** ADR/decision register resolves the conflict; lifecycle changes are blocked until then; tests are written against the accepted transition contract.

### DEC-02 — Choose the 60-day countdown event
**Priority:** Gate · **Spec:** §§4.4.3, 5.12, 6.3\
**Evidence:** [FileFreezeService](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Records/FileFreezeService.cs#L18) starts from signature. Section 5.12 says transition after final bundle delivery; §§4.4.3/6.3 also anchor 60 days to signature.\
**Decision:** Start at signature (current behavior) or at verified bundle delivery; state what happens if delivery fails or the report is re-signed.\
**Acceptance:** The choice and delivery-evidence definition are recorded; no timer semantics change before approval.

### DEC-03 — Set the firm’s report-signature policy
**Priority:** Gate · **Spec:** §§4.4.1, 6.3, 7.5\
**Evidence:** ADR-0016 supports certificate signing, but image-only signing is accepted unless ReportSigning:Required is enabled. The requirement text asks for digital credentials but does not say certificate-based signing is mandatory.\
**Decision:** Is the configured visual specimen/seal acceptable for the firm, or should certificate signing be required for reports? Also record who accepts certificate/trust and verification policy.\
**Acceptance:** Written policy, selected environment configuration, test evidence plan, and responsible signer. No story may hard-code “certificate required” before this decision.

### DEC-04 — Resolve recipient-routing purposes and delivery semantics
**Priority:** Gate · **Spec:** §§4.1.1, 4.1.5, 6.7\
**Evidence:** [CRM routing register](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L29). Roles are free text; existing receipt/notification routes are not uniformly used.\
**Decision:** Define contact role/purpose precedence for proposals/letters/final reports, invoices/receipts, and PBC requests (routed Audit Liaison versus assigned portal user); define missing, duplicate, inactive, or ambiguous recipients. Decide whether invoices are emailed and under which notification kind.\
**Acceptance:** Approved routing table and fail-closed/override policy. No external message is sent by this decision card.

### DEC-05 — Define Key 1 identity when no brief quotation exists
**Priority:** Gate · **Spec:** §§4.1.3, 6.8 AC-4\
**Evidence:** [ProposalOfferSha256](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Practice/PracticeCrmService.Dispatch.cs#L15) hashes the quotation artifact when present; fallback digest omits legal entity and 50/50 terms and does not hash service route directly.\
**Decision:** Require a generated brief quote before dispatch/acceptance, or define the complete canonical offer fields/hash. Decide whether an acceptance needs a separate append-only recorder/evidence record.\
**Acceptance:** The offer identity covers the exact presented entity, route, period, scope, fee, currency, payment terms, timeline, and revision or the owner explicitly approves a narrower identity; the record preserves responder, recorder, channel, timestamp, and evidence semantics.

### DEC-06 — Approve commercial document and payment policies
**Priority:** Gate · **Spec:** §§4.1.2, 4.1.4, 4.1.5\
**Evidence:** [commercial decisions register](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L36).\
**Decisions:** DOCX versus presentation for comprehensive proposals; profile-derived versus proposal-entered CVs; approved ISA 210/Internal Audit/ISRS 4400 clauses and submission deadlines; whether letters need certificate signing (separate from report policy); whether the advance invoice is drafted atomically with the letter or remains a separate finance step; whether every partial payment gets a receipt voucher or only settled milestones; whether request-level PBC clarification satisfies per-upload rejection; whether portal badges may poll at 30 seconds or require push.\
**Acceptance:** Record each choice and approver. Preserve financial posting/authorization boundaries and Microsoft sign-in.

### DEC-07 — Approve the planning/activation timing
**Priority:** Gate · **Spec:** §§3.2, 4.2.1–4.2.3\
**Evidence:** [planning register](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L84). Some draft engagements accept staffing, allocations, or milestones before paid activation; folder provisioning starts on activation, not risk acceptance.\
**Decision:** Must staffing, allocation, and milestone configuration wait for Partner acceptance or paid activation? Does “provision on Partner risk acceptance” mean the risk decision, or activation after the advance?\
**Acceptance:** State which preparatory work is permitted on a Draft engagement and which provider side effect is authorized at each gate.

### DEC-08 — Decide fieldwork content and import scope
**Priority:** Gate · **Spec:** §§4.3.1–4.3.2, 6.3\
**Evidence:** [fieldwork register](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L115). Current CSV intake is canonical-column based; mapping memory reuses prior accounts; the workprogram link is not procedure-filtered by FSLI; MUS lacks sample-size/interval derivation; evidence links are strongest for PBC uploads; going-concern assessment is a single record.\
**Decisions:** Specify supported vendor/export versions; rule-based suggestions for new accounts; assertion tags and per-FSLI checklist granularity; MUS assumptions (confidence, tolerable error, expected misstatement, population); whether auditor-created workpapers need first-class storage; exact ISA 570 item-level checklist.\
**Acceptance:** Approved examples, algorithms/professional boundaries, and inputs/outputs; system never invents an audit sample or professional conclusion.

### DEC-09 — Approve practice analytics definitions
**Priority:** Gate · **Spec:** §4.5.1–4.5.2\
**Evidence:** [practice analytics register](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L189).\
**Decisions:** Staff-level performance view; facility-cost and benefit categories (records only, not payroll execution); time-billed receivables scope; non-billable time classification; mixed-currency/missing-rate behavior; exact firm-book reporting categories.\
**Acceptance:** Definitions, roles allowed to view identifiable staff measures, and explicit out-of-scope boundary for payroll execution/payment initiation.

### DEC-10 — Approve interpretation of scheduled automation
**Priority:** Gate · **Spec:** §§6.6, 7.4\
**Evidence:** [automation register](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L246).\
**Decision:** Confirm that a scheduled file-freeze worker is the required Section 5.12 implementation and clarify whether a standing draft-invoice policy is compatible with the Section 6.6 prohibition on posting invoices. Define which additional task reminders/escalations are desired.\
**Acceptance:** Automation inventory distinguishes permitted preparation/notification from protected decisions, with an owner for each policy.

### DEC-11 — Choose management-letter and representation-letter content
**Priority:** Gate · **Spec:** §4.4.2\
**Evidence:** Management letter and LOR artifacts exist, but the current register leaves narrative structure and populated figures to the firm.\
**Decisions:** Required deficiency → impact → recommendation fields and severity taxonomy; figures/periods allowed in the representation letter and source of each value; reviewer/Partner approval of generated text.\
**Acceptance:** Approved templates and sample redacted artifacts; no generated assertion is presented as client-approved or auditor-concluded.

### EXT-01 — Authorize synthetic-tenant production-gate acceptance
**Priority:** P0 Gate · **Spec:** §§4.1.5, 4.2.3, 4.3.4, 4.4.2–4.4.3, 6.3, 7.6\
**Evidence:** AGENTS.md keeps Entra, mail, SharePoint and approved release checkpoint external; CI sets external effects off/simulation adapters on.\
**Authorization required before execution:** named test tenant/site, synthetic client, approved test accounts, existing least-privilege app configuration, approved recipients that cannot reach clients, rollback/cleanup ownership, and explicit approval of each provider operation. Do not create secrets, expand Graph permissions, send to real recipients, alter retention, or test production data.\
**Acceptance:** Separately record Entra sign-in, scoped client isolation, five-folder provisioning, mail dispatch/receipt/holding letter, exact release checkpoint read-back, and observed read-only provider behavior. A simulation, screenshot, or local Frozen state is not a pass. Remains BLOCKED_EXTERNAL until each authorized result is captured.

## 5. Implementation stories

Each implementation story depends on its listed decision cards where stated. Acceptance criteria are testable outcomes; “not run” must remain the result until evidence is produced.

### LIFE-01 — Complete lead-to-proposal lifecycle projection
**Priority:** P1 · **Depends on:** DEC-01; clarify whether leads belong in the engagement projection or a linked onboarding projection.\
**Spec:** §§3.1, 5 states 1–3, 6.1/6.3/6.8 AC-1.\
**Current evidence:** [EngagementLifecycleQuery](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs#L100) has no emitted LEAD_INGESTION result; fallback reaches proposal. Proposals can exist before an engagement. Period binding is still open in the workflow register.\
**Build:** Define the read-only source projection from lead/client profile through proposal dispatch to dual-key/advance billing. Enforce owner-approved minimum legal-entity and primary-contact data. Bind proposal, accepted offer, service route, entity and statutory period so a later engagement cannot silently use a different period. Do not add a second mutable stage field.\
**Safety/data:** Existing CRM records remain readable; deterministic backfill only where source fields prove the mapping, otherwise show an explicit incomplete-profile blocker. No auto-creation of accepted engagements.\
**Acceptance:** All eleven canonical codes map to exactly one phase; an incomplete lead cannot progress; a valid lead can; declined/closed lead cannot create an active engagement; scope and role checks deny unrelated clients; projection read writes no state; stale offer/period is blocked. Add unit and API/browser projection tests.

### LIFE-02 — Make planning-to-fieldwork gate require current accepted TB mapping
**Priority:** P0 · **Depends on:** DEC-01.\
**Spec:** §§3.2, 5 state 5, 6.3 Planning, 6.8 AC-7.\
**Current evidence:** The query uses current approved materiality to determine planning approval and separately notes TB presence; it does not directly require a current approved TB-to-FSLI mapping at the transition. See [EngagementLifecycleQuery](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs#L194).\
**Build:** Centralize a named Application readiness query over accepted TB dataset, approved mapping revision, current materiality and Partner planning approval. Recompute on entry and every protected command; invalidate stale approval when TB/mapping changes. Do not treat imported-but-unmapped data as ready.\
**Acceptance:** No TB, draft/rejected mapping, stale mapping, changed TB after approval, or absent Partner approval blocks fieldwork and identifies the exact blocker. A current approved TB/mapping plus current Partner approval passes. Cross-client datasets and stale revisions fail closed. Add tests for each predicate and same-request race.

### LIFE-03 — Align Partner Approval and SRM readiness projection
**Priority:** P1 · **Depends on:** DEC-01.\
**Spec:** §5 state 7→8, §6.3 Manager Review/Partner Sign-off, §6.8 AC-7.\
**Current evidence:** [EngagementLifecycleQuery](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs#L138) checks handover blockers only when Partner clearance and an opinion already exist. The projection can therefore report Manager Review while the Partner opinion decision is pending rather than exposing a clear Partner Approval next action at the agreed point.\
**Build:** After DEC-01, define and implement the authoritative Manager Review → Partner Approval readiness from current reviewed procedures, zero relevant open notes, current SRM, required confirmations, and required high-risk reviews. Project the Partner phase before the Partner decision if that is the accepted sequence. Keep signing/release blockers separate from stage-entry blockers.\
**Acceptance:** Each gate contributes one stable blocker; stale SRM/materiality or open note blocks transition; Partner opinion is not required merely to enter Partner Approval unless owner explicitly decides otherwise; once gates clear, phase and next permitted action are correct. Add projection and regression tests.

### AUD-01 — Add procedure ownership, FSLI linkage, and assignment-scoped access
**Priority:** P0 · **Spec:** §§4.3.1, 4.3.3, 5 states 6–7, 6.3/6.7.\
**Current evidence:** [AuditProcedure](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Domain/Audit/Audit.cs#L113) has no assignee or statement-line link. Current preparer visibility is role-level rather than assigned-procedure-level.\
**Build:** Add explicit current preparer assignment and stable statement-line identity (not display text) to procedure/catalogue linkage. Add assignment/reassignment command with role eligibility, expected revision, actor/reason and append-only assignment history. Restrict preparer work queues and submission to assigned procedures; retain reviewer and Partner scopes.\
**Migration:** Add nullable FK/line key and assignment history first; deterministic backfill only if one eligible existing assignment is unambiguous. Otherwise retain unassigned and route to manager queue. Do not infer ownership from the last editor. Expand/contract after backfill evidence; rollback must keep history and old procedure IDs.\
**Acceptance:** Assignment visibility and command checks enforce client/engagement scope; unassigned or reassigned-ineligible procedure blocks preparer submission; manager can reassign with reason; old results/reviews remain intact; concurrency fences stale assignment changes. Test migration on representative legacy rows, plus API/UI and cross-client denial.

### AUD-02 — Make returned results require corrected resubmission and project rework
**Priority:** P0 · **Depends on:** AUD-01, DEC-01.\
**Spec:** §3.3, §5 states 7→6, §6.3 Manager Review, §6.8 AC-5/6.\
**Current evidence:** Review decisions and reasons are append-only, but lifecycle uses CurrentResultRevision > 0 as executed; the query does not exclude a current result whose latest review is CHANGES_REQUIRED. Rework-task paths are gated by WorkflowExperience:Enabled and may route an ineligible prior preparer to manager review.\
**Build:** Define current-submission status from the latest result and latest review decision. Keep review evidence immutable; returned work remains incomplete until a new revision from the assigned preparer satisfies current input generation. Project the affected engagement back to Fieldwork while returned procedures remain; show exact procedures and assigned owners. Task is a convenience, never the gate.\
**Acceptance:** A returned current result cannot count as executed; a valid corrected revision restores Under Review; stale results cannot be reviewed; missing rejection reason and self-review fail; resubmission by another unassigned user fails; unrelated accepted procedures stay accepted; repeated task/review retries do not duplicate evidence. Test with workflow feature on and off.

### AUD-03 — Connect Angular preparer submission to the authoritative result command
**Priority:** P0 · **Depends on:** AUD-01 and AUD-02.\
**Spec:** §§3.3, 4.3.3, 6.3, 6.8 AC-6.\
**Current evidence:** The API creates immutable AuditProcedureResult revisions, but the verification register reports the native Angular preparer action does not call the result-creation route; see [fieldwork UI register](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L131).\
**Build:** Wire the fieldwork form to the existing authorized result command/endpoint. Render server-computed canSubmit, status, current revision, evidence, blocker, and assignee. Never locally mutate lifecycle status. Keep form drafts distinguishable from submitted immutable revisions.\
**Acceptance:** Submit produces exactly one versioned result and reviewer inbox item; retry with same idempotency key does not duplicate; stale revision/current input is rejected; returned procedure is assigned to the authorized preparer; client/non-engagement user cannot submit; keyboard and error-state journeys pass.

### AUD-04 — Remove client-wide write serialization for independent procedures
**Priority:** P1 · **Spec:** §4.3.1 row-level concurrency; §3.3 parallel line-item work.\
**Current evidence:** Submission/review uses a client-level row lock for procedures belonging to one client according to the current register; revision fencing prevents overwrite but independent line items wait on the same lock. No parallel distinct-procedure test was observed.\
**Build:** Reassess lock granularity and use the smallest aggregate lock that preserves engagement freeze and procedure revision invariants. Maintain serialization against freeze/report release where required. Do not weaken optimistic concurrency or tenant scope.\
**Acceptance:** Two authorized writes to distinct procedures/FSLIs can overlap and both commit; same-procedure stale writes serialize/reject; freeze races cannot admit a write after lock; no silent lost updates. Add deterministic concurrency tests against PostgreSQL and report the exact test cohort.

### CRM-01 — Implement recipient-purpose routing after policy approval
**Priority:** P1 · **Depends on:** DEC-04.\
**Spec:** §§4.1.1, 4.1.5; 6.7.\
**Current evidence:** Contact role is free text; receipt routing exists, but proposal/letter/report/PBC routing is not uniform. [CRM model](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Domain/Practice/Crm.cs#L141).\
**Build:** Implement approved role/purpose enums and validity windows; resolve recipient for each notification only after the decision’s precedence is known. Store the chosen contact and purpose in durable notification evidence. Missing/ambiguous contacts block dispatch with a staff-visible remedy; portal user recipient remains separately scoped.\
**Acceptance:** Each communication kind chooses only an active authorized purpose recipient; contact changes after queueing do not silently rewrite queued evidence; duplicate/inactive contact, wrong-client contact, and no recipient are handled by approved policy; tests prove no cross-client leakage.

### CRM-02 — Bind acceptance to complete offer identity and append-only response evidence
**Priority:** P0 · **Depends on:** DEC-05.\
**Spec:** §4.1.3, §6.8 AC-2/4.\
**Current evidence:** The no-quotation offer digest does not bind all printed terms; response fields are stored on a mutable proposal row without a complete append-only acceptance event. See [offer digest](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Practice/PracticeCrmService.Dispatch.cs#L15) and [register](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L39).\
**Build:** Implement DEC-05’s canonical offer representation. Persist append-only response evidence with respondent/contact, recorder, channel, timestamp, artifact/hash, proposal/quotation revision and reason; bind dual-key/document/activation checks to that current record.\
**Migration:** Preserve current proposal fields and historical approvals; backfill only verified exact offer identity. Legacy responses lacking proof are marked legacy/unverified and cannot silently satisfy new protected actions; no deletion.\
**Acceptance:** Changed entity/route/period/scope/fee/currency/terms/timeline invalidates acceptance; replay/current-offer mismatch fails; concurrent dispatch/acceptance is fenced; authorized response succeeds and signed history is immutable.

### COM-01 — Complete engagement letter terms and payment-document sequence
**Priority:** P1 · **Depends on:** DEC-06 and DEC-03 only if letter signing policy is chosen.\
**Spec:** §§4.1.4–4.1.5.\
**Current evidence:** Three route-specific template IDs currently differ mainly by heading; submission deadlines are absent; letter generation ensures fee milestones but does not create/post invoice in same command; partial payment does not produce a receipt. See [letter service](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Practice/CommercialDocumentService.cs#L87) and [commercial register](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md#L55).\
**Build:** After owner-approved clauses/deadlines/sequence, make each route render approved terms and linked exact offer revision. Implement the chosen invoice draft/authorization flow and receipt semantics while keeping invoice posting/payment allocation in Finance. Add only the selected visual or certificate signature policy.\
**Acceptance:** Wrong route, stale offer, missing required deadline or unauthorized signer blocks generation; output includes only approved clauses and exact figures; duplicate command is idempotent; payment records/references and receipt outcome match approved policy; no activation before full allocated 50% advance.

### COM-02 — Complete comprehensive proposal from approved firm/team data
**Priority:** P2 · **Depends on:** DEC-06.\
**Spec:** §4.1.2.\
**Current evidence:** reviewed DOCX generation has five chapters, requires CV text/timeline and avoids inventing credentials; current owner question is DOCX versus slide format and CV source. See [tender generation](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Application/Practice/CommercialDocumentService.Tender.cs#L12).\
**Build:** Implement only the approved output type. Where approved, compile current Partner/team CVs from authorized staff profiles, approved credentials, methodology, profile and fee/timeline; explicitly flag missing data rather than fabricate. Preserve reviewed quotation identity and revisions.\
**Acceptance:** Five required chapters and approved format render; missing/expired profile information blocks or is clearly disclosed; data from another firm/staff profile cannot leak; revised price invalidates the generated offer; verify output pages/content and accessible download.

### PLAN-01 — Enforce approved risk evidence and make planning controls explicit
**Priority:** P1 · **Spec:** §§4.2.1–4.2.3, 6.2–6.3.\
**Current evidence:** risk checklist exists but conflict-of-interest prompt/evidence enforcement is incomplete; full planning-form parity and specific milestones remain partial. Draft staffing/folder trigger requires DEC-07.\
**Build:** Complete required risk-question evidence and explicit conflict check according to approved policy; make the planning workspace show required staff roles, milestones, folder readiness, current TB/mapping, materiality/risk and Partner approval. Keep risk, scheduling and capacity separate from task completion.\
**Acceptance:** missing required evidence/Partner decision blocks protected planning; continuance starts fresh for new year; stale risk generation blocks; capacity and milestone edits follow DEC-07; no portal exposure of AML/KYC. Add negative authorization tests and reviewer/Partner approval tests.

### FIELD-01 — Filter workprograms by stable FSLI identity
**Priority:** P1 · **Depends on:** DEC-08.\
**Spec:** §4.3.1 FSLI actions; §6.3.\
**Current evidence:** statement-page link passes area/line query parameters but procedures are not filtered; procedure lacks an FSLI link.\
**Build:** Add approved stable FSLI keys to controlled catalog entries and procedure instances, then pass the key from statement row to workprogram query. Support clearly separated shared procedures only where catalog rules define them. Do not match by mutable display name.\
**Migration:** nullable additive key; map only exact known taxonomy; unresolved procedures remain visibly unlinked and do not silently inherit a line.\
**Acceptance:** each row opens only its assigned procedures plus explicitly shared work; invalid/unmapped line cannot submit; mapping revision changes invalidate dependent results; an unrelated line cannot be modified through altered query parameters.

### FIELD-02 — Complete approved fieldwork standards and evidence capabilities
**Priority:** P2 · **Depends on:** DEC-08.\
**Spec:** §4.3.2, §6.3.\
**Build:** According to owner decisions, version assertion tags/checklists; implement approved MUS calculation inputs/outputs as a deterministic calculator; link approved auditor-authored evidence files as first-class artifacts if selected; and expand going-concern assessment into approved checklist items. Keep all sampling choices and conclusions under human audit control.\
**Acceptance:** tested boundary cases and reproducible formula/version; no silent defaults; unsupported/insufficient inputs block; evidence hashes and access scope are immutable; checklist answers require evidence and reviewer; no automation chooses sample or opinion.

### PRACTICE-01 — Close approved practice analytics/bookkeeping gaps
**Priority:** P2 · **Depends on:** DEC-09.\
**Spec:** §§4.5.1–4.5.2.\
**Current evidence:** core time, rates, realization, utilization, budget variance and firm-books paths exist; some reports/categories and exact measure definitions are not accepted.\
**Build:** Implement only agreed staff view, category, receivable, nonbillable, and currency semantics. Reuse current aggregates; keep firm ledger separate from client accounting; do not add payroll execution, payment initiation, inventory, or client bookkeeping beyond its authorized mode.\
**Acceptance:** calculations reconcile to source time/fee/ledger records; missing rates or mixed currency show unavailable/blocked, not zero; role controls protect identifiable staff data; reports have documented cutoffs and reproducible totals. Existing approved calculations remain unchanged unless decision reopens them.

### TASK-01 — Prove templates and queues preserve mandatory gates
**Priority:** P1 · **Spec:** §§6.2, 6.4, 6.6–6.8, 7.4.\
**Current evidence:** versioned reviewed templates, pinned task plans, queues, Kanban/calendar, prerequisite evaluators and PBC reminders exist. Workflow UI endpoints are default-off at [UiEndpoints.WorkflowTasks](https://github.com/nirzaf/AuditSphere/blob/b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8/src/AuditSphereOps.Api/Ui/UiEndpoints.WorkflowTasks.cs#L36). A general automation runner and complete permitted-user notification/escalation surface do not exist.\
**Build:** Add server-side template publication validation proving mandatory checklist/gates cannot be removed; close approved editable task-group and dependency amendment gaps; show per-phase milestones and role-computed next permitted action. Add general task automation only if DEC-10 approves, with an explicit non-professional automation identity and allowlisted operational actions.\
**Acceptance:** cycle, stale revision, forbidden gate-removal, unauthorized assignee and cross-client cases fail closed; retries are idempotent; task completion never advances lifecycle; automation cannot access protected commands; default-off remains until separately approved configuration.

### REL-01 — Verify the five-part release artifact end-to-end without duplicating correspondence
**Priority:** P1 · **Spec:** §§4.4.1–4.4.2, 6.3, 6.8 AC-8.\
**Current evidence:** bundle source includes report/statements, management letter, representation letter, final fee note and Audit_trail.json; the latter serializes confirmations, PBC communications and cleared queries and participates in the source digest.\
**Build:** Add focused tests for each required entry, stale/changed source digest, signature policy, exact release identity, missing component, duplicate assembly and correspondence row limit. Inspect the actual extracted ZIP on synthetic data for required contents; correct omissions without adding a second correspondence implementation. Keep client release behind verified release checkpoint and recipient policy.\
**Acceptance:** all five contractual groups are present and match current source revisions; hash manifest verifies; replay returns the same bundle; stale input cannot produce current release; no bundle/email reaches an external client during automated tests.

### ARC-01 — Apply the owner-selected countdown semantics
**Priority:** P1 · **Depends on:** DEC-02.\
**Spec:** §§4.4.3, 5.11–5.12, 6.3.\
**Build:** If DEC-02 retains signature start, preserve current scheduling and add projection tests proving countdown/release evidence is accurately displayed. If delivery start is chosen, atomically create/reschedule only from verified final release evidence; define re-signing and delivery retry behavior. Maintain Partner early-lock digest/revision checks, idempotent worker lock, transaction fencing, and local-vs-provider status separation.\
**Acceptance:** exact selected start date; no countdown from unsigned/unreleased artifact; stale retry cannot change due date; early lock requires Partner and exact current reviewed digest; only committed local freeze projects Archived; provider immutability remains a separate external result.

### VERIFY-01 — Run the omitted requirement-specific test cohorts
**Priority:** P0 · **Owner:** implementation/release engineer\
**Spec:** all §§4–7; evidence boundary from prior review.\
**Current evidence:** exact CI run 38433824802 passed selected tests but not full .NET solution or several audit/completion/isolation cohorts.\
**Build:** After implementation, run repository AGENTS.md verification commands and the capability-specific checks; add focused tests for stories above, migration/backfill, concurrency, API scope and Angular journeys. Report exact command, commit, test filter/count, pass/fail/skip and external-effect configuration.\
**Acceptance:** full supported suite result is recorded for exact candidate SHA; focused filters and omitted suites are explicit; no “all tests pass” unless the full designated suite ran; production acceptance still waits for EXT-01.

### RELEASE-01 — Record release acceptance and close only supported requirements
**Priority:** P0 · **Depends on:** all P0/P1 stories, DEC cards, VERIFY-01 and EXT-01.\
**Spec:** §§5–7.\
**Build:** Produce release record showing source SHA, requirement disposition, CI evidence, authorized external evidence, unresolved owner decisions/deviations, rollback readiness, and business sign-off. Keep local application archive and external provider controls as separate assertions.\
**Acceptance:** every spec row is Implemented+verified, accepted deviation, explicit owner-approved deferral, or BLOCKED_EXTERNAL with owner; no unresolved P0 issue; Partner/Reviewer/client synthetic journey completed under authorized conditions; product owner approves the actual release. Do not call the product production-ready based only on CI.

## 6. Dependency order and execution waves

1. **Wave A — decide contracts:** DEC-01 through DEC-11. Decisions can proceed in parallel. Keep DEC-02 and DEC-03 independent: timer semantics do not imply certificate signing.
2. **Wave B — correctness foundations:** LIFE-02, AUD-01, CRM-02, PLAN-01. Schema changes are additive and migration-tested.
3. **Wave C — usable workflow:** LIFE-01/LIFE-03; AUD-02/AUD-03/AUD-04; FIELD-01/FIELD-02; CRM-01; COM-01/COM-02. Respect each decision dependency.
4. **Wave D — supporting capabilities:** TASK-01, PRACTICE-01, REL-01, ARC-01.
5. **Wave E — verification and acceptance:** VERIFY-01, EXT-01 only after explicit test-tenant authorization, then RELEASE-01. External test scheduling can proceed only after the synthetic tenant and least-privilege configuration are approved; do not substitute simulation for it.

If two stories edit the same aggregate/schema, merge changes through one compatible migration plan and one owner-approved contract; do not create competing parallel representations of lifecycle, assignment, offer identity, automation or release.

## 7. Pending requirement-to-story traceability matrix

**Status vocabulary:** Implemented = source path present; Partial = material gap remains; Missing = no required source path identified; Deviation = deliberate ADR/product choice; External = live provider/tenant acceptance; Decision = code cannot settle the ambiguity. Status is for the pinned baseline, not a claim that a story has been completed.

| Requirement | Baseline disposition | Backlog |
|---|---|---|
| §1.1 unlimited clients/files, unified ISA workflow | Core product paths exist; scale/cost and production acceptance not established | VERIFY-01, RELEASE-01 |
| §1.2 role boundaries and client-safe portal | Source authorization exists; assigned-work visibility and live identity remain incomplete | AUD-01, EXT-01 |
| §2 five modules and module boundaries | Present; retain financial/client/firm-ledger separations | Existing controls; PRACTICE-01 |
| §3.1 lead capture → proposal → dual key → letter | Partial: lead stage absent, offer period/fallback identity incomplete | LIFE-01, DEC-05, CRM-02, COM-01 |
| §3.2 accepted advance → provision → plan → TB → risk/materiality | Core exists; timing and current mapping gate gaps | DEC-07, LIFE-02, PLAN-01, EXT-01 |
| §3.3 submit/review/rework/SRM | Partial: UI submit, assignment, current rejected-result gate, lifecycle loop and concurrency | AUD-01–04, LIFE-03 |
| §3.4 report, confirmations, five-part delivery, lock | Core bundle and local freeze exist; provider release and timer wording need decision | REL-01, DEC-02, ARC-01, EXT-01 |
| §3.5 practice analytics and firm ledger | Partial; owner definitions remain | DEC-09, PRACTICE-01 |
| §4.1.1 channel labels | Present; WhatsApp ingestion/dispatch is accepted deviation | Preserve ADR-0017; no story |
| §4.1.1 org trees | Partial: PARENT/SUBSIDIARY/AFFILIATE; rendered tree depth unverified | VERIFY-01 |
| §4.1.1 role contact directory/routing | Partial; routing decisions and integration incomplete | DEC-04, CRM-01 |
| §4.1.2 brief quote content and timeline | Present; 1–2-page length not enforced | VERIFY-01; add a scope test if owner requires |
| §4.1.2 comprehensive proposal chapters/format/team CVs | Partial; DOCX and per-proposal CV text; output format is undecided | DEC-06, COM-02 |
| §4.1.3 exact dispatched offer and response evidence | Partial fallback digest and mutable response evidence | DEC-05, CRM-02 |
| §4.1.3 dual key, stale risk, hold blocking, readiness cards | Core implemented; external delivery remains | Preserve current gates; EXT-01; VERIFY-01 |
| §4.1.4 route templates/deadlines/signature | Partial approved wording/deadlines; signing policy undecided | DEC-03, DEC-06, COM-01 |
| §4.1.5 invoice with letter, partial receipts | Partial; sequence and receipt policy undecided | DEC-06, COM-01 |
| §4.1.5 email receipt/proposal/holding letter | Queue exists; actual delivery external | EXT-01 |
| §4.1.5 client workspace and isolated identity | Code path present; live SharePoint and Entra acceptance external; Microsoft sign-in is accepted deviation from temporary-password text | Preserve ADR-0003/0017; EXT-01 |
| §4.1.5 item status/rejection reason and temporal upload lock | Partial: polling, request-level clarification, lock surfaces need full acceptance | DEC-06, VERIFY-01, EXT-01 |
| §4.2.1 new/continuance risk evidence | Partial: conflict checklist/evidence edge | PLAN-01, VERIFY-01 |
| §4.2.2 capacity, staff roles, milestones | Partial; exact pre-activation allowance decision | DEC-07, PLAN-01 |
| §4.2.3 five folders | Code path exists; trigger/template and live creation open | DEC-07, EXT-01 |
| §4.2.4 PBT normalization | Accepted ADR deviation; mapped balances excluding tax | Preserve ADR-0011/0017; no remediation story |
| §4.2.4 PM/TE/SAD rounding and risk bands | Core calculator; verify criteria against owner’s approved bands | VERIFY-01 |
| §4.2.4 automated Green / mandatory Amber sample | Amber sampling remains professional judgment by ADR-0017; no automation may select sample | Preserve ADR-0017; DEC-08 only for MUS input policy |
| §4.3.1 vendor TB imports/history/new accounts | Partial, vendor profile and new-account suggestion decisions open | DEC-08, FIELD-01 |
| §4.3.1 direct approved mapping at fieldwork transition | Partial | LIFE-02 |
| §4.3.1 per-FSLI workprogram filter and procedure line identity | Partial | AUD-01, FIELD-01 |
| §4.3.1 independent line-item concurrency | Partial due client-level lock; no parallel test | AUD-04 |
| §4.3.2 preloaded assertion-tagged procedures | Partial; assertion taxonomy decision | DEC-08, FIELD-02 |
| §4.3.2 ad-hoc procedure rows | Existing capability; verify current revision and authorization | VERIFY-01 |
| §4.3.2 MUS / systematic / stratified sampling | Calculators exist; MUS sizing inputs and professional assumptions open | DEC-08, FIELD-02 |
| §4.3.2 digital/physical evidence | Partial; first-class PBC evidence versus auditor files open | DEC-08, FIELD-02 |
| §4.3.2 analytical review and Going Concern | Core calculation and gate exist; checklist granularity undecided | DEC-08, FIELD-02 |
| §4.3.3 preparer assignee and authorized submission | Missing procedure assignee; native UI command unconnected per current register | AUD-01, AUD-03 |
| §4.3.3 independent review, mandatory comment, append-only history | Implemented in source; full regression suite incomplete | Preserve; AUD-02, VERIFY-01 |
| §4.3.3 rework reassigns preparer and resets lifecycle | Partial; current rejected result can count; task flag and manager fallback | AUD-02 |
| §4.3.3 SRM current compilation and Partner gates | Partial stage predicate and test coverage | LIFE-03, VERIFY-01 |
| §4.3.4 critical confirmations block report | Core signing blocker; real dispatch and full suite unverified | Preserve; VERIFY-01, EXT-01 |
| §4.4.1 opinion/modification reasons and credentials | Partner path present; legal acceptance and optional cert policy | DEC-03, VERIFY-01, EXT-01 |
| §4.4.2 management letter and LOR contents | Artifacts present; narrative and figure owner decisions | DEC-11, REL-01 |
| §4.4.2 correspondence audit summary | Implemented as digest-bound Audit_trail.json with confirmations, PBC communications and cleared queries | Preserve; verify in REL-01, no rebuild |
| §4.4.2 final balance fee note | Bundle references posted invoice; verify output | REL-01 |
| §4.4.3 signature-anchored 60-day lock | Local schedule/freeze implemented; timer spec conflicts with Section 5 delivery condition | DEC-02, ARC-01 |
| §4.4.3 immutable timestamped logs/provider retention | Local evidence exists; external provider immutability not accepted | EXT-01, RELEASE-01 |
| §4.5.1 rates/profitability/utilization/variance | Core paths; staff/category definitions open | DEC-09, PRACTICE-01 |
| §4.5.2 rent/salary/benefits/withdrawals/expenses and reports | Firm ledger exists; category/report definition open; no payroll execution | DEC-09, PRACTICE-01 |
| §5.1 LEAD_INGESTION | Missing from engagement lifecycle output | LIFE-01 |
| §5.2–5.4 proposal, dual key, advance | Core gates exist; current offer identity and external delivery partial | CRM-02, EXT-01 |
| §5.5 planning → fieldwork accepted mapping | Partial | LIFE-02 |
| §5.6 fieldwork → manager review submitted work | Partial assignment and submit-status rule | AUD-01–03 |
| §5.7 manager review and rework return to fieldwork | Partial | AUD-02, LIFE-03 |
| §5.8 manager to Partner Approval | Gate predicate sequencing needs accepted lifecycle contract | DEC-01, LIFE-03 |
| §5.9 Partner sign/signature | Source exists; certificate policy remains optional/undecided | DEC-03 |
| §5.10 authorized release and countdown | Release source exists; live checkpoint external | DEC-02, EXT-01 |
| §5.11 countdown to local archive | Local committed freeze exists; provider read-only not established | ARC-01, EXT-01 |
| §6.1 seven-phase deterministic projection | Mapper implemented; actual lifecycle incomplete | LIFE-01–03 |
| §6.2 dashboard, blockers, phase links | Most UI present; role-computed next action and per-phase dates partial | TASK-01 |
| §6.3 phase-specific workflow details | Partial: assignee, filtered FSLI, current rework, stage gates and live release | LIFE-02/03, AUD-01–03, FIELD-01, EXT-01 |
| §6.4 templates/owners/dependencies/Kanban/calendar | Much implemented; gate-preservation/template rule gaps remain | TASK-01 |
| §6.5 annual engagement fresh gates and historical suggestions | Annual preparation exists; nonstandard calendar/full acceptance incomplete | VERIFY-01; include in release acceptance |
| §6.6 safe automation | PBC reminder path exists and is narrow/default-off; generic automation absent; freeze/invoice interpretation open | DEC-10, TASK-01, ARC-01 |
| §6.7 assignment-scoped preparer and client scope | Role/client authorization exists; assignment scope absent | AUD-01, EXT-01 |
| §6.8 AC-1 states/phases | Mapper complete; projection omits lead and lacks direct mapping gate | LIFE-01, LIFE-02 |
| §6.8 AC-2–5 Dual-Key and gate protections | Core paths exist; preserve and verify | CRM-02, VERIFY-01 |
| §6.8 AC-6 independent rework | Source gate exists, rework stage/assignment and full tests partial | AUD-01/02, VERIFY-01 |
| §6.8 AC-7 stale materiality/open notes | Partial SRM and direct accepted mapping predicate | LIFE-02/03 |
| §6.8 AC-8 confirmation blocker | Core path; test cohort/provider incomplete | VERIFY-01, EXT-01 |
| §6.8 AC-9 committed local freeze | Implemented; keep external state separate | ARC-01 |
| §6.8 AC-10 fresh annual sign-offs | Source says no copied approval rows; full journey unverified | VERIFY-01 |
| §6.8 AC-11 unrelated-client denial | Scope checks exist; complete isolation suite/live tenant not demonstrated | VERIFY-01, EXT-01 |
| §6.8 AC-12 idempotent automation | PBC reminder is idempotent; generic replay guarantee absent | TASK-01 |
| §7.1 authoritative commands/evidence | Implemented core; preserve | All stories / code review |
| §7.2 canonical lifecycle and five modules unchanged | Decision conflict recorded about stage 3/4 correction | DEC-01 |
| §7.3 deterministic phase projection | Mapper implemented; lifecycle partial | LIFE-01–03 |
| §7.4 support capabilities separate | Mostly implemented; broad automation/analytics acceptance open | TASK-01, PRACTICE-01 |
| §7.5 permissions, judgment, signing, fee and archive safeguards | Core paths exist; cert policy and provider archive acceptance open | DEC-03, EXT-01 |
| §7.6 external controls reviewed before production claim | Not accepted | EXT-01, RELEASE-01 |

## 8. Definition of completion and release checklist

Before any implementation is called complete:

- [ ] Story links identify the exact candidate SHA and approved decision IDs.
- [ ] Each changed command/query has server-side role, firm/client/engagement scope checks, current-input/revision fences, and failure behavior.
- [ ] Database changes are additive, migration/backfill was exercised on representative data, ambiguous rows remain explicitly unresolved, and rollback does not delete audit evidence.
- [ ] Every gate has positive, negative, stale-input, replay/idempotency, scope/security, concurrency (where applicable), and regression tests.
- [ ] Angular shows server-computed readiness and permitted actions; role/keyboard/error states have browser coverage.
- [ ] Full supported .NET and Angular verification commands were run and exact counts, skips, filters, and results are recorded at the candidate SHA.
- [ ] Five-part bundle entries and manifest were inspected from synthetic data; the existing correspondence trail was verified rather than duplicated.
- [ ] Entra, mail, SharePoint, release checkpoint and records-protection results remain BLOCKED_EXTERNAL until authorized live synthetic-tenant evidence is captured.
- [ ] No user/client data, secrets, tenant scopes, retention policy, production messages, or live records were changed outside an explicit separate authorization.
- [ ] Accepted deviations, unresolved decisions and deferrals are visible in the release evidence; no completion percentage is reported without a counted denominator.
- [ ] Product owner and audit-process owner approve production readiness separately from CI.

## 9. Validation notes

- Source material was read in full from the identified Library reference; source identity and 677-line length are retained above.
- The repository head was confirmed as b8b2743bbf9ff9e1f03fbacd445d60fe9547e5f8 before drafting. The recheck at delivery is reported alongside the artifact.
- This file is a backlog, not implementation evidence. No AuditSphere code, CI workflow, or external tenant was changed or exercised while authoring it.
- The prior completion review overstated one gap: the baseline Bundle service already includes Audit_trail.json with confirmation rows, PBC communications, and cleared queries. This backlog corrects that claim and assigns only output verification, not duplicate implementation.
- Source-local implementation and validation are deferred until the owner decisions and story acceptance tests above are resolved.
