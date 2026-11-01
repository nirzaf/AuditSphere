# AuditSphere — Pending Lifecycle Completion Audit
## Evidence-backed assessment and atomic specification

## 1. Review metadata

| Item | Verified scope / limitation |
|---|---|
| Repository / branch | `nirzaf/AuditSphere` / `master` |
| Inspected commit | [`538bc79fc977a4552277a0b9521f73622e04fbba`][RHEAD] |
| Commit timestamp | 1 November 2026, 13:26:27 UTC |
| Review date | 1 November 2026, Asia/Qatar. Final branch recheck still resolved to the inspected SHA. |
| Previous review | `c9a2a24db8a565df3337fb13665fd9ad500e0d23`. The [comparison][DIFF] returns one new commit, zero behind, and eight changed documentation/status paths; no application-source changes. |
| Requested scope | The pending items from that review: REM-09 candidate-query bounds; the user-facing EQR workflow and its completion evidence; positive offboarding boundaries; full-release, browser and external acceptance. |
| Access | Live GitHub connector: pinned source ranges, searches, API contracts, selected persistence/migration code, selected tests, dependency declarations and Actions run/job metadata. The earlier supplied `AUDITSPHERE-LIFECYCLE-REMAINING-AUDIT.md` was read as historical scope, not implementation proof. |
| Runtime limit | **NOT_RUN by this reviewer:** application tests, builds, migrations, browser journeys, SQL execution/query plans, external-provider checks and workflow dispatch. No repository checkout, deployed database, local uncommitted changes or ignored agent evidence was accessed. |
| Artifact / mutations | This report is the only new file, created in the output workspace outside the repository. No source edits, commits, PRs, deployments, permissions changes or workflow reruns. |
| Applicability | This is a focused pending-work audit, not a renewed audit of every A–X lifecycle feature or a certification of production readiness. |

**Requirement inventory:** **7 SOUND / 2 PARTIAL / 0 MISSING / 1 IMPROVABLE / 5 UNVERIFIED** — 15 atomic requirements.  
**Implementation plan:** **3 tasks: 2 essential engineering tasks and 1 optional improvement.**  
**Open questions / acceptance boundaries:** **5.**

“SOUND” means the particular inspected static control is adequate for the stated requirement. It does not mean the entire feature or every concurrency scenario has been executed successfully. Test bodies are distinguished from executed test results throughout.

### Existing architecture and versions

Keep the current Angular → authenticated ASP.NET API → Application commands/queries → Domain and EF/PostgreSQL structure. Existing code owners are identified in the [code map][MAP]; [AGENTS.md][AGENT] requires minimal changes, current scoped authority, retained evidence, focused local checks and full regressions in CI.

Inspected declarations pin .NET SDK **10.0.300**, EF Core **10.0.12**, Npgsql/provider **10.0.0**, Microsoft Graph **5.99.0**, Angular core/Material **22.2.1**, Angular CLI **22.2.3**, and npm **11.20.0**. TypeScript is declared as **~6.0.2**. These are source declarations, not an assertion about deployed installations or vendor support. No dependency upgrade is recommended. Sources: [global.json:1–7][SDK], [Directory.Packages.props:6–20][PKGS], [UI package.json:4–42][UIVERS].

## 2. Executive summary

**The remaining work is not another lifecycle rebuild.** The previously implemented backend EQR evidence binding, distinct-profile selection and offboarding finance composition remain visible in the current source. This report does not reopen the eight earlier remediation items as unimplemented.

Tracing the previously unverified EQR user journey establishes two narrower gaps:

1. **The completion checklist can report EQR as complete from the mutable case status alone, even when the signing command would reject its retained decision/evidence.** This is a confirmed readiness-display defect, not a demonstrated signing bypass.
2. **The native completion page does not expose the existing EQR review-basis and decision operations.** The backend supports them, but the inspected Angular workflow supplies EQR status presentation only.

The optional **REM-09** remains: progress evaluation materializes client-wide reporting-period/profile candidates before applying the engagement’s relevant service/date context. No measured performance incident is established.

Actual positive offboarding, revised professional EQR policy, browser acceptance, external permissions/custody and a passing full-release result remain separate decisions or verification boundaries. They are **not** converted into speculative implementation tasks.

**Priority:** correct the misleading EQR readiness projection; connect the existing, authorized EQR workflow without expanding professional policy; consider query narrowing only after measuring the relevant candidate workload.

