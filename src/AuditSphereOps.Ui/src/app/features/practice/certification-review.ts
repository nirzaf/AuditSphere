import { Component, inject, signal } from '@angular/core';
import { FormField, FormRoot, form, maxLength } from '@angular/forms/signals';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState } from '../../core/api';
import { bool, date, guid, instant, nat, nullable, obj, oneOf, sha256, text } from '../../core/decode';

export const certificationReview = obj({
  certificationId: guid, subjectUserId: guid, name: text, issuer: nullable(text), expiresOn: nullable(date),
  recordedByUserId: guid, recordedAt: instant, sourceDigest: sha256, revision: nat,
  latestReview: nullable(obj({ id: guid, revision: nat, decision: oneOf('VERIFIED', 'REJECTED', 'REVOKED'),
    reviewerUserId: guid, reviewedAt: instant, evidenceReference: text, reason: text, sourceDigest: sha256 })),
  verifiedForCurrentSource: bool, expired: bool, canReview: bool,
});

@Component({
  imports: [FormField, FormRoot, MatDialogModule, MatButtonModule],
  template: `<h2 mat-dialog-title>Certification verification</h2>
    <mat-dialog-content>
      @if (view.loading()) { <p role="status">Loading retained source…</p> }
      @if (view.error()) { <p role="alert">{{ view.error() }}</p> }
      @if (view.data(); as source) {
        <h3>{{ source.name }}</h3>
        <p>Issuer: {{ source.issuer || 'Not recorded' }}</p>
        <p>Expiry: {{ source.expiresOn || 'Not recorded' }} {{ source.expired ? '(Expired)' : '' }}</p>
        <p>Verification: {{ source.verifiedForCurrentSource ? 'Administratively verified for this source' : 'Not verified for this source' }}</p>
        <p>Recorded by {{ source.recordedByUserId }} at {{ source.recordedAt }}</p>
        <details><summary>Exact source identity</summary><p>{{ source.sourceDigest }}</p></details>
        @if (source.latestReview; as review) {
          <p>Latest decision: {{ review.decision }} · revision {{ review.revision }}</p>
          <p>Reviewed by {{ review.reviewerUserId }} at {{ review.reviewedAt }}</p>
          <p>Evidence: {{ review.evidenceReference || 'Not supplied' }}</p><p>Reason: {{ review.reason }}</p>
        }
        <p>A verification decision does not grant a role or replace professional approval.</p>
        @if (source.canReview) {
          <form [formRoot]="fields" (submit)="$event.preventDefault(); publish()">
            <fieldset [disabled]="cmd.busy() || !!pending()">
              <legend>Independent administrative decision</legend>
              <label>Decision <select [formField]="fields.decision"><option value="VERIFIED">Verified</option><option value="REJECTED">Rejected</option><option value="REVOKED">Revoked</option></select></label>
              <label>Retained evidence reference <input [formField]="fields.reference" /></label>
              <label>Reason <textarea [formField]="fields.reason"></textarea></label>
              <label><input type="checkbox" [formField]="fields.reviewed" />I reviewed this exact source and decision independently.</label>
            </fieldset>
            <button matButton type="submit" [disabled]="cmd.busy() || !!pending() || !valid()">Publish decision</button>
          </form>
        } @else { <p>Only an independent firm-wide Administrator can publish a decision.</p> }
      }
      @if (cmd.message()) { <p [attr.role]="cmd.failed() ? 'alert' : 'status'">{{ cmd.message() }}</p> }
      @if (cmd.uncertain()) {
        <p>The retained request is unchanged. Recover by submitting that exact request again; no new decision identity is created.</p>
        <button matButton [disabled]="cmd.busy()" (click)="recover()">Recover exact request</button>
      }
    </mat-dialog-content>
    <mat-dialog-actions><button matButton [disabled]="cmd.busy() || cmd.uncertain()" (click)="dialog.close()">Close</button>
      <button matButton [disabled]="cmd.busy() || !!pending()" (click)="view.reload()">Refresh source</button></mat-dialog-actions>`,
  styles: `label { display:block; margin-block:1rem; } input:not([type=checkbox]), textarea, select { display:block; width:100%; } p { overflow-wrap:anywhere; }`,
  host: { '(window:beforeunload)': 'beforeUnload($event)' },
})
export class CertificationReview {
  private readonly api = inject(Api);
  readonly dialog = inject(MatDialogRef<CertificationReview>);
  private readonly id = inject<string>(MAT_DIALOG_DATA);
  readonly view = this.api.resource(() => `/api/ui/practice/resources/certifications/${this.id}/review`, certificationReview);
  readonly cmd = new CommandState(this.api);
  readonly model = signal({ decision: 'VERIFIED', reference: '', reason: '', reviewed: false });
  readonly fields = form(this.model, p => { maxLength(p.reference, 1000); maxLength(p.reason, 4000); });
  readonly pending = signal<Record<string, unknown> | null>(null);
  valid() {
    const m = this.model();
    return m.reviewed && m.reason.trim().length > 0 && m.reason.trim().length <= 4000 &&
      m.reference.trim().length <= 1000 && (m.decision !== 'VERIFIED' || m.reference.trim().length > 0) &&
      (m.decision !== 'REVOKED' || (this.view.data()?.revision ?? 0) > 0);
  }
  async publish() {
    const source = this.view.data();
    if (!source?.canReview || !this.valid() || this.pending() || this.cmd.busy()) return;
    const m = this.model();
    this.pending.set({ certificationId: source.certificationId, requestId: crypto.randomUUID(), sourceDigest: source.sourceDigest,
      expectedRevision: String(source.revision), decision: m.decision, evidenceReference: m.reference.trim(), reason: m.reason.trim(), reviewed: true });
    await this.send();
  }
  async recover() {
    if (!this.pending() || !this.cmd.uncertain() || this.cmd.busy()) return;
    this.cmd.uncertain.set(false);
    await this.send();
  }
  private async send() {
    this.dialog.disableClose = true;
    await this.cmd.run('/api/ui/practice/resources/certifications/reviews', this.pending(), 'Decision retained.', () => {
      this.pending.set(null); this.model.set({ decision: 'VERIFIED', reference: '', reason: '', reviewed: false }); this.view.reload();
    });
    if (!this.cmd.uncertain()) { this.pending.set(null); this.dialog.disableClose = false; }
  }
  beforeUnload(event: BeforeUnloadEvent) { if (this.pending()) { event.preventDefault(); event.returnValue = ''; } }
}
