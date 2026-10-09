# STE v2.1 gap-closure story definitions (STE-GAP-001 to STE-GAP-010)

**Status: CURRENT.** The ten definitions that commits `9e946d1`, `9c625a2` and `27449f0` cite. No repository file defined them before this document (spike SPK-02).

The definitions below are copied verbatim from the owner's STE v2.1 gap analysis (prepared 2026-10-08; reviewed repository revision `214579b9583bae5a209cb4cf3476a5fcb459251d`). Sections 1 to 4 of that analysis are not reproduced. The mapping table is new. It records each story's state on `master` after STE-NXT-003 to STE-NXT-008. "Closed locally" means the code path and its UI or API exist and were read. It does not mean the acceptance criteria were executed; the acceptance manifest records that separately.

## Mapping to the verification table

| Story | Title | Priority | Verification rows | State on `master` | Remaining work |
| --- | --- | --- | --- | --- | --- |
| STE-GAP-001 | Versioned service-specific engagement letter templates | P1 | 4.1.4 (both rows) | Closed locally: templates by route, unsupported routes fail closed | Letter generation per route not executed |
| STE-GAP-002 | 50 % advance invoice automation | P1 | 4.1.5 (ADR-0007, preparation state) | Closed locally: nine-state preparation display (STE-NXT-006), PAID-advance activation gate (ADR-0012), host `AutomaticFeeInvoices` alignment pinned by `FeeAgreementConfigurationTests` | Preparation states not executed in a browser with a linked agreement |
| STE-GAP-003 | Controlled ±5 % practical materiality rounding | P0 | 4.2.4 (rounding) | Closed locally: rounding form and server flag (STE-NXT-004) | Runtime check of the form; rejection branch N06 |
| STE-GAP-004 | Live client portal provisioning and credential acceptance | P1 | 4.1.5 (sign-in), 4.2.3 (folders) | BLOCKED_EXTERNAL: local orchestration only | Real tenant evidence (story section "External acceptance evidence") |
| STE-GAP-005 | Automatic holding letter dispatch | P1 | 4.3.4 | Closed locally: one dispatch per blocker set; a blocked or dead-lettered mail operation is reported as FAILED with the operations-screen recovery route, and re-dispatch is honest about the no-op (`AuditDeliverablesTests`) | Re-arming is the guarded operations recovery; not executed end to end against a live mail provider |
| STE-GAP-006 | Partner early compliance lock | P0 | 4.4.3 (early lock) | Closed locally: panel and API (STE-NXT-005) | Refusal branches not executed |
| STE-GAP-007 | Provider-enforced read-only compliance archive | P0 | 4.4.3 (permanent archive) | BLOCKED_EXTERNAL: no provider protection implemented | Approved design (SPK-01 and ADR-0009 draft); real-tenant tests |
| STE-GAP-008 | Canonical eleven-stage lifecycle truthfulness | P0 | 5 (lifecycle) | Partly closed: derived projection, countdown anchor, archive only when FROZEN | Section F provider-protection state model; Stage 3 on proposal dispatch not verified |
| STE-GAP-009 | Approved STE default charge-out policy | P2 | 4.5.1 (rates, rate cards) | Closed locally: baseline drafts, rate-card page (STE-NXT-003) | Approval by a second person not executed at runtime |
| STE-GAP-010 | End-to-end acceptance journey | P0 | No verification row | The manifest generator now has an evidence model: a step is PASS only when every cited test is declared in the current source and its suite has a recorded zero-failure run. 21 of 62 checks PASS (domain-backed lifecycle, materiality, review, reporting and archive steps); overall result stays FAIL | 36 checks still have no mapped executed evidence (browser and external steps); the E2E run recorded 15 failures, so no browser step can pass until that run is clean |

## Gap index (verbatim from the analysis)

### Primary remaining gaps

| ID | Gap | Classification | Priority |
|---|---|---|---|
| STE-GAP-001 | Separate versioned engagement-letter templates for statutory audit vs Internal Audit/AUP, with explicit ISRS 4400 lineage | Partial | P1 |
| STE-GAP-002 | Make the 50% advance invoice automation satisfy the "alongside Engagement Letter" flow while preserving independent finance controls | Partial | P1 |
| STE-GAP-003 | Manager-controlled practical materiality rounding within strict ±5% bounds | Missing | P0 |
| STE-GAP-004 | Prove real client portal provisioning, credentials/first-sign-in behavior and live workspace permissions | Blocked External | P1 |
| STE-GAP-005 | Automatically dispatch the generated critical-confirmation Holding Letter to the correct client-management recipient | Partial | P1 |
| STE-GAP-006 | Partner-triggered early/manual compliance lock during the 60-day countdown | Missing | P0 |
| STE-GAP-007 | Provider-enforced read-only/immutable final audit archive, not only an application-local lock | Blocked External / Compliance | P0 |
| STE-GAP-008 | Correct and complete the canonical 11-stage lifecycle projection and gates | Partial / Correctness | P0 |
| STE-GAP-009 | Establish the STE v2.1 default QAR charge-out policy as an approved versioned baseline | Configuration / Policy Gap | P2 |
| STE-GAP-010 | End-to-end STE v2.1 acceptance journey proving all lifecycle gates and failure branches | Acceptance Gap | P0 |

## Story definitions (verbatim from the analysis)

# 5. Detailed Gap-Closure User Stories

---

# STE-GAP-001 — Versioned Service-Specific Engagement Letter Templates

