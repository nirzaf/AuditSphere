# Complete API process-isolation gate

This is process-lifetime containment, not a change to production rate limiting or a
claim that a specific CI failure was caused by retained hosts. It preserves the
complete suite and existing runner parallelism (two collections within a process).

The release job first verifies the existing same-run, exact-source build artifact.
The helper hashes the restored outputs, SDK runner and source, compiles two small
helpers using the pinned SDK and already restored xUnit assemblies, and retains
their sources, discovery inventories and every command log. No package is added.

`Discover.cs` calls xUnit 2's public discovery API with theory pre-enumeration. It
retains actual collection memberships and exact xUnit IDs. Custom frameworks,
deferred/error/custom cases, skipped rows and unsupported runner configuration fail
closed. `VsDiscover.cs` calls the pinned SDK's VSTest TranslationLayer API and joins
its exact test GUIDs using the adapter's `XunitTestCaseUniqueID` property. Both full
sets and class/method/display metadata must agree bijectively. Display strings are
never used to invent an identity or split theory methods.

Partitioning canonicalizes actual discovered collection memberships, keeps each
collection (and thus class/collection fixtures) together, and permits at most 24
rows and four collections per shard. An oversized collection fails, rather than
silently splitting a fixture. Every discovered ID must be assigned exactly once.
Only one shard process runs at a time, avoiding cross-process database-template
initialization races. Each fresh process rediscovers and exactly matches the frozen
VSTest inventory, then passes the actual discovered TestCase objects to RunTests.

Linux `/proc` bounds the owned process group's RSS at 3 GiB. Every shard has a
21-minute outer deadline, a 20-minute VSTest session deadline and the existing
15-minute blame-hang/no-dump setting. The aggregate has a 150-minute deadline.
Cleanup signals only each newly created process group, never unrelated dotnet or
database processes, and confirms the group has drained before proceeding. Mandatory
Linux-only owned-child deadline, cancellation and leader-exit/drain self-checks run
before discovery. A Mac smoke can verify runner/TRX behavior, but does not establish
that Linux supervisor proof. Process exit releases retained managed graphs.

Every shard needs successful runner completion, all-Passed exact-ID callbacks,
all-Passed TRX results, matching definitions/execution identities, consistent run
counters and no run errors. Missing, duplicated, unexpected, failed, skipped,
aborted or crashed tests fail the aggregate. Binary hashes are rechecked before
each shard and after execution. `coverage.json` begins FAILED and becomes PASSED
only after exact complete coverage. The existing always-upload TRX artifact retains
inventory, assignments, provenance, hashes, helper source, per-shard logs/TRX/events
and aggregate manifest, including on failure. Existing non-API gates are unchanged.

Pure protocol tests: `python3 -m unittest discover -s scripts/release -p test_ci_api_test_shards.py`.
Full API execution remains CI-only. Discovery/protocol checks are not a full-suite
pass or deployment evidence.
