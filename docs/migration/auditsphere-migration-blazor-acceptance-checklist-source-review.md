# Angular migration source review — acceptance checklist

**Status:** PARTIAL_REVIEWED  
**Reviewed against code:** `147ac2a02675b9c65ec43be520c76bc27837a167`  
**Pinned discovery snapshot:** `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`

This review compares the legacy `AcceptanceChecklistPanel.razor` user actions
with the current Angular/API/Application path. Its source hash matches the
pinned discovery snapshot:

`src/AuditSphereOps.Web/Components/Acceptance/AcceptanceChecklistPanel.razor`  
SHA-256: `9daa133edad9512b70188a3c119bafa2a8333bdce60cc8cedec62837f71b6b59`

## Action mapping

| Legacy behavior | Angular/API/Application owner | Evidence and remaining boundary |
|---|---|---|
| Load the server-derived new-client or continuance checklist, current answers, blockers and prior-cycle answers. | `AssessmentWorkspaceQuery` projects `/api/ui/clients/{id}/assessment`; Angular `features/acceptance/checklist.ts` decodes a bounded response, checks its client identity and current session, and discards stale route replies. | `AcceptanceJourneyTests.PartnerCompletesEvidenceAndSpecialistReviewBeforeAcceptingClient` verifies the new-client to continuance transition. Angular now displays the prior answer's evidence reference, matching the legacy panel. Full role/scope and stale-read coverage remains open. |
| Record a question answer and evidence reference. | `AssessmentCommandWorkspace` previews and records `ANSWER`; `AssessmentChecklistService.RecordAnswerAsync` remains the business authority. Angular uses typed Signal Forms, requires evidence when configured, binds review to the exact question revision/generation, and reconciles the immutable receipt without retrying an unknown command. | The PostgreSQL Playwright journey verifies required evidence and retained-receipt recovery after a lost response. A full answer-type, validation and failure matrix remains open. |
| Request a specialist review, then record its result. | `REQUEST_REVIEW` and `RECORD_REVIEW` use the same reviewed-preview, explicit-assent, receipt and recovery path. The Angular editor supports `CLEARED`, `HOLD` and `CONDITIONS`, including required evidence or conditions. | The journey requests a review, records a hold, then records a cleared result with evidence. The separate workspace projection reconstructs a bounded specialist timeline from immutable receipts. Every role and error combination is not yet covered. |
| Start the next continuance evaluation after an accepted decision. | The Angular `CONTINUANCE` command is available only when the projected current authority allows it; the Application service advances the client generation and selects the delta bank from stored decisions. | The PostgreSQL journey verifies the next generation and delta-only question bank. Command-specific stale and lost-response cases remain part of the open recovery matrix. |
| Show current answer evidence, answer revision, author, blockers and clearance state. | The Angular checklist response contains current and prior evidence references, revisions, specialist statuses and server-derived blockers. | Contract tests validate bounded prior-evidence decoding; the browser journey asserts the prior evidence reference is rendered. A complete field-by-field parity inventory remains open. |

## Authorization boundary

The Angular workspace uses the current application assessment role set and
keeps Partner decisions separate from answer and specialist-review authority.
`AssessmentRoutesPreserveLegacyStaffBoundaryAndRequirePartnerForDecisionDeepLink`
and the API-host journeys cover the relevant Staff, Manager, Partner and Senior
boundaries. The legacy checklist service's broad internal read helper is not
copied as a new Angular grant: the current assessment route intentionally
denies Senior and Reviewer identities that are outside its approved view
boundary. The narrower route is a security decision, not a missing role to add
without approval.

## Local verification

- Angular CI: 483/483 passed across 94 files.
- Angular production build passed; the existing initial bundle is 506.60 kB,
  6.60 kB above its 500 kB warning budget.
- PostgreSQL-backed API-host Playwright: 1/1 passed in 32 seconds, including
  the earlier continuance answer and its evidence reference.
- The Codex built-in browser loaded the Development acceptance route and showed
  its generic unavailable state to the current unassigned Development identity.
  No client details or commands were exposed. Positive authorized behavior was
  verified with isolated synthetic identities in Playwright.
- No EF entity/model changed; the pending-model check was not rerun. The full
  solution regression was not rerun; its latest complete pass remains 1001/1001
  at `ead85032de2ccc4d4c8043398fa8471d395376a9`.

The source row remains `PARTIAL`: the focused journeys establish important
positive and negative behavior, but they do not prove the complete role/scope,
validation, stale-content, external failure and retry/recovery matrix. This
review does not change the `NOT_READY` Blazor-removal decision.
