# AuditSphere — Agent Context Index

**Status: CURRENT** (pack prepared 2026-10-08 against `master` at merge `e0afe18`).
**Purpose:** the ordered set of inputs an AI coding agent needs for this repository, mapped to the file that fills each slot, so agents load a small, correct context instead of the whole `docs/` tree.
**Authority:** navigation only. It does not override `AGENTS.md`, the current architecture or the STE requirements.

## 1. Layered loading rule

| Layer | Loaded | Slots |
| --- | --- | --- |
| 1 Foundation | When the task touches product scope or vocabulary | 1–4 |
| 2 Contracts | When the task touches data, endpoints or limits | 5–7 |
| 3 Operating manual | Every session (automatically) | 8–9 |
| 4 Work breakdown | When choosing or scoping work | 10–12 |
| 5 Execution | Written fresh per task | 13 |

An agent should normally hold slot 8, one story from slot 11, and the two to four files that story names. Nothing else is required up front.

## 2. Slot map

| # | Slot | Authoritative file(s) | State |
| --- | --- | --- | --- |
| 1 | Product brief / PRD | [Product brief](../requirements/auditsphere-requirements-specification-product-brief-current.md) → detail in [STE v2.1 requirements](../requirements/auditsphere-accounting-module-requirements-current.md) | Brief **new**. Requirements copy is v2.1 but its header and portal-onboarding text predate two implemented decisions (ADR-0002, ADR-0003). |
| 2 | Glossary | [Domain glossary](../requirements/auditsphere-requirements-catalog-domain-glossary-current.md) | **New.** Previously spread across code constants and narrative docs. |
| 3 | Architecture overview | [Current architecture](auditsphere-architecture-current-architecture.md), [Code map](auditsphere-architecture-code-map.md) | Exists. Architecture doc links a missing file (`auditsphere-ui-mudblazor-conventions-migration-current.md`). Code map's Tests column names files removed in PR #30. |
| 4 | ADRs | [ADR register](adr/auditsphere-architecture-index-adr-register-current.md) + R2R-ADR-01..10 in [baseline and ADRs](../task_breakdown/reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md) | ADR-0001..0007 and ADR-0009 are approved under the owner's delegated project decision; ADR-0008 is superseded. Provider and professional gates remain separate. |
| 5 | Data model | [Data model contract](auditsphere-architecture-contract-data-model-current.md) | **New** index over `AuditSphereDbContext.<Module>.cs`, Domain files and migrations. Executable truth stays in code. |
| 6 | API contract | `contracts/auditsphere-openapi.json` (OpenAPI 3.1), [HTTP boundary](auditsphere-architecture-http-boundary-and-contract-current.md), drift gate `scripts/contracts/verify-openapi.sh` | Exists and is machine-readable. HTTP boundary doc still cites removed `HttpBoundarySecurityTests`. |
| 7 | Non-functional requirements | [NFR policy](auditsphere-architecture-policy-non-functional-requirements-current.md) | **New.** Consolidates limits already enforced in code; lists the targets nobody has set (P8). |
| 8 | Agent operating manual | `AGENTS.md` (Codex and others), `CLAUDE.md` → `@AGENTS.md` (Claude Code) | `AGENTS.md` **replaced**: same rules, 478 → about 100 lines (bytes fall only slightly, because the padding was blank lines and the new file adds the test-status and context-budget sections). `CLAUDE.md` **new**; needs the validator patch in section 4. |
| 9 | Test strategy and DoD | [Strategy and Definition of Done](../testing/auditsphere-testing-strategy-definition-of-done-current.md) | **New.** Reflects the restored suites (STE-NXT-001). |
| 10 | Roadmap | [STE closure roadmap](../execution/auditsphere-execution-workflow-roadmap-ste-closure-proposed.md) | **New.** Orders existing backlog (pending tasks, AS-PAR, STE packages, P-gates) into milestones. |
| 11 | Stories | [Remaining STE v2.1 stories](../execution/auditsphere-execution-user-stories-ste-v21-remaining-proposed.md); existing [pending tasks](../execution/auditsphere-execution-pending-tasks.md), [R2R/audit task index](../task_breakdown/auditsphere-r2r-index-task-breakdown.md) | Stories **new**, each grounded in a verified code gap. |
| 12 | Spikes | [Technical spikes](../execution/auditsphere-execution-tracker-technical-spikes-proposed.md) | **New.** |
| 13 | Task prompt | [Agent task prompt guide](../execution/auditsphere-execution-guide-agent-task-prompt-current.md) | **New** template plus a filled example. |
| — | Spec verification | [STE v2.1 gap verification](../execution/auditsphere-execution-tracker-ste-v21-gap-verification-current.md) | **New.** Requirement-by-requirement check against current code, separate from the earlier coverage table whose verification sources were test files. |

