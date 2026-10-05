# Angular migration source review — resource planning, budget and staffing

**Status:** PARTIAL_REVIEWED
**Reviewed against code:** `7ca600d1fb93c331fa1491e575fd4b7b24cdd6e8`
**Pinned discovery snapshot:** `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`

The three legacy sources below match their SHA-256 values in the pinned
inventory. They remain in the Web rollback/reference host. Route and component
mapping alone does not establish complete behavior parity.

| Legacy source | SHA-256 |
|---|---|
| `src/AuditSphereOps.Web/Components/Pages/ResourcePlanning.razor` | `2e6a2c726afddea68a3421ba8eecc120c9f76ed56068777b190103c4a24f2d2d` |
| `src/AuditSphereOps.Web/Components/Planning/EngagementBudgetPanel.razor` | `393025cf803dcb464e08347b155386ed673e569a959246cb86312448a5ba2171` |
| `src/AuditSphereOps.Web/Components/Planning/EngagementStaffingPanel.razor` | `d248a8852cc21ef06c7c80cd4e5227ede0e9b1963620ceb9b1208bc236255cea` |

## Behavior and ownership mapping

| Legacy behavior | Angular, API and Application owner | Current evidence and remaining gap |
|---|---|---|
| Show staff profiles, skills, certifications, weekly capacity, availability, planned allocation, approved actual utilization, and over-allocation; filter the displayed week range. Create or update a staff profile, certification, unavailability, and engagement allocation. | Angular `features/practice/resources.ts`, `resources.html`, `resource-editor.ts` and `resources.scss`; API `UiEndpoints.Resources.cs`; Application `ResourcePlanningWorkspaceQuery`, `ResourcePlanningCommandWorkspace` and `ResourcePlanningService`. | `AngularResourcePlanningJourneyTests` covers keyboard form submission, capacity, availability, allocation, and stale reviewed allocation refusal. The API composes Application services and bounded workspace queries. Exhaustive role/scope, field-validation, conflict and inaccessible-ID matrices are not established by this cohort. |
| Inspect budget-by-phase and risk-area totals against approved time; prepare a version from roles, activities, phases, risk areas, hours, currency and approved rate cards; keep preparation separate from approval. | Angular engagement planning in `features/engagements/planning.ts`; API `UiEndpoints.BudgetPreparation.cs` and `UiEndpoints.BudgetApproval.cs`; Application `BudgetPreparationWorkspace`, `BudgetApprovalWorkspace`, and existing `PracticeTimeService` queries. | `AngularBudgetPreparationJourneyTests` and `AngularBudgetApprovalJourneyTests` verify exact reviewed intent, independent approval, dropped-response receipt recovery after reload, and no duplicate dispatch. All rate-card, budget-line, reviewer-authority, conflict, denial and recovery branches are not covered here. |
| View engagement candidates and four staffing levels; add or revoke an engagement assignment with its matching AuditSphere role and scope. | Angular engagement planning in `features/engagements/planning.ts`; API `UiEndpoints.StaffingChange.cs`; Application `StaffingChangeWorkspace` and `StaffingService`. | `AngularStaffingChangeJourneyTests` verifies add/revoke recovery, immutable receipts and session-epoch invalidation on revoke. `AngularPlanningAssentJourneyTests` verifies changing candidate or level clears assent without posting. The journeys do not prove the complete staffing-role/scope matrix or live Microsoft site membership reconciliation. |

The legacy page composes Application resource commands but also queries active
engagement labels through its injected EF context. The native route performs
reads and commands through the API; the API resolves the trusted actor and
composes Application queries/services. Angular does not access EF Core or
Microsoft Graph directly.

## Local verification

The focused PostgreSQL-backed Release API-host Playwright cohort passed 12/12,
with zero failures and zero skips, using:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore --configuration Release -m:1 --filter 'FullyQualifiedName~AngularResourcePlanningJourneyTests|FullyQualifiedName~AngularBudgetPreparationJourneyTests|FullyQualifiedName~AngularBudgetApprovalJourneyTests|FullyQualifiedName~AngularStaffingChangeJourneyTests|FullyQualifiedName~AngularPlanningAssentJourneyTests'
```

The journeys cover both canonical and `/ui` route ownership for stale
allocation review, keyboard resource forms, budget preparation and independent
approval, staffing add/revoke recovery, and review invalidation when the
candidate or staffing level changes. They use isolated PostgreSQL fixtures;
the test evidence is local and does not represent a production acceptance.

The built-in browser rendered the authenticated Development Angular resource
planning route read-only. No business command was submitted.

## Open parity and retirement gates

All three source/action rows remain `PARTIAL`. In particular, the focused
journeys do not establish the exhaustive positive/negative role and scope
matrix, cross-firm and guessed-ID indistinguishability for all resources,
complete validation and provider-failure paths, or every stale/revoked-session
transition. Actual assistive-technology and wider-locale acceptance also
remain open.

Staffing describes Full Control on the client SharePoint site when dedicated
client-site provisioning is enabled. That external membership effect remains
separate from the engagement-scoped AuditSphere grant and is not verified by
these local journeys; Microsoft reconciliation stays subject to its own live
acceptance gates.

The full solution regression and EF pending-model check were not rerun for this
review. The latest recorded complete PostgreSQL-backed Release solution
regression remains 1001/1001 at
`ead85032de2ccc4d4c8043398fa8471d395376a9`, an earlier checkpoint. The
complete migration and AS-PAR-002 audits remain partial; Blazor retirement
stays `NOT_READY`. This review does not authorize deleting the rollback host.
