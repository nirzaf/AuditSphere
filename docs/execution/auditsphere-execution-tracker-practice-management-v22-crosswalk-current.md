# Practice Management v2.2 — Story and Candidate-Gap Crosswalk

**Status: CURRENT** as a reconciliation of [`auditsphere-execution-user-stories-cloud-practice-management-v22-proposed.md`](auditsphere-execution-user-stories-cloud-practice-management-v22-proposed.md) against the code. It is the PM-00 baseline. It is not a functional-testing claim and it closes no external gate.

**Baseline:** `master` at `733ab239`, plus the slice that introduced this document. Verified test evidence is in [`status.json`](status.json), not here.

**Dispositions:** `ALREADY_COMPLETE` · `PARTIAL` · `MISSING` · `OWNER_DECISION` (names the decision in section 7 of the story document) · `BLOCKED_EXTERNAL` · `DEVIATION` (recorded in an ADR) · `OPTIONAL` · `OUT_OF_SCOPE` · `NOT_REVERIFIED`.

**Basis:** `C` the code was read or changed in this slice · `R` taken from the row-level evidence in [`auditsphere-execution-tracker-ste-v22-gap-verification-current.md`](auditsphere-execution-tracker-ste-v22-gap-verification-current.md) · `N` not re-read here, carried from the prior review and still a hypothesis · `-` a scope classification that needs no code evidence.

## What the first slice changed

- **FIN-05, PM-10.** The firm trial balance report returns one profit and loss row per accounting month in the range, with the same month one year earlier as the comparator. The months come from the same posted lines as the range totals, so they add up to them. The comparator is absent, not zero, when the firm holds no period for that month. The export carries the rows as `PROFIT_LOSS_MONTH`.
- **TIME-01, PM-08.** Time capture, budget revision, budget preview and quotation pricing read one rate lookup. An exact approved card wins; an STE alias (Senior, Supervisor, Junior, Associate) borrows the baseline card; any other role still fails closed.
- **WF-02, PM-01.** The engagement dashboard names the people currently staffed at the level that owns the current step and says so when nobody is. It shows the days left to the scheduled freeze, and it does not describe a due but uncommitted freeze as archived.

## What the second slice changed

- **TIME-02, TIME-03, FIN-03 (ADR-0018).** Non-billable time has no charge-out value; utilization uses one billable basis in both views; the receivables report ages every posted firm invoice.
- **PM-07 first slice.** `GET /api/ui/engagements/{id}/communications` and an engagement panel list recorded outbound mail: PBC request emails, routed document dispatches and commercial notifications bound to the engagement. Engagement Partners, Managers and Administrators may read it. Message bodies are not projected, and provider acceptance is never worded as delivery.

## What the third slice changed

- **CRM-04.** A proposal can carry an optional timeline. It is part of the creation receipt and is rendered into the quotation and the engagement letter, so a changed timeline is a new revision that needs a fresh acceptance. Migration `ProposalTimeline` adds the column.
- **WF-05.** The work queue can show a loaded page as a list, a board by operational status, or a calendar by due date in Sunday-start weeks. The arrangements regroup the same tasks and offer no drag between columns.
- **WF-03.** The engagement dashboard shows an expandable checklist per phase. Task progress is stated beside the phase's gate status, and a phase with every task done and a blocked gate says so.

No lifecycle rule, gate, stage mapping or module boundary changed. The eleven-stage projection and the seven-phase mapper were not edited.

## Stories

| Story | Disposition | Note |
| --- | --- | --- |
| PM-00 | PARTIAL | This crosswalk, and the scenario map below. NR-11 and NR-12 have no single named test. |
| PM-01 | PARTIAL | Named staff, countdown and phase checklists are delivered. Per-phase milestone dates and a role-computed next action remain. |
| PM-02 | PARTIAL | Board and calendar views are delivered. Editable task groups and dependency authoring outside a template remain. |
| PM-03 | OWNER_DECISION | DEC-01 and DEC-03 gate the recurrence action. |
| PM-04 | PARTIAL | Reminder rule administration and escalation remain. |
| PM-05 | OWNER_DECISION | The quotation timeline is delivered. DEC-01, DEC-02 and DEC-09 gate the rest. |
| PM-06 | PARTIAL | Local behaviour present; live journeys are BLOCKED_EXTERNAL. |
| PM-07 | PARTIAL | The engagement communication timeline is built. Comments, mentions and a connected inbox are not. |
| PM-08 | PARTIAL | Aliases, non-billable valuation and the utilization basis are closed. Staff-level views wait on DEC-05. |
| PM-09 | PARTIAL | 50/50 flow present; partial-payment receipts wait on DEC-06. |
| PM-10 | PARTIAL | Monthly series and receivables scope are closed. Client, service and team splits wait on DEC-05. |
| PM-11 | OWNER_DECISION | DEC-07 gates assignee, sampling and the estimates register. |
| PM-12 | NOT_REVERIFIED | The R2R task cards were not reconciled in this slice. |
| PM-13 | OWNER_DECISION | DEC-02 and DEC-08; provider acceptance is BLOCKED_EXTERNAL. |
| PM-14 | BLOCKED_EXTERNAL | Live tenant, custody and drill evidence. |
| PM-15 | OPTIONAL | DEC-11. |
| PM-16 | PARTIAL | No keyboard or screen-reader journey was executed. |
| PM-17 | OPTIONAL | DEC-11. No decision recorded. |

