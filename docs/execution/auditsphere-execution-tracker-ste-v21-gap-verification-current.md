# STE v2.1 — Requirement Verification Against Current Code

**Status: CURRENT** as a requirement map. Its original source baseline is `e0afe18` (2026-10-08); later rows carry their implementation story and later verification is recorded in `docs/execution/status.json` and the current-slice handoff. This map is not a full acceptance certification of the current tree.
**Method:** each baseline row was checked against source at its cited commit. "Present" means the code path exists and was read; it does **not** by itself mean the behaviour was executed. Tests restored under STE-NXT-001 provide executable evidence, but current-tree acceptance requires the recorded suite results. The earlier [coverage table](auditsphere-ste-specification-coverage-current.md) remains the narrative record; its verification column cites test classes that no longer exist.

**Verdicts:** `PRESENT` code path exists · `GAP` required piece missing (story listed) · `DEVIATION` intentional difference (ADR listed) · `EXTERNAL` needs live tenant (`BLOCKED_EXTERNAL`) · `DECISION` needs an owner decision · `NOT CHECKED` not examined in this pass.

## Current-tree verification and decision status — 2026-10-10

- Legal confirmations use the separate controlled taxonomy: `LEGAL` is valid; `LEGAL_SERVICES` and unknown codes fail closed. Single and batch operations share validation, and the Angular selector is server-derived. `AuditConfirmationCommandIsolationTests.ConfirmationTaxonomy_KeepsLegalSeparateAndRejectsUnknownCategories` and `AngularConfirmationJourneyTests.NativeSignalForms_ObservedDispatch_IndependentAlternativeReview_AndSessionRevocation` passed on the current tree.
- Practice tasks and billable time pin the exact approved FSLI mapping version/code; corrections preserve lineage. This records attribution only and does not perform account classification or posting (ADR-0013). The PostgreSQL Domain and Angular E2E time-scope journeys passed.
- All locally executable STE acceptance checks have current-tree evidence; the generated manifest retains five `BLOCKED_EXTERNAL` checks for live tenant/workspace or provider protection. Local archive export and browser evidence do not claim provider immutability.
- ADR-0013, ADR-0014 and ADR-0015 and the earlier delegated architecture decisions are recorded as approved. T003 remains blocked on qualified methodology ownership plus independent sign-off for accounting standards, FX/consolidation policy, resource limits and golden fixtures. ADR-0010 approves the person-entered provision mechanism only; posting treatment remains a separate professional gate. T014 remains `NOT_STARTED` pending completed/reviewed T007 and T013.
- Exact regression counts, tested source commit, build warnings, OpenAPI/EF/documentation checks and the local Angular cache-backend note are recorded in `status.json`. No development/production migration or live tenant/provider operation was performed by this verification.