### What this does and does not change from the previous review

| Previous pending item | Current conclusion |
|---|---|
| REM-09 query optimization | Still optional and unimplemented in the inspected candidate-selection path. |
| EQR user-facing acceptance | Now decomposed into a confirmed projection defect, a confirmed native-UI integration gap, and separate professional-policy/browser acceptance. |
| Positive client offboarding | The implemented command remains a blocked-review recorder. No successful exit policy is inferred from it. |
| Fast CI previously in progress | The inspected SHA now has a recorded successful fast-CI run. |
| Full-release / external acceptance | Still not established by inspected evidence; image/fast-CI success does not close these boundaries. |

## 3. Component and integration map

| Area | Inspected path through the system | Boundary to preserve |
|---|---|---|
| EQR presentation | `features/audit/completion.ts` → completion GET → `EngagementCompletionWorkspaceQuery.GetAsync` → `EqrCase` | Read permission is not decision authority; historical status is not current concurrence. |
| EQR review / decision | Completion API `eqr-review-basis` and `eqr-decision` → `AuditDeliverableService` → `EqrCase` / append-only `EqrDecision` | Assigned reviewer/current authorization, Engagement Partner exclusion, exact reviewed facts/report, expected revision and original request identity. |
| EQR signing | Sign endpoint → `SignIndependentReportAsync` → retained concurrence and report-binding checks | Preserve the protected command boundary; do not weaken signing to match the simpler display. |
| Progress candidates | Detail/client/portfolio progress → `EngagementProgressQuery.BuildAsync` → profiles, acceptances, reporting periods, packages and releases | Preserve authorized population, snapshot consistency, real ambiguity and exact entity-package release matching. |
| Offboarding | `RecordOffboardingCloseoutAsync` → locked readiness composition → finance-owned snapshot calculation → retained `BLOCKED_DECISION` | No automatic exit approval, archival, debt disposal, grant revocation or provider change. |
| Release verification | Existing nightly/manual `release-verification.yml` resolves one immutable requested commit | Full suites and provider acceptance are separate from fast checks and image publication. |

Sources: [completion API:29–61][ROUTES], [EQR read:595–631][EQREAD], [EQR command:634–714][EQCMD], [EQR persistence:717–785][EQSTORE], [signing:517–559][SIGN], [progress read:48–81][PREAD], [progress candidates:203–226][PCAND], [offboarding command:44–160][OFFCMD], [release workflow:1–122][FULLWF].

## 4. Feature audit matrix

