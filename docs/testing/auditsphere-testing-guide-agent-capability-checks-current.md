# Agent capability checks

**Status:** CURRENT

Use the existing [code map](../architecture/auditsphere-architecture-code-map.md),
[task board](../execution/auditsphere-execution-index-task-board-current.md), and
[test strategy](auditsphere-testing-strategy-definition-of-done-current.md) through
one bounded task packet. The [capability catalog](agent-check-capabilities.json)
connects owning files, invariants, acceptance criteria, test selectors and existing
synthetic fixtures. It supplements task cards; it is not a second completion ledger.

## Shortest path

Run these commands from the repository root in a local development shell with Python 3.10 or later. On Windows, use `python scripts/agent/agent-check.py` in place of `./scripts/agent-check`.

1. List packets: `./scripts/agent-check --list`.
2. Inspect the packet and prerequisites: `./scripts/agent-check review-permissions`.
   This performs read-only tool/version, configuration and database queries. A
   blocker gives an action to take. Socket-access denial means this execution environment cannot verify the connection; it is not proof that PostgreSQL is down. The runner never installs tools, changes configuration,
   starts services, resets a database, applies migrations or requests Microsoft consent.
3. After implementing a coherent slice, opt in to focused verification:
   `./scripts/agent-check review-permissions --run --report artifacts/agent-check/review-run-01.json`.
   Test commands use the existing suites, build their test projects without restore,
   and may write build/cache outputs and disposable synthetic test databases.
   Every prerequisite must pass before any test command starts. The runner supports
   only the standard local PostgreSQL target; connection overrides are refused.
4. Add the mapped browser cohort with `--browser` when the change affects a browser
   flow. This requires the existing Angular dependencies, initial bundle and the
   project’s Playwright Chromium installation. An explicit `--run --browser` (or
   `--run --layer browser`) rebuilds the current Angular bundle before tests, so
   stale presentation output cannot silently stand in for the edited source. A
   failed build stops the cohort. Preflight alone never builds or installs anything.
5. Inspect the report, then record observed results in
   [status.json](../execution/status.json) and the
   [current slice](../execution/auditsphere-execution-current-slice.md).
   Run the broader checks required by AGENTS.md before release or a completion claim.

Use `--layer unit`, `--layer api`, or `--layer browser` for an explicit layer. For example, `./scripts/agent-check accounting-calculation --layer unit` inspects the pure-calculator cohort without requiring PostgreSQL; this narrower selection does not pass the omitted API or browser layers.

`--timeout 600` limits each verification command. A timeout terminates its process
group on Unix and is a failure; Windows terminates the direct process, so inspect
for remaining child processes before another run. `--json` prints the complete
machine-readable report. Report files are written only when explicitly requested,
under the ignored artifacts/agent-check directory. Use a new filename for every
run; existing evidence and source files cannot be overwritten. The default console
output includes the packet, synthetic actors and states, selected commands and
omitted checks. Alternate E2E repository or artifact roots are refused because
they can serve a different checkout.

## Pilot scope and fixtures

| Packet | Focused baseline | Optional browser evidence |
| --- | --- | --- |
| `engagement-ui` | Engagement projection contracts, paging and protected-content lifetime | Existing engagement-profile synthetic journey |
| `review-permissions` | Shared role/scope authorization, exact-scope review-note commands and revoked cookie identity | Existing exact-client scope journey |
| `accounting-calculation` | Exact trial-balance calculator and source API boundary | Existing trial-balance upload journey |

Each acceptance criterion maps explicitly to named checks, or declares its missing
evidence. A passing unit selection cannot mark its omitted browser criteria verified.
The review packet includes permitted note-thread changes and sibling/guessed-ID
denial, alongside the shared authorization baseline. A change to a particular
review command also needs that command’s permitted/denied, independent-review and
stale-revision cases from its task card. A pilot passing does not establish complete
review-workflow acceptance.

The existing E2E `OwnedHost` and `PgTestSchema` fixtures create fresh synthetic
identities, grants, records and isolated databases and dispose them afterwards.
Repeat these journeys to recreate their state; do not reset a persistent environment.
They are transient automated fixtures, not a persistent browser login service.
For manual built-in-browser verification, use approved synthetic records in a
supported local or acceptance host and record the exact roles, states, interactions,
console/network errors and screenshots. Live tenant acceptance remains separate.