**Priority:** P1  
**Spec coverage:** 4.1.4 Engagement Letter Generation  
**Primary personas:** Engagement Partner, Commercial Manager  
**Classification:** Partial implementation

## User story

**As an Engagement Partner,**  
I want the Engagement Letter to be generated from the approved template that corresponds to the engagement's service type,  
so that statutory audits and Internal Audit/Agreed-Upon Procedures engagements retain the correct ISA/ISRS wording, template identity and approval lineage.

## Current implementation

The current commercial service already:

- requires dual-key clearance before generating an Engagement Letter;
- binds the document to the accepted quotation;
- embeds approved Partner signature and firm seal evidence;
- dynamically changes the heading based on the service route.

However, the renderer currently records a single Engagement Letter template identity:

`COMMERCIAL-ENGAGEMENT-LETTER-v1`

The non-statutory branch is rendered as a generic "Internal audit / agreed-upon procedures — agreed service terms" document rather than proving a separately governed ISRS 4400/AUP template.

## Required behavior

Introduce explicit, separately versioned service templates such as:

- `EL-STATUTORY-AUDIT-ISA210-v1`
- `EL-INTERNAL-AUDIT-v1`
- `EL-AUP-ISRS4400-v1`

The exact naming is implementation-specific; the requirement is that the selected template identity is explicit and immutable.

## Acceptance criteria

- [ ] A Financial Statement Audit selects the approved statutory audit/ISA 210 template.
- [ ] An AUP engagement selects an approved ISRS 4400 template.
- [ ] Internal Audit can use its own approved template rather than silently sharing AUP wording.
- [ ] Unsupported service routes fail closed with a visible "template not configured" blocker.
- [ ] The generated document persists the exact template ID/version.
- [ ] Template selection is derived server-side from the approved service profile; the client cannot provide an arbitrary template ID.
- [ ] The current accepted quotation revision, acceptance decision, Partner signature specimen and firm seal specimen remain bound to the generated letter.
- [ ] Superseding a template does not mutate historical letters.
- [ ] Regenerating against a newer approved template creates a new document/version and does not overwrite the former document.
- [ ] ISRS 4400 wording is only used for the governed AUP service route.
- [ ] Existing dual-key and authorization behavior remains unchanged.

## Suggested implementation touchpoints

- `CommercialDocumentService.cs`
- `CommercialDocumentRenderer.cs`
- service-profile / engagement-type domain configuration
- commercial settings/admin UI
- commercial document persistence if a stronger template identity field is required

## Test scenarios

1. Statutory audit → ISA 210 template selected.
2. AUP → ISRS 4400 template selected.
3. Internal Audit → internal-audit template selected.
4. Missing template → generation blocked.
5. Attempted caller template substitution → rejected.
6. Template v2 published after v1 letter → v1 document remains byte-identical and still references v1.
7. Partner signature/seal revocation after generation does not mutate the historical document.

## Definition of done

- Domain/application tests
- PostgreSQL persistence test for immutable template lineage
- API contract tests
- Angular/browser journey for each service route
- migration and backward compatibility for existing documents
- current STE coverage register updated with exact evidence

---

# STE-GAP-002 — Specification-Compliant 50% Advance Invoice Automation

**Priority:** P1  
**Spec coverage:** 3.1, 4.1.5 and lifecycle `ADVANCE_BILLING`  
**Primary personas:** Finance Manager, Engagement Partner  
**Classification:** Partial implementation

## User story

**As a Finance Manager,**  
I want the 50% advance invoice workflow to be initiated automatically when the current approved Engagement Letter is generated,  
so that the commercial handoff follows the STE sequence without bypassing AuditSphere's independent invoice review/posting controls.

## Current implementation

AuditSphere already has:

- a 50% advance milestone;
- `FeeAgreementService`;
- `AutomaticFeeInvoiceHandler`;
- safe idempotent discovery;
- current-letter verification;
- FinanceManager authority checks;
- an independent administrator standing-authorization check;
- normal invoice review/posting/mail gates.

The current standing automation:

- is **off by default** in worker configuration;
- creates **drafts only**;
- does not mean the invoice is officially posted/sent.

This security design should be preserved.

## Required behavior

The STE requirement must be satisfied as an orchestration contract:

1. valid dual-key Engagement Letter generated;
2. 50% advance milestone created;
3. configured automation creates exactly one invoice draft;
4. the normal finance review/posting process produces the official invoice;
5. delivery status is separately recorded;
6. portal activation remains blocked until payment is actually recorded/allocated.

Do **not** weaken finance segregation by auto-posting an invoice using the Engagement Partner's authority.

## Acceptance criteria

- [ ] Generating a current approved Engagement Letter creates or confirms one 50% advance milestone.
- [ ] With approved automation policy enabled, exactly one invoice draft is produced for that milestone.
- [ ] Duplicate worker retries cannot create a second invoice draft.
- [ ] If automation policy is disabled, the UI explicitly shows that advance-invoice preparation is pending rather than implying the requirement is complete.
- [ ] Official invoice status is distinct from draft status.
- [ ] Independent review/posting remains required.
- [ ] Invoice dispatch status is distinct from queueing status.
- [ ] Engagement/portal activation is impossible until the advance milestone is fully paid and allocated.
- [ ] Changed/revoked commercial acceptance or stale Engagement Letter prevents automation.
- [ ] Re-generating the Engagement Letter must not duplicate a valid current invoice.
- [ ] Balance invoice cannot be generated before the advance is paid and final release exists.
- [ ] All actions are auditable by milestone, invoice, actor and exact document/quotation identities.

