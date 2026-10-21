# Practice Management v2.2 — Story and Candidate-Gap Crosswalk

**Status: CURRENT** as a reconciliation of [`auditsphere-execution-user-stories-cloud-practice-management-v22-proposed.md`](auditsphere-execution-user-stories-cloud-practice-management-v22-proposed.md) against the code. It is the PM-00 baseline. It is not a functional-testing claim and it closes no external gate. The expanded 87-criterion specification has its own qualified ASPM keys and source audit; see the [criterion audit](auditsphere-execution-report-practice-management-criterion-audit-current.md).

**Baseline:** the original PM v2.2 candidate-gap review was `master` at `733ab239`. The expanded-spec source audit is against `master` at `a6e160c3fdc6b2abed9631340d0f48d7453d700e`; its CI and inspection limits are recorded in the linked audit and [`status.json`](status.json).

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
| PM-00 | PARTIAL | This crosswalk and scenario map; NR-11 has explicit firm/client/consolidation source-book assertions. The NR-12 matrix below maps five v2.2/host switches to off-path evidence, including an Angular 403-state spec; the work-queue feature-off route is browser-verified in an owned Test instance; deployed configuration and remaining switches remain unverified. |
| PM-01 | PARTIAL | Named staff, countdown, phase checklists, operational task dates and saved Audit Plan milestones mapped to phases are delivered. A saved statutory cutoff is displayed as user-entered plan data; approved/versioned jurisdictional source mapping, legal validation and broad browser acceptance remain. |
| PM-02 | PARTIAL | Digest-bound task groups and task queues retain their pinned template source. Reviewed task-edge amendments enforce scope, revisions and replay while preserving original template edges. The focused plan journey completes every task yet activation remains blocked, with no activation/materiality/opinion/release/freeze records created. Template-declared materiality/PBC/procedure prerequisites now pin exact source bindings and immutable origin, with focused scope/replay and browser evidence. Broader role and release acceptance remains open; see `status.json` under `workflowTemplateProfessionalBoundary` and `templateEvidencePrerequisites20261020`. |
| PM-03 | OWNER_DECISION | DEC-01 and DEC-03 gate the recurrence action. |
| PM-04 | PARTIAL | Versioned reminder administration, independent review and finite schedule windows are implemented behind existing flags. Broader escalation and release/live acceptance remain. |
| PM-05 | OWNER_DECISION | The timeline is required for new brief quotations; early drafts and historical artifact recovery remain supported. DEC-01, DEC-02 and DEC-09 gate the rest. |
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
| PM-16 | PARTIAL | Focused keyboard, dialog, live-region, responsive and screen-reader-proxy checks are recorded in `status.json` under `accessibilityRenderedSweep20261020`. These technical checks do not replace independent human screen-reader acceptance, which remains pending. |
| PM-17 | OPTIONAL | DEC-11. No decision recorded. |

## Candidate gaps from the prior review

This table retains the **older PM v2.2 candidate-gap namespace and scope** for historical traceability. Its bare IDs and `ALREADY_COMPLETE` dispositions are not status for the new `ASPM-*` requirements. Several same-numbered IDs have different meanings; use the expanded-spec audit matrix below for those criteria.

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
| WF-02 | PARTIAL | C | The engagement view shows named staffed users, countdown, role-computed next action, operational task dates and explicitly saved Audit Plan milestones by phase. The plan's statutory cutoff is not independently validated against law; approved source mapping and broad browser acceptance remain open. |
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
| CLOUD-01 | PARTIAL | N | The Blazor host is retired and merged as `e7ebe27c`; parity and operational acceptance evidence remain partial. |
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
| NR-11 three books — **COMPLETE** | `LedgerTests.LedgerWorkflow_IsBalancedIdempotentAndImmutable` proves firm posting leaves client native journals/imported GL untouched; `FirmOperationsTests.Analytics_ReconcileBilledCollectedStandardValueAndCost_FromApprovedRecords` asserts a posted firm invoice leaves client books unchanged; `ClientAccountingTests.SalesInvoiceIndependentPostIsAtomicScopedRecoverableAndReconcilesWithNativeGL` proves client invoice posting creates no firm journal/posting and leaves firm analytics unchanged; `ClientAccountingTests.GroupWorkflow_UsesApprovedComponentPackagesWithoutMutatingThem` compares component TB/GL/native-journal source manifest and approved package manifest before and after the group run. | All named firm-invoice, client-source-invoice, reporting-aggregate and consolidation source-book boundaries have focused regression assertions. |
| NR-12 feature-off compatibility | See the scoped matrix immediately below. | Five workflow/host switches are mapped to existing API/worker tests and explicit uncovered UI/configuration surfaces; NR-12 remains partial. |

