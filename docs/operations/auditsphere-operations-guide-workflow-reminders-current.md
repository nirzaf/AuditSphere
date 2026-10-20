# AuditSphere — Reviewed workflow reminders

**Status:** CURRENT operating guidance for the matching migrated release. Implementation and acceptance evidence remain in [status.json](../execution/status.json); this guide does not authorize deployment or enable execution.

**Purpose:** Configure bounded internal reminder tasks without changing professional approvals or treating task creation as delivered email.

## Configuration and access

Open **Practice → Reminder rules** (`/app/practice/reminder-rules`). The existing trusted session and firm-wide grant checks apply. Administrator/Partner users configure rules; a different eligible user must review the exact revision before publication.

The API exposes rule administration when `WorkflowExperience:Enabled` is true. Execution additionally requires `WorkflowReminders:Enabled` in the applicable API/worker configuration. The existing general worker owns discovery and execution; the isolated mail, PBC and client-sites workers do not execute this handler.

This configuration permits administration while keeping automatic execution off:

```json
{
  "WorkflowExperience": { "Enabled": true },
  "WorkflowReminders": { "Enabled": false, "BatchSize": 25 }
}
```

Equivalent process environment keys are `WorkflowExperience__Enabled`, `WorkflowReminders__Enabled` and `WorkflowReminders__BatchSize`. Batch size must be within 1–100. A catalog flag describes host configuration; it does not prove a healthy worker or provider delivery. Use the approved deployment procedure for host configuration changes.

## Review a rule

1. Choose the supported source (`TASK_DUE` or `PBC_DUE`) and recipient policy (`RESPONSIBLE_STAFF` or `ENGAGEMENT_REVIEWER`).
2. Set the offset, repeat interval, timezone, quiet hours and effective UTC time. Offsets are within −30–365 days; a positive repeat interval is within 1–90 days.
3. For a repeating rule, enter **Maximum scheduled windows**, within 1–100. This is part of the exact reviewed definition and digest. A one-time rule uses repeat interval zero and no repeating-window bound.
4. Save the draft, have a different eligible reviewer inspect it, then explicitly publish the reviewed revision. Changed content needs a fresh review. Retain the draft request key until its receipt is known; after a lost response, look up that request rather than creating another intent blindly.

The JSON/API field is `maxScheduledWindowCount`. A repeating draft must supply its explicit count; a one-time definition uses null or omits the field. A retry of an already saved legacy intent recovers that intent and does not authorize publishing or executing an unbounded rule.

Rules create operational tasks only. Source evidence, recipient eligibility, publisher authority, rule version and source revision are checked again during execution. Completion of a reminder task does not accept PBC evidence, approve materiality or a procedure, post an invoice or journal, issue a report/release, or freeze an audit file.

## What the window limit means

The limit counts schedule windows for one exact source due-date revision and rule revision. It is neither a durable-operation retry limit nor a delivery receipt count. A recipient receives at most the existing idempotent intent/task for an eligible window; the worker does not backfill every missed window.

For a source due on 1 January, offset zero, repeat interval seven days and maximum three windows:

| Window | Opens | Can originate a new reminder intent? |
| --- | --- | --- |
| 1 | 1 January | Yes, subject to current source and authorization checks |
| 2 | 8 January | Yes, subject to the same checks |
| 3 | 15 January | Yes, subject to the same checks |
| Later windows | From 22 January | No; the schedule is exhausted |

Quiet periods can defer eligible work without creating another occurrence identity. An already queued intent belonging to an allowed window can retry later only while its exact rule/source/recipient and recovery guards still permit it. A reviewed change to the source due-date revision or publication of a new rule revision has its own schedule identity; old tasks and receipts remain retained.

## Existing rules and diagnostics

The additive migration preserves existing definitions, digests and decisions. A legacy repeating rule with no explicit maximum remains readable but requires a newly bounded, independently reviewed revision before it can execute or resume. Do not update its published row, backfill an invented policy, or mark old work delivered.

| Diagnostic | Meaning | Operator action |
| --- | --- | --- |
| `NEEDS_REVISION` / `workflow-reminder-needs-revision` | Legacy repeating rule has no reviewed maximum | Create and review a bounded replacement revision |
| `SCHEDULE_WINDOWS_EXHAUSTED` / `workflow-reminder-schedule-windows-exhausted` | No new window is permitted by the bound | Review source work and policy; do not reset history to force another reminder |
| `NO_ELIGIBLE_RECIPIENT` | No currently authorized recipient was resolved | Review the exact engagement's staffing and grants |
| `workflow-reminder-source-changed` | Retained intent no longer matches its source | Refresh the source; preserve the old receipt and let normal discovery assess current work |
| `workflow-reminder-recovery-review-required` | A suspension/resumption requires explicit recovery review | Review the retained operation through Operations using an authorized administrator |

The catalog shows the latest evaluation diagnostic; execution history describes individual durable operations. A batch diagnostic is not proof that every source in the firm has the same state. Suspending a rule prevents new automatic work; resuming it does not silently release operations retained by the recovery guard.

## Verification and rollout

- Keep execution disabled until the matching schema, rule review and intended source/recipient behavior are accepted in the target environment.
- Verify the last permitted window, the first exhausted window, duplicate/retry behavior, revoked authority, a legacy unbounded rule and a one-time rule using synthetic data.
- Verify that database guards reject edits to a reviewed/published bound and preserve rule history. Follow the existing backup/migration procedure; do not downgrade away retained policy evidence.
- Inspect the actual rule and execution screens. A queued or completed internal task establishes no Microsoft email acceptance or delivery. Mail configuration, consent and business-delivery evidence retain their separate gates.

Implementation owners: [rule service](../../src/AuditSphereOps.Application/Practice/WorkflowReminderRuleService.cs), [discovery](../../src/AuditSphereOps.Application/Practice/WorkflowReminderDiscovery.cs), [execution handler](../../src/AuditSphereOps.Application/Practice/WorkflowReminderTaskHandler.cs), and [schedule model](../../src/AuditSphereOps.Domain/Practice/WorkflowReminderRules.cs).