| Feature | Atomic requirement | Status | Evidence — verified path:line | Action |
|---|---|---|---|---|
| R01 — EQR backend | Submit and replay a decision against the exact reviewed facts/report and original case revision. | SOUND | [AuditDeliverableService.cs:595–631][EQREAD] and [634–714][EQCMD]; [UiEndpoints.Completion.cs:54–61][ROUTES] | Preserve the implemented backend contract. |
| R02 — EQR signing | An existing EQR case requires a retained current concurrence bound to the report being signed. | SOUND | [AuditDeliverableService.cs:547–559][SIGN], [776–785][EQSTORE] | Preserve; no new applicability rule is asserted. |
| R03 — EQR records | Retain actual decision actor, exact evidence, revision and append-only history without manufacturing legacy provenance. | SOUND | [Completion.cs:106–144][MODEL]; [EqrDecisionHistory.cs:13–86][MIG1], [108–158][MIG2] | Preserve existing history and populated-downgrade protection. |
| R04 — EQR checklist | “EQR completed” reflects effective current decision evidence, not only `EqrCase.Status`. | PARTIAL | [EngagementCompletionWorkspaceQuery.cs:113–124][PROJ] versus [AuditDeliverableService.cs:547–559][SIGN] | F01 / PEND-01. |
| R05 — EQR native workflow | An eligible reviewer can inspect the existing basis and record/recover the supported decision from the native completion workspace. | PARTIAL | [UiEndpoints.Completion.cs:54–61][ROUTES]; [completion.ts:326–333][UIVIEW], [375–473][UISTATE]; endpoint search and full component read | F02 / PEND-02; no new role/applicability model. |
| R06 — Progress profiles | Separate accepted stages do not multiply one qualifying profile into false ambiguity. | SOUND | [EngagementProgressQuery.cs:213–220][PCAND]; [PortfolioWorkspaceTests.cs:257–330][PTEST] | Keep the acceptance-existence predicate. |
| R07 — Progress correctness | Respect explicit profile choice, genuine period ambiguity and engagement-local entity-package release matching. | SOUND | [EngagementProgressQuery.cs:291–361][PEVAL] | Preserve these predicates while optimizing; not approval of every service methodology. |
| R08 — Progress query bounds | Materialize candidates relevant to the evaluated non-audit service/period contexts, not unrelated client history. | IMPROVABLE | [EngagementProgressQuery.cs:213–226][PCAND], [294–361][PEVAL] | F03 / REM-09, optional and measurement-led. |
| R09 — Offboarding safety | A blocked-review receipt is not reported as a completed exit or combined archive action. | SOUND | [PracticeCrmService.OffboardingCloseout.cs:44–160][OFFCMD] | Preserve `BLOCKED_DECISION` and separate archival. |
| R10 — Offboarding finance | Compose the finance-owned calculation under the closeout’s supported snapshot contract. | SOUND | [OffboardingCloseout.cs:77–119][OFFCMD]; [OffboardingReadiness.cs:158–183][OFFFIN]; [FirmReceivablesAgingQuery.cs:44–74][FIN] | No repeat implementation of the repaired transaction mismatch. |
| R11 — Professional policy | Applicable EQR/service profiles, reviewer eligibility and legacy activation are professionally approved for the deployment. | UNVERIFIED | [roles-and-privileges-current.md:159–163][POLICY] identifies unresolved EQR decisions; no deployment acceptance was inspected | OQ-01; do not invent decisions. |
| R12 — Positive offboarding | Approved closure/disposition rules and evidence establish the relationship’s actual end. | UNVERIFIED | [OffboardingCloseout.cs:44–160][OFFCMD]; [OffboardingReadiness.cs:70–99][OFFCOVER] explicitly limit the current assessment | OQ-02; blocked-review functionality is not positive closeout. |
| R13 — Full release | A successful complete regression result is bound to the intended immutable candidate. | UNVERIFIED | [exact-head Actions listing][RUNLIST] contains fast CI/images, not an inspected successful full-release result; [workflow][FULLWF] | OQ-03 / verification only, not a replacement CI implementation. |
| R14 — Connected browser journey | Real native EQR and group/offboarding recovery journeys pass under realistic role, stale-state and interruption scenarios. | UNVERIFIED | Source/tests inspected; no browser journey executed by this review. Existing [Angular fixture][UITEST] and [backend test][EQTEST] do not establish that journey | OQ-04 / acceptance evidence. |
| R15 — External operations | Deployed Entra/Graph/SharePoint, report-signing and custody settings satisfy the relevant approved boundaries. | UNVERIFIED | No live tenant/provider configuration inspected; [AGENTS.md][AGENT] and [offboarding coverage][OFFCOVER] keep these separate | OQ-05; no speculative provider implementation. |

**Counts:** SOUND 7; PARTIAL 2; MISSING 0; IMPROVABLE 1; UNVERIFIED 5. “MISSING 0” does not mean all functionality exists: EQR is a **partially connected capability**, and deliberate policy/acceptance boundaries remain unverified.

## 5. Confirmed findings

### F01 — EQR completion presentation does not evaluate retained-evidence currentness

**Severity: Medium. Classification: PARTIAL. Requirement R04. Task PEND-01.**

**Current behavior:** The completion query adds an `EQR completed` gate with an `ok` tone whenever `eqr.Status == "CONCURRED"`. It does not inspect the decision pointer, decision revision, reviewed facts or report identity in that branch. The Angular page renders the server gates and the EQR status. [Projection:113–124][PROJ]; [UI:307–333][UIVIEW].

**Expected behavior:** Preserve historical case/decision information, but present completion as current only when the relevant existing retained-decision/report checks establish it. A case’s historical status alone must not imply current readiness.

**Established counterexamples from code:**

- A legacy `CONCURRED` case can have no current decision pointer/revision: the migration adds nullable/default-zero fields without inventing decision history. The checklist still renders `ok`; `CurrentEqrDecisionAsync` returns null and signing refuses.
- A report/facts change can make an earlier concurrence insufficient. Signing compares the decision’s reviewed digest/report identity, while the checklist’s EQR branch still tests only the stored case status.

Evidence: [migration:13–86][MIG1], [decision resolution:776–785][EQSTORE], [signing:547–559][SIGN].

