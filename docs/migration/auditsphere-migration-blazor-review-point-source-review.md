# ReviewPoint source/action review

**Disposition: PARTIAL**
**Reviewed against repository commit:** `55ba972d308a3d92fbd690629daa28b70f98ee32`
**Review observed:** 2026-10-05

## Legacy behavior and Angular ownership

The legacy page at `src/AuditSphereOps.Web/Components/Pages/ReviewPoint.razor`
owns `/app/reviews/{Id:guid}`. It shows the target kind and ID, target revision,
raiser, timestamp, significance and gate status, comment, and links back to the
engagement and completion checklist. Significant uncleared points are marked as
blocking. An authorized internal user can clear or reopen a point.

The Angular owner is `ReviewPointRecord` in
`src/AuditSphereOps.Ui/src/app/features/audit/records.ts`. The API maps
`GET /api/ui/reviews/{id}` to `AuditRecordQueries.ReviewPointAsync` and
`POST /api/ui/reviews/{id}/disposition` to
`AuditPlanningService.SetReviewPointDispositionAsync`. The query checks firm,
client, engagement and the internal audit role set. The command revalidates the
engagement scope in a transaction and locks the review-point row before
updating it. Angular reloads the resource after either disposition command;
the shared resource and command components provide unavailable and safe
command-result states.

## Verification evidence

The pinned Razor source hash matches the inventory:
`af63b1a7cf1132238a1836c2c6c68d12a97e10c06e3b7ca5ec8612f293b09415`.
The Angular replacement hash is
`e5f24ffa4fd86540fad14b7cb41f82e857a223da65c3b318d1a815858918a709`.

The focused PostgreSQL-backed API-host browser journey passed **1/1**:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj \
  --no-restore --configuration Release -m:1 \
  --filter 'FullyQualifiedName~AngularClientScopeAuditDetailJourneyTests'
```

It verifies the significant blocking message, successful clear and reopen with
persisted state, sibling and random-ID denial without protected markers,
same-document route restoration, six viewport widths, and visible keyboard
focus. After the exact client grant is revoked, the Angular page removes the
review context and actions, and another clear command is refused without
changing the row. The focused PostgreSQL-backed Domain test
`ReviewPointDisposition_IsScopedAndRetrySafe` also passed **1/1**; it verifies
same-action retry, a different-firm target denial, and a stale session epoch.
The built-in Development browser rendered the generic unavailable state for a
guessed review ID without disclosing record content.

## Gaps keeping this row partial

- A complete role-by-role and scope matrix, including expired grants and
  cross-firm HTTP response equivalence, is not yet recorded.
- Network failures, stale route responses, and every command failure/recovery
  branch are not covered end-to-end.
- Screen-reader and broader locale acceptance remain open beyond the tested
  keyboard focus and responsive widths.

The source row remains `PARTIAL`; this evidence does not close the full
AS-PAR-002 authorization audit or migration acceptance.