## Candidate gaps from the prior review

| Gap | Disposition | Basis | Note |
| --- | --- | --- | --- |
| CRM-01 | PARTIAL | R | Parent, subsidiary and affiliate kinds exist; no separate holding kind and tree depth is unverified. |
| CRM-02 | OWNER_DECISION | R | DEC-09. Contact role is free text and routing precedence is undecided. |
| CRM-03 | PARTIAL | N | Per-recipient delivery receipts exist for commercial notifications; routing to purpose contacts waits on DEC-09. |
| CRM-04 | ALREADY_COMPLETE | C | Closed: an optional proposal timeline, rendered on the quotation and the letter and bound into the offer identity. An approved one or two page layout is still DEC-02. |
| CRM-05 | OWNER_DECISION | N | DEC-02. Proposal format and CV selection need an approved layout. |
| CRM-06 | OWNER_DECISION | R | DEC-01. The Key 1 exact-revision rule when no brief quotation exists. |
| CRM-07 | OWNER_DECISION | R | DEC-02. Letter clauses and deadline wording. |
| CRM-08 | PARTIAL | R | Local lifecycle present; live mail acceptance is BLOCKED_EXTERNAL. |
| CRM-09 | PARTIAL | R | Lead channel and minimum data are validated; the lead-level pipeline view was not re-read. |
| CRM-10 | OPTIONAL | - | Needs a scope decision. |
| CRM-11 | OPTIONAL | - | Needs a scope decision; ADR-0017 keeps channels as labels. |
| CRM-12 | BLOCKED_EXTERNAL | R | Live invitation, revocation and site provisioning. |
| WF-01 | PARTIAL | R | WorkflowExperience stays off until operationally accepted. |
| WF-02 | PARTIAL | C | This slice adds the named staffed users and the countdown. A per-phase milestone date and a role-computed next action remain open. |
| WF-03 | ALREADY_COMPLETE | C | Closed: expandable per-phase checklists on the engagement dashboard, from the engagement's operational tasks. |
| WF-04 | PARTIAL | R | Template-derived dependencies exist; editable groups and ad hoc dependency authoring do not. |
| WF-05 | ALREADY_COMPLETE | C | Closed: board and calendar arrangements of the work queue, over the same loaded tasks as the list. |
| WF-06 | PARTIAL | R | Queues and review inbox exist; procedure-level assignee waits on DEC-07. |
| WF-07 | MISSING | R | No preview, approve, create recurrence action. |
| WF-08 | OWNER_DECISION | R | DEC-01 and DEC-03. The annual offer-period guard is in place; acceptance expiry is undecided. |
| WF-09 | PARTIAL | N | PBC reminders exist and are off by default; rule administration was not re-read. |
| WF-10 | MISSING | N | No general task automation. PM-04 limits it to reminders and drafts. |
| WF-11 | PARTIAL | R | Task event trace exists; one stream across requests, notes and mail does not. |
| WF-12 | OPTIONAL | - | Needs a scope decision. |
| WF-13 | PARTIAL | R | See the section 5 rows of the register; ADR-0004 governs. |
| COM-01 | OPTIONAL | C | DEC-11 for a connected inbox. The mandatory first slice of PM-07 is built: an engagement timeline of recorded outbound mail. |
| COM-02 | OPTIONAL | - | Needs a scope decision. |
| COM-03 | PARTIAL | N | Rejection reasons exist on PBC review; per-request conversation was not re-read. |
| COM-04 | BLOCKED_EXTERNAL | R | Live mail, invitation, holding letter and delivery. |
| COM-05 | PARTIAL | N | Polling only. Push transport needs an architecture decision. |
| COM-06 | OPTIONAL | - | Needs a scope decision. |
| COM-07 | OPTIONAL | - | Needs a scope decision. |
| COM-08 | BLOCKED_EXTERNAL | R | Provider isolation and exact hash receipt in a live tenant. |
| COM-09 | BLOCKED_EXTERNAL | R | Release checkpoint read-back and provider archive protection. |
| COM-10 | PARTIAL | N | Capability-level audit records exist; one unified record does not. |
| COM-11 | OPTIONAL | - | Needs a scope decision. |
| COM-12 | OUT_OF_SCOPE | - | AGENTS.md excludes external eSignature providers. |
| TIME-01 | ALREADY_COMPLETE | C | Closed in this slice: time capture, budgets, budget previews and quotations share one alias-aware rate lookup. |
| TIME-02 | ALREADY_COMPLETE | C | Closed under ADR-0018: non-billable and no-charge time carries hours and cost and no charge-out value. |
| TIME-03 | ALREADY_COMPLETE | C | Closed under ADR-0018: the weekly grid and department analytics both use approved billable time over capacity. |
| TIME-04 | OWNER_DECISION | R | DEC-05. Measures and audience for staff-level views. |
| TIME-05 | OPTIONAL | R | Leave and holidays already reduce capacity as recorded unavailability (ADR-0017); a holiday table is optional. |
| TIME-06 | OPTIONAL | - | Needs a scope decision. |
| TIME-07 | NOT_REVERIFIED | N | Carried from the prior review. |
| FIN-01 | DEVIATION | R | ADR-0007: drafts only and off by default. Enabling it is an owner action. |
| FIN-02 | OWNER_DECISION | N | DEC-06. |
| FIN-03 | ALREADY_COMPLETE | C | Closed under ADR-0018: every posted firm invoice is aged, typed as advance, final, time or other. |
| FIN-04 | PARTIAL | C | Value, realization and contribution no longer fail on non-billable time (ADR-0018). Per-client and per-team splits wait on DEC-05. |
| FIN-05 | ALREADY_COMPLETE | C | Closed in this slice: month-by-month series with a same-month prior-year comparator, on screen and in the export. |
| FIN-06 | OWNER_DECISION | R | DEC-05. |
| FIN-07 | OPTIONAL | - | Needs a business decision. |
| FIN-08 | OUT_OF_SCOPE | - | AGENTS.md excludes payment initiation. |
| FIN-09 | OPTIONAL | - | Needs a scope decision. |
| FIN-10 | OPTIONAL | - | Needs a scope decision. |
| FIN-11 | OWNER_DECISION | R | DEC-04. |
| ACC-01 | NOT_REVERIFIED | N | Carried from the prior review. |
| ACC-02 | NOT_REVERIFIED | N | Carried from the prior review. |
| ACC-03 | OWNER_DECISION | N | DEC-04. |
| ACC-04 | NOT_REVERIFIED | N | Carried from the prior review. |
| ACC-05 | PARTIAL | N | Confirmation alternatives wait on DEC-07. |
| ACC-06 | PARTIAL | R | Phase workbench links exist; procedure assignee waits on DEC-07. |
| ACC-07 | OWNER_DECISION | N | DEC-07. Sampling method. |
| ACC-08 | NOT_REVERIFIED | N | Carried from the prior review. |
| ACC-09 | OWNER_DECISION | N | DEC-07. |
| ACC-10 | OWNER_DECISION | R | DEC-07. ADR-0017 records a critical-estimate flag; the register and SRM section are undecided. |
| ACC-11 | OWNER_DECISION | R | DEC-02. |
| ACC-12 | NOT_REVERIFIED | N | Carried from the prior review. |
| ACC-13 | NOT_REVERIFIED | N | Carried from the prior review. |
| ACC-14 | PARTIAL | R | See the section 6.5 rows of the register. |
| INT-01 | OPTIONAL | - | DEC-11. |
| INT-02 | OPTIONAL | - | DEC-11. |
| INT-03 | OPTIONAL | - | DEC-11. |
| INT-04 | OPTIONAL | - | DEC-11. |
| INT-05 | OPTIONAL | - | DEC-11. |
| INT-06 | OPTIONAL | - | DEC-11. |
| CLOUD-01 | PARTIAL | N | The Blazor retirement gate remains NOT_READY. |
| CLOUD-02 | BLOCKED_EXTERNAL | R | Live Entra journeys. |
| CLOUD-03 | BLOCKED_EXTERNAL | R | Live selected-site grants. |
| CLOUD-04 | BLOCKED_EXTERNAL | R | Checkpoint and archive custody. |
| CLOUD-05 | BLOCKED_EXTERNAL | N | Backup custody and restore drill. |
| CLOUD-06 | BLOCKED_EXTERNAL | N | Secrets custody, collector and alerts. |
| CLOUD-07 | PARTIAL | C | No full-suite run exists for the commit that carries this record; see status.json. |
| CLOUD-08 | BLOCKED_EXTERNAL | N | DEC-10 targets. |
| CLOUD-09 | PARTIAL | N | Whole-application isolation review is incomplete. |
| CLOUD-10 | BLOCKED_EXTERNAL | N | Branch protection, handover and pilot. |
| CLOUD-11 | OPTIONAL | - | DEC-11. |
| CLOUD-12 | OPTIONAL | - | DEC-11. |
| CLOUD-13 | OPTIONAL | - | DEC-11. |

