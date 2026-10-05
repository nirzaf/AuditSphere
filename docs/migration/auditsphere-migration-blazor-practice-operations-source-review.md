# Angular migration source review — practice operations

**Status:** PARTIAL_REVIEWED
**Reviewed against code:** `10444247ba77285f51dfe69eae8217dcd96ec853`
**Pinned discovery snapshot:** `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`

The four legacy page sources below match their SHA-256 values in the pinned
inventory. They remain in the Web rollback/reference host. Existing route
owners and a representative journey do not by themselves establish complete
behavior parity.

| Legacy source | SHA-256 |
|---|---|
| `src/AuditSphereOps.Web/Components/Pages/PracticeTime.razor` | `874c2f2ba3c333aa20f5fefb426273b232b42e082fffe4212552e6aee6d38f17` |
| `src/AuditSphereOps.Web/Components/Pages/PracticeAnalytics.razor` | `f18185fa9b719b8166a73cefcb1cec8cffdd3341b2d4c6a2901c0359ec7018f9` |
| `src/AuditSphereOps.Web/Components/Pages/ProjectProgress.razor` | `4380115c3ae65676877d0b1949134632e64e171b2f075fa483041b257ef345dd` |
| `src/AuditSphereOps.Web/Components/Pages/TechnicalLibrary.razor` | `9d272c9e265539b83bb9f963f826c88b3c95fd18d41c20f308e5fd5d18cefa4e` |

## Behavior and ownership mapping

| Legacy behavior | Angular, API and Application owner | Current evidence and remaining gap |
|---|---|---|
| Show authorized open tasks, the actor's own entries and an independent time-approval queue; create scoped work tasks, record drafts, submit and approve time. | Angular `features/practice/time.ts`; API `UiEndpoints.Time.cs`; Application `PracticeTimeWorkspaceQuery` and `PracticeTimeService`. | The focused cohort verifies a time draft submitted by staff and approved by a different reviewer, plus an engagement-scoped identity that sees its assigned task and entry while sibling-engagement task, narrative and reporting-period markers remain absent. Complete cross-firm/guessed-ID, input validation, revocation during action and every command failure/retry path remain open. |
| Report engagement economics, utilization by department and milestone performance from approved source records, with formula definitions and an explicit reporting period. | Angular `features/practice/analytics.ts`; API `UiEndpoints.PracticeInsights.cs`; Application `PracticeAnalyticsQuery` in `FirmOperationsServices.cs`. | The firm-operations browser journey opens analytics as a Partner and checks the formula definition that charge-out rates are not costs. The built-in browser showed the authorized empty Development period and all formula definitions. The cohort did not assert populated metric values, missing-input and mixed-currency behavior, department-capacity boundaries or the full analytics authorization matrix. |
| Publish the implementation task-card snapshot with module/phase summaries, status filtering and untracked-area disclosure; refuse access to non-administrators and fail closed if the packaged snapshot is invalid. | Angular `features/admin/progress.ts`; API `UiEndpoints.Operations.cs`; Application `FirmAdministrationQuery`; API `ProjectProgressReader` and the packaged `project-progress` files. | `AngularRouteAndShellMigrationSweepTests` checks the page's published counts, filters, static task-card bars, untracked-area explanation and a staff denial with no tracker data. `ProjectProgressReaderTests` checks the packaged manifest, unique task counts and path-escape refusal. The remaining task-state, malformed-file, stale-publication and admin-boundary cases are not all covered. |
| Search the firm technical library; read published entries and history; prepare a new entry/version and have a distinct Partner or Administrator publish the immutable version for its configured audience. | Angular `features/library/library.ts`; API `UiEndpoints.PracticeInsights.cs`; Application `TechnicalLibraryWorkspaceQuery` and `TechnicalLibraryService`. | The firm-operations journey opens a Manager-prepared draft, publishes it as a Partner, searches its published content and verifies the version result. The fixture creates the draft through the Application service; it does not exercise Angular's create-entry or prepare-version form. Audience isolation, duplicate-code, second-approver denial, historical-version and full input/error cases remain open. |

The native API resolves the trusted actor and composes Application queries and
commands; Angular does not access EF Core directly. The technical library
service applies firm and audience boundaries, separates curators from
publishers and records a content hash for each immutable version. The
project-progress route delegates to current firm-administrator authorization
and reads a packaged snapshot without mutating application task state.

## Local verification

The focused PostgreSQL-backed Release API-host Playwright cohort passed 5/5,
with zero failures and zero skips, at code/test commit
`10444247ba77285f51dfe69eae8217dcd96ec853`:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore --configuration Release -m:1 --filter 'FullyQualifiedName~AngularPracticeTimeScopeParityJourneyTests|FullyQualifiedName~AngularPracticeBillingLedgerParityTests.AngularTimeApprovalBillingAndFirmCloseKeepTheirBoundaries|FullyQualifiedName~FirmOperationsJourneyTests.LibraryPublishAnalyticsAndExpenseToTrialBalance|FullyQualifiedName~AngularRouteAndShellMigrationSweepTests.ParameterlessRoutesRenderAndPreserveRoleSpecificShells|FullyQualifiedName~AngularAccessibilitySweepTests'
```

The cohort covers time entry submission and independent approval; sibling
engagement time isolation; library publication and published-version search;
analytics formula display; project-progress filtering and staff denial; and
the 20-route Angular accessibility contract including these practice routes.
The suite compiled Domain, Application, Infrastructure, API, Worker and E2E
projects in Release before running the browser tests.

The Codex built-in browser rendered the Development Time, Analytics, Technical
Library and Project Progress routes. Time and the library showed authorized
empty states, Analytics displayed its empty-period result and formula list,
and Project Progress displayed the packaged tracker. This was read-only; no
business command was submitted. No browser-console claim is made.

## Open parity and retirement gates

All four source/action rows remain `PARTIAL`. The practice-time tests do not
establish every role, firm/client scope, input, revoked-session and uncertain
outcome. Analytics still needs populated result and edge-case assertions. The
library still needs UI create/version journeys and audience/curator/publisher
denial coverage. The progress page still needs its complete corrupt/stale
snapshot matrix. Actual assistive-technology, wider-locale, production,
canary/rollback and separate owner acceptance remain open.

The full solution regression and EF pending-model check were not rerun for this
slice. The latest complete PostgreSQL-backed Release solution regression
remains 1001/1001 at `ead85032de2ccc4d4c8043398fa8471d395376a9`, an earlier
checkpoint. A concurrent unstaged edit to
`src/AuditSphereOps.Application/Accounting/ConsolidationOverviewQuery.cs` was
present during the focused build and test run; it compiled successfully but
was not part of this review or its eventual documentation commit. The separate
unstaged `docs/execution/angular-source-inventory.json` edit also remains
outside this review. AS-PAR-002 stays partial and Blazor retirement remains
`NOT_READY`; this review does not authorize removing the rollback host.
