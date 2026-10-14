import { Component, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { arr, bool, dec, guid, instant, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeCashFlowAllocationReview = obj({ financialPackageId: guid, sourceManifestHash: text, required: bool,
  complete: bool, canReview: bool, decision: text, evidenceReference: nullable(text), comment: nullable(text),
  decidedByUserId: nullable(guid), decidedAt: nullable(instant), movements: arr(obj({ financialPackageId: guid, cashFlowLineId: guid, section: text,
    description: text, amount: dec, currency: text, movementDate: nullable(text), activityCode: nullable(text),
    contributions: arr(obj({ generalLedgerLineId: guid, generalLedgerTransactionId: guid, sourceImportBatchId: guid,
      sourceLineHash: text, signedAmount: dec, currency: text, preparedByUserId: nullable(guid) }), 2000) }), 2000) });

@Component({
  selector: 'audit-cash-flow-allocation-review',
  imports: [FormsModule, MatButtonModule, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="allocation-review-heading">
      <h2 id="allocation-review-heading">Independent cash-flow allocation review</h2>
      <button matButton="outlined" type="button" (click)="review.reload()">Refresh allocation review</button>
      <audit-state [loading]="review.loading()" [error]="review.error()" label="the source allocation review" />
      @if (review.data(); as r) {
        @if (!r.required) { <p>No cash-flow schedule is stored for this package.</p> }
        @else {
          <p>Schedule completeness: {{ r.complete ? 'complete' : 'incomplete' }} · independent decision: {{ r.decision }}</p>
          <p>Source manifest: <code>{{ r.sourceManifestHash }}</code></p>
          @if (r.decidedAt) { <p>Decision recorded by <code>{{ r.decidedByUserId }}</code> at {{ r.decidedAt }} · {{ r.evidenceReference }}{{ r.comment ? ' · '+r.comment : '' }}</p> }
          <div class="table-scroll"><table><thead><tr><th scope="col">Movement</th><th scope="col">Accepted GL line</th><th scope="col">Source transaction / batch</th><th scope="col">Source digest</th><th scope="col">Preparer</th><th scope="col" class="number">Signed contribution</th></tr></thead>
            <tbody>@for (m of r.movements; track m.cashFlowLineId) {
              @for (c of m.contributions; track c.generalLedgerLineId) {
                <tr><td>{{ m.financialPackageId === packageId() ? 'Current' : 'Comparative' }} · {{ m.section }} · {{ m.description }} · {{ m.amount | money }} {{ m.currency }}</td><td><code>{{ c.generalLedgerLineId }}</code></td>
                  <td><code>{{ c.generalLedgerTransactionId }}</code><br /><code>{{ c.sourceImportBatchId }}</code></td><td><code>{{ c.sourceLineHash }}</code></td>
                  <td><code>{{ c.preparedByUserId ?? 'Legacy preparer unavailable' }}</code></td><td class="number">{{ c.signedAmount | money }} {{ c.currency }}</td></tr>
              }
              @if (!m.contributions.length) { <tr><td>{{ m.section }} · {{ m.description }}</td><td colspan="5">No accepted GL line contributions are recorded.</td></tr> }
            }</tbody></table></div>
          @if (r.canReview && r.complete && r.decision === 'PENDING') {
            <form class="inline-form" (submit)="$event.preventDefault(); decide(r.sourceManifestHash)">
              <label>Decision <select name="decision" [(ngModel)]="form.decision"><option value="APPROVED">Approve</option><option value="CHANGES_REQUIRED">Changes required</option><option value="REJECTED">Reject</option></select></label>
              <label>Evidence reference <input name="evidence" [(ngModel)]="form.evidenceReference" maxlength="2000" required /></label>
              <label>Comment <textarea name="comment" [(ngModel)]="form.comment" maxlength="4000" rows="3"></textarea></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">{{ cmd.busy() ? 'Recording…' : 'Record independent decision' }}</button>
            </form>
          } @else if (!r.canReview) { <p>Only a distinct authorized accounting reviewer can decide this allocation manifest.</p> }
          @else if (r.decision !== 'PENDING') { <p>This exact immutable source manifest already has a reviewer decision. A changed schedule requires a new package build and manifest.</p> }
          @else { <p>Every movement must reconcile exactly to persisted accepted-line contributions before independent review.</p> }
        }
      }
      <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
    </section>
  `,
})
export class CashFlowAllocationReview {
  private readonly api = inject(Api);
  readonly packageId = input.required<string>();
  readonly review = this.api.resource(() => this.packageId() ? `/api/ui/accounting/packages/${this.packageId()}/cash-flow-allocation-review` : null,
    decodeCashFlowAllocationReview, 'The package or its source-allocation review is not available in the current firm scope.');
  readonly cmd = new CommandState(this.api);
  form = { decision: 'APPROVED', evidenceReference: '', comment: '' };

  decide(hash: string): void {
    if (!this.form.evidenceReference.trim()) {
      this.cmd.failed.set(true); this.cmd.message.set('An evidence reference is required for the independent decision.'); return;
    }
    void this.cmd.run(`/api/ui/accounting/packages/${this.packageId()}/cash-flow-allocation-review`, {
      expectedSourceManifestHash: hash, decision: this.form.decision,
      evidenceReference: this.form.evidenceReference, comment: this.form.comment || null,
    }, 'Independent source-allocation decision recorded against the exact manifest.', () => {
      this.form = { ...this.form, evidenceReference: '', comment: '' };
    }).finally(() => this.review.reload());
  }
}
