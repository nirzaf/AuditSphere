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

Constraints:
- AGENTS.md applies in full.
- <story-specific constraints>

Verify with (run all; report each result):
- <commands from AGENTS.md §3 that apply>
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
- dotnet build AuditSphereOps.slnx --no-restore --configuration Release --maxcpucount:1
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
- Never paste specification text into the prompt; link the story, which links the specification.