### NR-12 scoped feature-off compatibility matrix

Scope: the v2.2 workflow and host switches `WorkflowExperience`, `PbcReminders`, `ClientSites`, `AutomaticFeeInvoices`, and `AngularUi`. These rows map existing evidence; they do not claim that the complete matrix has been dynamically rerun on the current commit.

| Switch and default | Off-path compatibility | Existing focused evidence | Remaining verification |
| --- | --- | --- | --- |
| `WorkflowExperience:Enabled` — `false` | Work queue/review/reminder APIs deny with the feature-off reason; nested task, dependency, evidence and receipt routes are not found. Sending an ordinary PBC request remains a PBC workflow and does not create a task binding. | `WorkflowDashboardApiTests.DisabledExperienceKeepsTheExistingDenialAndConfigurationUnchanged` checks unauthenticated denial, stable configuration and unchanged side-effect fingerprint; `WorkflowEvidenceApiTests.DisabledExperienceDoesNotExposePrerequisiteOrReceiptRoutes`; `WorkflowDependencyApiTests.DisabledExperienceDoesNotExposePrerequisiteOrReceiptRoutes`; `EngagementDeadlineReminderApiTests.DisabledWorkflowExperienceReturnsTheQueueScopeDenial`; `PbcWorkflowTaskTests.DisabledWorkflowDoesNotCreatePbcFollowUpTask`; Angular `work-queue-display.spec.ts` verifies a 403 shows the unavailable explanation and no summary/task cards. | Built-in browser on source `c0174b84`, owned Test API with `WorkflowExperience__Enabled=false`: My tasks, Team and review inbox returned HTTP 403; unavailable messages, no task rows/actions and no console errors. No writes. Deep accessibility fixture passed 1/1. Live deployment configuration remains unverified; see `workflowFeatureOffBrowser20261020` in `status.json`. |
| `PbcReminders:Enabled` — `false` | Worker discovery queues no reminder. Request state, reminder receipts, reminder communications and durable mail operations remain unchanged. | `PbcReminderTests.DisabledReminderPolicyLeavesRequestHistoryAndOutboxUnchanged`. | No reminder-administration UI exists in this slice; verify the configured worker state in the live acceptance environment separately. |
| `ClientSites:Enabled` — `true` by default | When explicitly off, discovery enqueues no site operation, creates no site row, and leaves client and existing workspace identity/revision/remote IDs unchanged. The rollout boundary is a separate fail-closed requirement; disabled provisioning does not authorize historical migration. | `ClientSharePointSitesTests.DisabledPolicy_DoesNotEnqueueOrChangeExistingClientRepositories`; `EnabledPolicyWithoutExplicitBoundary_FailsClosedWithoutTreatingHistoryAsEligible`. | API/worker effective configuration and administration status/retry UI are not jointly exercised with the switch off; exact-site live provisioning remains external. |
| `AutomaticFeeInvoices:Enabled` — `false` | API reports `PendingAutomationDisabled` and the manual finance route; worker discovery enqueues no draft. Automation only prepares a draft when enabled; submission, approval and posting remain separate human-controlled actions. | `FeeAgreementConfigurationTests.AutomaticFeeInvoicesFlagIsReadAndFailsClosed`; `FeeAgreementConfigurationTests.EveryHostDeclaresTheSameAutomaticFeeInvoicesPolicy`; `CommercialWorkflowTests.OneClickDocuments_AreBrandedImmutableAndBoundToTheApprovedQuotation` checks the disabled worker enqueue count and pending state before the enabled path. | No authenticated browser journey has verified the disabled-state presentation against the current deployment configuration. |
| `AngularUi:Enabled` / `AngularUi:CanonicalRoutes` — `true` / `true` | Canonical mode with Angular disabled refuses API startup; missing or incompatible assets also refuse startup. The host does not silently route canonical Angular pages to a retired presentation host. | `CanonicalAngularHostTests.CanonicalRoutesRejectMissingOrIncompatibleAssetsAtStartup`. | Disabled-mode route rendering has no browser journey; the supported canonical deployment remains enabled. |

The global `ExternalEffects:Enabled`, live release gates, and independent Microsoft capability/consent switches are not NR-12 workflow flags. Their activation, permissions and external side effects remain governed by the release-safety and M365 permission matrices; this table does not certify them.

## What can be built next without a decision