**Root cause:** The command owner gained exact retained-evidence checks, but the completion projection still uses the earlier mutable-status shortcut.

**Impact:** Staff receive inconsistent readiness guidance and can be told review is complete before the signing command rejects it. **No signing bypass or production incident is established.** The counterexamples are statically traced; this review did not execute them.

### F02 — The current EQR backend is not connected to a native decision-entry workflow

**Severity: High for an engagement awaiting EQR. Classification: PARTIAL. Requirement R05. Task PEND-02.**

**Current behavior:** The API exposes both the authorized review-basis GET and version-bound decision POST. The inspected completion component decodes `eqrStatus`, displays it, and implements other completion actions, but has no EQR basis loader, decision form, submit handler or original EQR-request recovery action. The component’s imports contain no EQR child workflow. Repository searches for the endpoint/expected-basis terms were followed into the API, Application owner, current component and tests; this is not inferred from one unsuccessful search. [API:54–61][ROUTES]; [component schema/imports][UISCHEMA]; [component state/actions:375–473][UISTATE].

**Expected behavior:** The existing authorized reviewer workflow should be accessible through the native workspace, using the existing exact-basis and request-replay contract rather than requiring a caller to compose HTTP requests.

**Root cause:** Backend construction and its API/tests were completed without the corresponding native caller.

**Impact:** Backend availability does not establish an accountant-usable EQR journey. A required review cannot be completed through the inspected completion screen.

**Scope qualification:** This is **not** authority to introduce new EQR roles, decide applicability, relax independence or activate the later unapproved role specification. The current policy explicitly retains existing assignment/signing controls while keeping D-EQR-01–04 open. PEND-02 must be limited to existing supported authority/cases; any expansion stays at OQ-01. [Policy:159–163][POLICY].

### F03 — Non-audit completion candidate reads include unrelated history

**Severity: Low. Classification: IMPROVABLE. Requirement R08. Task REM-09.**

**Current behavior:** After determining that non-audit engagements exist, the query materializes qualifying profiles for the selected clients, all reporting periods for those clients, and packages for the selected engagement IDs. Service route, exact period dates and applicable basis are narrowed later in the engagement loop. [Candidates:203–226][PCAND]; [evaluation:291–361][PEVAL].

**Expected improvement:** Retrieve the candidate union needed for the evaluated non-audit contexts before materialization, preserving every candidate needed to detect genuine ambiguity.

**Impact:** The source demonstrates avoidable work that grows with unrelated history. It does **not** establish a latency incident, memory failure, missing index or SLA breach. Existing firm/client filters are not being characterized as a cross-tenant leak.

**Root cause:** Batched candidate discovery is client-wide, while the actual predicates are engagement-context-specific. Keep this optional; prefer a small query refinement only when measurement confirms useful reduction.

## 6. Atomic implementation plan

These tasks address only F01–F03. They do not recreate already implemented EQR storage, first-sign-in handling, group issuance/archive, offboarding recovery or task orchestration.

Before each change, Codex must read the current `AGENTS.md`, recheck `master`/local modifications and incoming paths, and preserve concurrent work. Reassess each finding against newer code; skip satisfied work with evidence. The implementer chooses the smallest compatible design. This report does not authorize a merge, deployment, provider mutation or destructive migration.

### PEND-01 — Make the EQR checklist distinguish recorded status from current concurrence
**Priority:** Essential / P1. **Objective:** Close F01 / R04.

**Verified affected components**
- `EngagementCompletionWorkspaceQuery.GetAsync`, `CompletionGate` and `EngagementCompletionWorkspace`. [PROJAUTH] [PROJ]
- Existing EQR decision/currentness/signing logic in `AuditDeliverableService`; `EqrCase` / `EqrDecision`. [SIGN] [EQSTORE] [MODEL]
- `features/audit/completion.ts` decoder/gate presentation and `completion.spec.ts`. [UISCHEMA] [UIVIEW] [UITEST]
- Existing `AuditDeliverablesTests` and the completion API contract. [EQTEST] [ROUTES]

**Required change**
1. Reuse or minimally extract the existing command-owner predicates that identify the effective retained concurrence and its report/facts binding. Do not create a parallel quality-review engine.
2. Project historical status separately from effective readiness. A stale, unbound or unavailable decision must not render the EQR-completed gate as `ok`.
3. Where decision attribution is displayed, distinguish the retained decision-maker from reviewer assignment. Do not manufacture a reviewer, report identity or concurrence for legacy rows.
4. Keep the protected signing checks authoritative. Do not relax them to match the old display.

