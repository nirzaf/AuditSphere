# AuditSphere — Assessment Decision Source-Action Review

**Disposition:** `PARITY_VERIFIED` for `AssessmentDecision.razor` only. This result does not establish parity for the broader acceptance family or authorize Blazor retirement.

**Reviewed legacy source snapshot:** `d00f63edb0fad05f7384c0eb8d2dafbc4c1a1971`. The source hash is recorded in `docs/execution/angular-source-inventory.json`; the review applies to that exact historical artifact.

## Source-to-Angular crosswalk

| Legacy behavior | Angular/API behavior | Evidence and result |
|---|---|---|
| `/app/assessments/{Id}/decision` accepts a client ID and opens the Partner decision workflow. | The Angular compatibility route resolves the authorized client and redirects to its current assessment workspace. The decision action is shown only when the API projection grants current Partner authority. | `app.routes.ts`, `assessment-route.ts`, `AssessmentRouteQuery.ResolveAsync`, and `AcceptanceWorkspaceQuery`. The browser journey opens the exact URL in canonical and `/ui` modes and confirms the resulting assessment route. |
| Require an authenticated internal actor with current Partner authority for the exact client; a weaker client-scoped role must not expose the decision action. | The API derives the actor from the trusted cookie and applies the client-scoped server authorization decision. The Senior control user can view the assessment but receives no Partner decision action. | `AngularAssessmentDecisionRouteJourneyTests.LegacyDecisionDeepLinkEnforcesPartnerGrantAndRecoversLostDecisionOnce`, with a client-scoped Senior control and an authorized Partner actor, passed in both route modes. |
| Capture outcome, service route, rationale and conditions; validate required fields and preserve review of the exact proposed action. | Angular presents the same decision fields, validates them, renders an exact-action preview, and requires fresh explicit assent before dispatch. | `assessment-command-editor.html`, `assessment-command-editor.ts`, `assessment-command-contracts.ts`, and the decision journey's preview and assent assertions. |
| Persist an immutable decision against the expected assessment generation; revoked authority or stale state must not publish. | The reviewed Application command rechecks authority and generation while publishing. If authority is revoked after preview, it persists neither a decision nor a receipt. | `AcceptanceChecklistTests.Receipts.ReviewedDecisionCommandRefusesAuthorityRevokedAfterPreview` passed 1/1. The journey verifies exactly one decision and one DECISION receipt after recovery. |
| Recover safely if the accepted command response is lost; do not dispatch a duplicate decision. | On a dropped response, Angular shows request recovery. After reload it reconciles the retained receipt, allows acknowledgement, and displays the recorded decision without resubmitting. | The browser journey drops the accepted POST response, reloads, reconciles and acknowledges the receipt; it asserts one dispatch, one immutable decision and one receipt. |
| A professional decision must not create an engagement or grant Microsoft access. | Engagement activation and Microsoft administration remain separate workflows with separate authority. | `EngagementActivationWorkspace` and its acceptance tests are separate from the decision command. No Entra role or access grant is created by this action. |

## Verification evidence

- At code/test commit `af19fda6`, the focused PostgreSQL-backed Playwright journey passed **2/2**: the exact deep link in canonical and `/ui` modes, weaker-role denial, Partner preview/assent, lost-response reload recovery, and single-receipt/single-decision assertions.
- The reviewed-command revocation regression passed **1/1** against the PostgreSQL-backed Domain test project.
- The Domain.Tests and E2E.Tests Release project builds passed with zero warnings and errors at that checkpoint.
- These are focused results, not a current whole-solution regression. The later API-only fixture conversion is a separate in-progress change and requires a fresh focused run before its own acceptance.

## Remaining review boundary

`AssessmentDetail.razor`, `AcceptanceChecklistPanel.razor`, questionnaire editing, specialist workflows, historical selection, timeline, progress, and the remaining source/action rows are not covered by this review. The overall migration gate remains `NOT_READY`; see [`auditsphere-migration-blazor-removal-readiness.md`](auditsphere-migration-blazor-removal-readiness.md).