- **NR-12 remainder:** matrix documented and work-queue 403 display pinned by the Angular spec. The work-queue route is now browser-verified with the switch explicitly off. Verify effective configuration and presentation for the remaining UI-controlled switches where acceptance infrastructure permits; add only tests for demonstrated gaps. Do not create a generic all-flags test or duplicate covered API/worker behavior.
- **PM-01 remainder:** the seven-phase UI now displays only explicitly saved Audit Plan dates under their corresponding phases, separately from operational task deadlines. A statutory cutoff is shown as saved plan data and is not legal validation. Obtain the approved, versioned jurisdictional source mapping and complete broader role, responsive and assistive-technology acceptance.

Everything else that is open waits on a decision listed as not decided in ADR-0018, on live infrastructure, or on a scope choice. Those are not development defects.

## Complete-product specification inventory

The [complete-product specification](../requirements/auditsphere-requirements-specification-complete-practice-management-proposed.md) extends this crosswalk. The earlier next-work conclusion above describes the older scope; it does not classify the expanded product requirements. PMCOMP-00's source reconciliation is recorded as **AUDITED_WITH_OPEN_ACCEPTANCE**. This is not a product completion claim. Exact source identity and current verification belong to `status.json` under `completePracticeManagement`.

### PMCOMP-00 micro-spec

- Intent: reconcile the supplied product criteria with existing services and original PM/STE/R2R/CP identities before feature changes.
- Authority: current requirements and approved ADRs remain controlling. The supplied proposed document is authorized as implementation scope, but does not supply professional, contractual or live-provider approvals.
- Write scope: this crosswalk, the preserved supplied specification, documentation index and execution ledger. No runtime, schema, provider or production changes in this inventory slice.
- Acceptance: inspect every criterion against its owning command/query, existing test body and applicable original task; record actual gaps and decision/external gates independently. Candidate anchors below are discovery starting points, not proof that the whole criterion is implemented.
- Verification: documentation links/naming and diff checks only. Existing application evidence is retained separately; full current-source release acceptance remains CI/CD.

### Existing discovery anchors

| Family | Existing Application owner | Existing test cohort | Product packages |
| --- | --- | --- | --- |
| CRM | [PracticeCrmService.cs](../../src/AuditSphereOps.Application/Practice/PracticeCrmService.cs) | [PracticeCrmTests.cs](../../tests/AuditSphereOps.Domain.Tests/PracticeCrmTests.cs) | PMCOMP-03 |
| COM | [FeeAgreementService.cs](../../src/AuditSphereOps.Application/Practice/FeeAgreementService.cs) | [BillingTests.cs](../../tests/AuditSphereOps.Domain.Tests/BillingTests.cs) | PMCOMP-04 |
| WF | [EngagementWorkflowTaskService.cs](../../src/AuditSphereOps.Application/Practice/EngagementWorkflowTaskService.cs) | [EngagementWorkflowTaskTests.cs](../../tests/AuditSphereOps.Domain.Tests/EngagementWorkflowTaskTests.cs) | PMCOMP-05/06/07 |
| CLIENT | [ClientPortalService.cs](../../src/AuditSphereOps.Application/Documents/ClientPortalService.cs) | [ClientPortalOnboardingTests.cs](../../tests/AuditSphereOps.Domain.Tests/ClientPortalOnboardingTests.cs) | PMCOMP-08/09 |
| RES | [PracticeTimeService.cs](../../src/AuditSphereOps.Application/Practice/PracticeTimeService.cs) | [PracticeTimeTests.cs](../../tests/AuditSphereOps.Domain.Tests/PracticeTimeTests.cs) | PMCOMP-10 |
| FIN | [FirmReceivablesAgingQuery.cs](../../src/AuditSphereOps.Application/Practice/FirmReceivablesAgingQuery.cs) | [FirmReceivablesAgingQueryTests.cs](../../tests/AuditSphereOps.Domain.Tests/FirmReceivablesAgingQueryTests.cs) | PMCOMP-11/12 |
| ACC | [FinancialStatementCalculator.cs](../../src/AuditSphereOps.Application/Accounting/FinancialStatementCalculator.cs) | [FinancialStatementTests.cs](../../tests/AuditSphereOps.Domain.Tests/FinancialStatementTests.cs) | PMCOMP-13/16 |
| AUD | [FileFreezeService.cs](../../src/AuditSphereOps.Application/Records/FileFreezeService.cs) | [AuditDeliverablesTests.cs](../../tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs) | PMCOMP-14/15 |
| UX | [EngagementWorkflowWorkspaceQuery.cs](../../src/AuditSphereOps.Application/Acceptance/EngagementWorkflowWorkspaceQuery.cs) | [EngagementLifecycleQueryTests.cs](../../tests/AuditSphereOps.Domain.Tests/EngagementLifecycleQueryTests.cs) | PMCOMP-02/17 |
| UAT | [EngagementLifecycleQuery.cs](../../src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs) | [EngagementLifecycleQueryTests.cs](../../tests/AuditSphereOps.Domain.Tests/EngagementLifecycleQueryTests.cs) | PMCOMP-19 |
| NFR | [FileFreezeService.cs](../../src/AuditSphereOps.Application/Records/FileFreezeService.cs) | [ReleaseTests.cs](../../tests/AuditSphereOps.Domain.Tests/ReleaseTests.cs) | PMCOMP-18/19 |

