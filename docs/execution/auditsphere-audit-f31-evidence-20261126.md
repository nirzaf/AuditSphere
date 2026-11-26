# F31 complete bundle-size evidence

## Scope and disposition

- **Finding:** F31, complete five-part bundle size bound.
- **Source basis:** audit baseline `62059aa2e622bcb6245a211380021f5cf315cd97`; F31 work began on `master` at `efeed02cbf656027817ff31c4152ca5877e858f2` after F27.
- **Disposition:** `IMPLEMENTED` for the complete rendered parts/manifest aggregate and completed ZIP length check.
- **Pre-change behavior:** `AssembleBundleAsync` checked the source report, management letter, scan, statement inputs and correspondence before rendering, but omitted the certified statement, fee note and manifest bytes from the aggregate. It did not bound the completed ZIP before persistence.

## Changed files and behavior

- `src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs` — keeps the fixed 100 MiB source precheck; after all ten rendered parts and `manifest.json` are in `files`, `BundlePartsWithinLimit` sums every `LongLength` with checked 64-bit arithmetic and refuses overflow or an over-limit result using `ErrorCodes.GateBlocked` and the existing assembly-limit message. The finished ZIP stream length is checked against the same production limit before `ToArray()` and before constructing or adding `CommercialDeliverableBundle`.
- `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.BundleIntegrity.cs` — checks exact-ceiling acceptance, one-byte-over refusal and 64-bit overflow without allocating large test files.
- `tests/AuditSphereOps.Domain.Tests/AuditDeliverablesTests.Assembly.cs` — extends `FivePartBundle_RequiresReviewedReleaseAndPostedBalance_IsIdempotent_AndClientScoped` with an actual command refusal. A bounded internal limit seam lowers only the final assembled-byte ceiling for the test; the real source precheck remains 100 MiB. It asserts no bundle is added and the retained deliverable/artifact IDs and hashes are unchanged, then the existing normal assembly path succeeds.
- `docs/architecture/auditsphere-architecture-code-map.md` — documents the complete bound and no-bundle refusal.

The seam cannot raise the production 100 MiB limit and is not part of the public API. No source files, release evidence, invoices or other professional records are changed by the refusal.

## Executed check

PostgreSQL-backed Domain tests passed 2/2:

```sh
AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~FivePartBundle_RequiresReviewedReleaseAndPostedBalance_IsIdempotent_AndClientScoped|FullyQualifiedName~BundlePartsWithinLimit_UsesCheckedInclusive64BitArithmetic'
```

The command-level case completed the existing supported prerequisite flow; the test-only final limit caused the rendered-parts guard to refuse before ZIP creation/persistence. Source identity snapshots remained unchanged and the subsequent production-limit assembly succeeded. No migration, API contract, Angular, browser or production check applied to this backend-only change. `git diff --check` passed. `python3 scripts/docs/validate-markdown-documentation.py` passed: 195 Markdown files, 3,077 relative links, 295 backticked repository paths and zero deleted references. Exact-SHA CI starts with the push.
