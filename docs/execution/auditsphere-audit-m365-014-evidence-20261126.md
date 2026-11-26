# M365-014 — Graph directory response byte bound

**Disposition:** IMPLEMENTED. This records a source correction and focused offline verification, not live tenant acceptance.

**Source:** verified baseline `62059aa2e622bcb6245a211380021f5cf315cd97`; implementation pushed in `6a36c301ce9f603bcc48b2102ead83f778078c31`.

At the baseline, `GraphDirectoryReader` rejected a declared `Content-Length` above 262,144 bytes, then passed the response stream directly to `JsonDocument.ParseAsync`. A missing or understated header therefore bypassed the intended response bound.

`GraphDirectoryReader.ReadBoundedJsonAsync` now reads at most 262,145 bytes before parsing and uses the existing `directory-reader-graph-rejected` refusal when the actual body exceeds 262,144 bytes. `BrowseActiveAsync`, `SearchAsync`, and `GetByIdAsync` share this helper. The early length-header check, cancellation, response/stream disposal, 429/503 retry behavior, exact tenant binding, `User.Read.All` role check and exact-object identity check remain in place. No API shape, permission, package, provider setting or migration changed.

`GraphDirectoryReaderTests.GraphResponsesEnforceByteBoundForHeaderlessAndMisleadingLengths` covers exact-limit success and maximum-plus-one refusal for all three methods, with both absent and understated length headers; it verifies the existing refusal code and bounded bytes read. `GraphResponseReadPropagatesCancellation` covers cancellation for each method and verifies stream disposal.

Focused command:

```text
dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~GraphDirectoryReaderTests'
```

Result: **Passed, 15/15**. `python3 scripts/docs/validate-markdown-documentation.py` and `git diff --check` also passed. The exact-source fast CI run `45883992897` for this commit is **PENDING**. No Graph/provider calls or live tenant changes were made.
