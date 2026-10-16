import { Component, ElementRef, effect, input, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { OperationSnapshot } from './operations-command-contracts';
import { OperationsCommandState } from './operations-command-state';

@Component({ selector: 'audit-operation-snapshot', template: `
  <dl><dt>Operating mode</dt><dd>{{ value().operatingMode }}</dd><dt>Deployment / recovery / policy epochs</dt><dd>{{ value().deploymentEpoch }} / {{ value().recoveryEpoch }} / {{ value().policyGeneration }}</dd>
    @if (value().operation; as o) {
      <dt>Operation</dt><dd>{{ o.id }} · {{ o.kind }}</dd><dt>Client / engagement</dt><dd>{{ o.clientId || 'Firm operation' }} / {{ o.engagementId || 'None' }}</dd>
      <dt>Target / correlation</dt><dd>{{ o.targetId }} / {{ o.correlationId }}</dd><dt>Status</dt><dd>{{ o.status }}</dd>
      <dt>Expected revision / attempt token / count</dt><dd>{{ o.expectedRevision }} / {{ o.attemptToken }} / {{ o.attemptCount }}</dd>
      <dt>Reconciliation / active lease</dt><dd>{{ o.isReconciliation ? 'Yes' : 'No' }} / {{ o.hasLease ? 'Yes' : 'No' }}</dd>
      <dt>Error classification</dt><dd>{{ o.errorCode || 'None' }}</dd><dt>Cancellation disposition</dt><dd>{{ o.cancellationDisposition || 'None recorded' }}</dd>
      <dt>Next attempt / completed</dt><dd>{{ o.nextAttemptAt || 'Not scheduled' }} / {{ o.completedAt || 'Not completed' }}</dd>
      <dt>Result evidence present</dt><dd>{{ o.hasResultEvidence ? 'Yes' : 'No' }}</dd>
    }
    @if (value().recoverySession; as s) {
      <dt>Recovery session / external epoch</dt><dd>{{ s.id }} / {{ s.externalEpoch }}</dd><dt>Restore point</dt><dd>{{ s.restorePoint }}</dd>
      <dt>Reconciliation scope</dt><dd>{{ s.reconciliationScope }}</dd><dt>Recorded findings</dt><dd>{{ s.findings || 'None recorded' }}</dd>
      <dt>Session created</dt><dd>{{ s.createdAt }}</dd><dt>Restart approval</dt><dd>{{ s.approvedRestartAt || 'Not recorded' }} · {{ s.approvedByUserId || 'No approving operator' }}</dd>
    }
  </dl>
`, styles: ['dd { overflow-wrap: anywhere; white-space: pre-wrap; }'] })
export class OperationSnapshotView { readonly value = input.required<OperationSnapshot>(); }

@Component({ selector: 'audit-operations-command-review', imports: [FormsModule, MatButtonModule, OperationSnapshotView], template: `
  @if (flow().preview(); as p) {
    <section class="panel" aria-label="Exact operator command preview"><h2 #previewHeading tabindex="-1">Review operator command</h2><p>{{ p.summary }}</p>
      <p>Action {{ p.canonicalInput.action }} · request {{ p.requestId }}</p>
      <h3>Before this command</h3><audit-operation-snapshot [value]="p.before" />
      @if (p.canonicalInput.disposition) { <p>Proposed cancellation disposition: {{ p.canonicalInput.disposition }}</p> }
      @if (p.canonicalInput.findings) { <p>Proposed recovery findings: {{ p.canonicalInput.findings }}</p> }
      <p>Reviewed basis <code>{{ p.reviewBasis }}</code></p><p>Exact request fingerprint <code>{{ p.requestHash }}</code></p>
      <p>This authorizes the displayed local operator transition. It does not certify a provider effect or make a professional audit conclusion.</p>
      @for (blocker of p.blockers; track blocker) { <p role="alert">{{ blocker }}</p> }
      <label><input type="checkbox" [ngModel]="flow().reviewed()" (ngModelChange)="flow().reviewed.set($event)" [disabled]="flow().busy() || !p.canProceed" /> I reviewed this exact operator command, current state and effect.</label>
      <button matButton="filled" (click)="flow().execute()" [disabled]="flow().busy() || !flow().reviewed() || !p.canProceed">Confirm reviewed operator command</button>
    </section>
  }
  @if (flow().uncertain()) {
    <section class="panel" aria-label="Original operator request recovery"><h2>Recover original operator request</h2>
      <p role="alert">The command outcome is not confirmed. Current queue status and matching history are not an original actor-owned receipt. No automatic retry is performed.</p>
      @if (flow().pending(); as reference) { <p>Request {{ reference.requestId }} · <code>{{ reference.requestHash }}</code></p>
        <button matButton="outlined" (click)="flow().reconcile()" [disabled]="flow().busy()">Look up original operator receipt</button> }
      @else { <p>Tab checkpoint storage is unavailable. Restore storage before sending a command.</p> }
      @if (flow().absent()) { <p>Point-in-time absence does not rule out a delayed commit. Re-enter the original intent and prepare a fresh review of the same request; its ID and hash remain retained.</p> }
    </section>
  }
  @if (flow().receipt(); as r) {
    <section class="panel" aria-label="Verified original operator receipt"><h2>Verified original operator receipt</h2>
      <p>{{ r.action }} · request {{ r.requestId }} · <code>{{ r.requestHash }}</code></p>
      <p>Actor {{ r.actorId }} · exact session {{ r.actorEpoch }} · recorded {{ r.createdAt }}</p>
      <p>Command event {{ r.commandEvent.id }} · operation event {{ r.commandEvent.operationEventId || 'Firm recovery transition' }}</p>
      <h3>Recorded before</h3><audit-operation-snapshot [value]="r.commandEvent.before" />
      <h3>Recorded after</h3><audit-operation-snapshot [value]="r.commandEvent.after" />
      <p>This receipt proves the original local operator command. A worker may have advanced since this event; provider success is not implied.</p>
    </section>
  }
  @if (flow().message()) { <p [attr.role]="flow().failed() || flow().uncertain() ? 'alert' : 'status'">{{ flow().message() }}</p> }
`, styles: ['p, code { overflow-wrap: anywhere; white-space: pre-wrap; }'] })
export class OperationsCommandReview {
  readonly flow = input.required<OperationsCommandState>();
  private readonly previewHeading = viewChild<ElementRef<HTMLHeadingElement>>('previewHeading');

  constructor() {
    effect(() => {
      const requestId = this.flow().preview()?.requestId;
      const heading = this.previewHeading()?.nativeElement;
      if (!requestId || !heading) return;
      queueMicrotask(() => {
        if (heading.isConnected && this.flow().preview()?.requestId === requestId) heading.focus();
      });
    });
  }
}