## Suggested implementation touchpoints

- `FeeAgreementService.cs`
- `AutomaticFeeInvoices.cs`
- `AuditSphereOps.Worker/Program.cs`
- Worker configuration validation
- commercial/finance Angular workspaces
- `EngagementLifecycleQuery.cs`

## Test scenarios

- policy enabled / disabled
- worker retry
- stale EL
- revoked standing authority
- finance user grant revoked after enqueue
- same milestone replay
- failed draft creation followed by safe recovery
- portal activation before/after payment

## Definition of done

The STE flow can be demonstrated from Engagement Letter → invoice draft → independent post → payment → receipt → portal activation without a manual hidden handoff and without weakening finance controls.

---

# STE-GAP-003 — Controlled ±5% Practical Materiality Rounding

**Priority:** P0  
**Spec coverage:** 3.2 and 4.2.4  
**Primary personas:** Audit Manager/Reviewer, Engagement Partner  
**Classification:** Missing

## User story

**As an Audit Manager,**  
I want to apply documented practical rounding to calculated PM, TE and SAD thresholds within the specification's strict ±5% limit,  
so that practical audit thresholds can be used without losing the original mathematical calculation or Partner approval trail.

## Current implementation

The current engine correctly:

- derives benchmark values from the current approved mapped/sealed TB;
- enforces benchmark-rate ranges;
- calculates PM, TE and SAD;
- binds results to the exact dataset/mapping digest;
- invalidates stale calculations;
- requires independent Partner approval.

What is **not** represented is a user-controlled practical rounding decision. Existing calls to numeric `Math.Round` are calculation precision, not the specification's managerial ±5% practical adjustment.

## Domain design

Prefer an append-only decision record rather than mutating `MaterialityCalculation`.

Suggested model:

`MaterialityRoundingDecision`

Fields should include at minimum:

- ID
- FirmId / ClientId / EngagementId
- MaterialityCalculationId
- computed PM / TE / SAD
- adjusted PM / TE / SAD
- PM delta amount and delta %
- TE delta amount and delta %
- SAD delta amount and delta %
- rationale
- rule/policy version
- decided-by Manager user
- decided-at
- revision/input digest
- superseded-by or current decision semantics

The approved `MaterialityAssessment` should reference or cryptographically bind the exact current calculation + rounding decision.

## Business rules

- absolute percentage adjustment for each adjusted threshold must be `<= 5.0000%`;
- positive and negative rounding are permitted;
- `SAD > 0`;
- `TE > 0`;
- `PM > 0`;
- `SAD <= TE <= PM`;
- adjusted thresholds cannot be entered without the original system calculation;
- no caller-provided "computed value" can substitute for the engine's stored calculation;
- a changed TB/mapping invalidates both the calculation and rounding decision;
- a changed rounding decision invalidates prior approval and downstream work that depends on materiality where appropriate;
- Partner approval must bind to the exact rounded values.

## Acceptance criteria

- [ ] Manager sees computed PM/TE/SAD and may leave them unchanged.
- [ ] Manager may enter practical rounded values and a mandatory rationale.
- [ ] `+5.0000%` and `-5.0000%` are accepted.
- [ ] `+5.0001%` and `-5.0001%` are rejected.
- [ ] A proposed value that breaks `SAD <= TE <= PM` is rejected.
- [ ] Original mathematical values remain permanently visible.
- [ ] Adjusted values are never represented as the engine's original calculation.
- [ ] Adjustment percentage is server-computed, not trusted from the UI.
- [ ] Partner approval is for the exact calculation + adjustment revision.
- [ ] The person who prepares the materiality decision cannot approve the same decision when existing independence rules require a separate Partner.
- [ ] Any newer approved mapping or TB dataset marks the calculation/rounding decision stale and blocks use.
- [ ] FSLI risk classification and SAD evaluation use the current approved effective thresholds.
- [ ] Existing historical calculations remain readable.

## UI requirements

Show:

- benchmark and source amount
- policy rate
- computed PM / TE / SAD
- rounded/effective PM / TE / SAD
- delta amount
- delta %
- ±5% boundary indicator
- manager rationale
- Partner approval identity/time
- stale/current badge

## Test scenarios

- no adjustment
- exact ±5% boundary
- over-boundary rejection
- hierarchy violation
- zero/negative threshold
- stale TB
- stale mapping
- concurrent rounding decisions
- Partner approval against stale adjustment
- FSLI classification before/after approved rounding

## Definition of done

All consumers of materiality use one authoritative effective-materiality projection that is reproducible from:

`sealed TB + approved mapping + calculation policy + manager rounding decision + Partner approval`.

---

# STE-GAP-004 — Live Client Portal Provisioning & Credential Acceptance

**Priority:** P1  
**Spec coverage:** 3.1 and 4.1.5; 4.2.3 external workspace  
**Primary personas:** Client Audit Liaison, Administrator  
**Classification:** Blocked External

## User story

**As a Client Audit Liaison,**  
I want the portal identity and isolated client workspace to be provisioned and verified against the real configured Microsoft environment,  
so that I can securely sign in, satisfy the first-login credential requirement and upload only to my authorized engagement.

## Current implementation

The repository already has local orchestration for:

- portal readiness/intents;
- first-sign-in upload gating;
- participant/delegation controls;
- SharePoint/Graph workspace provisioning;
- five-folder structure;
- exact selected-site bindings;
- read/write capability tests;
- upload state and exact-byte evidence.

