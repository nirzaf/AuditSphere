# Workflow template evidence prerequisites

**Status:** CURRENT implementation guide; current-source release and browser acceptance are recorded separately in [execution status](../execution/status.json). This guide does not authorize deployment, configuration changes or professional approval.

## Purpose and boundaries

A template can declare evidence prerequisites for its operational tasks. Creating or completing tasks never approves materiality, accepts client evidence, reviews procedures, activates an engagement or issues reports. Those decisions remain in their existing authorized workspaces.

The feature uses existing workflow services, Application authorization and PostgreSQL. It adds no Microsoft permissions, provider identity or external messages. Existing `WorkflowExperience:Enabled` controls still apply.

## Configure a template

1. Open **Workflow templates** as an authorized firm-wide Partner or Administrator.
2. Create or edit a draft. For each step, add up to eight **Authoritative evidence prerequisites**.
3. Give each declaration a unique stable requirement key within that step and select a supported kind:

| Kind | Required source | Satisfaction comes from |
| --- | --- | --- |
| Current approved materiality | Current engagement materiality context | Existing current independent materiality approval |
| Accepted or closed PBC request | Exact request in the same client and engagement | Existing PBC acceptance or closure |
| Current reviewed procedure | Exact procedure in the same client and engagement | Existing current procedure review and generation checks |

4. Save the draft. A different authorized person reviews the exact revision before publication.
5. Edit published instructions through a new revision. Existing plans retain their original revision; publishing a replacement does not rewrite them.

Declare current materiality at most once per step. Distinct PBC/procedure requirements must resolve to distinct exact sources within that step; duplicate references are rejected, not silently combined.

Use requirement keys to distinguish declarations, rather than embedding a client request identifier in a reusable template. Descriptive PBC references are never interpreted as executable source bindings.

## Instantiate an engagement plan

1. Open the engagement’s **Operational task plan** and preview an appropriate published template.
2. For each PBC or procedure prerequisite, obtain the exact source identifier from that engagement’s authorized workspace and enter it beside its requirement key. Materiality uses the current engagement context.
3. Preview again. Source selection changes clear the previous preview and assent. The server checks exact firm, client and engagement scope; foreign or unavailable sources cannot be bound.
4. Resolve missing source bindings before creating tasks. Evidence may still be pending: creating a plan attaches the prerequisite; it does not satisfy it.
5. Review the exact preview, staffing, schedule and template revision, then create the operational tasks.

If the required PBC request does not exist yet, create it through the existing PBC workflow first. A typed PBC instruction or descriptive reference is not a substitute for an existing exact evidence source. Changing current source inputs can require a fresh preview.

## Review and recover

- Task **Evidence prerequisites** shows current source state and the originating template requirement. When current materiality resolves an assessment, its evaluated assessment reference is shown separately from the context-based prerequisite binding. Readiness is evaluated live; there is no editable “satisfied” flag.
- Existing authorized, reviewed coordination-rule withdrawal remains available. Withdrawal does not approve source evidence, and append-only history retains template provenance.
- For an unconfirmed plan execution, retain the original browser recovery key and use **Recover saved receipt**. Do not submit a new plan to work around an uncertain result.
- Existing plans recover their original receipt and source bindings. Replacing a published template does not migrate or rebind those tasks automatically.
- Legacy templates with no declarations retain their original content identity and behavior. No historical professional evidence is backfilled or rewritten.

## Deployment and verification

Migration `WorkflowTemplateEvidenceRequirements` adds declaration rows and nullable rule provenance. PostgreSQL guards restrict declaration changes to draft revisions, cap each step at eight declarations and retain exact task/template provenance across rule revisions. Downgrade refuses to discard retained declarations or provenance. Follow the existing backup and migration approval process; do not treat an application rollback as a database downgrade.

Focused checks, browser observations and remaining release gates belong in [execution status](../execution/status.json). Passing a build or synthetic journey does not prove production readiness or replace human methodology and accessibility acceptance.

## Code ownership

- [Template service](../../src/AuditSphereOps.Application/Practice/WorkflowTemplateService.cs)
- [Engagement plan service](../../src/AuditSphereOps.Application/Practice/EngagementWorkflowPlanService.cs)
- [Live evidence query](../../src/AuditSphereOps.Application/Practice/WorkflowTaskEvidenceQuery.cs)
- [Template editor](../../src/AuditSphereOps.Ui/src/app/features/practice/workflow-templates.ts)
- [Plan UI](../../src/AuditSphereOps.Ui/src/app/features/engagements/engagement-task-plan.ts)