## 3. Context hazards found (2026-10-08, `e0afe18`)

These are measured, not estimated. Each one degrades agent accuracy; stories STE-NXT-001 and STE-NXT-002 address them.

| Hazard | Evidence | Effect on an agent |
| --- | --- | --- |
| Automated tests absent (resolved by STE-NXT-001) | Commit `47ca0b05` removed the Domain, Api and E2E test-case files and every `*.spec.ts`; CI gates were rewritten in `aa9ff73`. `tests/` held only fixtures and project files until STE-NXT-001 restored the suites. | The old `AGENTS.md` and README still instruct `dotnet test` and `npm run test:ci`. An agent following them either fails or reports a vacuous pass. Acceptance criteria cannot be executed. |
| Oversized "start here" files | At the time of writing, `docs/execution/status.json` was about 1.2 MB and the current slice about 460 KB. Both are steps 5–6 of the index reading order. Resolved: both are now under the size budgets enforced by the docs gate (STE-NXT-002). | Either file alone can exhaust a session's context before any code is read. |
| Blank-line padding | The old `AGENTS.md` had 391 blank lines out of 478; the docs index 546 of 673; the M365 user stories 2,583 of 3,143; the code map 581 of 932. | Token cost with no content; pushes real instructions out of context. Resolved: blank-line runs under `docs/` are collapsed (STE-NXT-002). |
| Dead references | At `e0afe18`, 35 backticked or linked paths pointed to files that did not exist. The backticked-path check now runs in the docs gate and reports zero dead references (STE-NXT-002). | Agents open missing files or trust stale verification sources. |
| Stale test citations | The code map names 132 distinct `*Tests` classes; the STE coverage table cites 17 as verification sources. None existed at `e0afe18`, before STE-NXT-001 restored the suites. Re-check each citation before you rely on it. | Coverage claims look verified when their evidence is gone. |
| Decisions without records | Entra sign-in replacing emailed passwords, lifecycle as a derived projection, Full Control site staffing, PNG credentials, test removal, and reporting-context identity — recorded in the current ADR register through ADR-0015. | Agents "fix" deliberate deviations back to the spec wording. |

## 4. Applying this pack

1. Copy the contents of the pack's `repo/` folder to the repository root. `AGENTS.md` is a replacement; every other file is new.
2. Apply the `CLAUDE.md` allowance before committing it, or omit `CLAUDE.md`: both `scripts/docs/validate-markdown-documentation.py` and `scripts/docs/validate-markdown-filenames.py` reject root files other than `README.md` and `AGENTS.md`. Add `"CLAUDE.md"` to `CONVENTIONAL_EXCEPTIONS` in both scripts and list it in §2 of the naming policy. The pack includes this as `patches/0001-allow-claude-md-root-file.patch` (apply with `git apply` from the repository root).
3. Add the new files to `docs/auditsphere-docs-index.md` (one line each under the matching section) and point step 5 of "Start here" at the `jq` query in `AGENTS.md` §6 rather than at the whole `status.json`.
4. Run the four docs checks in `AGENTS.md` §3. The pack was checked with them; see [the application record](../execution/auditsphere-execution-report-agent-pack-application-historical.md).
5. Owner review: confirm or correct each `PROPOSED` ADR, then rename its suffix to `-approved`.