**Permissions / failure cases**
The completion reader retains existing scope. Restricted evidence must not leak through explanations or newly added fields. No case means “not assigned / applicability not established,” not automatic clearance or an invented mandatory-review requirement. Preserve history when evidence becomes stale; do not overwrite the old decision.

**Binary acceptance criteria**
- A legacy `CONCURRED` case with no retained effective decision is not presented as current EQR completion.
- A valid current retained concurrence can display current completion for its exact report basis.
- Changing the relevant report/evidence makes the projection non-current without changing the retained decision.
- A later `CHANGES_REQUESTED` / `IN_PROGRESS` decision is not displayed as completed.
- Existing signing refusals and scope-denial cases remain intact.
- A missing EQR case does not become an automatically approved or automatically required review.

**Focused verification**
Extend the existing EQR/worksheet projection test fixture with current, legacy-unbound and stale-report cases; add the minimum Angular rendering assertions. The existing exact-basis test already regenerates a report to exercise stale submission, so reuse its setup rather than inventing a second comprehensive fixture. [EQTEST]

**Compatibility / rollout**
Prefer an additive read-model change and corresponding typed decoder/OpenAPI update if new fields are necessary. No new table or historical backfill is justified. Deploy API/UI compatibly; do not roll back by erasing decisions.

### PEND-02 — Connect the existing EQR review and decision contract to the native workspace
**Priority:** Essential / P1 for the requested end-to-end workflow. **Objective:** Close F02 / R05.  
**Dependency:** PEND-01 for truthful resulting readiness; professional-policy expansions remain OQ-01, not part of this task.

**Verified affected components**
- `features/audit/completion.ts` and `completion.spec.ts`. [UISTATE] [UITEST]
- `UiEndpoints.Completion.cs`: existing review-basis GET and decision POST, `EqrDecisionInput`. [API] [ROUTES]
- `GetEqrReviewBasisAsync`, `RecordEqrDecisionRequest`, `RecordEqrDecisionAsync` and retained decision state. [EQREAD] [EQCMD] [EQSTORE]
- `CompletionContractTraceTests.EqrDecisionBoundary_RejectsMissingReviewedBasis` and existing EQR domain regression. [APITEST] [EQTEST]

**Required change**
1. Add a focused EQR section or small existing-page child component; no separate application, generic workflow framework or new role catalogue.
2. Read the authorized basis and present the exact report identity/hash, case revision and relevant review context. The existing report download can support inspection; a hash alone is not evidence of a human review.
3. Expose only supported actions to the currently eligible reviewer. Preserve existing assignment checks, the Engagement Partner exclusion and server authority. Do not infer authority from navigation, a client-side job title or Administrator oversight.
4. Require explicit human submission. Send the exact reviewed case revision/facts/report identity with the supported decision content, discussion confirmation, notes and stable request key. A stale response requires re-review; never silently replace the submitted basis.
5. Bind drafts/assent to the current firm, actor/session and engagement. Invalidate assent on context or basis changes.
6. Reuse the existing original-command recovery approach: after an unknown outcome, preserve the original identity/basis and recover that exact operation before permitting a replacement. Do not automatically re-submit a different decision or treat refreshed status as proof of the original request.
7. Confirm the retained result, refresh the effective projection and distinguish “decision recorded” from “current concurrence.” Do not add signing or release effects to this UI action.

**Permissions / policy limits**
Construction covers the **existing supported reviewer/assignment contract**, not a new applicability policy. Do not add automatic case creation/assignment, make reviews universally mandatory, grant a new EQR role, reinterpret Administrator eligibility, or transform legacy cases into approved reviews. Existing-case decision UI may require a small authoritative availability projection; that is a proposed engineering detail, not an existing API claim. Any newly activated population or changed professional authority must remain blocked pending OQ-01.

**Binary acceptance criteria**
- An already eligible assigned reviewer can inspect basis A and submit a supported decision carrying A; the retained decision ID and refreshed state are shown.
- The Engagement Partner, an unrelated client/engagement user and an unauthorized/unassigned reviewer cannot submit or obtain restricted basis through UI or direct HTTP.
- Evidence changes from A to B before submission: A is rejected; no concurrence on B is manufactured.
- Editing a decision or navigating to another context invalidates the old assent; delayed responses cannot set success in the new context.
- A simulated lost response is recoverable with the same request/basis/content and resolves to the original decision; a changed-intent replay remains a conflict.
- Storage/recovery failure produces an actionable blocked state, not success, a new request identity or plaintext persistence of sensitive notes.
- The recorded decision does not itself sign a report, clear unrelated holds, release an artifact or mark professional applicability approved.