## Non-regression scenarios mapped to existing tests

Names are tests in `tests/AuditSphereOps.Domain.Tests`. A named test covers the core of the scenario; it was located by name and its body was not re-read for every clause, so treat a row as a pointer and not as proof of full coverage.

| Scenario | Existing tests | Gap |
| --- | --- | --- |
| NR-01 no commercial gate bypass | `EngagementLifecycleQueryTests.AdvanceBillingNeverAdvancesOnStaleConditionalOrDeclinedEvidence`; `CommercialWorkflowTests.FeeAgreement_RefusesAcceptedProposalWithoutRecordedDispatch` | A workflow-automator actor is not exercised. |
| NR-02 advance paid is distinct | `EngagementActivationAdvanceGateTests.LinkedAgreementMustHavePaidAdvanceBeforeActivation`, `.ActivationWithoutLinkedFeeAgreementIsBlocked` | None found. |
| NR-03 effective fieldwork | `PlanningResourcesAndMaterialityTests.Materiality_IsCalculatedFromTheMappedTrialBalance_AndGoesStaleWhenTheSourceIsReplaced` | Task completion against a stale gate is not named. |
| NR-04 reviewer independence | `PlanningResourcesAndMaterialityTests.AuditProcedure_ExecutionAndReviewStaffingRanks_EnforcesRiskBandRules`; `AuditDeliverablesTests.ChangesRequiredReviewCreatesOneLinkedOperationalReworkTaskWithoutChangingReviewAuthority` | None found. |
| NR-05 confirmations and opinions | `AuditDeliverablesTests.CriticalUnreturnedConfirmationKeepsBlockingAfterAlternativeClosureAndCriticalityReassessment` | None found. |
| NR-06 release bytes | `AuditDeliverablesTests.FivePartBundle_RequiresReviewedReleaseAndPostedBalance_IsIdempotent_AndClientScoped`; `ReleaseTests.ReleaseGate_RejectsMissingCheckpointAndWrongManifest_ThenIsIdempotent` | A live checkpoint is BLOCKED_EXTERNAL. |
| NR-07 archive | `AuditDeliverablesTests.FileFreezesSixtyDaysAfterSigning_RefusesWrites_AmendsWithApproval_AndTracesActivity`, `.FrozenFileRefusesReviewNoteAttemptsAndRecordsTheRefusedWrite` | Provider protection is BLOCKED_EXTERNAL. |
| NR-08 tenant and sibling isolation | `ClientProfileWorkspaceTests.ClientCommercialAuthorityDoesNotWidenEngagementRoleOrAggregateSiblingData`; `GlobalSearchQueryTests.ClientScopedUser_FindsOwnClientEngagementAndRequest_NeverTheSibling`; `EngagementCommunicationTimelineTests` | Whole-application coverage is CLOUD-09. |
| NR-09 recurring year | `EngagementLifecycleQueryTests.PriorPeriodAcceptedOfferDoesNotAdvanceANewPeriodEngagement` | No recurrence action exists to test. |
| NR-10 durable retries | `PbcTests.MailWorker_DeliversQueuedNotification_ExactlyOnce`; `EngagementActivationReviewTests.ExactReviewConcurrentRecoveryAndAppendOnlyEvidenceActivateOnce` | None found. |
| NR-11 three books | No single named test was located. `FirmReceivablesAgingQueryTests` shows firm receivables read firm invoices only. | A named cross-ledger negative test is missing. |
| NR-12 feature-off compatibility | `PbcWorkflowTaskTests.DisabledWorkflowDoesNotCreatePbcFollowUpTask` | One flag only; no general flag-off suite. |

## What can be built next without a decision

- **NR-11 and NR-12:** a named cross-ledger negative test and a flag-off suite.
- **PM-01 remainder:** a per-phase milestone date and a role-computed next action.

Everything else that is open waits on a decision listed as not decided in ADR-0018, on live infrastructure, or on a scope choice. Those are not development defects.
