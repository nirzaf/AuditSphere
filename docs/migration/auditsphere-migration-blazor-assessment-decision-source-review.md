# AuditSphere — Assessment Decision Source-Action Review

**Disposition:** `PARITY_VERIFIED` for the reviewed `AssessmentDecision.razor` behavior at the pinned source snapshot. The review found and fixed a disclosure through the Partner-only decision deep link; PostgreSQL-backed role assertions and canonical plus `/ui` browser journeys now verify the corrected boundary and decision recovery. This disposition applies only to this source artifact, not to the surrounding assessment feature family or migration gate.

**Reviewed legacy source snapshot:** `d00f63edb0fad05f7384c0eb8d2dafbc4c1a1971`. The source hash is recorded in `docs/execution/angular-source-inventory.json`; the review applies to that exact historical artifact.

## Source-to-Angular crosswalk

| Legacy behavior | Angular/API behavior | Evidence and result |
|---|---|---|
| `/app/assessments/{Id}/decision` accepts a client ID and opens the Partner decision workflow. | The Angular compatibility route now uses a separate `/api/ui/assessments/{id}/decision` resolver, then redirects to the current assessment workspace. | `AssessmentRouteQuery.ResolveDecisionAsync`, `UiEndpoints.Routes`, and `assessment-route.ts`. The prior resolver did not distinguish the Partner-only decision URL from an ordinary assessment link. |
| Require an authenticated internal actor with current Partner authority for the exact client; a weaker client-scoped role must not see the decision page's assessment data. | The dedicated resolver requires a current Partner grant. Ordinary assessment reads now use the role set present in the legacy detail page; `Senior` and `Reviewer` do not gain access through this route. | PostgreSQL-backed role-boundary test and current API-only browser journeys pass; the Senior sees a generic denial and no assessment profile. |
| Capture outcome, service route, rationale and conditions; validate required fields and preserve review of the exact proposed action. | Angular presents the same decision fields, validates them, renders an exact-action preview, and requires fresh explicit assent before dispatch. | `assessment-command-editor.html`, `assessment-command-editor.ts`, `assessment-command-contracts.ts`, and the decision journey's preview and assent assertions. |
| Persist an immutable decision against the expected assessment generation; revoked authority or stale state must not publish. | The reviewed Application command rechecks authority and generation while publishing. If authority is revoked after preview, it persists neither a decision nor a receipt. | `AcceptanceChecklistTests.Receipts.ReviewedDecisionCommandRefusesAuthorityRevokedAfterPreview` passed 1/1. The journey verifies exactly one decision and one DECISION receipt after recovery. |
| Recover safely if the accepted command response is lost; do not dispatch a duplicate decision. | On a dropped response, Angular shows request recovery. After reload it reconciles the retained receipt, allows acknowledgement, and displays the recorded decision without resubmitting. | The browser journey drops the accepted POST response, reloads, reconciles and acknowledges the receipt; it asserts one dispatch, one immutable decision and one receipt. |
| A professional decision must not create an engagement or grant Microsoft access. | Engagement activation and Microsoft administration remain separate workflows with separate authority. | `EngagementActivationWorkspace` and its acceptance tests are separate from the decision command. No Entra role or access grant is created by this action. |

## Verification evidence

- At code/test commit `ce307625`, the updated API-only Playwright journey passed **2/2** across canonical and `/ui` route modes. The Senior received `Access denied`, no assessment profile or decision action rendered, and the Partner completed preview, explicit assent, lost-response recovery and receipt acknowledgement with exactly one dispatch, decision and receipt.
- The PostgreSQL-backed role-boundary regression passed **1/1**. The reviewed-command revocation regression passed **1/1** at its previously recorded code checkpoint. The Angular production build passed with its existing 5.46 kB initial-bundle budget warning.
- The full solution regression remains open. No result from the prior checkpoint is promoted to current-head acceptance.

## Remaining review boundary

`AssessmentDetail.razor`, `AcceptanceChecklistPanel.razor`, questionnaire editing, specialist workflows, historical selection, timeline, progress, and the remaining source/action rows are not covered by this review. The overall migration gate remains `NOT_READY`; see [`auditsphere-migration-blazor-removal-readiness.md`](auditsphere-migration-blazor-removal-readiness.md).