**Focused verification**
Add one native valid-review journey and its meaningful stale/denial/recovery cases using synthetic identities. Extend the existing Angular spec and API boundary test where each adds distinct evidence; preserve the domain replay tests. Exercise keyboard/focus and route/session replacement in the actual browser before calling the UI accepted.

**Schema / rollout**
The required decision history and basis/replay fields already exist. Do not add another decision table. Keep existing request keys, the `v2:` provenance distinction and immutable history. Build the presentation without activating unapproved methodology. Deploy contract-compatible API/UI changes together; disabling the new UI must not destroy retained decisions.

### REM-09 — Narrow completion candidates to evaluated contexts
**Priority:** Optional / P3. **Objective:** Address F03 / R08 without changing completion policy.

**Verified affected components**
- `EngagementProgressQuery.BuildAsync` candidate queries and the current per-engagement selection predicates. [PCAND] [PEVAL]
- Existing detail/client/portfolio callers and `PortfolioWorkspaceTests.NonAuditProgressReachesCompletionWhenProfileIsApprovedAndEvidenceIsCurrent`. [PREAD] [PTEST]

**Required change**
First measure candidate counts and the generated query plan on representative isolated test data. If worthwhile, push the relevant non-audit client/service/date context into candidate discovery and avoid materializing unrelated histories. Keep the current acceptance-existence predicate. Let Codex choose the minimal translated query shape; do not prescribe a new repository abstraction, cache, dynamic-query framework or dependency.

**Binary acceptance criteria**
- Current gates, percentages and selected identities remain unchanged across detail/client/portfolio views for the same authorized data.
- Adding unrelated historical periods/profiles does not increase the materialized candidate population for the fixed evaluated context beyond the deliberately supported query shape.
- Two accepted stages for one profile still yield one profile; genuinely distinct eligible profiles/period bases remain ambiguous.
- Explicit profile choices, wrong-scope exclusions, missing profiles and entity-package release matching retain their current behavior.
- No arbitrary `Take` limit silently suppresses a valid candidate or makes ambiguous data appear unique.
- Before/after measurements show a concrete reduction without an unacceptable increase in round trips or query complexity; otherwise defer this optional change with evidence.

**Verification / risk**
Extend the existing progress fixture with relevant versus unrelated histories and preserve revocation/snapshot regressions. Measure in the isolated test database, not production. No schema/index migration is warranted without a demonstrated query-plan need. Rollback is a code revert; it must not alter stored approvals or profile policy.

## 7. Open questions and unverified areas

These are **not implementation tasks**. Missing acceptance evidence is not evidence of a new software defect.

| ID | What remains unknown | Safe default / evidence needed |
|---|---|---|
| OQ-01 — Professional EQR/service authority | Which deployed service profiles, reviewer eligibility rules, exact evidence-change boundary and legacy activation policy have qualified approval? The current roles document explicitly leaves D-EQR-01–04 open. | Preserve existing guards and assignments; do not activate the later role specification or invent method-owner approval. Identify approved policy versions and selected cases before expanding PEND-02’s operational population. [POLICY] |
| OQ-02 — Positive offboarding | What actions and authorized dispositions establish an actual completed relationship exit, including open work, receivables/credits, unbilled work, access and retained records? | Keep `BLOCKED_DECISION` and separate archival. Obtain an approved closeout policy and the owning modules’ evidence. Do not rename blocked reviews to completed exits, require every debt to become zero without policy, write off balances, or delete identities/evidence. [OFFCMD] [OFFCOVER] |
| OQ-03 — Exact full-release acceptance | Has the intended immutable release candidate passed the complete Domain/API/browser/Angular contract with retained source-bound results? | Run/read the existing full workflow on the chosen candidate; inspect its resolved-source metadata, job results and relevant artifacts. The reviewed exact-head listing establishes fast/image success only. [FULLWF] [RUNLIST] |
| OQ-04 — Interactive connected acceptance | Do native EQR review, group issue/archive and offboarding recovery behave correctly under real navigation, session replacement, interruption and accessibility interactions? | Obtain current-source browser/network and persisted-outcome evidence. Do not substitute a component mock, test name or backend test for the full journey. This review performed none. |
| OQ-05 — External configuration and custody | Are deployed Entra/Graph/SharePoint permissions, provider readback, signing credentials, storage/retention and operational recovery approved and verified for the actual environment? | Keep external effects and unsupported claims blocked. Inspect authorized configuration and exact-resource receipts with the responsible owner; no new provider or broad permission change is justified by this review. [AGENT] [OFFCOVER] |

