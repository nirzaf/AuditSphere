import { Component, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { PlanningCommandState } from './planning-command-state';
import { PlanningPreview, planningPayloadKey } from './planning-command-contracts';

@Component({ selector: 'audit-planning-command-review', imports: [FormsModule, MatButtonModule], template: `
  @if (flow().preview(); as p) {
    <section class="panel" aria-label="Exact planning preview">
      <h2>Review planning action</h2><p>{{ p.summary }}</p>
      <dl>
        <dt>Action</dt><dd>{{ p.state.action }}</dd><dt>Engagement</dt><dd>{{ p.state.engagementId }}</dd>
        @for (line of lines(p); track line.label) { <dt>{{ line.label }}</dt><dd>{{ line.value }}</dd> }
        @if (p.state.source; as source) {
          <dt>Approved mapping</dt><dd>{{ source.mappingId }} · version {{ source.mappingVersion }}</dd>
          <dt>Trial balance</dt><dd>{{ source.datasetId }} · {{ source.currency }}</dd><dt>Dataset SHA-256</dt><dd><code>{{ source.datasetDigest }}</code></dd>
        }
        @if (p.state.materiality; as m) {
          <dt>Current materiality assessment</dt><dd>{{ m.assessmentId }} · {{ m.state }} · {{ m.currency }}</dd>
          <dt>Current PM / TE / SAD</dt><dd>{{ m.planningMateriality }} / {{ m.tolerableError }} / {{ m.sadThreshold }}</dd>
          <dt>Computed PM / TE / SAD</dt><dd>{{ m.computedPlanningMateriality }} / {{ m.computedTolerableError }} / {{ m.computedSadThreshold }}</dd>
          <dt>Materiality rationale</dt><dd>{{ m.rationale }}</dd><dt>Qualitative considerations</dt><dd>{{ m.qualitativeConsiderations || 'None recorded' }}</dd>
          <dt>Rounding rationale</dt><dd>{{ m.roundingRationale || 'No rounding recorded' }}</dd>
          <dt>Prepared by</dt><dd>{{ m.preparedByUserId }}</dd><dt>Policy</dt><dd>{{ m.policyVersion }}</dd>
        }
        @if (p.materialityOutcome; as outcome) {
          <dt>Preview benchmark</dt><dd>{{ outcome.benchmarkAmount }} {{ outcome.currency }}</dd>
          <dt>Resulting planning materiality</dt><dd>{{ outcome.planningMateriality }} {{ outcome.currency }}</dd>
          <dt>Resulting tolerable error</dt><dd>{{ outcome.tolerableError }} {{ outcome.currency }}</dd>
          <dt>Resulting SAD threshold</dt><dd>{{ outcome.sadThreshold }} {{ outcome.currency }}</dd>
        }
        @if (p.state.risk; as risk) {
          <dt>Risk</dt><dd>{{ risk.area }} · {{ risk.assertion }} · {{ risk.riskId }}</dd><dt>Description</dt><dd>{{ risk.description }}</dd>
          <dt>Drivers</dt><dd>{{ risk.drivers }}</dd><dt>Significance</dt><dd>{{ risk.significanceDecision }}</dd>
          <dt>Response</dt><dd>{{ risk.response }}</dd><dt>Controls considered</dt><dd>{{ risk.controlsConsidered || 'None recorded' }}</dd>
          <dt>Current assessment</dt><dd>{{ risk.assessmentId || 'Not assessed' }} · {{ risk.band || 'No band' }}</dd>
          <dt>Assessment rationale</dt><dd>{{ risk.assessmentRationale || 'None recorded' }}</dd>
          <dt>Fraud / critical estimate</dt><dd>{{ risk.fraud ? 'Yes' : 'No' }} / {{ risk.criticalEstimate ? 'Yes' : 'No' }}</dd>
        }
        @if (p.riskBandOutcome) { <dt>Resulting risk band</dt><dd>{{ p.riskBandOutcome }}</dd> }
        @if (p.state.procedure; as procedure) {
          <dt>Procedure</dt><dd>{{ procedure.procedureId }} · {{ procedure.status }} · result revision {{ procedure.resultRevision }}</dd>
          <dt>Existing linked risk</dt><dd>{{ procedure.riskId || 'None' }}</dd>
        }
        @if (p.canonicalInput.assignRiskOwner; as assign) {
          @for (owner of p.state.owners; track owner.userId) { @if (owner.userId === assign.ownerUserId) { <dt>Selected staffed owner</dt><dd>{{ owner.name }} · {{ owner.staffingLevel }}</dd> } }
        }
        <dt>Reviewed source fingerprint</dt><dd><code>{{ p.reviewBasis }}</code></dd>
        <dt>Exact request fingerprint</dt><dd><code>{{ p.requestHash }}</code></dd>
      </dl>
      @for (blocker of p.state.blockers; track blocker) { <p role="alert">{{ blocker }}</p> }
      <label><input type="checkbox" [ngModel]="flow().reviewed()" (ngModelChange)="flow().reviewed.set($event)" [disabled]="flow().busy() || !p.state.canProceed" /> I reviewed this exact planning preview, source evidence and effect.</label>
      <button matButton="filled" (click)="flow().execute()" [disabled]="flow().busy() || !flow().reviewed() || !p.state.canProceed">Confirm reviewed planning action</button>
    </section>
  }
  @if (flow().uncertain()) {
    <section class="panel" aria-label="Exact planning request recovery"><h2>Recover planning request</h2>
      <p role="alert">The command outcome is not confirmed. Matching history is not an actor-owned receipt. Do not repeat the action blindly.</p>
      @for (pending of flow().pending(); track pending.engagementId + pending.action) {
        <p>{{ pending.action }} · engagement {{ pending.engagementId }} · request {{ pending.reference.requestId }} · <code>{{ pending.reference.requestHash }}</code></p>
        <button matButton="outlined" (click)="flow().reconcile(pending.action)" [disabled]="flow().busy()">Look up exact planning receipt</button>
        @if (flow().absent().includes(pending.action)) { <p>No receipt was found at lookup time. Re-enter the exact original intent and prepare a fresh preview of this same request before explicitly submitting again.</p> }
      }
    </section>
  }
  @if (flow().receipt(); as receipt) {
    <section class="panel" aria-label="Verified planning receipt"><h2>Verified planning receipt</h2><p>{{ receipt.action }} · {{ receipt.result.summary }}</p>
      <p>Request {{ receipt.requestId }} · <code>{{ receipt.requestHash }}</code></p><p>Actor {{ receipt.actorId }} · session {{ receipt.actorEpoch }} · {{ receipt.createdAt }}</p>
      <p>Persisted result {{ receipt.result.resultId }}</p>
    </section>
  }
  @if (flow().message()) { <p [attr.role]="flow().failed() || flow().uncertain() ? 'alert' : 'status'">{{ flow().message() }}</p> }
`, styles: ['dd, code { overflow-wrap: anywhere; }'] })
export class PlanningCommandReview {
  readonly flow = input.required<PlanningCommandState>();
  lines(p: PlanningPreview): { label: string; value: string }[] {
    const fields = p.canonicalInput[planningPayloadKey[p.canonicalInput.action]];
    return Object.entries(fields ?? {}).map(([key, value]) => ({ label: key.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, c => c.toUpperCase()),
      value: value === null ? 'None' : typeof value === 'boolean' ? value ? 'Yes' : 'No' : String(value) }));
  }
}
