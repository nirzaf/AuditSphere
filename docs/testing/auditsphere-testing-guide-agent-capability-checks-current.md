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
   blocker gives an action to take; it never installs tools, changes configuration,
   starts services, resets a database, applies migrations or requests Microsoft consent.
3. After implementing a coherent slice, opt in to focused verification:
   `./scripts/agent-check review-permissions --run --report artifacts/agent-check/review-permissions.json`.
   Test commands use the existing suites, build their test projects without restore,
   and may write build/cache outputs and disposable synthetic test databases.
   Every prerequisite must pass before any test command starts. The runner supports
   only the standard local PostgreSQL target; connection overrides are refused.
4. Add the mapped browser cohort with `--browser` when the change affects a browser
   flow. This requires the built Angular bundle and the project’s existing Playwright
   Chromium installation. The runner does not build the bundle or install a browser.
5. Inspect the report, then record observed results in
   [status.json](../execution/status.json) and the
   [current slice](../execution/auditsphere-execution-current-slice.md).
   Run the broader checks required by AGENTS.md before release or a completion claim.

Use `--layer unit`, `--layer api`, or `--layer browser` for an explicit layer. For example, `./scripts/agent-check accounting-calculation --layer unit` inspects the pure-calculator cohort without requiring PostgreSQL; this narrower selection does not pass the omitted API or browser layers.

`--timeout 600` limits each verification command. A timeout terminates its process
group on Unix and is a failure; Windows terminates the direct process, so inspect
for remaining child processes before another run. `--json` prints the complete
machine-readable report. Report files are written only when explicitly requested;
`artifacts/` is ignored by Git. The default console output is a compact task packet.

## Pilot scope and fixtures

| Packet | Focused baseline | Optional browser evidence |
| --- | --- | --- |
| `engagement-ui` | Engagement projection contracts, paging and protected-content lifetime | Existing engagement-profile synthetic journey |
| `review-permissions` | Shared role/scope authorization and revoked cookie identity | Existing exact-client scope journey |
| `accounting-calculation` | Exact trial-balance calculator and source API boundary | Existing trial-balance upload journey |

The review packet is a shared authorization baseline. A change to a particular
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

## Evidence and measurement

The report records the source commit, working-tree paths, hashes of packet source
files, source stability during execution, selected commands, prerequisites,
executed test counts, durations and bounded sanitized failure excerpts. Zero
executed tests cannot pass. A changed source tree cannot receive a focused-pass
verdict. An unexecuted suite remains unexecuted; full release and external acceptance
are explicitly unverified. A working-tree run is evidence for the recorded source
plus changes, not proof that the commit alone passed.

No raw test logs are retained. Failure excerpts include assertions and request or
trace identifiers when the commands emit them. Common credentials, connection
strings, URLs, tokens and email addresses are redacted. Inspect reports before
sharing; automated redaction is not a guarantee against every sensitive value.

Supply measured task preparation and editing time with `--discovery-seconds` and
`--editing-seconds`, and observed rework with `--rework-count`. The runner measures
preflight time, verification-command wait time (including compilation), and
prerequisite failures. Missing measurements stay null. Compare the same task and
checks before/after adoption, and record independent acceptance regressions in the
existing ledger. No speedup or regression reduction is claimed without that comparison.

To maintain packets, edit the catalog, keep selectors aligned with existing test
files, and run `./scripts/agent-check --validate-catalog`. The lightweight safety
checks are `python3 -m unittest discover -s scripts/agent -p 'test_*.py'`.
