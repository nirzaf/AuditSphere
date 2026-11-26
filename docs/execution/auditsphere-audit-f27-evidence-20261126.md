# F27 fiscal month/day validation evidence

## Scope and disposition

- **Finding:** F27, possible fiscal month/day pairs.
- **Source basis:** audit baseline `62059aa2e622bcb6245a211380021f5cf315cd97` (26 November 2026). Before the scoped commit, `HEAD` and fetched `origin/master` were both `ba32d995662cae21f060d66239068c77b0c2da01`.
- **Disposition:** `IMPLEMENTED` for new and revised profiles, persisted current-profile validation, and regression coverage. `BLOCKED_CONTEXT` for the existing-row precheck on a specifically authorized migration target.
- **Pre-change behavior:** the application accepted any month from 1–12 and day from 1–31. The current-profile database check used the same broad day range.

## Changed files and behavior

- `src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.Profiles.cs` — `CreateProfileAsync` and `ReviseProfileAsync` now use `IsValidFiscalMonthDay`, requiring a valid Gregorian month/day pair. Invalid requests return the existing `MappingInvalid` result before persistence.
- `src/AuditSphereOps.Infrastructure/Persistence/AuditSphereDbContext.ClientAccounting.cs` — `ck_client_accounting_profile_values` now enforces February ≤29, April/June/September/November ≤30, and other months ≤31.
- `src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261126185024_ClientFiscalCalendarMonthDay.cs` and `.Designer.cs`, plus `AuditSphereDbContextModelSnapshot.cs` — EF-generated forward constraint migration and snapshot. It changes no stored values and leaves immutable revision-history constraints unchanged.
- `src/AuditSphereOps.Ui/src/app/features/accounting/workspace.ts` — the existing profile form displays the calendar validation message, disables submission for invalid pairs, and preserves the form for correction. February 29 is allowed as a stored start date; no rollover behavior is defined.
- `src/AuditSphereOps.Ui/src/app/features/accounting/workspace.spec.ts` — exercises the form through actual input events, invalid submission refusal, preserved fields, and valid February 29 submission.
- `tests/AuditSphereOps.Domain.Tests/ClientAccountingTests/ClientAccountingTests.Profiles.cs` — PostgreSQL-backed create/revise boundaries and immutable revision-history assertion; direct SQL with April 31 is rejected by the database constraint.
- `docs/architecture/auditsphere-architecture-code-map.md` — records the persisted month/day rule and the separate unresolved rollover policy.

The execution records are `docs/execution/auditsphere-execution-current-slice.md` and `docs/execution/status.json`.

## Executed checks

`./scripts/agent-check --list` returned `engagement-ui`, `review-permissions`, and `accounting-calculation`; no existing F27 packet matched.

Migration generation succeeded:

```sh
dotnet ef migrations add ClientFiscalCalendarMonthDay --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Api --configuration Release
```

Focused PostgreSQL-backed tests passed 2/2:

```sh
AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~ClientAccountingTests.AccountingSetup_DefaultsBlankCurrencyToQar|FullyQualifiedName~ClientAccountingTests.ProfileFiscalCalendar_RejectsImpossiblePairsAndRetainsRevisionHistory'
```

The migration was present in the isolated per-test PostgreSQL schema. The regression's raw SQL update of the current profile to April 31 failed with PostgreSQL check-constraint SQLSTATE `23514`; the row and revision history remained unchanged.

The affected Angular spec passed 11/11:

```sh
npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/accounting/workspace.spec.ts'
```

EF model drift check passed:

```sh
dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Api --configuration Release
```

Output: `No changes have been made to the model since the last migration.`

A temporary read-only probe, removed after use, was run against only the isolated root test database:

```sh
AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~TransientProfilePrecheck.ReadOnlyProfileTableAndInvalidPairInventory'
```

It found no `public.client_accounting_profiles` table in that database. No deployment or production data was queried. Because this test database cannot establish the retained rows on a migration target, obtain the specifically authorized target identity and perform the required read-only row inventory before applying the migration. Incompatible rows must remain untouched until an owner-directed remediation decision; the new constraint will refuse the migration if they exist.

`git diff --check` passed. `python3 scripts/docs/validate-markdown-documentation.py` passed (194 Markdown files, 3,076 relative links, 286 repository paths, zero deleted references). Fast CI run `45880703868` is `IN_PROGRESS` on exact SHA `efeed02cbf656027817ff31c4152ca5877e858f2`. No production migration or release was performed.