The absence of a positive exit outcome in the inspected command is **verified**; what is unverified is the approved target exit policy and acceptance needed to specify it safely. Similarly, PEND-02 closes an existing UI connection; it does not resolve OQ-01 by choosing professional policy.

## 8. Verification and rollout

### What this review observed

| Evidence class | Result / limitation |
|---|---|
| Pinned static source | Relevant code, API inputs, models, migration protections, selected UI/test bodies and declared dependencies inspected at `538bc79…`. |
| Exact-head fast CI | GitHub records **success** for [run 41311277764][CI], including Docs Health, Angular build/test job, .NET Release, fast database-free guards, EF/OpenAPI checks and publication. Run/job metadata was inspected; this is not a full database regression result. |
| Image workflow | GitHub records **success** for [run 41311427566][IMAGES], with API, worker, migrations and summary jobs successful. Image bytes, deployed use and requested-source provenance were not independently validated from downloaded artifacts. |
| Full-release result | No passing full-release result was established for this candidate in the inspected evidence. The [exact-head listing][RUNLIST] returned the above two runs. Workflow-head metadata is not a substitute for the full workflow’s resolved requested SHA. |
| Test bodies | The EQR domain test exercises changed-report refusal and original-request replay; the API test rejects missing reviewed basis; the progress fixture includes both accepted stages on one profile. These assertions were read, not run by the reviewer. [EQTEST] [APITEST] [PTEST] |
| Local / browser / external checks | **NOT_RUN** by this review. No full-suite, performance, migration, live-provider or deployment acceptance claimed. |

### Suggested focused commands — not executed

Use the repository’s current installed/pinned toolchain and approved isolated test database. Run a focused cohort after a coherent change; do not reinstall or run the full suite after every edit. Existing verified targets:

```bash
# Existing EQR evidence/replay regression; extend nearby assertions for PEND-01.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~EqrDecisionRequiresExactReviewedBasisAndRecoversOriginalIntent'

# Existing EQR HTTP validation boundary; extend only where the changed contract needs it.
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~EqrDecisionBoundary_RejectsMissingReviewedBasis'

# Native completion presentation/interaction regressions.
npm --prefix src/AuditSphereOps.Ui run test:ci -- \
  --include 'src/app/features/audit/completion.spec.ts'

# Optional query refinement: retain current progress/profile semantics.
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj \
  --no-restore -c Release --maxcpucount:1 \
  --filter 'FullyQualifiedName~NonAuditProgressReachesCompletionWhenProfileIsApprovedAndEvidenceIsCurrent'

# Only when API contracts are changed.
bash scripts/contracts/verify-openapi.sh

git diff --check
```

These are existing source-verified targets, not a claim that they already cover the proposed new UI/projection cases. Add minimal high-signal assertions to them or adjacent appropriate tests. Report `BLOCKED_TEST_ENV` rather than substituting production data when the isolated PostgreSQL environment is unavailable.

### Dependency-ordered rollout

1. **PEND-01:** correct EQR readiness presentation while leaving signing enforcement and retained history unchanged.
2. **PEND-02:** connect the supported native workflow using that truthful projection and the existing exact-basis/replay contract. Exercise the complete human action with synthetic authorized users before operational acceptance.
3. **REM-09:** optional, independent and measurement-led; do not delay correctness fixes for it.
4. For the chosen immutable candidate, obtain successful exact-source fast checks and the existing full-release workflow result. Diagnose any actual failures from their logs; do not predeclare unrelated defects or create a replacement CI pipeline.
5. Close OQ-01/02/04/05 with their respective professional, browser and external evidence. CI success does not authorize those decisions or a deployment.

**Main regression risks:** treating a historical `CONCURRED` string as current clearance; accidentally widening completion-reader access to reviewer-only evidence; losing an unresolved request on context change; silently rebinding assent to newer evidence; suppressing real profile/period ambiguity during optimization; and accepting a successful fast/image run as proof of end-to-end release readiness.

