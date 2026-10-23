import { Component, inject, input, linkedSignal, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, guid, instant, nullable, obj, oneOf, sha256, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

/** Independent content review of one exact draft presentation (CP-13/14). The server decides every rule; this only renders the result. */
export const decodeStatementContentReview = obj({
  financialPackageId: guid, presentationRevisionId: guid, snapshotSha256Hex: sha256, approved: bool, canReview: bool, sourceIsCurrent: bool,
  latestReviewId: nullable(guid), reviewedChecks: arr(text, 20),
  reviews: arr(obj({ id: guid, decision: oneOf('APPROVED', 'RETURNED'), reason: text, reviewedByUserId: guid, reviewedAt: instant,
    snapshotSha256Hex: sha256 }), 50),
});
export type StatementContentReviewState = ReturnType<typeof decodeStatementContentReview>;

/** The original intent of a content decision, kept until the server answers so a lost reply retries the same decision. */
export interface PendingContentDecision { requestId: string; key: string }

export function contentDecisionIntent(pending: PendingContentDecision | null, state: StatementContentReviewState, decision: string,
  reason: string | null, newRequestId: string): PendingContentDecision {
  const key = JSON.stringify([state.presentationRevisionId, state.snapshotSha256Hex, state.latestReviewId, decision, reason]);
  return pending && pending.key === key ? pending : { requestId: newRequestId, key };
}

export function contentDecisionRequest(state: StatementContentReviewState, decision: string, reason: string | null,
  requestId: string): Record<string, unknown> {
  return { decision, reason, requestId, expectedSnapshotSha256Hex: state.snapshotSha256Hex, expectedPreviousReviewId: state.latestReviewId };
}

@Component({
  selector: 'audit-statement-content-review',
  imports: [FormsModule, MatButtonModule, ...SHARED],
  template: `
    <section aria-label="Independent content review">
      <h4>Independent content review</h4>
      <audit-state [loading]="state.loading()" [error]="state.error()" label="the content review" />
      @if (state.data(); as s) {
        <p role="status">{{ s.approved
          ? (s.sourceIsCurrent
            ? 'Approved: the note, disclosure and equity content of this exact draft snapshot was approved by an independent reviewer.'
            : 'Historical approval: an independent reviewer approved this exact snapshot, but its source is no longer current or available.')
          : 'Not approved. This draft cannot become a signing candidate until an independent reviewer approves its note, disclosure and equity content.' }}</p>
        <p class="source-currentness" role="status">{{ s.sourceIsCurrent
          ? 'Source current: this draft matches its package, layout and supporting source evidence.'
          : 'Source stale or unavailable. Recorded content decisions remain historical; pin a new draft from current source before review or signing.' }}</p>
        <dl class="review-identity">
          <dt>Presentation revision</dt><dd><code>{{ s.presentationRevisionId }}</code></dd>
          <dt>Snapshot SHA-256</dt><dd><code>{{ s.snapshotSha256Hex }}</code></dd>
        </dl>
        <p><small>An approval covers only this snapshot (<code>{{ s.snapshotSha256Hex.slice(0, 12) }}</code>). Equity evidence stays labelled as reviewed manual evidence, not ledger-derived. This is not a formal statement approval.</small></p>
        @if (s.reviews.length) {
          <div class="table-scroll"><table><caption class="sr-only">Content review decisions, newest first</caption>
            <thead><tr><th scope="col">Decision</th><th scope="col">Reason</th><th scope="col">Reviewer ID</th><th scope="col">Review ID</th><th scope="col">Reviewed at</th></tr></thead>
            <tbody>@for (r of s.reviews; track r.id) { <tr><td><audit-status [value]="r.decision" /></td><td>{{ r.reason || 'No reason recorded' }}</td><td><code>{{ r.reviewedByUserId }}</code></td><td><code>{{ r.id }}</code></td><td><time [attr.datetime]="r.reviewedAt">{{ r.reviewedAt }}</time></td></tr> }</tbody></table></div>
        }
        @if (s.canReview && s.sourceIsCurrent) {
          <form class="inline-form" (submit)="$event.preventDefault(); record(s)">
            <label>Decision <select name="contentDecision" [(ngModel)]="decision"><option value="APPROVED">APPROVED</option><option value="RETURNED">RETURNED</option></select></label>
            <label>Reason <textarea name="contentReason" [(ngModel)]="reason" maxlength="2000" rows="2"></textarea></label>
            <button matButton="filled" type="submit" [disabled]="busy() || uncertain()">{{ busy() ? 'Recording…' : 'Record content decision' }}</button>
          </form>
          <p><small>The preparer who pinned this draft cannot record this decision. A return needs a reason.</small></p>
        }
        @if (message()) { <p role="status" aria-live="polite">{{ message() }}</p> }
      }
    </section>
  `,
  styles: `
    .review-identity dd { margin-inline-start: 0; overflow-wrap: anywhere; }
    td code { overflow-wrap: anywhere; }
  `,
})
export class StatementContentReview {
  private readonly api = inject(Api);
  readonly packageId = input.required<string>();
  readonly revisionId = input.required<string>();
  readonly state = this.api.resource(
    () => (this.packageId() && this.revisionId()
      ? `/api/ui/accounting/packages/${this.packageId()}/presentation/pins/${this.revisionId()}/content-review` : null),
    decodeStatementContentReview, 'The content review is not available in the current firm scope.');
  decision = 'APPROVED';
  reason = '';
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly message = linkedSignal({ source: this.revisionId, computation: () => '' });
  private pending: PendingContentDecision | null = null;

  async record(s: StatementContentReviewState): Promise<void> {
    if (!s.canReview || !s.sourceIsCurrent || this.busy() || this.uncertain()) return;
    const reason = this.reason.trim() || null;
    const intent = contentDecisionIntent(this.pending, s, this.decision, reason, crypto.randomUUID());
    this.pending = intent;
    this.busy.set(true);
    try {
      const r = await this.api.command(
        `/api/ui/accounting/packages/${this.packageId()}/presentation/pins/${this.revisionId()}/content-review`,
        contentDecisionRequest(s, this.decision, reason, intent.requestId));
      this.message.set(r.ok ? 'Content decision recorded for this exact draft snapshot.' : r.message);
      if (!r.ok && r.unknown) this.uncertain.set(true);
      if (r.ok || !r.unknown) this.pending = null;
      if (r.ok) this.reason = '';
      this.state.reload();
    } finally { this.busy.set(false); }
  }
}
