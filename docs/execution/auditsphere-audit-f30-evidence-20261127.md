# F30 source and verification evidence — 2026-11-27

## Disposition

`ALREADY_FIXED` at tested HEAD `21ddeba998ed46809f82198816b2f9a86ebf2966`, compared with verified baseline `62059aa2e622bcb6245a211380021f5cf315cd97`. No product source or test changes were made for F30.

The baseline parser accepted signed decimal amounts without enforcing the stored `numeric(19,6)` magnitude. Current `TrialBalanceCsvImporter.ParseAmount` rejects below `-ClientNativeAmountRules.MaximumStoredAmount` and above the positive maximum before normalization. `ParseSourceSideAmount` reuses that bound for debit and credit independently, then preserves the nonnegative source-side rule before net subtraction. `TrialBalanceXlsxImporter` uses the shared parser for literal numeric cells. CSV and XLSX boundary tests cover both signed maxima, positive and negative `10,000,000,000,000` refusal, and debit/credit overflow even where the net cancels.

Paths with changes between the baseline and tested HEAD: `src/AuditSphereOps.Application/Accounting/TrialBalanceCsvImporter.cs`, `src/AuditSphereOps.Application/Accounting/TrialBalanceXlsxImporter.cs`, `tests/AuditSphereOps.Domain.Tests/AdjustmentBridgeTests.cs`, and `tests/AuditSphereOps.Domain.Tests/TrialBalanceXlsxImporterTests.cs`. `ClientNativeAmountRules.MaximumStoredAmount` already supplied the shared bound and did not need a modification. The bounded import flows call these parsers before persistence; no schema, migration or API behavior change was needed.

## Verification

Command:

```text
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~MixedCurrency_DuplicateAccount_ExcessScale_Formula_AreRejected|FullyQualifiedName~DebitCreditProfile_PreservesSourceSidesAndComputesSignedBalance|FullyQualifiedName~TrialBalanceXlsxImporterTests' --logger 'trx;LogFileName=f30-bounded-tb-amounts.trx' --results-directory /tmp/auditsphere-f30-bounded-tb-amounts
```

Result: PASS, 4/4 tests, 0 failed, 0 skipped. The test result was run from the main checkout on `21ddeba998ed46809f82198816b2f9a86ebf2966`; it is focused local evidence, not full CI or production acceptance. The original upload/provenance policy F23 remains separately `BLOCKED_DECISION`.
