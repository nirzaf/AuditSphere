import { Component, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { ReviewPointCommandState } from './review-point-command-state';
import { reviewPointStatus } from './review-point-command-contracts';

@Component({ selector: 'audit-review-point-command-review', imports: [FormsModule, MatButtonModule], template: `
  @if (flow().preview(); as p) {
    <section class="panel" aria-label="Exact review point preview">
      <h2>Review recorded disposition</h2><p>{{ p.summary }}</p>
      <p>{{ p.canonicalInput.action }} · review point {{ p.before.reviewPointId }} · client {{ p.before.clientId }} · engagement {{ p.before.engagementId }}</p>
      <dl class="facts"><dt>Immutable target</dt><dd>{{ p.before.targetKind }} · {{ p.before.targetId }} · target revision {{ p.before.targetRevision }}</dd>
        <dt>Original comment</dt><dd>{{ p.before.comment }}</dd><dt>Raised by</dt><dd>{{ p.before.raisedByUserId }} · {{ p.before.raisedAt }}</dd></dl>
      <div class="table-scroll"><table aria-label="Disposition before and proposed after"><thead><tr><th>Field</th><th>Persisted before</th><th>Proposed after</th></tr></thead><tbody>
        <tr><th scope="row">Disposition revision</th><td>{{ p.expectedDispositionRevision }}</td><td>{{ p.nextDispositionRevision }}</td></tr>
        <tr><th scope="row">Disposition</th><td>{{ p.before.status }}</td><td>{{ status(p.cleared, p.before.significant) }}</td></tr>
        <tr><th scope="row">Significant point completion blocker</th><td>{{ p.before.significant && !p.before.cleared ? 'Yes' : 'No' }}</td><td>{{ p.before.significant && !p.cleared ? 'Yes' : 'No' }}</td></tr>
        <tr><th scope="row">Immutable target revision</th><td>{{ p.before.targetRevision }}</td><td>{{ p.before.targetRevision }}</td></tr>
      </tbody></table></div>
      <p>This records an administrative disposition by the executing operator. It does not establish independent review or a professional conclusion.</p>
      <p>Reviewed basis <code>{{ p.reviewBasis }}</code> · exact request <code>{{ p.requestHash }}</code></p>
      @for (blocker of p.blockers; track blocker) { <p role="alert">{{ blocker }}</p> }
      <label><input type="checkbox" [ngModel]="flow().reviewed()" (ngModelChange)="flow().reviewed.set($event)" [disabled]="flow().busy() || !p.canProceed" /> I reviewed this exact review point, immutable target and disposition change.</label>
      <button matButton="filled" (click)="flow().execute()" [disabled]="flow().busy() || !p.canProceed || !flow().reviewed()">Confirm reviewed disposition</button>
    </section>
  }
  @if (flow().uncertain()) {
    <section class="panel" aria-label="Exact review point request recovery"><h2>Recover disposition request</h2>
      <p role="alert">The disposition outcome is not confirmed. Matching history is not an actor-owned receipt. Do not repeat blindly.</p>
      @for (p of flow().pending(); track p.reviewPointId + p.action) {
        <p>{{ p.action }} · point {{ p.reviewPointId }} · request {{ p.reference.requestId }} · <code>{{ p.reference.requestHash }}</code></p>
        <button matButton="outlined" (click)="flow().reconcile(p.action)" [disabled]="flow().busy()">Look up exact disposition receipt</button>
        @if (flow().absent().includes(p.action)) { <p>No receipt was found at lookup time. A delayed command may still commit. Prepare a fresh review of this same request and explicitly assent before submitting again.</p> }
      }
    </section>
  }
  @if (flow().receipt(); as r) {
    <section class="panel" aria-label="Verified review point receipt"><h2>Verified recorded disposition</h2>
      <p>{{ r.action }} · review point {{ r.reviewPointId }} · event {{ r.dispositionEvent.id }}</p>
      <p>Actor {{ r.actorId }} · exact session {{ r.actorEpoch }} · {{ r.createdAt }}</p>
      <p>Request {{ r.requestId }} · <code>{{ r.requestHash }}</code></p>
      <p>Target {{ r.dispositionEvent.before.targetKind }} · {{ r.dispositionEvent.before.targetId }} · immutable target revision {{ r.dispositionEvent.before.targetRevision }}</p>
      <p>Disposition {{ r.dispositionEvent.before.status }} → {{ r.dispositionEvent.after.status }} · disposition revision {{ r.dispositionEvent.before.dispositionRevision }} → {{ r.dispositionEvent.after.dispositionRevision }}</p>
    </section>
  }
  @if (flow().message()) { <p [attr.role]="flow().failed() || flow().uncertain() ? 'alert' : 'status'">{{ flow().message() }}</p> }
`, styles: ['td, dd, code, p { overflow-wrap: anywhere; }'] })
export class ReviewPointCommandReview {
  readonly flow = input.required<ReviewPointCommandState>();
  readonly status = reviewPointStatus;
}