The repository itself records that live provider behavior remains an external acceptance boundary in some environments.

## Required acceptance

This story is not complete from fake-provider tests alone.

## Acceptance criteria

- [ ] A new client receives a unique external/member identity according to the approved identity path.
- [ ] The designated Audit Liaison receives the intended sign-in/invitation communication.
- [ ] Where a temporary password path is used, first-login password change is enforced.
- [ ] Where federated/external identity is used, an equivalent first-sign-in evidence gate is recorded; do not fabricate a password-reset event.
- [ ] Upload controls remain unavailable before first-sign-in evidence.
- [ ] A real client workspace is provisioned in the selected SharePoint location.
- [ ] The standard engagement folder taxonomy is physically present.
- [ ] A capability read/write test succeeds for the intended PBC location.
- [ ] Client A cannot enumerate/read/write Client B's workspace.
- [ ] Revoking portal participation removes subsequent access.
- [ ] Final release blocks application-mediated upload.
- [ ] Unknown provider outcomes are reconciled idempotently rather than blindly retried.
- [ ] Tokens, passwords and Graph secrets are never stored in audit evidence.

## External acceptance evidence

Capture:

- tenant/site/drive identifiers
- provisioned identity identifier
- invite/send provider correlation ID
- first-sign-in observed timestamp
- workspace binding IDs
- capability test result
- access-revocation test
- cross-client denial test

Do not record reusable credentials.

## Definition of done

The same behavior proven by local/fake-provider tests is demonstrated in the approved real tenant and the results are recorded as controlled acceptance evidence.

---

# STE-GAP-005 — Automatic Holding Letter Dispatch for Critical Confirmations

**Priority:** P1  
**Spec coverage:** 3.3, 4.3.4  
**Primary personas:** Audit Manager, Client MD/GM  
**Classification:** Partial implementation

## User story

**As an Audit Manager,**  
I want a critical-confirmation blocker to automatically create and dispatch the current Holding Letter to the designated client-management contact,  
so that the client is formally notified without relying on an undocumented manual communication step.

## Current implementation

`AuditDeliverableService.GenerateReportAsync` already:

- detects critical confirmations lacking a returned and independently evaluated response;
- blocks the Independent Auditor's Report;
- generates a Pending Confirmation / Holding Letter;
- returns the generated Holding Letter ID.

The reviewed path does not itself prove automatic dispatch to the configured client-management recipient.

## Required behavior

- determine the recipient using the CRM communication-routing rules;
- generate the letter against the exact current confirmation set;
- enqueue/send using the normal auditable outbound channel;
- persist dispatch intent, provider status/correlation and final delivery state separately;
- avoid duplicate messages for the same unchanged blocker set;
- resend only on an explicit action or a materially changed current blocker set according to policy.

## Acceptance criteria

- [ ] One or more critical unreturned confirmations block report release.
- [ ] A Holding Letter is generated from the exact current outstanding set.
- [ ] The recipient is resolved from the configured MD/GM/final-report route.
- [ ] Missing recipient blocks dispatch with an actionable error; it does not choose an arbitrary email.
- [ ] Queueing is not displayed as delivered.
- [ ] Provider failure is visible and retryable.
- [ ] Idempotent retry cannot generate/send duplicates.
- [ ] A change in the critical confirmation set creates a new letter revision/digest.
- [ ] Once all critical confirmations are returned and independently evaluated, the blocker clears.
- [ ] The historical Holding Letter and dispatch evidence remain in the correspondence audit trail.

## Suggested implementation touchpoints

- `AuditDeliverableService.cs`
- CRM routing resolver
- outbound email durable operation
- completion/confirmations Angular UI
- final bundle correspondence manifest

---

# STE-GAP-006 — Partner Early/Manual Compliance Lock

**Priority:** P0  
**Spec coverage:** 3.4, 4.4.3 and lifecycle `COMPLIANCE_COUNTDOWN`  
**Primary persona:** Engagement Partner  
**Classification:** Missing

## User story

**As an Engagement Partner,**  
I want to intentionally lock a completed audit file before the automatic 60-day deadline,  
so that a file that is fully assembled can enter the read-only archive immediately as allowed by the STE lifecycle.

## Current implementation

`FileFreezeService` currently:

- schedules a freeze for 60 days after the signed report;
- has a durable due-file worker;
- blocks writes after local freeze;
- records refused write attempts;
- supports controlled post-freeze amendment windows.

No explicit Partner early/manual lock command was found in the reviewed current source.

## Required command

Suggested intent:

`RequestEarlyComplianceLock`

The exact name may differ.

Inputs:

- EngagementId
- expected freeze revision
- mandatory Partner confirmation
- optional/required lock rationale according to firm policy
- reviewed archive readiness digest

## Gate conditions

Early lock must fail unless:

- current final signed report exists;
- final deliverables/bundle release is complete;
- client upload is already frozen;
- final archive readiness checks pass;
- no incompatible amendment window is open;
- actor is an authorized Engagement Partner;
- expected revision/currentness checks pass.

## Acceptance criteria

- [ ] Partner can trigger an early lock during `COMPLIANCE_COUNTDOWN`.
- [ ] Non-Partner users cannot trigger it.
- [ ] An already frozen engagement is idempotently reported as frozen.
- [ ] A stale expected revision is rejected.
- [ ] Open/incomplete finalization blockers prevent early lock.
- [ ] The local engagement freeze occurs atomically with the command or durable operation publication.
- [ ] Professional write paths become blocked immediately when the local freeze is committed.
- [ ] The lifecycle projection changes to `ARCHIVED_READ_ONLY` only after actual freeze success.
- [ ] Early lock creates an immutable event recording actor, timestamp, reason and source archive digest.
- [ ] Provider read-only enforcement is invoked through STE-GAP-007 and its status is separately visible.
- [ ] Existing controlled amendment workflow remains available according to governance policy.

