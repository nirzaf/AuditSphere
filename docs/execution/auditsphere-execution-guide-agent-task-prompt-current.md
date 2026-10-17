# AuditSphere — Agent Task Prompt Guide

**Status: CURRENT.** The only input written fresh for each agent session. Everything else is already in the repository; the prompt points at it.

## Template

```text
Implement <STORY-ID>: <one-line title>
Story file: docs/execution/auditsphere-execution-user-stories-ste-v21-remaining-proposed.md#<anchor>

Read first (only these, then code you discover from them):
- <2–4 exact paths from the story's "Read first">

Pattern to follow: <one existing file that does the same kind of thing>

Scope:
- In: <layers and files you expect to change>
- Out: <what must not change, e.g. no migration, no Web/Razor edits, no new npm packages>

Branch/PR policy:
- <owner-directed branch/worktree and whether staging, commit, push, PR creation and merge are authorized; include the exact merge codeword when one is required>

Constraints:
- AGENTS.md applies in full. A named owner-approved task rule may narrow procedural instructions for that task; it does not waive product invariants, fail-closed behavior, security restrictions, professional approval, release gates or authorization for external effects.
- <story-specific constraints>

Verify with (run each applicable focused check once; report results and omitted checks with reasons):
- <focused commands from AGENTS.md §4 that apply>
- <story-specific checks, or the manual steps per the Definition of Done §2>

Stop and ask if:
- an ADR would need to change,
- a migration would alter or drop existing data,
- the acceptance criteria conflict with code you find.

Report:
- files changed, commands run with results, commands not run and why,
- assumptions made, open questions, anything you found that looks wrong but left alone.
```

## Filled example — STE-NXT-004

```text
Implement STE-NXT-004: Practical materiality rounding in Angular
Story file: docs/execution/auditsphere-execution-user-stories-ste-v21-remaining-proposed.md#ste-nxt-004--practical-materiality-rounding-in-angular

Read first:
- src/AuditSphereOps.Application/Audit/MaterialityPracticalRounding.cs
- src/AuditSphereOps.Application/Audit/MaterialityEngineService.cs (ApplyPracticalRoundingAsync, RoundingRoles)
- src/AuditSphereOps.Application/Audit/AuditPlanWorkspaceQuery.cs
- src/AuditSphereOps.Ui/src/app/features/audit/plan.ts

Pattern to follow: the existing "Approve calculated materiality" action and CanApproveMateriality flag in the same two files.

Scope:
- In: AuditPlanWorkspace record + query (new CanApplyPracticalRounding), plan.ts form and decoder,
  regenerated contracts/auditsphere-openapi.json.
- Out: MaterialityPracticalRounding rules, the rounding endpoint, migrations, Web/Razor, npm packages.

Constraints:
- The flag uses the same authorization request as ApplyPracticalRoundingAsync; Angular only renders it.
- Amounts stay decimal strings end to end.

Verify with:
- dotnet build src/AuditSphereOps.Api/AuditSphereOps.Api.csproj --no-restore --configuration Release --maxcpucount:1
- npm --prefix src/AuditSphereOps.Ui run build
- bash scripts/contracts/verify-openapi.sh (after regenerating the contract)
- Manual, as Manager then Partner on a local engagement with an approved mapping and sealed TB:
  round PM 53,421.00 → 53,000.00 with a rationale (accepted); try a value 6 % away (refused with
  the server message); confirm the Partner's approve button now targets the rounded draft;
  confirm a Preparer sees no rounding form. Record as MANUAL_OBSERVATION.

Stop and ask if: the workspace cannot tell which assessment is the current effective draft.

Report: as in the template.
```

## Rules for writing prompts

- One story per session. If a story looks bigger than a session, split it in the stories file first.
- Name files, never "the relevant files".
- Give one pattern file. Agents copy patterns closely, so pick the cleanest example.
- Put the stopping conditions in; agents otherwise push through ambiguity.
- When the owner requires a dedicated branch or reviewable PR, name the exact branch/worktree and state whether staging, committing, pushing and PR creation are authorized. Preserve unrelated dirty changes and stage only authorized task-owned paths. Never push directly to `master`; never merge without the exact owner codeword `AUDITSPHERE-MERGE-APPROVED`. A task-specific instruction to leave work unstaged, uncommitted or unpushed takes precedence. Do not rewrite global or historical instructions as part of a task-specific branch policy.
- Never paste specification text into the prompt; link the story, which links the specification.