| Spec | Requirement | Evidence at `e0afe18` | Verdict |
| --- | --- | --- | --- |
| 1.1 | No per-file licensing limit | No licensing gate found; caps are per-query bounds (NFR §3). Not every query audited. | PRESENT (partial audit) |
| 1.2 | Four personas | `StaffingLevels`, RoleGrant scopes, `RoleAssignmentService` | PRESENT |
| 4.1.1 | Lead channels Phone/WhatsApp/Email/Web Form/Referral/In-person | `Domain/Practice/Crm.cs` channel constants | PRESENT |
| 4.1.1 | Group structure (holding, subsidiary, affiliate) | `ClientRelationshipKinds` | PRESENT |
| 4.1.1 | Entity profile (registration, tax ID, signatories) | `RegistrationNumber`, `TaxRegistrationNumber`, `SignatoryAuthority` on the client | PRESENT |
| 4.1.1 | Role-based contact routing | `PracticeCrmService.Routing.cs` | PRESENT |
| 4.1.2 | Brief quotation | `QuotationService`, `QuotationCalculator` | PRESENT |
| 4.1.2 | Five-chapter proposal | `CommercialDocumentService.Tender.cs`; chapter fields incl. `IndustryCredentials`, `AuditMethodology` | PRESENT |
| 4.1.2 | Pricing review, dispatch and client response | AS-PAR-009 `PARTIAL` in `status.json` `remainingLocalWork` | GAP (existing AS-PAR-009) |
| 3.1 | Dispatch "via Email / WhatsApp" | Email via consented Graph mail; WhatsApp is a capture label only | DEVIATION (product brief non-goals) |
| 4.1.3 | Dual-key gate before engagement letter | `CommercialDocumentService.cs` refuses without current unconditional Partner acceptance; client acceptance key per coverage row | PRESENT |
| 4.1.4 | Templates per engagement type (ISA 210, internal audit, ISRS 4400) | `EngagementLetterTemplates.cs` (added `9e946d1`); unsupported routes fail closed | PRESENT |
| 4.1.4 | Template choice visible in Angular | At e0afe18 (NOT CHECKED): not examined. Now: the service route is shown on the proposal (`src/AuditSphereOps.Ui/src/app/features/commercial/proposal.ts:173`) and each generated letter shows its template version (`features/commercial/documents.ts:206`). The template follows the route on the server (`EngagementLetterTemplates.Resolve`); the user makes no template choice, as the glossary defines it. | PRESENT (checked in STE-NXT-008) |
| 4.1.5 | 50 % advance invoice with the letter | Fee agreement and milestones created with the letter; optional draft automation | DEVIATION (ADR-0007) |
| 4.1.5 | Advance-invoice preparation state shown | At e0afe18 (GAP): `FeeAgreementWorkspaceQuery` returns `AdvancePreparation`; Angular `features/commercial/fee-agreement.ts` does not decode or render it. Now: `features/commercial/fee-agreement.ts` decodes the nine states and renders each beside the advance milestone with `audit-status` | PRESENT (STE-NXT-006) |
| 4.1.5 | Emailed temporary password, forced reset | Microsoft sign-in with first-sign-in requirement instead | DEVIATION (ADR-0003); live EXTERNAL (P1) |
| 4.1.5 | Portal status badges | `PbcStates` (mapping in the glossary) | PRESENT |
| 4.1.5 | Mandatory rejection reason shown to client | `PbcService.cs` refuses `CLARIFICATION_REQUIRED` without `ClarificationReason` | PRESENT |
| 4.1.5 | Uploads freeze at report release | Client release guard shared by PBC and representation uploads (coverage row 4.4.2) | PRESENT (not re-read) |
| 4.2.1 | Acceptance (UBO, AML, KYC, integrity, independence) and continuance (fees, ownership, loans, litigation, fraud) | `Application/Acceptance/QuestionnaireSeed.cs`, `AcceptanceChecklistService.cs` | PRESENT |
| 4.2.2 | Capacity calendar with leave | `StaffAvailabilityKinds.Leave`, `ResourceGridCalculator` | PRESENT |
| 4.2.2 | Milestones relative to statutory cutoff | `Domain/Audit/StatutoryMilestones.cs` | PRESENT |
| 4.2.3 | Five-folder directory on acceptance | `EngagementWorkspaceProvisioningHandler`; live SharePoint creation | PRESENT; live EXTERNAL (P2) |
| 4.2.4 | Benchmark ranges (PBT 5–10, Revenue 0.5–2, Assets 0.5–1, Net assets 1–2 %) | `MaterialityCalculator.RateRanges` — exact match | PRESENT |
| 4.2.4 | TE 50–75 %, SAD 3–5 % of PM | `PerformanceRange`, `TrivialRange` | PRESENT |
| 4.2.4 | **Normalized** PBT | Code states "no normalization applied" (`MaterialityEngineService.cs`); ADR-0011 records the approved no-normalization product choice | DEVIATION (ADR-0011 APPROVED); engagement-specific judgment remains human |
| 4.2.4 | Practical rounding within ±5 % | At e0afe18 (GAP): `MaterialityPracticalRounding`, `POST /api/ui/materiality/{assessmentId}/rounding`; no Angular caller. Now: form in `features/audit/plan.ts`, offered only when the server flag `canApplyPracticalRounding` is true, which uses the command's own authorization | PRESENT (STE-NXT-004) |
| 4.2.4 | Green / Amber / Red stratification and routing | `RiskBands`, `RiskBandRules`, `FsliRiskBandRules`, `RiskBandService`, `ProcedureRiskBandEvaluator` (the coverage table's `RiskBandRoutingService` does not exist) | PRESENT |
| 4.3.1 | TB via Excel/CSV, FSLI mapping with memory | `TrialBalanceXlsxImporter`; mapping memory per STE package 4 | PRESENT |
| 4.3.1 | Split P&L / balance-sheet dashboard with AR and workprogramme triggers | At e0afe18 (NOT CHECKED): statement contracts and fieldwork screens exist; layout not inspected. Now: `src/AuditSphereOps.Ui/src/app/features/engagements/statements.html`: split toggle (line 77); P&L section before the balance sheet (lines 100 and 274); prior, variance and % variance columns; an `AR Test` and an `Audit Workprogram` entry point on every line in both sections (lines 209, 223, 386, 400) | PRESENT: layout checked in STE-NXT-008; the AR Test and Audit Workprogram links carry their context to their screens (STE-NXT-013) |
| 4.3.1 | Row-level concurrency | `ExpectedRevision` / `ExpectedVersion` fences across Application commands | PRESENT |
| 4.3.2 | Standard procedures, ad-hoc steps, physical index, going concern | `Domain/Audit/FieldworkConnections.cs`, `Fieldwork.cs` | PRESENT |
| 4.3.2 | MUS, systematic, stratified sampling | `Domain/Audit/AuditSamplingEngine.cs` | PRESENT |
| 4.3.3 | Return with mandatory comments, rework | `CHANGES_REQUIRED` statuses and anchored review notes | PRESENT |
| 4.3.3 | SRM auto-compiled on final approval | `DeliverableKinds.SummaryReviewMemorandum`; STE package 5 | PRESENT |
| 4.3.4 | Critical confirmation blocks report; holding letter auto-generated and queued | `AuditDeliverableService.cs` report path; `HoldingLetterDispatch.cs` (one per blocker set) | PRESENT |
| 4.4.1 | Four opinions; modified → FSLI + basis | `AuditOpinionTypes` (`UNMODIFIED` = "Clean/Unqualified") | PRESENT |
| 4.4.1 | Partner signature and seal | PNG visual credentials | DEVIATION (ADR-0005) |
| 4.4.2 | Five-part bundle | `AuditDeliverableService.Bundle.cs` (100 MB bound) | PRESENT |
| 4.4.3 | 60-day countdown from signature, worker freeze | `FileFreeze` model, worker schedule | PRESENT |
| 4.4.3 | Partner manual (early) lock | At e0afe18 (GAP): `FileFreezeService.RequestEarlyComplianceLockAsync`, `POST /api/ui/engagements/{id}/early-lock` and `/readiness`; no Angular caller. Now: panel in `features/audit/completion.ts` (readiness, digest-bound confirmation, stale refusal reloads readiness) | PRESENT (STE-NXT-005) |
| 4.4.3 | Permanently read-only archive | Local only; SharePoint staff hold Full Control | EXTERNAL (P2, ADR-0006, SPK-01) |
| 4.5.1 | QAR charge-out rates 1,000 / 750 / 500 / 200 | At e0afe18 (GAP): `SteChargeOutRateBaseline.Rates` exact; `InitializeDraftsAsync` has **no caller**. Now: `POST /api/ui/practice/rate-cards/ste-baseline` calls `InitializeDraftsAsync` (drafts only, idempotent); page `features/practice/rate-cards.ts` | PRESENT (STE-NXT-003) |
| 4.5.1 / 4.1.2 | Rate cards can be created and approved | At e0afe18 (GAP, highest priority): `PracticeTimeService.ReviseRateCardAsync` and `ApproveRateCardAsync` have no caller in Api, Web or Worker; only a test seed inserts `RateCardVersions`. Quotation, budget and time capture all read **approved** cards, so a fresh deployment cannot price a quotation without direct database writes. Now: revision, approval by a different firm-wide approver and the quotation option list (`QuotationService.ListRateOptionsAsync`, approved cards only) are wired through `UiEndpoints.RateCards.cs` and `features/practice/rate-cards.ts` | PRESENT (STE-NXT-003) |
| 4.5.1 / audit-work attribution | Billable time can retain its exact approved FSLI mapping | ADR-0013 defines the technical lineage decision; `WorkTask` and `TimeEntry` pin mapping version and FSLI, and time capture validates the approved mapping | PRESENT in code; current-tree regression evidence is recorded separately |
| 4.5.1 | Profitability, realization, budget variance | `PracticeAnalyticsQuery` in `FirmOperationsServices.cs` | PRESENT |
| 4.5.2 | Ledger: rent, salaries and benefits, petty cash, overheads | `FirmExpenseCategories`: `RENT`, `SALARIES`, `PETTY_CASH`, `UTILITIES`, `OTHER` | PRESENT |
| 3.5 | End-of-service benefits | No accrual implementation found; ADR-0010 decides the recording mechanism, while the applicable measurement treatment still blocks posting (STE-NXT-014) | DECIDED_MECHANISM / PROFESSIONAL_TREATMENT_BLOCKED |
| 4.5.2 | Partner withdrawals | Posted as equity drawings in `LedgerService.cs`, not as expenses | PRESENT (correct treatment) |
| 4.5.2 | Firm TB, P&L, AR ageing (advance/final) | Ledger services; `FirmReceivablesAgingQuery.cs`; `features/finance/receivables-aging.ts` | PRESENT |
| 5 | Lifecycle states and gates | `EngagementLifecycleQuery` projection | DEVIATION in form (ADR-0004); PRESENT in substance |
| — | Requirement document accuracy | Header architecture row, portal box and §4.1.5 bullets retain and annotate original wording; approved deviations cite ADR-0002 to ADR-0007 and ADR-0009 (STE-NXT-007) | PRESENT (STE-NXT-007 completed under delegated ADR decisions) |

## Summary

- 4 verified functional gaps at `e0afe18`, all "built but not wired": rate-card administration and the charge-out baseline (no entry point at all — blocks quotation pricing on a fresh deployment), rounding UI, early-lock UI, advance-preparation display (STE-NXT-004 to 006). Wired on `master` by STE-NXT-003 to STE-NXT-006; each now has a page and tests.
- Product decisions STE-NXT-009 and STE-NXT-010 are recorded in APPROVED ADR-0010 and ADR-0011. ADR-0013 approves technical FSLI lineage for practice time; ADR-0014 records the existing six-decimal/ToEven arithmetic convention; ADR-0015 resolves reporting-context identity and lineage. The end-of-service recording mechanism is decided, but its professional measurement treatment remains blocked under STE-NXT-014; PBT normalization is resolved as no normalization. R2R framework-edition, FX-method and consolidation-method approvals remain blocked under T003.
- 1 open existing story with commercial impact: AS-PAR-009.
- Archive immutability and live Microsoft behaviour remain external (ADR-0006, SPK-01; BLOCKED_EXTERNAL).
- Not checked at `e0afe18`: engagement-letter template selection in Angular, the split-dashboard layout. The engagement-letter template choice was checked in STE-NXT-008 and is present. The split-dashboard layout is present, and its per-line links carry their context to the screens (STE-NXT-013). Requirements copy accuracy was open at `e0afe18`; STE-NXT-007 applied the corrections and the owner-delegated ADR confirmations are recorded in the current ADR register.