These paths were checked to exist. The test cohorts are not blanket evidence for every row: inspect the named test bodies and add only a missing behavior assertion where necessary. `NOT_REVERIFIED` means the criterion has not yet undergone the required current-code audit, not that the feature is absent.

### Criterion audit queue

Every expanded requirement is now prefixed with `ASPM-` in the [current 87-row evidence matrix](auditsphere-execution-report-practice-management-criterion-audit-current.md). The matrix records code state, inspected source/test bodies, residual blockers, and whether a legacy task is an exact alias or only a package-level trace. Test-body inspection is not a test pass.

### Separate acceptance gates

D1 and DEC-01 through DEC-10 require their specified human or live evidence where applicable. Existing approved ADRs are reused. PM-NATIVE and PM-SAAS are conditional release claims; this task does not enable them. PMCOMP-20 remains conditional on DEC-11. The illustrative monthly fee is not a firm tariff, and no monthly cadence or new fee policy is approved by this inventory. The original API/Angular parity backlog remains active in its own tracker; duplicate implementation is not requested.

### Initial inspected criterion evidence

The notes below preserve evidence from earlier implementation slices. Where they disagree with the current expanded-criteria audit, use the [current evidence matrix](auditsphere-execution-report-practice-management-criterion-audit-current.md); do not reinterpret older same-numbered PM IDs as ASPM IDs.

