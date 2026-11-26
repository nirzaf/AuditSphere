# PRA-015 quotation rate lookup evidence — 2026-11-26

**Disposition: IMPLEMENTED.** The measure-first review found repeated SQL rate-card reads in one `QuotationService.SaveAsync` call. No pricing policy or selected-rate behavior changed.

## Baseline and correction

At `5846ec3b0fb6ebe80467ecf4dfaecb4e0c14b488`, a PostgreSQL-backed command interceptor measured rate-table SQL reads for repeated and distinct direct and STE-alias tuples. Counts were `2 / 2 / 6 / 6` (repeated direct / distinct direct / repeated alias / distinct aliases). Distinct aliases such as `Senior` and `Supervisor` each resolved the same canonical approved card but repeated its lookup.

`QuotationService.SaveAsync` now keeps two dictionaries local to that call: trimmed exact input tuples reuse direct resolution, and exact canonical role/activity/currency tuples reuse alias fallback. It still selects the latest approved card with the existing query, preserves direct-card precedence, and checks every line's `ExpectedRateCardId`. The local cache cannot carry stale rates into another command. The optimized counts are `1 / 2 / 2 / 3`. The regression also verifies both aliases persist the canonical approved rate ID and a changed expected ID returns `revision.stale` without saving.

## Focused verification

Against the isolated PostgreSQL test database:

- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~CommercialWorkflowTests.QuotationRateLookupCounts_RepeatedAndDistinctDirectAndAliasTuples'` — 1/1 passed. The same regression was first run against the unmodified service and passed with the baseline read counts.
- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~CommercialWorkflowTests.Quotation_IsCalculatedFromApprovedRates_Versioned_AndApprovedByTheConfiguredMatrix|FullyQualifiedName~CommercialWorkflowTests.AngularQuotationWorkspaceUsesReviewedRatesAndRevisionFences|FullyQualifiedName~CommercialWorkflowTests.QuotationCommands_RecheckAuthorityAfterFirmLockWait'` — 7/7 passed.

This measures scoped query counts, not elapsed time or production performance. No API, UI, schema, or dependency changed. Exact-source CI is pending after this slice is committed and pushed; no deployment or external acceptance is implied.