**Rollback:** Prefer a reviewed code revert or disabling the newly exposed UI, preserving append-only decisions and existing server gates. No destructive downgrade, data cleanup, reattribution of legacy decisions, grant rewrite or automatic provider rollback is specified.

## 9. Out of scope

Reimplementation of the earlier eight backend/UI remedies; a full A–X codebase re-audit; new service routes, tax filing or advisory modules; a new EQR role/applicability system; positive-offboarding policy invention; external provider replacement; dependency upgrades; broad refactoring; and any repository mutation, live tenant action, merge or deployment.

**Conclusion:** The focused code work is two EQR presentation/workflow corrections plus one optional progress-query refinement. Professional policy, positive exit acceptance, real browser behavior, external configuration and full-release evidence remain explicit boundaries—not fabricated implementation work or a claim of “100% completion.”

<!-- Commit-pinned evidence references; line ranges are original repository source lines. -->

[RHEAD]: https://github.com/nirzaf/AuditSphere/commit/538bc79fc977a4552277a0b9521f73622e04fbba
[DIFF]: https://github.com/nirzaf/AuditSphere/compare/c9a2a24db8a565df3337fb13665fd9ad500e0d23...538bc79fc977a4552277a0b9521f73622e04fbba
[AGENT]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/AGENTS.md#L5-L112
[MAP]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/docs/architecture/auditsphere-architecture-code-map.md#L253-L288
[SDK]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/global.json#L1-L7
[PKGS]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/Directory.Packages.props#L6-L20
[UIVERS]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Ui/package.json#L4-L42
[PREAD]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Practice/EngagementProgressQuery.cs#L48-L81
[PCAND]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Practice/EngagementProgressQuery.cs#L203-L226
[PEVAL]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Practice/EngagementProgressQuery.cs#L291-L361
[PTEST]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/tests/AuditSphereOps.Domain.Tests/PortfolioWorkspaceTests.cs#L257-L330
[API]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Api/Ui/UiEndpoints.Completion.cs#L14-L16
[ROUTES]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Api/Ui/UiEndpoints.Completion.cs#L29-L61
[EQREAD]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs#L595-L631
[EQCMD]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs#L634-L714
[EQSTORE]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs#L717-L785
[SIGN]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs#L517-L559
[PROJ]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Completion/EngagementCompletionWorkspaceQuery.cs#L113-L124
[PROJAUTH]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Completion/EngagementCompletionWorkspaceQuery.cs#L39-L62
[UISCHEMA]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Ui/src/app/features/audit/completion.ts#L15-L48
[UIVIEW]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Ui/src/app/features/audit/completion.ts#L307-L333
[UISTATE]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Ui/src/app/features/audit/completion.ts#L375-L473
[UICOMMANDS]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Ui/src/app/features/audit/completion.ts#L214-L265
[UITEST]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Ui/src/app/features/audit/completion.spec.ts#L15-L66
[EQTEST]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.cs#L1274-L1324
[APITEST]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/tests/AuditSphereOps.Api.Tests/CompletionContractTraceTests.cs#L101-L127
[MODEL]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Domain/Completion/Completion.cs#L106-L144
[MIG1]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261031182610_EqrDecisionHistory.cs#L13-L86
[MIG2]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261031182610_EqrDecisionHistory.cs#L108-L158
[POLICY]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/docs/architecture/auditsphere-architecture-user-roles-and-privileges-current.md#L159-L163
[OFFCMD]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Practice/PracticeCrmService.OffboardingCloseout.cs#L44-L160
[OFFCOVER]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Practice/PracticeCrmService.OffboardingReadiness.cs#L70-L99
[OFFFIN]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Practice/PracticeCrmService.OffboardingReadiness.cs#L158-L183
[FIN]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/src/AuditSphereOps.Application/Practice/FirmReceivablesAgingQuery.cs#L44-L74
[FULLWF]: https://github.com/nirzaf/AuditSphere/blob/538bc79fc977a4552277a0b9521f73622e04fbba/.github/workflows/release-verification.yml#L1-L122
[CI]: https://github.com/nirzaf/AuditSphere/actions/runs/41311277764
[IMAGES]: https://github.com/nirzaf/AuditSphere/actions/runs/41311427566
[RUNLIST]: https://api.github.com/repos/nirzaf/AuditSphere/actions/runs?head_sha=538bc79fc977a4552277a0b9521f73622e04fbba&per_page=10
