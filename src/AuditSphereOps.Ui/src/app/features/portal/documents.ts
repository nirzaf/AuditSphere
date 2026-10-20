import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, guid, instant, nat, nullable, obj, sha256, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { revision } from './contracts';
import { clientUtcTime } from '../clients/client-contracts';
const portalDocumentContext = obj({ clientLegalName: text, serviceRoute: text, periodStart: text, periodEnd: text });
export const sharedDocuments = obj({ documents: arr(obj({ reviewId: guid, deliverableId: guid, kind: text, title: text, version: revision,
  sha256, acknowledgedAt: nullable(instant), comments: arr(obj({ body: text, fromClient: bool, resolution: nullable(text) }), 1000), context: portalDocumentContext }), 200),
  signedLetters: arr(obj({ id: guid, deliverableId: guid, version: nat, managementSignatory: text, contentSha256: sha256, uploadedAt: instant, verified: bool, current: bool }), 100),
  bundles: arr(obj({ id: guid, sha256, manifestSha256: sha256, assembledAt: instant, acknowledgedAt: nullable(instant), context: portalDocumentContext }), 100) });
type SignedScan = ReturnType<typeof sharedDocuments>['signedLetters'][number];
type PortalDocumentContext = ReturnType<typeof sharedDocuments>['documents'][number]['context'];
@Component({ selector: 'audit-portal-documents', imports: [ReactiveFormsModule, MatButtonModule, ...SHARED], template: `
  <section class="panel"><h2>Documents for your review</h2><audit-state [loading]="ws.loading()" [error]="ws.error()" label="your shared documents" />
    <audit-command-message [message]="message()" [failed]="failed()" />
    @if (commentUncertain() && commentPending()) { <button matButton (click)="recoverComment()" [disabled]="busy()">Check exact comment receipt</button> }
    @if (ws.data(); as w) {
      @for (d of w.documents; track d.reviewId) {
        <article class="document"><h3><a [href]="'/api/deliverables/' + d.deliverableId + '/download'">{{ d.title }} v{{ d.version }}</a></h3><p class="fingerprint">SHA-256 {{ d.sha256 }}</p>
          <dl class="context"><dt>Client legal name</dt><dd>{{ d.context.clientLegalName || 'Not recorded' }}</dd>
            <dt>Service</dt><dd>{{ d.context.serviceRoute || 'Service not recorded' }}</dd>
            <dt>Period start</dt><dd>{{ d.context.periodStart || 'Not recorded' }}</dd>
            <dt>Period end</dt><dd>{{ d.context.periodEnd || 'Not recorded' }}</dd></dl>
          @for (c of d.comments; track $index) { <p>{{ c.fromClient ? 'Client' : 'Auditor' }}: {{ c.body }} @if(c.resolution) { — resolved: {{ c.resolution }} }</p> }
          @if (d.acknowledgedAt) { <p>Acknowledged {{ d.acknowledgedAt }}</p> }
          @else {
            <button matButton (click)="select(d.reviewId)" [disabled]="busy()">Review this version</button>
            @if (selected() === d.reviewId) { <form [formGroup]="review" (ngSubmit)="comment(d.reviewId)">
              <label for="document-comment">Comment on {{ d.title }}</label><textarea id="document-comment" formControlName="body" maxlength="4000"></textarea>
              <button matButton="filled" [disabled]="busy() || uncertain() || !review.controls.body.value.trim()">Send comment</button>
              <label><input type="checkbox" formControlName="reviewed" /> I reviewed this exact document version and hash.</label>
              <button matButton type="button" (click)="acknowledge(d.reviewId, d.sha256)" [disabled]="busy() || uncertain() || !review.controls.reviewed.value">Acknowledge this version</button>
            </form> }
          }
          @if (d.kind === 'REPRESENTATION_LETTER' && d.acknowledgedAt) {
            <p>Print this exact version on client letterhead, have authorized management sign it, and upload the complete PDF scan (maximum 10 MB).</p>
            <button matButton (click)="select(d.reviewId)" [disabled]="busy()">Upload signed letter</button>
            @if (selected() === d.reviewId) { <form [formGroup]="signed" (ngSubmit)="upload(d.deliverableId, d.sha256)">
              <label for="management-signatory">Management signatory</label><input id="management-signatory" formControlName="signatory" maxlength="200" />
              <label for="signed-file">Management-signed PDF</label><input id="signed-file" type="file" accept=".pdf,application/pdf" (change)="choose($event)" [disabled]="busy()" />
              <button matButton="filled" [disabled]="busy() || uncertain() || signed.invalid || !file()">Upload signed PDF</button>
            </form> }
          }
          @for (s of w.signedLetters; track s.id) { @if (s.deliverableId === d.deliverableId) {
            <section class="signed-scan">
              <p><a [href]="'/api/representation-scans/' + s.id + '/download'" [attr.aria-label]="scanDownloadLabel(s, d.context)">Management-signed PDF scan</a></p>
              <dl class="context">
                <dt>Management signatory</dt><dd>{{ s.managementSignatory }}</dd>
                <dt>Source letter version</dt><dd>{{ s.version }}</dd>
                <dt>Scan uploaded</dt><dd><time [attr.datetime]="s.uploadedAt">{{ utcTime(s.uploadedAt) }}</time></dd>
                <dt>Scan SHA-256</dt><dd class="fingerprint">{{ s.contentSha256 }}</dd>
                <dt>Source letter status</dt><dd><audit-status [value]="s.current ? 'CURRENT' : 'STALE'" /></dd>
                <dt>Separate verification status</dt><dd><audit-status [value]="s.verified ? 'VERIFICATION RECORDED' : 'NOT VERIFIED'" /></dd>
              </dl>
              <p class="scan-note">Uploading a scan or acknowledging a document alone does not verify signature authenticity.</p>
            </section>
          } }
        </article>
      } @empty { <p>No reports or letters are shared with your current scope.</p> }
      @for (b of w.bundles; track b.id) { <article class="document"><p><a [href]="'/api/deliverable-bundles/' + b.id + '/download'" [attr.aria-label]="bundleDownloadLabel(b)">Download five-part final bundle</a></p>
        <p class="fingerprint">ZIP SHA-256 {{ b.sha256 }} · manifest SHA-256 {{ b.manifestSha256 }}</p>
        <dl class="context"><dt>Client legal name</dt><dd>{{ b.context.clientLegalName || 'Not recorded' }}</dd>
          <dt>Service</dt><dd>{{ b.context.serviceRoute || 'Service not recorded' }}</dd>
          <dt>Period start</dt><dd>{{ b.context.periodStart || 'Not recorded' }}</dd>
          <dt>Period end</dt><dd>{{ b.context.periodEnd || 'Not recorded' }}</dd></dl>
        <p>Final bundle assembled at <time [attr.datetime]="b.assembledAt">{{ utcTime(b.assembledAt) }}</time>. This is an assembly time, not evidence of delivery or client receipt.</p>
        @if (b.acknowledgedAt) { <p>Receipt acknowledged by your client account at <time [attr.datetime]="b.acknowledgedAt">{{ utcTime(b.acknowledgedAt) }}</time>. This is a client report, not proof of email delivery or an independently observed download.</p> }
        @else { <label><input type="checkbox" [checked]="receiptReviewed() === b.id" (change)="reviewReceipt(b.id, $event)" /> I confirm that I received this exact final bundle.</label>
          <button matButton (click)="acknowledgeBundle(b)" [disabled]="busy() || uncertain() || receiptReviewed() !== b.id">Record receipt acknowledgment</button> }
      </article> }
    }
  </section>
`, styles: `.fingerprint { overflow-wrap: anywhere; } .document { padding-block: 1rem; border-bottom: 1px solid var(--mat-sys-outline-variant); } .context { display: grid; grid-template-columns: minmax(0, .9fr) minmax(0, 1.3fr); gap: .25rem .75rem; margin-block: .75rem; } .context dt { min-width: 0; font-weight: 600; overflow-wrap: anywhere; } .context dd { min-width: 0; margin: 0; overflow-wrap: anywhere; } .signed-scan { margin-block: .75rem; padding: .75rem; border-inline-start: 3px solid var(--mat-sys-outline-variant); } .scan-note { font-size: .875rem; } textarea { width: 100%; min-height: 6rem; }` })
export class PortalDocuments {
  private readonly api = inject(Api); private readonly session = inject(SessionService); private readonly fb = inject(FormBuilder);
  readonly utcTime = clientUtcTime;
  readonly ws = this.api.resource(() => '/api/ui/portal/documents', sharedDocuments);
  readonly review = this.fb.nonNullable.group({ body: ['', Validators.maxLength(4000)], reviewed: false });
  readonly signed = this.fb.nonNullable.group({ signatory: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(200)]] });
  readonly selected = signal(''); readonly receiptReviewed = signal(''); readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal(''); readonly failed = signal(false); readonly file = signal<File | null>(null);
  private pendingComment: { reviewId: string; requestId: string; body: string } | null = null;
  readonly commentPending = signal(false);
  readonly commentUncertain = signal(false);
  bundleDownloadLabel(bundle: ReturnType<typeof sharedDocuments>['bundles'][number]): string {
    const context = bundle.context;
    return `Download five-part final bundle for client ${context.clientLegalName || 'not recorded'}; service ${context.serviceRoute || 'not recorded'}; period start ${context.periodStart || 'not recorded'}; period end ${context.periodEnd || 'not recorded'}; current bundle ${bundle.id}; assembled ${this.utcTime(bundle.assembledAt)}`;
  }
  scanDownloadLabel(scan: SignedScan, context: PortalDocumentContext): string {
    return `Download Management-signed PDF scan for client ${context.clientLegalName || 'not recorded'}; management signatory ${scan.managementSignatory}; source letter version ${scan.version}; uploaded ${this.utcTime(scan.uploadedAt)}; scan ${scan.id}`;
  }
  /** This editor visit ended (the portal shell closed or the page left); late command callbacks must not touch it. */
  private destroyed = false;
  constructor() { inject(DestroyRef).onDestroy(() => { this.destroyed = true; }); effect(() => {
    this.session.invalidation();
    const saved = this.ws.data();
    if (!saved) { this.selected.set(''); this.receiptReviewed.set(''); this.review.reset(); this.signed.reset(); this.file.set(null);
      this.pendingComment = null; this.commentPending.set(false); this.commentUncertain.set(false); }
    else if (this.uncertain()) {
      if (this.commentUncertain()) this.message.set('The comment outcome is still unknown. Check its exact request receipt before retrying.');
      else { this.uncertain.set(false); this.message.set('Saved portal state was reloaded. Review it before trying another action.'); }
    }
  }); }
  select(id: string): void { this.selected.set(id); this.review.reset(); this.signed.reset(); this.file.set(null); }
  choose(e: Event): void { const file = (e.target as HTMLInputElement).files?.[0]; this.file.set(file && file.size > 0 && file.size <= 10 * 1024 * 1024 ? file : null);
    if (!this.file()) { this.failed.set(true); this.message.set('Select a non-empty PDF of at most 10 MB.'); } }
  comment(id: string): Promise<void> { const submitted = this.review.controls.body.value;
    if (!submitted.trim() || this.review.controls.body.invalid) return Promise.resolve();
    if (!this.pendingComment || this.pendingComment.reviewId !== id || this.pendingComment.body !== submitted)
      { this.pendingComment = { reviewId: id, requestId: crypto.randomUUID(), body: submitted }; this.commentPending.set(true); this.commentUncertain.set(false); }
    const pending = this.pendingComment;
    return this.run(`/api/ui/portal/documents/${id}/comment`, { requestId: pending.requestId, body: pending.body }, 'Comment sent to the audit team.', () => {
      this.pendingComment = null; this.commentPending.set(false); this.commentUncertain.set(false); this.uncertain.set(false);
      if (this.selected() === id && this.review.controls.body.value === submitted) {
        this.review.controls.body.setValue('');
        this.review.controls.body.markAsPristine();
      }
    }); }
  async recoverComment(): Promise<void> {
    const pending = this.pendingComment; if (!pending || this.busy()) return;
    const generation = this.session.invalidation(); this.busy.set(true);
    try {
      const receipt = await this.api.get(`/api/ui/portal/documents/${pending.reviewId}/comment-receipts/${pending.requestId}`,
        obj({ succeeded: bool, value: nullable(guid) }));
      if (this.destroyed || generation !== this.session.invalidation()) return;
      if (receipt.succeeded && receipt.value) {
        this.pendingComment = null; this.commentPending.set(false); this.commentUncertain.set(false); this.uncertain.set(false); this.failed.set(false);
        this.message.set('The exact comment request is saved.');
        if (this.review.controls.body.value === pending.body) { this.review.controls.body.setValue(''); this.review.controls.body.markAsPristine(); }
        this.ws.reload();
      } else this.message.set('No exact receipt is visible yet. Retry only with the original comment text and request identity.');
    } catch { if (!this.destroyed && generation === this.session.invalidation()) this.message.set('The exact receipt could not be checked. Keep the original request unchanged and check again.'); }
    finally { this.busy.set(false); }
  }
  acknowledge(id: string, sha: string): void { if (!this.review.controls.reviewed.value) return;
    void this.run(`/api/ui/portal/documents/${id}/acknowledge`, { sha256: sha, reviewed: true }, 'Acknowledged. The exact version reviewed is recorded.'); }
  upload(id: string, hash: string): void { const file = this.file(); if (!file || this.signed.invalid) return;
    const form = new FormData(); form.append('file', file); form.append('expectedHash', hash); form.append('signatory', this.signed.controls.signatory.value);
    void this.run(`/api/ui/portal/documents/${id}/signed-representation`, form, 'Signed PDF uploaded. Engagement Partner verification is pending.'); }
  reviewReceipt(id: string, event: Event): void { this.receiptReviewed.set((event.target as HTMLInputElement).checked ? id : ''); }
  async acknowledgeBundle(bundle: { id: string; sha256: string; manifestSha256: string }): Promise<void> {
    if (this.receiptReviewed() !== bundle.id) return;
    await this.run(`/api/ui/portal/bundles/${bundle.id}/acknowledge`,
      { contentSha256: bundle.sha256, manifestSha256: bundle.manifestSha256, received: true },
      'Your receipt report for this exact bundle was recorded. It does not confirm provider delivery or prove a browser download.');
  }
  private async run(url: string, body: unknown, success: string, afterSuccess?: () => void): Promise<void> {
    if (this.busy() || this.uncertain() || this.destroyed) return; const generation = this.session.invalidation(); this.busy.set(true);
    try { const r = await this.api.command(url, body); if (this.destroyed || generation !== this.session.invalidation()) return;
      this.failed.set(!r.ok); this.message.set(r.ok ? success : r.message);
      if (!r.ok && r.unknown) {
        this.uncertain.set(true);
        if (url.includes('/portal/documents/') && url.endsWith('/comment')) this.commentUncertain.set(true);
      }
      if (r.ok) afterSuccess?.();
      if (r.ok || (!r.ok && r.unknown && !this.commentUncertain())) this.ws.reload();
    } finally { this.busy.set(false); }
  }
}