## UI

Add an **Early Lock Audit File** action in the completion/records view with:

- due date
- days remaining
- archive readiness summary
- provider protection status
- explicit irreversible-action warning
- confirmation input

---

# STE-GAP-007 — Provider-Enforced Read-Only Compliance Archive

**Priority:** P0  
**Spec coverage:** 4.4.3 and terminal `ARCHIVED_READ_ONLY`  
**Primary personas:** Engagement Partner, Compliance Administrator  
**Classification:** Blocked External / Compliance

## User story

**As a Compliance Administrator,**  
I want the final audit archive to be protected against direct provider-side edits, deletes and overwrites after lock,  
so that the STE/ISA 230 read-only requirement remains true even when a user bypasses the AuditSphere application and accesses SharePoint directly.

## Current implementation

The local freeze is strong inside AuditSphere.

However, `FileFreezeService` explicitly records:

`ExternalReadOnly = BLOCKED_EXTERNAL`

The repository coverage register also notes that users with direct SharePoint Full Control can bypass an application-only lock.

Therefore the terminal compliance invariant is **not yet proven** at the external file repository.

## Architectural requirement

Implement an approved provider protection strategy. Acceptable designs may include, subject to firm/Microsoft governance:

- moving/copying the exact released archive to a protected archive location whose ACL no longer grants ordinary staff write access;
- changing permissions after archive assembly;
- provider-supported record/retention protection if formally approved;
- a separately controlled immutable archive repository.

Do not claim immutability solely because AuditSphere refuses writes.

## Mandatory invariants

- protected bytes must be the exact released/archive bytes;
- content hashes before and after provider transfer must match;
- the protected object/location identity is persisted;
- ordinary engagement staff cannot edit/delete/overwrite the protected archive directly;
- regulator/read-only export remains available;
- provider-protection failure prevents the system from claiming externally protected state;
- the audit log records protection request, provider correlation, observation and verification.

## Acceptance criteria

- [ ] Local freeze transitions to frozen.
- [ ] Provider protection operation is issued idempotently.
- [ ] Provider readback confirms the exact file/tree identity and hashes where technically possible.
- [ ] A direct provider edit attempt by a normal engagement staff user is denied.
- [ ] A direct provider delete attempt is denied.
- [ ] A direct provider overwrite/replacement attempt is denied.
- [ ] Authorized compliance read succeeds.
- [ ] Cross-client access remains denied.
- [ ] A failed/unknown provider result remains `BLOCKED_EXTERNAL` or an equivalent non-compliant state.
- [ ] The UI does not label provider protection "Complete" until verification succeeds.
- [ ] Controlled amendment policy is explicitly reconciled with provider protection; reopening must not silently make protected historical bytes mutable.

## Security tests

- direct Graph write after lock
- direct SharePoint UI edit after lock
- direct delete
- direct rename/move if rename/move would violate preservation policy
- privilege revocation during provider operation
- replayed provider request
- provider timeout / uncertain result
- hash mismatch on copied archive
- attempt to substitute a sibling engagement's archive

## Definition of done

A real-tenant acceptance test proves that an engagement user who previously had active-work access cannot modify or delete the terminal protected audit archive.

---

# STE-GAP-008 — Canonical Eleven-Stage Lifecycle Truthfulness & Gate Alignment

**Priority:** P0  
**Spec coverage:** Section 5 System State Machine plus all module handoffs  
**Primary personas:** All internal users  
**Classification:** Partial / correctness gap

## User story

**As an AuditSphere user,**  
I want the displayed canonical lifecycle stage to be computed only from the exact STE gate conditions that have actually been satisfied,  
so that the system never presents an engagement as further advanced than its authoritative evidence permits.

## Current implementation

`EngagementLifecycleQuery` defines all eleven stage constants and provides a useful projection, but several conditions need tightening.

## Required corrections

### A. Represent true `LEAD_INGESTION`

The current engagement-scoped query cannot represent a lead before an engagement exists.

Implement either:

- a commercial-lifecycle projection whose aggregate key may begin as Lead/Opportunity and later resolve to Engagement, or
- a separate pre-engagement lifecycle projection composed into the same UI.

The UI must be able to show Stage 1 before an engagement record exists.

### B. Planning approval must mean current approved materiality

Do not treat "a materiality calculation exists" as equivalent to Partner-approved planning.

Required gate:

- current sealed/balanced TB;
- current approved mapping to FSLIs;
- current materiality calculation;
- current effective rounding decision if applicable;
- required independent Partner materiality/planning approval;
- required staffing/planning conditions.

### C. Managerial Review → Partner Approval gate

The transition must explicitly require the current specification gate:

- all applicable workprograms submitted/reviewed as required;
- zero open review notes;
- current SRM compiled;
- critical confirmations returned and independently evaluated;
- required high-risk routing/reviews complete.

A later report-generation blocker is not a substitute for the state-machine transition condition.

### D. Countdown anchor

The 60-day compliance countdown must use the final Partner report-signature date, matching `FileFreezeService`, not an unrelated release date when those timestamps differ.

### E. Never infer archive completion from elapsed time alone

`60 days elapsed` means the freeze **must be due**, not that it definitely succeeded.