- **CRM-01 / PMCOMP-03:** `PracticeCrmService.CreateLeadAsync` retains canonical channel/owner and request identity; `ConvertToClientDraftAsync` binds conversion. Existing `PracticeCrmTests.CommercialWorkflow_ConvertsIdempotently_AndLeavesAcceptancePending` asserts one client, no engagement, pending clearance, awaiting-acceptance portal intent and no client role grant. Its body was inspected; it was not executed in this documentation slice. The entire lead action-history and invalid-stage browser criterion remains unverified.
- **CRM-05 / PMCOMP-01/02:** `EngagementLifecycleService.CreateDraftAsync` stores route/profile and creates a Draft with `ProfessionalWorkBlocked = true`, serialized against client safety and duplicate service-period creation. This supplies a blocked shell; it does not establish an approved accounting-only service policy, review or completion profile. Those remain separate requirements.
- **CRM-03 / PM-05:** `PracticeCrmService.Routing.cs` filters purpose routes by firm/client, revocation, effective dates and active contact; it refuses absent/ambiguous routes and retains recipient identity in dispatch records. On `395d2ef0`, `CommercialMailDeliveryHandler` also rechecks the frozen prospect email or exact client commercial route immediately before sending a proposal. Two focused PostgreSQL worker tests pass: changed prospect email and revoked client route leave the notification queued and block the operation before contacting the provider. DEC-09 precedence, fee receipt/voucher and holding-letter send guards, initiating-staff reauthorization, live provider acceptance and the integrated journey remain open; CRM-03 is partial.
- **NFR-05/06/08 / PM-14/16:** the current NFR authority explicitly leaves HTTP p95/concurrency targets, recovery goals and supported browser/locale/assistive-technology acceptance open. No numeric target or accessibility certification is invented here. See [current NFR authority](../architecture/auditsphere-architecture-policy-non-functional-requirements-current.md#5-availability-performance-and-recovery--open).
- **UAT-15 / PM-14:** production-like recovery, live exact-site readback, certificate/checkpoint and independent operator evidence are required separately. Local fixture tests do not close this gate.

Next local inventory slice: inspect the protected audit commercial activation fixture and task-plan/service-profile boundaries, then reconcile the remaining criterion bodies and original task aliases. Preserve the original broader parity work and its exact-source release runs.

### COM-03 / FIN-02 residual micro-spec: reversal settlement truth

- Existing behavior: fee payment marks a milestone PAID only after full allocation; approved receipt-allocation reversal reduces effective applied cash but leaves that mutable settlement projection PAID. Activation/readiness consumes that projection. This is an existing approved audit advance invariant, independent of proposed monthly fee policies.
- Required change: preserve the database-enforced immutable PAID milestone, receipt pointer and paid date. Require a posted, same-client/currency invoice and current net allocations alongside the retained PAID marker; subtract independently approved reversals. A paid flag alone is never proof of current settlement. Use one shared predicate for activation, onboarding, portal eligibility, downstream billing and bundle gates. The untracked staff read projects INVOICED while currently short; no stored history moves backwards. Pending/rejected reversals change nothing. An initial backward-update prototype was refused by the existing trigger and removed; the trigger is preserved.
- Paths: `FeeAgreementService.Settlement.cs`, the existing settlement writer and its downstream gate readers; extend `CommercialWorkflowTests.AgreedFee_AdvanceReceiptEmailAndBalance_FollowTheFiftyFiftyCycleOnceEach`; run the existing receivables reversal cohort too. No schema, provider, fee-policy or autonomous posting change.
- Independent arithmetic: a fully allocated advance of 12,500 less an approved 100 reversal is 12,400 applied and 100 outstanding. New actual 100 allocation can restore paid state; replay cannot subtract twice. The existing illustrative 20,000/4,000/6,000 complete-product journey remains separate acceptance work.
- Acceptance: pending/rejected preserve current settlement; approved reversal makes readiness unpaid while stored PAID evidence remains intact; original source documents remain byte-identical; same review replay is idempotent; authorized new payment restores current settlement. A controlled firm-lock race queues reversal before a replay holding stale paid evidence; replay must observe the reversed balance.

Settlement publication also reacquires the existing firm fence after receipt allocation and rechecks current authority and cash before its first historical PAID transition. Later receipts never replace the original immutable settlement identity. Credits remain AR adjustments, not cash satisfying the mandatory advance. The regression distinguishes a zero AR balance after credit from an outstanding cash advance.

### Additional criterion inspection

- **COM-03 / FIN-02:** the existing commercial workflow has actual invoice review/posting, split cash allocations, replay/conflict bounds and retained payment vouchers. The current residual is the reversal-adjusted settlement predicate described above; the stored milestone is deliberately historical. The supplied QAR 20,000 / 4,000 / 6,000 complete-product journey and all service-policy decisions remain unverified.
- **WF-01 / PMCOMP-01/06:** `EngagementWorkflowPlanService.TemplatesAsync` offers only published, unarchived matching-service-route revisions. Preview refuses a different route. `EngagementWorkflowPlanTests.PlanPinsPublishedTemplateAndSchedule_AndRecoversRetryWithoutDuplicateTasks` is the existing starting cohort. A reviewed service-profile/cadence/template policy is not established by route matching alone; do not enable a new monthly profile from this source inspection.
- **UX-01 / PMCOMP-02 / PM-01:** `EngagementWorkflowWorkspaceQuery.GetAsync` projects the authorized lifecycle, seven display phases, blockers, responsible users and a canonical internal next-action link. The phase is read-only. It remains an audit-lifecycle projection; approved accounting-only completion and role-specific next-action acceptance remain separate work.

The shared activation fixture now supplies actual posted invoice, receipt and allocation inputs instead of random financial IDs. A focused negative/positive activation scenario uses the specified QAR 20,000 fee and QAR 4,000 then QAR 6,000 allocations; it does not claim the entire lead-to-release UAT journey.

### WF-06 / PMCOMP-17 micro-spec: visible blocked prerequisites

- Existing source: `WorkflowTaskQueueQuery` evaluates unmet task dependencies, current evidence requirements and the linked PBC state before deriving completion eligibility. The Angular list and shared board/calendar card display raw operational status and prerequisite counts but no explicit blocked-readiness label.
- Change scope: `work-queue.ts` and its existing component spec. Render a separate BLOCKED prerequisite label for active OPEN/IN_PROGRESS tasks with unmet task/evidence counts or a linked PBC that is not accepted/closed. Use the same presentation helper in all layouts. Current grants, queue membership, persisted task status and server command authority are not changed. No global blocked-count claim is introduced.
- Acceptance: task/evidence/PBC prerequisites display blocked consistently; missing PBC state fails closed; current accepted/closed PBC clears that blocker; missing viewer action permission alone does not imply a workflow blocker; completed task history stays completed. One existing component case checks all three layouts and independent boundaries. Run the affected spec/build once after the slice and inspect the built-in browser.

Verification: the affected Angular component cohort passes and both Angular and API Release builds pass. Built-in browser inspection reached the canonical engagement and manual-task form on the isolated synthetic browser database. Task creation was refused with “Client safety state is unavailable.” The safety guard remains intact; dependency creation/completion and responsive browser acceptance are NOT_VERIFIED. This slice adds readiness presentation only; WF-06 and complete-product acceptance remain partial. Exact commands/results are recorded in the execution ledger.

Follow-up browser evidence: repaired the direct SQL fixture’s omitted client safety rows using the normal initial generation and conflict-safe insertion. Through the built-in browser, created/assigned two synthetic tasks, reviewed their exact dependency, observed BLOCKED in all three layouts with completion withheld, then completed the predecessor and observed the dependent blocker clear and completion return. Console warning/error capture was empty. No professional approval, provider action or production data changed. Responsive and broader WF-06 acceptance remain unverified.

### WF-03 source identity in the task queue

The queue previously omitted retained binding source identities. The existing grant-scoped projection now carries the source key and exact template revision/step IDs, and list/board/calendar display those references without shortening them. Empty historical source keys are explicitly shown as not recorded. This is read-only lineage disclosure, not a new approval, source reconstruction, or template migration. The broader required-task evidence and next-action acceptance remains partial.

Focused verification: existing scoped PostgreSQL task-lineage test passes; the queue component cohort and Angular/API builds pass. Built-in browser confirms retained manual source identity in list, board and calendar without console warnings/errors. A later focused synthetic journey authors and independently publishes a template, creates the engagement plan, and verifies exact Required/Optional source, revision and step identities in all three layouts, with a manual control task. Built-in board/calendar inspection confirms narrow-screen reflow and no console warnings/errors. Broader workflow and human assistive-technology acceptance remain open. Exact results and source scope are under `workflowTemplateLineage20261021` in the execution ledger.

### WF-02/03 review context across layouts

Board/calendar cards now retain the FSLI, full mapping identity, PBC navigation, reviewer comment and resubmission explanation previously shown only in the list, plus the existing task-activity action. The focused component case checks those facts in every layout. Browser verification opened actual synthetic create/assign/dependency history from the board and retained that panel when switching to calendar. No console warnings/errors were captured. Existing component cohort and Angular build pass; live PBC/review fixtures and responsive acceptance remain unverified. No backend state or professional decision changes.

### FIN-06 monthly reporting boundary correction

Existing firm reporting derives operating revenue/expense from posted firm ledger lines, excludes year-end closing/opening activity from monthly P&L and uses a nullable prior-year comparator when that period is absent. Valid year-0001 and December-9999 periods exposed date arithmetic exceptions. The projection now omits the nonexistent prior year and stops before advancing past its last month. The existing balanced-expense/ledger regression covers both supported boundaries without changing normal-period arithmetic. FIN-06 full acceptance and production data remain separate.

Verification: the existing balanced-expense/ledger/monthly-P&L regression passed with both supported date boundaries. No full local suite, schema, API contract, client ledger or production data change. FIN-03 ageing source and its as-of regression were inspected but not rerun in this slice; that inspection alone is not criterion acceptance.

### FIN-07 monthly report size contract

The UI decoder accepts at most 1,200 monthly P&L rows. The backend now checks the actual first-to-last stored reporting month span before materializing activity and returns `finance.report-limit` above that boundary. Requested dates alone do not trigger refusal: a broad date query with only two stored reporting months still succeeds. Screen and CSV export share the same query. No partial/truncated financial report is returned. The existing ledger regression checks exactly 1,200 months, 1,201 months and an oversized stored span. The existing focused ledger regression passed with all boundary assertions; broader FIN-07 source/export and browser-error acceptance remain open.

### FIN-06/07 retained expense discovery and recovery

The firm-books workspace previously stopped at 200 expenses and recovery searched only that first page. It now exposes bounded live expense pages with deterministic date/creation/ID ordering and continuation metadata. An exact expense selector is scoped to the same firm-wide finance authorization and lets uncertain submit/review/post recovery resolve older identities without relying on page membership. Page validation refuses unsupported values; final authorization is rechecked before returning account/expense metadata. No evidence bytes are added to the response. Live list pages may shift when records are inserted; these are not sealed report snapshots. Focused database, API and Angular checks pass, including older exact-ID recovery; the initial Angular fixture lacked a session and was corrected. Positive browser finance acceptance remains pending because the synthetic identity lacks finance access. Full source-to-report and release gates remain open.

### RES-06 weekly capacity reconciliation

Analytics previously truncated weekly capacity divided by five while the resource grid retained the full weekly total. Both now use the existing resource calculator’s shared daily allocation: whole minutes divide across working days and remainder minutes go to the earliest working days (Sunday first). A 2,401-minute week is 481 + 480 + 480 + 480 + 480; a full 481-minute Sunday absence leaves 1,920 in both views. Full-week leave removes the entire configured capacity. Approved billable minutes remain the utilization numerator; non-billable actual effort stays separate. The bounded analytics day loop also avoids advancing beyond the last supported date. Both focused pure/PostgreSQL tests passed. Positive browser and full current-SHA CI acceptance remain unverified; no personnel audience or professional policy changes.

### RES-01 supported planning ranges

Grid reads and allocation writes now share a bounded complete-week range check. Unsupported minimum/maximum dates and week counts outside 1–12 are refused before date arithmetic; requests are no longer silently clamped. Existing staffing/role fences remain intact and invalid allocation requests retain the prior allocation population. The first and last complete representable weeks remain supported. The resource endpoint documents its bad-request response. Focused Domain and API tests passed, and OpenAPI drift passed. The built-in browser refused an unsupported date and recovered the grid after a valid date was submitted. No planning record changed. Broader staffing/capacity acceptance remains open.

### RES-01 phase/risk allocation attribution

Allocation requests now identify an assignable audit phase and an optional bounded risk-area label. A line is unique by firm, engagement, staff member, normalized Sunday week, phase and risk area, so different planning lines can coexist without overwriting one another. The reviewed preview/hash, durable receipt, API input, EF model and PostgreSQL guard bind the exact tuple; receipt replay remains recoverable before current-shape validation. Existing rows backfill to visible `UNASSIGNED`/no-risk values and continue to count in total capacity; new positive-minute lines cannot use `UNASSIGNED`, and a reviewed zero-minute request removes only its exact phase/risk line. The Angular form requires an explicit phase, normalizes the chosen date to Sunday and displays phase/risk attribution in the saved allocations list. A focused PostgreSQL regression checks two distinct lines, aggregate capacity, exact-line removal and replay; Angular and browser acceptance details are in `status.json` under `targetedVerification.practiceResourcePhaseRisk20261018`. ASPM-RES-01 remains partial for broader range-boundary, role, responsive/accessibility and current release acceptance.

### RES-01 exact recorded availability projection

The resource grid API now returns each saved availability record overlapping its selected complete-week interval, scoped per staff member; Angular displays kind, inclusive dates and minutes per day for LEAVE, TRAINING and PUBLIC_HOLIDAY. No reason text or approval state is inferred, and the existing unavailable-capacity calculation and authorization checks are unchanged. The focused PostgreSQL service, Angular decoder/rendering and API-host browser journey provide local evidence; the source SHA, exact counts and browser-host limitation are recorded in `status.json`. Manual built-in browser acceptance remains blocked by the stale local shell on port 5029. ASPM-RES-01 remains PARTIAL.

### RES-02 certification verification (current)

Certification recording, expiry and independent exact-source verification remain distinct. Review decisions are append-only; staffing candidate and assignment paths use the latest applicable decision, while preserving prior assignment history. A deterministic PostgreSQL interleaving now proves that a committed revocation wins before a waiting, previously reviewed higher-level assignment and writes no new assignment, receipt or grant. The prior implementation packet and early “verification missing” notes below this area described an earlier state; they are superseded by the current criterion row above and `status.json#targetedVerification.staffingCertificationRevocationRace20261018`. RES-02 remains PARTIAL pending broader resource, role, responsive/accessibility and current release acceptance.

### Client-collaboration source audit

Independent read-only inspection used source base `0c023535`; it did not run tests or change providers. Identity/session/first-sign-in and selected-source byte isolation implementations are present, but source inspection does not close current-commit or live acceptance. The highest-priority correction is CLIENT-06: align request write projections, replies and comments with first-sign-in and Active engagement checks, retaining staff and released-bundle acknowledgement semantics. A disjoint implementation worker is assigned; its output still requires review.

CLIENT-02 now records an assigned independent reviewer’s acceptance against the exact provider-verified upload ID, upload revision, request submission revision and content hash. The immutable receipt retains provider identity/digest, reviewer and acceptance time; PostgreSQL guards reject mismatched receipts and later source/decision rewrites. Focused Domain and Angular contract checks, Release build, and OpenAPI drift verification pass. The built-in browser did not open the PBC route for the available session, so positive UI review acceptance remains unverified; live acceptance remains external. CLIENT-04 drops retained correspondence `DispatchedByUserId` from timeline projection; notification actors require tracing their actual retained source rather than guessing. CLIENT-05 lacks a unified client-facing document lifecycle for provider acceptance, proven delivery, withdrawal and supersession. Existing portal acknowledgement is not delivery proof. Live Entra, selected-site read-back and professional/profile closure gates remain separate.

RES-02 persistence implementation adds a new, initially empty review history with exact retained source snapshots, firm/user foreign keys, independent identity constraints, unique request/revision fences and append-only/source/head guards. Existing qualifications are not backfilled as verified. Focused verification passes: two Domain evidence checks and one PostgreSQL persistence check (3 passed, 0 failed), including append-only/source/head refusals, duplicate request refusal and disposable-schema down/up rehearsal. The API Release build has zero warnings/errors and EF reports no pending model changes. No development, browser or hosted database was migrated. Review publication, API/Angular review actions and staffing integration remain unfinished; these checks do not prove administrative authorization or live acceptance.

RES-02 review publication is now implemented in the Application service: current firm-wide Administrator authorization is rechecked under the firm/identity fence, exact-source and expected-head checks reject stale decisions, and a retained request digest permits only identical retries. Reviewers cannot verify their own or their recorded source. Withdrawal appends a new head without rewriting prior evidence. The Application Release build passes. The existing persistence test passed; the new publication test first timed out acquiring the shared test-template advisory lock, then passed when rerun alone. It covers independent administrator publication, exact retry identity, changed-intent refusal, stale-head refusal, non-administrator denial and retained withdrawal history. API/Angular adapters, staffing eligibility integration, concurrency/revocation ordering and browser acceptance were subsequently implemented or verified as recorded below and in `status.json`.

RES-02 bounded review workspace now exposes the exact source, latest retained decision, revision, independent review permission and applicability separately from expiry. The focused publication/workspace test passes (1 passed, 0 failed), covering verified then withdrawn projection and client-identity denial. Application build passes; API/Angular adapters and staffing integration are still required.

RES-02 API review/read adapters now use existing authentication and CSRF boundaries, explicit response schemas and standard conflict codes. Review detail reads require firm-wide planning authority; publication requires an independent firm-wide Administrator. One focused API test passes for unauthenticated/forged-CSRF denial, exact retry, changed-intent/stale-head conflicts and verified readback. The API/emitter build passes and OpenAPI was regenerated. The scoped-manager Domain denial assertion subsequently passed in the focused publication cohort; review UI, staffing integration and concurrency acceptance remain pending.

CLIENT-06 shared write preconditions now cover current client authority, first sign-in, Active engagement and existing release/freeze restrictions. Request projections, replies/comments, upload/resume and request acknowledgment/resubmission reuse that gate; staff upload authorization and released-bundle receipt behavior remain separate. The initial ClientPortalOnboardingTests run had six passes and one stale-revision fixture failure; after correcting the fixture, the new write-gate test passed alone. No all-green combined rerun or released-bundle acknowledgement cohort was executed. Browser/live and full release acceptance remain pending.

RES-02 Angular review dialog now opens by exact certification identity, shows retained source/latest decision/expiry independently, and preserves an uncertain request for explicit identical recovery. Fifteen focused Angular checks and the Angular build pass. The built-in browser observed initial not-verified state, VERIFIED revision 1 and REVOKED revision 2; console warnings/errors were absent. Only the synthetic auditsphere_browser database received the migration and one clearly labeled fixture qualification, now withdrawn. No actual staff qualification or production acceptance is asserted. Staffing integration and concurrency/role-revocation acceptance remain unfinished.

CLIENT-04 attribution correction preserves the retained correspondence dispatcher in the combined activity projection and displays recorded actor IDs in Angular. Notifications without a retained actor remain explicitly unknown; zero-valued legacy correspondence actor identities map to unknown rather than an invented user. Five focused Angular specs, Angular build and synthetic browser rendering pass, with no console warnings/errors. The original bounded timeline test passed before the additional legacy-actor case; the final combined Domain verification is pending while staffing fixtures are repaired. Provider delivery and a unified immutable communication acceptance record are separate remaining scope.

RES-02 staffing integration now uses the latest exact-source review for candidate/list flags, Manager/Partner assignment and preview eligibility. The preview binds review identities/digests, so a changed head invalidates an older confirmation; prior assignments, grants and receipts are preserved after qualification withdrawal. A deterministic PostgreSQL race test now holds certification revocation at the publication lock, observes a reviewed assignment waiting on that lock, commits revocation first, and proves the assignment is refused without a new assignment, receipt or staffing grant. The earlier synthetic browser check confirms that a withdrawn qualification no longer labels its candidate Certified, with clean console output and no assignment/grant mutation. Full journeys, broader responsive/accessibility acceptance and current-SHA release verification remain open; RES-02 remains PARTIAL.