## Inspecting an owned browser fixture

The analytics paging, reviewed engagement-creation, scoped assessment and client upload-recovery journeys support an optional
built-in-browser handoff through `OwnedBrowserFixtureHandoff`. Supply unique
basenames in `AUDITSPHERE_BROWSER_FIXTURE_READY_FILE` and
`AUDITSPHERE_BROWSER_FIXTURE_RELEASE_FILE` when running one journey. The ready JSON
appears in the operating system temporary directory only after automated assertions
pass. It gives the current local origin, route and expiry. Open that origin in the
built-in browser, use its synthetic test sign-in, and inspect the flow. Create the
release file to finish; otherwise the host expires after 90 seconds. The test removes
its ready file and disposes the owned database. This is local synthetic evidence;
it grants no tenant access and cannot establish live acceptance. Normal CI leaves
both variables unset and does not pause.

For the client portal, select `AUDITSPHERE_BROWSER_FIXTURE_TARGET=accessibility-client-portal`
and the existing `LostChunkAcknowledgement_ResumesExactFileFromPersistedChunkReceipt` test.
The synthetic client has completed first sign-in; the ready route opens its exact PBC request.

## Evidence and measurement

The report records the base source commit, working-tree paths, hashes of packet
source, authority and fixture files, a complete repository source digest, source
stability during execution, selected and omitted commands, prerequisites,
executed/passed/failed/skipped counts, durations and bounded sanitized failures. HTML,
SCSS and newly added files participate in source stability. Zero executed tests,
skipped tests and malformed or inconsistent results cannot pass. A changed source tree cannot receive a focused-pass
verdict. An unexecuted suite remains unexecuted; full release and external acceptance
are explicitly unverified. A working-tree run is evidence for the recorded source
plus changes, not proof that the commit alone passed. The testedCommit field is
populated only for a stable clean-tree execution. Criterion-level
MAPPED_CHECKS_PASSED describes only the mapped evidence; it never records task
completion or closes the declared limitation.

No raw test logs are retained. Failed .NET results retain their test name, assertion,
bounded stack trace, request/trace identifiers and sanitized log context from TRX,
even when those details fall outside the console tail. Timeouts, socket-access
denials, skipped tests, invalid results and command failures remain distinguishable.
A denied test-runner socket requires a permitted local shell; it does not call for
a database reset. Common credentials, connection
strings, URLs, tokens and email addresses are redacted. Inspect reports before
sharing; automated redaction is not a guarantee against every sensitive value.

Supply measured task preparation and editing time with `--discovery-seconds` and
`--editing-seconds`, observed rework with `--rework-count`, and independently found
regressions with `--acceptance-regressions`. Do not enter zero for an unperformed
acceptance check. The runner measures
preflight time, verification-command wait time (including compilation), and
prerequisite failures. Missing measurements stay null. Compare the same task and
checks before/after adoption, and record independent acceptance regressions in the
existing ledger. No speedup or regression reduction is claimed without that comparison.

### Comparing the pilots

1. Choose a comparable task and environment for each pilot. Include the same task
   shape, machine, configuration, source/test cohort and cache conditions; use a
   non-sensitive label with `--measurement-context` on both recorded runs.
2. Record discovery/editing time while working and run the selected checks once
   after the coherent slice. Supply only measurements actually taken. A report
   with no execution is preflight evidence, not a baseline performance result.
3. Keep baseline and candidate reports under distinct filenames. Compare them
   without running commands or touching the database:

   ```bash
   ./scripts/agent-check --compare artifacts/agent-check/baseline.json artifacts/agent-check/candidate.json
   ```

4. Inspect the candidate-minus-baseline deltas. Different capabilities, contexts,
   selected commands, test-source hashes or executed counts are refused. Both
   reports must have stable passing focused evidence. Missing measurements stay
   unmeasured; a faster single run does not establish a causal improvement.
5. Record the comparison and independent acceptance in the existing execution
   ledger. Full release checks and live/provider or professional acceptance remain
   separate. Repeat comparable tasks before changing architecture on the strength
   of a timing observation.

To maintain packets, edit the catalog, keep selectors aligned with existing test
files, and run `./scripts/agent-check --validate-catalog`. The lightweight safety
checks are `python3 -m unittest discover -s scripts/agent -p 'test_*.py'`.
