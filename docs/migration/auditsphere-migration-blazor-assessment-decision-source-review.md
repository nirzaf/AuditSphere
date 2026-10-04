# AuditSphere — Assessment Decision Source-Action Review

**Disposition:** `PARTIAL` for `AssessmentDecision.razor` only. The wider acceptance family and the other acceptance source files remain open.

**Reviewed source snapshot:** `40ada3c9744e5b72b160dcd6a1739c07a62e298c` (`master`, 2026-10-04).

## Source-to-Angular crosswalk

| Legacy behavior | Angular/API behavior | Evidence and result |
|---|---|---|
| `/app/assessments/{Id}/decision` accepts a client ID and presents the Partner decision form. | The Angular route uses `AssessmentRoute`, which resolves the authorized client and redirects to the current client acceptance workspace. The decision editor is available only when the server projection sets `canDecide`. | `app.routes.ts`, `assessment-route.ts`, `AssessmentRouteQuery.ResolveAsync`, `AcceptanceWorkspaceQuery`, and `checklist.html`. The route catalogue contract confirms the native route exists. The exact legacy decision URL has not yet been exercised as a browser journey. |
| Require an authenticated internal actor with a current `Partner` grant for the exact client; read the current client input generation before editing. | The command API derives identity from the trusted cookie, validates CSRF on writes, and delegates authority to `AssessmentCommandWorkspace`. `DECISION` requires `Partner`; authorization and generation are rechecked while publishing. | `UiEndpoints.Acceptance.cs`, `AssessmentCommandWorkspace.cs`, `AssessmentCommandReceiptApiTests.ReviewedAssessmentCsrfRecoveryIsolationAndRetainedEvidenceAreEnforced`, and `AcceptanceChecklistTests.Receipts`. A separate reviewed-`DECISION` revocation-during-publication regression is still needed. |
| Capture outcome, service route, rationale and conditions; require outcome, route and rationale; prevent invalid conditions. | The Angular Signal Form presents the same decision fields and validates them before preview. The server rejects malformed or irrelevant fields and blocks acceptance when checklist conditions are not ready. | `assessment-command-editor.html`, `assessment-command-editor.ts`, `assessment-command-contracts.ts`, and `AssessmentCommandWorkspace.cs`. |
| Record an immutable decision against the expected generation; stale generation or lost authority must not publish. | Angular first requests an exact preview, shows the proposed decision and effect, then requires fresh explicit assent. The Application command serializes publication and retains the decision plus actor-owned receipt atomically; retries reconcile the same request. | `checklist.html`, `checklist.ts`, `AssessmentCommandWorkspace.cs`, `AcceptanceChecklistTests.Receipts.DecisionReceiptRecoversAfterImmutableDecisionWithoutRepeatingIt`, and the direct-decision authority test `AcceptanceChecklistTests.AssessmentCommandRefusesAnActorRevokedWhileWaitingForPublicationLock`. The latter does not exercise the newer reviewed-command receipt path. |
| On success, return to the assessment record; do not imply that a professional decision creates an engagement or grants Microsoft access. | The accepted command receipt is acknowledged in the current assessment workspace. Activation remains a separate reviewed Partner workflow. | `AngularAssessmentParityJourneyTests.ExactHistoryProfileProgressAndCurrentDecisionAssentRemainScoped`; `EngagementActivationWorkspace` and its separate acceptance tests. |

## Existing verification evidence

- The Angular assessment E2E journey covers both canonical and `/ui` host modes, historical decision display, role-gated current decision controls, field editing, exact preview, changed-rationale assent invalidation, successful receipt acknowledgement, and safe handling of a guessed decision ID. Its lost-response browser recovery path exercises an answer command, not a Partner decision. At `c562da2f109ce26ab2f061c00976823ff06aea61`, the complete E2E project passed 219/219 with no failures or skips; see `verification.angularBlazorE2EHostDecoupling` in `docs/execution/status.json`.
- PostgreSQL-backed API and Domain evidence covers CSRF, replay, actor/client isolation, current authority, decision readiness, immutable evidence, and revocation during publication. The focused results are recorded in `angularAssessmentCommandReceipts` and its related assessment entries in `docs/execution/status.json`.
- The current route/component and command sources are unchanged between the reviewed snapshot and those recorded focused runs. The only later change to the assessment E2E journey opts it out of starting the legacy Web host.

## Remaining work

1. Add and run a browser journey starting at the exact legacy `/app/assessments/{clientId}/decision` deep link in canonical and `/ui` route modes. Assert that it resolves to the authorized current assessment and exposes the decision editor only for a current Partner grant.
2. Add a decision-specific reviewed-command revocation test and a lost-response/reload browser journey, asserting one immutable decision, one receipt and no duplicate dispatch.
3. Review `AssessmentDetail.razor` and `AcceptanceChecklistPanel.razor` separately. This review does not cover questionnaire editing, specialist workflows, historical selection, timeline, progress or the rest of the acceptance source-action set.
4. The automated checks cited above are existing evidence; no new runtime tests were run for this documentation review. The full Blazor-removal gate remains `NOT_READY`.
