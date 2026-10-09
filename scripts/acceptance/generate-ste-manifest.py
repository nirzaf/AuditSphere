#!/usr/bin/env python3
"""Generates the STE v2.1 acceptance manifest (STE-GAP-010).

Writes docs/execution/ste-v2-1-acceptance-manifest.json. The manifest is an evidence register, not a test run: it records
the repository commit and migration head, the specification version, every mandatory journey step and negative branch
with the evidence actually recorded, each gap story's status and open items, the external-provider evidence still
required, and the overall result. It executes nothing and never marks a check PASS without recorded evidence.

Overall result, applied in order:
  FAIL              a check failed, or a check has no executed evidence and no external block
  PASS              every mandatory check passed with recorded evidence
  BLOCKED_EXTERNAL  every unresolved mandatory check is blocked by an external provider

Usage: python3 scripts/acceptance/generate-ste-manifest.py [output-path]
Stdlib only, matching scripts/docs and scripts/ui.
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST_RELATIVE = "docs/execution/ste-v2-1-acceptance-manifest.json"
DEFAULT_OUTPUT = ROOT / MANIFEST_RELATIVE
MIGRATIONS = ROOT / "src" / "AuditSphereOps.Infrastructure" / "Persistence" / "Migrations"
MIGRATION_FILE = re.compile(r"^\d{14}_\w+\.cs$")
TEST_REMOVAL_COMMIT = "47ca0b05"

SPECIFICATION = {
    "title": "STE Audit Management Tool — Functional Requirements & End-to-End System Workflow Specification",
    "version": "2.1",
    "gapAnalysis": "AuditSphere_STE_v2_1_Gap_Closure_User_Stories, prepared 2026-10-08",
    "gapAnalysisReviewedRevision": "214579b9583bae5a209cb4cf3476a5fcb459251d",
}

RESULT_RULE = (
    "FAIL when a mandatory check failed, or has no executed evidence and no external block. "
    "PASS only when every mandatory check passed with recorded evidence. "
    "BLOCKED_EXTERNAL when every unresolved mandatory check is blocked by an external provider. "
    "A check is never marked PASS without recorded evidence."
)

# currentStatus: IMPLEMENTED_LOCAL = required behaviour is in code and UI with no executed acceptance evidence;
# PARTIAL = known gaps listed in openItems; BLOCKED_EXTERNAL = needs a real provider or tenant; ACCEPTANCE_GAP = the
# journey itself is not executed. baselineStatus is copied from the gap-analysis coverage table.
STORIES = [
    {
        "id": "STE-GAP-001",
        "title": "Versioned Service-Specific Engagement Letter Templates",
        "priority": "P1",
        "baselineStatus": "Partial",
        "currentStatus": "PARTIAL",
        "localEvidence": [
            "src/AuditSphereOps.Application/Practice/EngagementLetterTemplates.cs",
            "src/AuditSphereOps.Application/Practice/CommercialDocumentRenderer.cs",
            "src/AuditSphereOps.Application/Practice/CommercialDocumentService.cs",
        ],
        "implemented": [
            "Templates EL-STATUTORY-AUDIT-ISA210-v1, EL-INTERNAL-AUDIT-v1 and EL-AUP-ISRS4400-v1 are resolved by service route.",
            "A route without a configured template fails closed with 'template not configured'.",
        ],
        "openItems": [
            "Letter generation for each route has not been executed by a recorded check or at runtime.",
        ],
    },
    {
        "id": "STE-GAP-002",
        "title": "Specification-Compliant 50% Advance Invoice Automation",
        "priority": "P1",
        "baselineStatus": "Partial",
        "currentStatus": "PARTIAL",
        "localEvidence": [
            "src/AuditSphereOps.Application/Practice/FeeAgreementWorkspaceQuery.cs",
            "src/AuditSphereOps.Api/Ui/UiEndpoints.FeeAgreement.cs",
            "src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs",
            "src/AuditSphereOps.Ui/src/app/features/commercial/fee-agreement.ts",
            "src/AuditSphereOps.Ui/src/app/features/commercial/fee-agreement.spec.ts",
        ],
        "implemented": [
            "The advance-preparation state is derived from the advance milestone and the durable-operation status.",
            "Activation is refused until a linked fee agreement's 50% advance milestone is PAID.",
        ],
        "openItems": [
            "The activation refusal and the preparation states have not been executed with a linked fee agreement at runtime.",
            "AutomaticFeeInvoices:Enabled is read by the API (preparation state) and by the worker (draft creation); both must carry the same value in each deployment.",
        ],
    },
    {
        "id": "STE-GAP-003",
        "title": "Controlled ±5% Practical Materiality Rounding",
        "priority": "P0",
        "baselineStatus": "Missing",
        "currentStatus": "IMPLEMENTED_LOCAL",
        "localEvidence": [
            "src/AuditSphereOps.Application/Audit/MaterialityPracticalRounding.cs",
            "src/AuditSphereOps.Application/Audit/MaterialityEngineService.cs",
            "src/AuditSphereOps.Application/Audit/AuditPlanningService.cs",
            "src/AuditSphereOps.Application/Audit/AuditPlanWorkspaceQuery.cs",
            "src/AuditSphereOps.Api/Ui/UiEndpoints.AuditPlan.cs",
            "src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261007214800_MaterialityPracticalRounding.cs",
            "src/AuditSphereOps.Ui/src/app/features/audit/plan.ts",
            "src/AuditSphereOps.Ui/src/app/features/audit/plan.spec.ts",
        ],
        "implemented": [
            "Rounding creates a new effective draft materiality assessment and a rounding decision that keeps the computed and adjusted figures side by side.",
            "The ±5% bound is exact decimal arithmetic; SAD ≤ TE ≤ PM and positive values are enforced in the domain and by a database check constraint.",
            "Approval binds to the current effective head; a superseded head is refused.",
        ],
        "openItems": [
            "The rounding form is covered by component tests (plan.spec.ts) and the flag by a Domain test; it was not exercised in a browser against seeded data.",
        ],
    },
    {
        "id": "STE-GAP-004",
        "title": "Live Client Portal Provisioning & Credential Acceptance",
        "priority": "P1",
        "baselineStatus": "Blocked External",
        "currentStatus": "BLOCKED_EXTERNAL",
        "localEvidence": [
            "src/AuditSphereOps.Application/Documents/ClientPortalService.cs",
            "src/AuditSphereOps.Application/Documents/ClientPortalWorkspaceQuery.cs",
            "src/AuditSphereOps.Application/Documents/EngagementWorkspaceProvisioning.cs",
            "src/AuditSphereOps.Application/Documents/ClientSharePointSites.cs",
            "src/AuditSphereOps.Application/Documents/PbcRepositoryProvisioning.cs",
        ],
        "implemented": [
            "Portal intents, the first-sign-in upload gate, participant controls, the five-folder structure, exact selected-site bindings and workspace provisioning are implemented locally.",
        ],
        "openItems": [
            "No real tenant provisioning, invitation, first sign-in, capability read/write test, access revocation or cross-client denial has been performed or recorded.",
        ],
        "blocker": "Needs the approved Microsoft tenant, provisioned portal identities and the selected SharePoint sites. No live acceptance is recorded.",
        "externalEvidenceRequired": [
            "tenant, site and drive identifiers",
            "provisioned identity identifier",
            "invite/send provider correlation ID",
            "first-sign-in observed timestamp",
            "workspace binding IDs",
            "capability read/write test result",
            "access-revocation test result",
            "cross-client denial test result",
        ],
        "evidenceRule": "Reusable credentials, tokens, passwords and Graph secrets are never recorded.",
    },
    {
        "id": "STE-GAP-005",
        "title": "Automatic Holding Letter Dispatch for Critical Confirmations",
        "priority": "P1",
        "baselineStatus": "Partial",
        "currentStatus": "IMPLEMENTED_LOCAL",
        "localEvidence": [
            "src/AuditSphereOps.Application/Completion/HoldingLetterDispatch.cs",
            "src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs",
            "src/AuditSphereOps.Application/Completion/EngagementCompletionWorkspaceQuery.cs",
            "src/AuditSphereOps.Api/Ui/UiEndpoints.Completion.cs",
            "src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261008085154_HoldingLetterDispatch.cs",
            "src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261008094929_HoldingLetterBlockerKey.cs",
            "src/AuditSphereOps.Ui/src/app/features/audit/completion.ts",
        ],
        "implemented": [
            "The report attempt queues the holding letter when critical confirmations are outstanding.",
            "One dispatch per blocker-set revision, enforced by a unique index on firm and dispatch key.",
            "Recipient routing uses the Completion contact purpose only; there is no fallback address.",
        ],
        "openItems": [
            "A FAILED dispatch is visible but has no operator retry path.",
            "Dispatch of a real generated Holding Letter has not been exercised at runtime.",
        ],
        "observedLocally": [
            "The completion page showed the holding-letter section in state NONE (browser check).",
        ],
    },
    {
        "id": "STE-GAP-006",
        "title": "Partner Early/Manual Compliance Lock",
        "priority": "P0",
        "baselineStatus": "Missing",
        "currentStatus": "IMPLEMENTED_LOCAL",
        "localEvidence": [
            "src/AuditSphereOps.Application/Records/FileFreezeService.cs",
            "src/AuditSphereOps.Domain/Records/FileFreeze.cs",
            "src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261007215407_EarlyComplianceLock.cs",
            "src/AuditSphereOps.Api/Ui/UiEndpoints.Completion.cs",
            "src/AuditSphereOps.Ui/src/app/features/audit/completion.ts",
            "src/AuditSphereOps.Ui/src/app/features/audit/completion.spec.ts",
        ],
        "implemented": [
            "The early lock requires the Partner role in scope and Partner confirmation, and checks the freeze revision, the final release, the reviewed archive readiness digest and the rationale under a row lock.",
            "Early-lock records are append-only (trigger-protected).",
        ],
        "openItems": [
            "The early-lock panel is covered by component tests (completion.spec.ts); it was not exercised in a browser against a scheduled freeze.",
        ],
    },
    {
        "id": "STE-GAP-007",
        "title": "Provider-Enforced Read-Only Compliance Archive",
        "priority": "P0",
        "baselineStatus": "Blocked External / Compliance",
        "currentStatus": "BLOCKED_EXTERNAL",
        "localEvidence": [
            "src/AuditSphereOps.Application/Records/FileFreezeService.cs",
            "src/AuditSphereOps.Domain/Records/FileFreeze.cs",
            "src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs",
        ],
        "implemented": [
            "The local freeze is enforced inside AuditSphere; the lifecycle returns provider status separately and warns when provider read-only enforcement is not observed.",
        ],
        "openItems": [
            "No provider protection strategy is implemented; FileFreezeService records ExternalReadOnly as BLOCKED_EXTERNAL.",
        ],
        "blocker": "Needs an approved provider protection strategy, provider readback evidence and real-tenant denial tests. AuditSphere does not claim provider immutability.",
        "externalEvidenceRequired": [
            "protection request identity and provider correlation ID",
            "provider readback of the file or tree identity and hashes",
            "denied direct edit, delete and overwrite by an ordinary engagement staff user",
            "authorized compliance read result",
            "cross-client denial result",
            "failed or uncertain provider result shown as not protected",
        ],
    },
    {
        "id": "STE-GAP-008",
        "title": "Canonical Eleven-Stage Lifecycle Truthfulness & Gate Alignment",
        "priority": "P0",
        "baselineStatus": "Partial / Correctness",
        "currentStatus": "PARTIAL",
        "localEvidence": [
            "src/AuditSphereOps.Application/Acceptance/EngagementLifecycleQuery.cs",
            "src/AuditSphereOps.Application/Acceptance/EngagementLifecycleService.cs",
            "src/AuditSphereOps.Application/Practice/PracticeLeadQuery.cs",
            "src/AuditSphereOps.Ui/src/app/features/commercial/leads.ts",
            "src/AuditSphereOps.Ui/src/app/features/engagements/engagement-lifecycle.html",
        ],
        "implemented": [
            "Leads without a proposal are projected as LEAD_INGESTION (Stage 1) on the lead list.",
            "Planning requires approved, non-stale materiality, and managerial review hands over to Partner approval only when the completion gate is clear.",
            "The compliance countdown is anchored to the report-signature time, and an engagement is shown as archived only when its local freeze is FROZEN.",
            "Proposal-generation drafts remain at Stage 2; Stage 3 requires a dispatched proposal with SentAt evidence.",
            "The lifecycle response and Angular UI expose local archive and provider-protection states separately; unknown state values fail decoding.",
            "A due but uncommitted freeze remains in the compliance countdown with an explicit blocker.",
        ],
        "openItems": [
            "Proposal dispatch and lifecycle projection still need a joined browser journey; the current PostgreSQL lifecycle test covers the projection boundary.",
            "The external provider read-only state remains BLOCKED_EXTERNAL until approved live provider readback evidence exists (STE-GAP-007).",
        ],
        "observedLocally": [
            "The engagement lifecycle showed Stage 2 of 11 with no compliance warning (browser check).",
            "A new lead listed with lifecycleStage LEAD_INGESTION (browser check).",
        ],
    },
    {
        "id": "STE-GAP-009",
        "title": "Approved STE Default Charge-Out Rate Policy",
        "priority": "P2",
        "baselineStatus": "Configuration / Policy Gap",
        "currentStatus": "IMPLEMENTED_LOCAL",
        "localEvidence": [
            "src/AuditSphereOps.Application/Practice/SteChargeOutRateBaseline.cs",
            "src/AuditSphereOps.Application/Practice/PracticeTimeService.cs",
            "src/AuditSphereOps.Api/Ui/UiEndpoints.Time.cs",
            "src/AuditSphereOps.Ui/src/app/features/practice/time.ts",
            "src/AuditSphereOps.Application/Practice/RateCardWorkspaceQuery.cs",
            "src/AuditSphereOps.Api/Ui/UiEndpoints.RateCards.cs",
            "src/AuditSphereOps.Ui/src/app/features/practice/rate-cards.ts",
            "src/AuditSphereOps.Ui/src/app/features/practice/rate-cards.spec.ts",
        ],
        "implemented": [
            "Baseline initialization creates DRAFT rate cards only, for QAR: Engagement Partner 1000, Audit Manager 750, Audit Supervisor 500 and Audit Associate 200 per hour.",
            "Rate resolution uses the exact approved card first, then the STE role alias, and otherwise fails closed with time.rate-missing.",
        ],
        "openItems": [
            "Approval by a separate Partner, preparer refusal, aliases and unknown-role fail-closed behavior are covered by the PostgreSQL-backed Domain test; the screen approval action has not been exercised in a browser.",
        ],
        "observedLocally": [
            "Baseline initialization created four DRAFT cards (browser check).",
            "Approval by the preparer was refused with 'Rate card preparers cannot approve their own version.' (browser check).",
        ],
    },
    {
        "id": "STE-GAP-010",
        "title": "Full STE v2.1 End-to-End Acceptance Journey",
        "priority": "P0",
        "baselineStatus": "Acceptance Gap",
        "currentStatus": "ACCEPTANCE_GAP",
        "localEvidence": [
            "scripts/acceptance/generate-ste-manifest.py",
            "docs/testing/auditsphere-testing-test-case-catalog.md",
        ],
        "implemented": [
            "This manifest lists all 48 happy-path steps and 14 negative branches, each with its evidence status.",
        ],
        "openItems": [
            "The manifest maps only journey steps with a declared source test and a recorded zero-failure run; unmatched local steps remain NOT_EXECUTED.",
            "Steps that depend on the Microsoft tenant remain BLOCKED_EXTERNAL until live evidence and independent acceptance are recorded.",
        ],
    },
]

HAPPY_PATH = [
    ("J01", "Lead created with entity and contact profile."),
    ("J02", "Client converted/profiled with relevant relationship/contact routing."),
    ("J03", "Brief or comprehensive proposal generated."),
    ("J04", "Proposal dispatched and client commercial acceptance recorded."),
    ("J05", "New/continuance acceptance checklist completed."),
    ("J06", "Independent Partner risk acceptance recorded."),
    ("J07", "Engagement Letter generated from correct service-specific template."),
    ("J08", "50% advance invoice workflow initiated."),
    ("J09", "Official invoice posted/delivered through approved finance flow."),
    ("J10", "Payment recorded and allocated."),
    ("J11", "Official receipt generated."),
    ("J12", "Portal identity/workspace provisioned."),
    ("J13", "First-sign-in rule satisfied."),
    ("J14", "Standard directory available."),
    ("J15", "staff assigned and capacity/schedule visible."),
    ("J16", "TB uploaded and sealed."),
    ("J17", "mapping approved."),
    ("J18", "materiality calculated."),
    ("J19", "optional practical rounding applied within ±5%."),
    ("J20", "independent Partner materiality approval recorded."),
    ("J21", "FSLI risk bands visible and staffing routes enforced."),
    ("J22", "fieldwork program adopted."),
    ("J23", "parallel FSLI work executed."),
    ("J24", "digital and physical evidence linked."),
    ("J25", "sampling run persisted/reproducible."),
    ("J26", "analytical review completed/reviewed."),
    ("J27", "going-concern assessment completed/reviewed."),
    ("J28", "Preparer submits work."),
    ("J29", "Reviewer returns at least one item with mandatory note."),
    ("J30", "Preparer reworks and resubmits."),
    ("J31", "review note resolved."),
    ("J32", "SRM generated from current evidence."),
    ("J33", "Partner reviews required Red areas."),
    ("J34", "critical confirmations are returned/evaluated."),
    ("J35", "Partner clears current SRM."),
    ("J36", "opinion selected."),
    ("J37", "report generated and signed/sealed."),
    ("J38", "Management Letter generated."),
    ("J39", "LOR generated, downloaded, signed by client and re-uploaded."),
    ("J40", "correspondence trail included."),
    ("J41", "five-part bundle assembled."),
    ("J42", "final 50% invoice generated/posting workflow completed."),
    ("J43", "client portal uploads become read-only/frozen."),
    ("J44", "countdown begins from report signature."),
    ("J45", "either automatic day-60 freeze or Partner early lock succeeds."),
    ("J46", "archive becomes locally read-only."),
    ("J47", "provider protection verification succeeds or remains explicitly external-blocked."),
    ("J48", "regulator/read-only export succeeds."),
]

NEGATIVE_BRANCHES = [
    ("N01", "Client accepted quote but Partner risk gate missing → no Engagement Letter."),
    ("N02", "Partner risk gate complete but client commercial acceptance missing → no Engagement Letter."),
    ("N03", "Advance invoice unpaid → no active upload workspace."),
    ("N04", "First sign-in not completed → no upload."),
    ("N05", "Stale TB/mapping → materiality cannot be approved."),
    ("N06", "Practical rounding >5% → rejected."),
    ("N07", "Junior assigned to Amber/Red work below required level → rejected."),
    ("N08", "Open review note → completion blocked."),
    ("N09", "Critical confirmation outstanding → audit report blocked + Holding Letter generated."),
    ("N10", "Modified opinion without affected FSLI/basis → rejected."),
    ("N11", "Final release → subsequent client upload rejected."),
    ("N12", "Day 60 with a due but uncommitted freeze → cannot falsely claim archived."),
    ("N13", "Direct provider edit after verified archive protection → denied."),
    ("N14", "Cross-client user → denied throughout."),
]

# Checks that can only pass against a real Microsoft tenant or provider, mapped to the story that owns the external block.
EXTERNAL_BLOCKS = {
    "J12": "STE-GAP-004",
    "J13": "STE-GAP-004",
    "J14": "STE-GAP-004",
    "J47": "STE-GAP-007",
    "N13": "STE-GAP-007",
}


def git(*args: str) -> str:
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip()


# Journey steps with executed local evidence. Each citation is "<test source path>#<method>", so a mapping is verifiable:
# the generator refuses to run when a cited file or method is absent, which keeps an invented citation from silently
# passing. It also requires the current source and test trees, and each cited test file, to match the recorded suite-run
# commit. A step with no citation stays NOT_EXECUTED; a step whose suite recorded failures stays NOT_EXECUTED with that
# run named, because a suite-level pass cannot prove one step.
EVIDENCE = {
    "J18": ["tests/AuditSphereOps.Domain.Tests/PlanningResourcesAndMaterialityTests.cs#Materiality_IsCalculatedFromTheMappedTrialBalance_AndGoesStaleWhenTheSourceIsReplaced"],
    "J19": [
        "tests/AuditSphereOps.Domain.Tests/MaterialityPracticalRoundingTests.cs#StoryCriterion_PlanningMateriality_53421_to_53000_is_accepted_and_a_value_past_the_bound_is_refused",
        "tests/AuditSphereOps.Domain.Tests/MaterialityPracticalRoundingTests.cs#BoundaryAdjustmentsOfExactlyFivePercentAreAccepted",
        "tests/AuditSphereOps.Domain.Tests/MaterialityPracticalRoundingTests.cs#AdjustmentsJustBeyondFivePercentAreRejected",
    ],
    "J29": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#InlineNotesBlockApproval_AndOnlySomeoneStaffedAboveThePreparerReviews"],
    "J30": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#LateAndReopenedNotesBlockClearanceUntilRespondedResolvedAndReviewed"],
    "J31": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#LateAndReopenedNotesBlockClearanceUntilRespondedResolvedAndReviewed"],
    "J32": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#SrmPresentsCurrencyAwareDifferences_AndDocumentsDoNotAssertAutomaticImmateriality"],
    "J33": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#SrmClearanceOpinionReportsHoldingLetterClientLoopAndPngSigning"],
    "J34": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#CriticalUnreturnedConfirmationKeepsBlockingAfterAlternativeClosureAndCriticalityReassessment"],
    "J35": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#SrmClearanceOpinionReportsHoldingLetterClientLoopAndPngSigning"],
    "J36": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#SrmClearanceOpinionReportsHoldingLetterClientLoopAndPngSigning"],
    "J37": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#SrmClearanceOpinionReportsHoldingLetterClientLoopAndPngSigning"],
    "J38": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.ManagementLetter.cs#ManagementLetterRendersOnlyDesignatedCompleteMatters_AndStalesOnTheirChange"],
    "J39": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.Assembly.cs#SignedRepresentation_IsVersionBound_Idempotent_Immutable_AndScoped"],
    "J41": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.Assembly.cs#FivePartBundle_RequiresReviewedReleaseAndPostedBalance_IsIdempotent_AndClientScoped"],
    "J44": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#FileFreezesSixtyDaysAfterSigning_RefusesWrites_AmendsWithApproval_AndTracesActivity"],
    "J45": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.EarlyLock.cs#PartnerEarlyLock_FailsClosedThenFreezesOnlyTheReviewedReadiness"],
    "J46": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#FrozenFileRefusesReviewNoteAttemptsAndRecordsTheRefusedWrite"],
    "N03": ["tests/AuditSphereOps.Domain.Tests/EngagementActivationAdvanceGateTests.cs#LinkedAgreementMustHavePaidAdvanceBeforeActivation"],
    "N06": ["tests/AuditSphereOps.Domain.Tests/MaterialityPracticalRoundingTests.cs#AdjustmentsJustBeyondFivePercentAreRejected"],
    "N08": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#InlineNotesBlockApproval_AndOnlySomeoneStaffedAboveThePreparerReviews"],
    "N09": ["tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#CriticalUnreturnedConfirmationKeepsBlockingAfterAlternativeClosureAndCriticalityReassessment"],
    "N12": ["tests/AuditSphereOps.Domain.Tests/EngagementLifecycleQueryTests.cs#EngagementTraversesCanonicalStagesAndEnforcesScope"],
}

SUITE_RUNS = {
    "tests/AuditSphereOps.Domain.Tests/": "domain",
    "tests/AuditSphereOps.Api.Tests/": "api",
    "tests/AuditSphereOps.E2E.Tests/": "e2e",
}
STATUS_PATH = ROOT / "docs" / "execution" / "status.json"


def recorded_runs() -> dict:
    if not STATUS_PATH.is_file():
        raise SystemExit(f"Recorded run ledger is missing: {STATUS_PATH}")
    return json.loads(STATUS_PATH.read_text()).get("testSuites", {}).get("runs", {})


def suite_for(path: str) -> str | None:
    for prefix, suite in SUITE_RUNS.items():
        if path.startswith(prefix):
            return suite
    return None


def verify_citation(citation: str) -> None:
    path, _, method = citation.partition("#")
    if not path or not method:
        raise SystemExit(f"Evidence citation must be '<path>#<method>': {citation}")
    source = ROOT / path
    if not source.is_file():
        raise SystemExit(f"Evidence source is missing from the checkout: {path}")
    if f" {method}(" not in source.read_text():
        raise SystemExit(f"Evidence method {method} is not declared in {path}")


def check(check_id: str, text: str, runs: dict) -> dict:
    blocked_by = EXTERNAL_BLOCKS.get(check_id)
    if blocked_by:
        return {"id": check_id, "text": text, "result": "BLOCKED_EXTERNAL", "blockedBy": blocked_by, "evidence": []}
    citations = EVIDENCE.get(check_id)
    if not citations:
        return {"id": check_id, "text": text, "result": "NOT_EXECUTED", "blockedBy": None, "evidence": []}
    for citation in citations:
        verify_citation(citation)
    paths = [citation.partition("#")[0] for citation in citations]
    unknown = [path for path in paths if suite_for(path) is None]
    if unknown:
        raise SystemExit("Evidence cites a suite with no recorded run mapping: " + ", ".join(unknown))
    suites = sorted({suite_for(path) for path in paths})
    for suite in suites:
        run = runs.get(suite)
        if run is None:
            return {"id": check_id, "text": text, "result": "NOT_EXECUTED", "blockedBy": None, "evidence": citations}
        if run.get("failed"):
            return {"id": check_id, "text": text, "result": "NOT_EXECUTED", "blockedBy": None, "evidence": citations,
                    "note": f"{suite} recorded {run['failed']} failure(s) on {run.get('ranOn')}"}
        ran_on = run.get("ranOn")
        if not ran_on:
            return {"id": check_id, "text": text, "result": "NOT_EXECUTED", "blockedBy": None, "evidence": citations,
                    "note": f"{suite} has no repository snapshot for its recorded run"}
        source_diff = subprocess.run(
            ["git", "diff", "--quiet", ran_on, "--", "src", "tests"], cwd=ROOT, check=False
        )
        if source_diff.returncode != 0:
            return {
                "id": check_id,
                "text": text,
                "result": "NOT_EXECUTED",
                "blockedBy": None,
                "evidence": citations,
                "note": f"source or test trees differ from the {suite} run snapshot {ran_on}",
            }
        for citation in citations:
            path = citation.partition("#")[0]
            recorded_source = subprocess.run(
                ["git", "show", f"{ran_on}:{path}"], cwd=ROOT, capture_output=True, text=True, check=False
            )
            if recorded_source.returncode != 0 or recorded_source.stdout != (ROOT / path).read_text(encoding="utf-8"):
                return {
                    "id": check_id,
                    "text": text,
                    "result": "NOT_EXECUTED",
                    "blockedBy": None,
                    "evidence": citations,
                    "note": f"{path} differs from the {suite} run snapshot {ran_on}",
                }
    evidence = citations + [
        f"docs/execution/status.json#testSuites.runs.{suite} ({runs[suite].get('passed')} passed, 0 failed, {runs[suite].get('ranOn')})"
        for suite in suites
    ]
    return {"id": check_id, "text": text, "result": "PASS", "blockedBy": None, "evidence": evidence}


def tally(checks: list[dict]) -> dict:
    counts = {"PASS": 0, "FAIL": 0, "NOT_EXECUTED": 0, "BLOCKED_EXTERNAL": 0}
    for item in checks:
        counts[item["result"]] += 1
    return counts


def overall_result(checks: list[dict]) -> str:
    results = {item["result"] for item in checks}
    if results == {"PASS"}:
        return "PASS"
    if results <= {"PASS", "BLOCKED_EXTERNAL"}:
        return "BLOCKED_EXTERNAL"
    return "FAIL"


def overall_reason(counts: dict, checks: list[dict]) -> str:
    blockers = sorted({item["blockedBy"] for item in checks if item["blockedBy"]})
    return (
        f"{counts['NOT_EXECUTED']} mandatory checks have no executed evidence; "
        f"{counts['BLOCKED_EXTERNAL']} are blocked by external providers ({', '.join(blockers)}); "
        f"{counts['FAIL']} checks failed."
    )


def build() -> dict:
    evidence = sorted({path for story in STORIES for path in story["localEvidence"]})
    missing = [path for path in evidence if not (ROOT / path).is_file()]
    if missing:
        raise SystemExit("Evidence paths missing from the checkout:\n  " + "\n  ".join(missing))

    migrations = sorted(p.name[:-3] for p in MIGRATIONS.iterdir() if MIGRATION_FILE.match(p.name))
    runs = recorded_runs()
    happy = [check(check_id, text, runs) for check_id, text in HAPPY_PATH]
    negative = [check(check_id, text, runs) for check_id, text in NEGATIVE_BRANCHES]
    checks = happy + negative
    counts = tally(checks)

    return {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "generatedBy": "scripts/acceptance/generate-ste-manifest.py",
        "repository": {
            "commit": git("rev-parse", "HEAD"),
            "branch": git("rev-parse", "--abbrev-ref", "HEAD"),
            "workingTreeClean": git("status", "--porcelain", "--", ".", f":(exclude){MANIFEST_RELATIVE}") == "",
        },
        "migrations": {"head": migrations[-1], "count": len(migrations)},
        "specification": SPECIFICATION,
        "testEvidence": {
            "testCaseIds": sorted(EVIDENCE),
            "executedTestSuites": sorted({suite_for(c.partition("#")[0]) for citations in EVIDENCE.values() for c in citations}),
            "removedInCommit": TEST_REMOVAL_COMMIT,
            "restoredBy": "STE-NXT-001",
            "note": "A step is PASS only when every cited test is declared in the current source, the source and test trees match the recorded suite-run commit, and that run has zero failures. Steps with no citation stay NOT_EXECUTED, and a suite-level pass never proves an uncited step.",
        },
        "stageTransitionEvidenceIds": [],
        "generatedDocuments": [],
        "externalProviderEvidence": [
            {"storyId": story["id"], "status": "NOT_RECORDED", "recordedArtifacts": [], "required": story["externalEvidenceRequired"]}
            for story in STORIES if "externalEvidenceRequired" in story
        ],
        "externalBlockers": [
            {"storyId": story["id"], "blocker": story["blocker"]}
            for story in STORIES if story["currentStatus"] == "BLOCKED_EXTERNAL"
        ],
        "journey": {"happyPath": happy, "negativeBranches": negative},
        "stories": STORIES,
        "summary": {"mandatoryChecks": len(checks), **counts},
        "overallResult": overall_result(checks),
        "overallReason": overall_reason(counts, checks),
        "resultRule": RESULT_RULE,
    }


def main(argv: list[str]) -> int:
    output = Path(argv[1]) if len(argv) > 1 else DEFAULT_OUTPUT
    manifest = build()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"wrote {output} overallResult={manifest['overallResult']}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
