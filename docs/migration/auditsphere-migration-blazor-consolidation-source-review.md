# AuditSphere migration source review — group consolidation

**Status:** PARTIAL_REVIEWED
**Reviewed against code/test commit:** `343592fc5a2403a27551a17034a2c50989e9437a`
**Pinned discovery snapshot:** `71722283cb2f3d63a7f7c07742d66c30234b65dc`

Both legacy source files match the SHA-256 values in the pinned discovery inventory. The source
inventory is evidence of what was discovered; this review compares those sources with current
Angular, API and Application owners. It does not treat route existence as behavior parity or accept
the Blazor retirement gate.

| Legacy source | SHA-256 | Destination |
|---|---|---|
| `src/AuditSphereOps.Web/Components/Pages/Consolidation.razor` | `5a386619363b0ce4d6cde6c6abbf696d6f5d3b060a749bfb77592ae945cf2647` | US-027 |
| `src/AuditSphereOps.Web/Components/Pages/AdvancedConsolidationWorkflow.razor` | `f4c9eea3e8c58e73b6882d663f65f160b2dcbeee059450dddc6dde37c3e6edc8` | US-028 |

## Source-to-target behavior

| Legacy behavior | Angular, API and Application owner | Evidence and disposition |
|---|---|---|
| Consolidation overview: list only authorized group scopes, component and external packs, ownership/intercompany counts, FX pins, advanced-method readiness, elimination journals and current approved reports. The page has no mutation actions. | Angular `src/AuditSphereOps.Ui/src/app/features/consolidation/overview.ts`; `GET /api/ui/consolidation`; `ConsolidationOverviewQuery` and `ConsolidationService.GetLatestReportAsync`. | `ConsolidationOverviewQuery` starts from active group grants and rechecks each group role for the actor's firm. Child counts and reports are limited to those group IDs and firm. `SiblingClientIsolationJourneyTests.SiblingGroupNeverChangesWhatAGroupScopedUserSees` verifies that adding a sibling group does not change the scoped user's response or rendered content. **Partial:** exhaustive aggregate-field assertions, cross-firm/expired-grant and very-large-group paging/load behavior remain open. The legacy report approval time uses server-local time; Angular renders the timestamp's UTC text without a timezone label, so the display is not yet equivalent. |
| Advanced workflow: show the exact group scope and approved sources; submit a source-bound IFRS schedule; independently approve it; run a verified current/comparative execution; independently approve the execution. | Angular `src/AuditSphereOps.Ui/src/app/features/consolidation/advanced.ts`; `GET /api/ui/consolidation/advanced/{scopeId}` and four POST endpoints in `UiEndpoints.Consolidation.cs`; `AdvancedConsolidationWorkspaceQuery` and `ConsolidationService` advanced methods. | The query hides scopes unless the actor has an allowed active group grant in the current firm. Schedule submission canonicalizes inputs and validates method/source evidence; schedule and execution approvals recheck maker/checker, current group revision, approved sources and method-owner acceptance. Execution creation is keyed to its source manifest digest. `AngularAdvancedConsolidationAuthorizationJourneyTests` covers an erroneously granted client identity, nondisclosing denial, in-place route change clearing, scoped draft recovery and responsive tables. **Partial:** the current browser evidence does not exercise every role/status/error branch or all four mutations end to end. The Angular page automatically reloads after a command, but on an unknown result its action buttons remain visually enabled while `CommandState` silently refuses another dispatch; there is no explicit saved-state acknowledgement/recovery flow. |

## Verification performed

- The pinned legacy SHA-256 values match the current committed Web source.
- Focused PostgreSQL-backed API-host Playwright journeys for advanced-group authorization, route
  clearing and sibling-group isolation passed **3/3** in an isolated snapshot. The Razor, Angular,
  API, Application query/service and E2E sources involved in this review matched the reviewed
  checkout byte-for-byte. The tests used synthetic identities and owned test databases.
- The built-in browser could not complete an authenticated manual view: `localhost:5099` refused
  the connection, while the existing `localhost:5105` tab showed the safe `Access unavailable`
  state because it had no signed-in test identity. No business action was submitted there.
- A full solution regression, standalone Application/API test cohorts, EF model-drift check,
  assistive-technology review and production-like canary were not performed for this source review.

## Result and open evidence

Both source-action records are **PARTIAL**, not `PARITY_VERIFIED`. Still required are field-by-field
overview assertions, all-role and expired/cross-firm direct API matrices, every advanced workflow
command's accepted/refused/stale/unknown recovery behavior, explicit paging/load bounds, report time
display parity, assistive-technology and wider-locale acceptance, and production-like rollback and
owner acceptance. The legacy Web project remains a reference/rollback host; retirement stays
`NOT_READY`.
