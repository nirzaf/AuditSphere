# Workpaper source/action review

**Disposition: PARTIAL**
**Reviewed against repository commit:** `e8c1788a0847517375b3e93861937c866a6028f3`
**Review observed:** 2026-10-05

## Legacy behavior and Angular ownership

The legacy page at `src/AuditSphereOps.Web/Components/Pages/Workpaper.razor`
owns `/app/audit/workpapers/{Id:guid}`. It presents workpaper details, the
planned-procedure link, frozen submission history, a working draft editor, and
separate submitted-snapshot content. For a working paper it supports save,
discard, submit-for-review, reload-current-target, and server-side autosave.
Submission freezes the exact saved content. It also clears protected state
when the current identity or scope cannot read the record.

The Angular owner is
`src/AuditSphereOps.Ui/src/app/features/audit/workpaper.ts`, served through the
API-host routes. The API exposes:

- `GET /api/ui/audit/workpapers/{id}`
- `POST /api/ui/audit/workpapers/{id}/draft`
- `POST /api/ui/audit/workpapers/{id}/draft/discard`
- `POST /api/ui/audit/workpapers/{id}/submit`

The read projection is `AuditRecordQueries.WorkpaperAsync`; the commands are
`AuditPlanningService.SaveWorkpaperDraftAsync`,
`DiscardWorkpaperDraftAsync`, and `SubmitWorkpaperAsync`. Angular does not call
the database or Microsoft Graph directly. The query checks firm identity and
current audit authorization for the exact client and engagement. Commands
re-check the engagement scope and fence writes by workpaper revision, input and
policy generations, draft revision, and save identity. A discarded draft keeps
its lifecycle record; a submission consumes the exact draft and creates an
immutable submission snapshot.

## Verification evidence

The source-discovery inventory's Workpaper hash matches the reviewed Razor
file: `f8ad1c0396637bda6e119eda461f5b6a5cae27d2db999bf050ecf4baca1264b4`.
The Angular page hash is
`1e0b96907b3a4fc1a551f7508b6e7e74c74ec3db5e9692454f5517347b56fe48`.

The focused PostgreSQL-backed API-host browser cohort passed **5/5**, with no
failures or skips. It exercised the Workpaper save/recovery journey together
with client-scope detail denial, access-revocation clearing, and fieldwork to
workpaper linkage:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj \
  --no-restore --configuration Release -m:1 \
  --filter 'FullyQualifiedName~AngularWorkpaperSaveRecoveryJourneyTests|FullyQualifiedName~AngularClientScopeAuditDetailJourneyTests|FullyQualifiedName~AngularAuditRevocationParityJourneyTests|FullyQualifiedName~AngularAuditFieldworkParityJourneyTests'
```

The existing Workpaper Angular unit suite covers discard, navigation choices,
lost-save reconciliation and exact idempotent retry, changed-target refusal,
revocation, route reuse, and `beforeunload`. The browser save/recovery journey
records a draft while dropping the acknowledgement, confirms the exact
persisted save without a duplicate POST, then submits once and checks the
persisted draft and immutable submission. The adjacent detail journey covers
client/engagement scope, sibling and guessed identifiers, and denied content.
The Development built-in-browser check rendered the generic unavailable state
for an unassigned guessed ID without workpaper content or commands.

## Gaps keeping this row partial

- The complete role-by-role and grant-scope matrix, including expired grants
  and cross-firm IDs, is not yet directly asserted over the Angular HTTP and
  browser surface.
- Every stale, unavailable, validation, timeout, and recovery state is not yet
  covered end-to-end.
- Draft discard's persisted lifecycle and cleared editor are now covered by a
  dedicated Angular/API-host browser journey. The broader discard-on-navigation
  and refused-discard recovery combinations remain outside that journey.
- Human screen-reader and broader locale acceptance remain external to the
  current DOM and automated-browser evidence.

This review does not promote the row to parity verified. It closes one source
review gap only; the wider AS-PAR-002 authorization/retry audit, production
acceptance, and Blazor retirement remain open.

### Follow-up browser evidence — 2026-10-07

At code commit `4d88b1ff`,
`AngularWorkpaperSaveRecoveryJourneyTests.DiscardDraftFromWorkpaperPagePersistsDiscardedLifecycle`
passed **1/1** in 34 seconds on an owned isolated PostgreSQL/API host. It loaded
the persisted server draft in the Angular editor, discarded it through the
page, verified the editor cleared, and confirmed in PostgreSQL that the draft
lifecycle is `DISCARDED`, the original content remains in the historical row,
and no submission was created. No browser console or page errors were observed.

The dedicated browser-discard gap is closed for this source review. The row
remains `PARTIAL`: complete role/scope, expired-grant, cross-firm and guessed-ID
matrices, remaining stale/failure/recovery branches, human screen-reader and
wider-locale acceptance, and production migration gates remain open. The full
solution regression and EF drift check were not rerun.