The stage must remain in a due/error state until the local freeze operation has actually committed.

### F. Provider protection truthfulness

Do not present externally immutable/read-only status if external protection remains `BLOCKED_EXTERNAL`.

A useful UI model is:

- Local archive state
- Provider protection state
- Canonical lifecycle state
- Compliance warning

The product may reach local `ARCHIVED_READ_ONLY` while showing a separate external compliance warning, but it must not imply provider immutability.

## Acceptance criteria

- [ ] Stage 1 can be displayed for a lead with no engagement.
- [ ] Proposal dispatch advances to Stage 3 only after the specified commercial event.
- [ ] Dual-key stage cannot advance with only one key.
- [ ] Advance-billing stage cannot advance until advance payment is confirmed.
- [ ] Planning cannot advance on an unapproved materiality calculation.
- [ ] Stale materiality returns planning to blocked/current-action-required state.
- [ ] Fieldwork cannot be represented as complete merely because each procedure has a result revision; required submission/review semantics are honored.
- [ ] Managerial review cannot advance with an open review note.
- [ ] Managerial review cannot advance with a stale/missing SRM.
- [ ] Managerial review cannot advance with a current critical confirmation outstanding.
- [ ] Partner Approval requires required Red-risk review and current clearance.
- [ ] Deliverable Release requires actual final package/release evidence.
- [ ] Client upload freeze happens at final release.
- [ ] Countdown date equals report-signature date + 60 calendar days.
- [ ] Day 60 with an unexecuted/failed freeze is not shown as successfully archived.
- [ ] Early lock from STE-GAP-006 advances only after successful freeze.
- [ ] Stage transitions are monotonic only when evidence remains current; if an allowed upstream revision invalidates a gate before irreversible release, the UI shows the correct blocked/stale state.
- [ ] Archived terminal history remains immutable.

## Suggested implementation

Introduce a single authoritative lifecycle evaluator composed from small gate evaluators:

- `CommercialGate`
- `DualKeyGate`
- `AdvancePaymentGate`
- `PlanningGate`
- `FieldworkGate`
- `ManagerialReviewGate`
- `PartnerApprovalGate`
- `ReleaseGate`
- `ComplianceFreezeGate`

Each gate should return:

- Satisfied
- Blocked
- Stale
- ExternalBlocked
- reason codes
- evidence identity/digest
- responsible role
- deep link

Avoid duplicating business rules in UI components.

## Test matrix

Test every transition with:

- exact happy-path evidence;
- each individual required input missing;
- stale upstream evidence;
- authorization revoked;
- cross-client data;
- concurrent update;
- external unknown result where relevant.

Also test that each stage can be reached and that no stage can be skipped by inserting only a later artifact.

---

# STE-GAP-009 — Approved STE Default Charge-Out Rate Policy

**Priority:** P2  
**Spec coverage:** 3.5 and 4.5.1  
**Primary persona:** Firm Administrator / Finance Manager  
**Classification:** Configuration / policy gap

## User story

**As a Firm Administrator,**  
I want the STE v2.1 standard QAR charge-out schedule to be available as an approved versioned baseline,  
so that profitability and realization use the specified role values unless the firm deliberately approves a later policy version.

## Required baseline

- Engagement Partner — **QAR 1,000/hour**
- Audit Manager — **QAR 750/hour**
- Audit Supervisor / Senior — **QAR 500/hour**
- Audit Associate / Junior — **QAR 200/hour**

## Current implementation

AuditSphere already has a stronger versioned rate-card mechanism and captures the rate used on approved time. The gap is not the calculation engine; it is proving that the STE default schedule is an approved initialized policy rather than relying on ad-hoc manual rate creation.

## Acceptance criteria

- [ ] A new STE-configured firm can initialize the four required default QAR rates.
- [ ] The baseline is versioned/effective-dated.
- [ ] The actor approving a rate policy is recorded.
- [ ] Existing time entries retain their captured historical rate after a later rate-card change.
- [ ] Role aliases map explicitly: Supervisor/Senior → 500; Associate/Junior → 200.
- [ ] Unknown roles do not silently inherit a rate.
- [ ] Missing effective rate prevents standard-value calculation rather than substituting zero.
- [ ] A firm may publish a later approved rate version if business policy permits, while retaining the STE baseline history.
- [ ] UI labels distinguish standard charge-out value from actual staff cost.

## Definition of done

A seeded/configured STE firm can reproduce the specification's example cost calculation without test-only database setup.

---

# STE-GAP-010 — Full STE v2.1 End-to-End Acceptance Journey

**Priority:** P0  
**Spec coverage:** entire specification and Section 5 state machine  
**Primary personas:** Preparer, Reviewer, Partner, Client, Finance Manager  
**Classification:** Acceptance gap

## User story

**As a Product Owner / Audit Partner,**  
I want one controlled end-to-end acceptance suite to prove the complete STE v2.1 lifecycle,  
so that individual module tests do not create a false impression that all cross-module gates work together.

## Happy-path journey

The acceptance journey must prove:

1. Lead created with entity and contact profile.
2. Client converted/profiled with relevant relationship/contact routing.
3. Brief or comprehensive proposal generated.
4. Proposal dispatched and client commercial acceptance recorded.
5. New/continuance acceptance checklist completed.
6. Independent Partner risk acceptance recorded.
7. Engagement Letter generated from correct service-specific template.
8. 50% advance invoice workflow initiated.
9. Official invoice posted/delivered through approved finance flow.
10. Payment recorded and allocated.
11. Official receipt generated.
12. Portal identity/workspace provisioned.
13. First-sign-in rule satisfied.
14. Standard directory available.
15. staff assigned and capacity/schedule visible.
16. TB uploaded and sealed.
17. mapping approved.
18. materiality calculated.
19. optional practical rounding applied within ±5%.
20. independent Partner materiality approval recorded.
21. FSLI risk bands visible and staffing routes enforced.
22. fieldwork program adopted.
23. parallel FSLI work executed.
24. digital and physical evidence linked.
25. sampling run persisted/reproducible.
26. analytical review completed/reviewed.
27. going-concern assessment completed/reviewed.
28. Preparer submits work.
29. Reviewer returns at least one item with mandatory note.
30. Preparer reworks and resubmits.
31. review note resolved.
32. SRM generated from current evidence.
33. Partner reviews required Red areas.
34. critical confirmations are returned/evaluated.
35. Partner clears current SRM.
36. opinion selected.
37. report generated and signed/sealed.
38. Management Letter generated.
39. LOR generated, downloaded, signed by client and re-uploaded.
40. correspondence trail included.
41. five-part bundle assembled.
42. final 50% invoice generated/posting workflow completed.
43. client portal uploads become read-only/frozen.
44. countdown begins from report signature.
45. either automatic day-60 freeze or Partner early lock succeeds.
46. archive becomes locally read-only.
47. provider protection verification succeeds or remains explicitly external-blocked.
48. regulator/read-only export succeeds.

## Mandatory negative branches

The suite must also prove at least:

- client accepted quote but Partner risk gate missing → no Engagement Letter;
- Partner risk gate complete but client commercial acceptance missing → no Engagement Letter;
- advance invoice unpaid → no active upload workspace;
- first sign-in not completed → no upload;
- stale TB/mapping → materiality cannot be approved;
- practical rounding >5% → rejected;
- junior assigned to Amber/Red work below required level → rejected;
- open review note → completion blocked;
- critical confirmation outstanding → audit report blocked + Holding Letter generated;
- modified opinion without affected FSLI/basis → rejected;
- final release → subsequent client upload rejected;
- day 60 with failed freeze operation → cannot falsely claim archived;
- direct provider edit after verified archive protection → denied;
- cross-client user → denied throughout.

## Evidence artifact

Generate a machine-readable acceptance manifest containing:

- repository commit
- migration/model revision
- specification version
- test case IDs
- stage transition evidence IDs
- generated document IDs/hashes
- current external-provider acceptance evidence
- known external blockers
- overall PASS / BLOCKED_EXTERNAL / FAIL result

A `PASS` must never be emitted while an asserted mandatory check contains TODO/incomplete evidence.

---

# 6. Recommended Implementation Order

## Phase A — Correctness and compliance gaps

1. **STE-GAP-003** — practical materiality rounding
2. **STE-GAP-008** — canonical lifecycle truthfulness
3. **STE-GAP-006** — early/manual Partner lock
4. **STE-GAP-001** — service-specific Engagement Letter templates

These are primarily local code changes and remove direct mismatches with the supplied functional specification.

## Phase B — Automation and external controls

5. **STE-GAP-002** — advance-invoice orchestration
6. **STE-GAP-005** — Holding Letter dispatch
7. **STE-GAP-004** — real portal/workspace acceptance
8. **STE-GAP-007** — provider-enforced archive protection

`STE-GAP-007` should be treated as a release/compliance blocker if production claims include immutable provider-side archival.

## Phase C — Policy initialization and release acceptance

9. **STE-GAP-009** — STE baseline rate policy
10. **STE-GAP-010** — full lifecycle acceptance

---

# 7. Cross-Cutting Engineering Requirements

Every gap story must preserve the architectural safety rules already present in AuditSphere.

## 7.1 Authorization

- Every command resolves Firm/Client/Engagement scope from persisted data.
- Never trust caller-supplied client IDs as authorization evidence.
- Partner-only decisions remain Partner-only.
- Administrator role must not silently imply audit or finance decision authority.
- Client users remain isolated to their authorized client/engagement.

## 7.2 Evidence lineage

Persist exact source identity for every derived result:

- TB dataset and digest
- mapping version
- materiality policy version
- rounding decision
- workprogram version
- result revision
- reviewed evidence version
- SRM digest
- confirmation response revision
- report/deliverable version
- signature/seal specimen identity
- release/bundle hashes
- archive/protection identity

## 7.3 Staleness

Any approval must fail if its reviewed inputs changed after review.

Do not silently carry an approval over to a new:

- TB
- mapping
- materiality calculation
- materiality rounding decision
- workpaper result
- confirmation response
- SRM
- audit opinion
- deliverable/bundle
- archive snapshot

## 7.4 Concurrency

Use row/version fencing for mutable workflow records.

Required conflict behavior:

- reject stale writes;
- return a specific conflict/stale error;
- reload current persisted state;
- never last-write-wins professional decisions.

## 7.5 Idempotency

External/durable operations must use stable business identities.

Examples:

- one current advance milestone
- one invoice draft per fee milestone
- one Holding Letter dispatch per blocker-set revision
- one freeze request per freeze revision
- one provider protection action per exact archive digest

## 7.6 External operation semantics

Keep these states distinct:

- requested
- queued
- executing
- provider accepted
- provider result uncertain
- verified
- failed
- externally blocked

"Queued" is never "Delivered".

## 7.7 Audit trail

Record at minimum:

- actor
- effective role/scope
- session epoch where applicable
- timestamp
- old/new state
- exact target ID/revision
- rationale/comment where required
- operation correlation
- result

## 7.8 Test pyramid

For each story, add appropriate:

- pure domain tests
- PostgreSQL integration tests
- API contract tests
- Angular component/unit tests
- browser/API-host journeys
- real-provider acceptance tests for external stories

---

# 8. Explicit Non-Gaps — Do Not Rebuild These From Scratch

The following areas should be extended/reused rather than replaced:

- dual-key acceptance
- acceptance/continuance questionnaire infrastructure
- resource calendar and staffing
- mapped/sealed TB source
- materiality benchmark calculation
- FSLI risk-band classification
- program catalog/adoption
- procedure tailoring
- deterministic sampling
- evidence link model
- analytical review
- going-concern assessment
- workpaper review/rework
- review-note workflow
- SRM and Partner clearance
- confirmation cases and criticality
- opinion engine
- report/Management Letter/LOR generation
- signature/seal specimen handling
- completion bundle assembly
- fee milestone and finance segregation
- portal status/re-upload semantics
- local file-freeze write guards
- controlled archive amendment workflow
- time/rate-card infrastructure
- practice analytics
- firm-book ledger/reporting

Older task cards should not cause these capabilities to be duplicated under parallel models or services.

---

# 9. Notable Current-Code Findings That Should Be Fixed During the Stories

## Finding A — Materiality documentation overstates practical rounding

Current repository documentation says rounding ranges are enforced, but the reviewed materiality source only proves mathematical precision rounding and policy percentage ranges. The product should not mark STE practical rounding complete until STE-GAP-003 exists and is tested.

## Finding B — Lifecycle projection can overstate planning progress

`EngagementLifecycleQuery` currently considers the planning basis approved when a materiality calculation exists, even though a calculation can still be awaiting independent Partner approval.

This must be corrected in STE-GAP-008.

## Finding C — Lifecycle archive state can be inferred from elapsed time

The lifecycle query can project archived state after 60 elapsed days even if the durable local freeze operation has not successfully completed.

The actual `EngagementFileFreeze.State` must be authoritative.

## Finding D — Countdown timestamp sources are inconsistent

The compliance requirement is based on report signature. `FileFreezeService` follows that rule. The lifecycle projection should use the same source rather than preferring release time.

## Finding E — Provider read-only remains explicit external blocker

The current local freeze correctly marks external protection as blocked rather than fabricating provider immutability. Preserve that truthfulness until STE-GAP-007 passes real-provider acceptance.

---

# 10. Release Gate for "STE v2.1 Complete"

Do not mark the supplied specification as fully implemented until all of the following are true:

- [ ] STE-GAP-001 complete
- [ ] STE-GAP-002 complete or an approved product decision formally narrows the "automatic invoice" interpretation while preserving the required user-visible flow
- [ ] STE-GAP-003 complete
- [ ] STE-GAP-004 real-tenant acceptance complete for the production identity/storage model
- [ ] STE-GAP-005 complete if "trigger automated Holding Letter to client" includes dispatch in the accepted business interpretation
- [ ] STE-GAP-006 complete
- [ ] STE-GAP-007 complete or the production claim explicitly excludes provider-level immutable archive compliance
- [ ] STE-GAP-008 complete
- [ ] STE-GAP-009 approved/configured
- [ ] STE-GAP-010 full acceptance manifest passes
- [ ] current migrations applied through normal deployment controls
- [ ] no current STE coverage row relies solely on historical/stale documentation
- [ ] external acceptance evidence is current for the production tenant
- [ ] no known blocker is hidden behind an "Implemented" label

---

# 11. Suggested GitHub Issue/Epic Structure

Create one Epic:

**Epic: STE v2.1 Specification Final Gap Closure**

Children:

- `STE-GAP-001 — Service-specific Engagement Letter templates`
- `STE-GAP-002 — Automated 50% advance invoice orchestration`
- `STE-GAP-003 — ±5% practical materiality rounding`
- `STE-GAP-004 — Live portal/workspace provisioning acceptance`
- `STE-GAP-005 — Critical-confirmation Holding Letter dispatch`
- `STE-GAP-006 — Partner early/manual compliance lock`
- `STE-GAP-007 — Provider-enforced read-only audit archive`
- `STE-GAP-008 — Canonical lifecycle gate alignment`
- `STE-GAP-009 — STE QAR charge-out baseline`
- `STE-GAP-010 — End-to-end STE v2.1 acceptance suite`

Recommended labels:

- `spec:ste-v2.1`
- `gap-closure`
- `audit`
- `compliance`
- `P0` / `P1` / `P2`
- `blocked-external` where applicable
- `needs-migration` where applicable
- `needs-e2e`
- `needs-live-acceptance`

---

# 12. Final Assessment

AuditSphere should **not** be treated as an early-stage audit workflow implementation. The updated codebase already contains most of the difficult audit execution mechanics required by STE v2.1.

The highest-value remaining work is to close the exact compliance and orchestration edges:

1. practical materiality rounding;
2. exact lifecycle-gate truthfulness;
3. early/manual archive lock;
4. real provider-side immutable/read-only archive protection;
5. service-specific Engagement Letter governance;
6. safe automation of commercial handoffs and Holding Letter communications;
7. a production-grade end-to-end acceptance proof.

Completing those stories allows the team to make a substantially stronger and more defensible claim that the supplied **Audit Management Tool Specification v2.1** has been implemented end-to-end rather than merely represented across individual modules.
