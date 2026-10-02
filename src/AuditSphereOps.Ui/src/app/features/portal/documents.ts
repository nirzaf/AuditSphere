import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, guid, instant, nat, nullable, obj, sha256, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { revision } from './contracts';
export const sharedDocuments = obj({ documents: arr(obj({ reviewId: guid, deliverableId: guid, kind: text, title: text, version: revision,
  sha256, acknowledgedAt: nullable(instant), comments: arr(obj({ body: text, fromClient: bool, resolution: nullable(text) }), 1000) }), 200),
  signedLetters: arr(obj({ id: guid, deliverableId: guid, version: nat, managementSignatory: text, contentSha256: sha256, uploadedAt: instant, verified: bool, current: bool }), 100),
  bundles: arr(obj({ id: guid, sha256, assembledAt: instant }), 100) });
@Component({ selector: 'audit-portal-documents', imports: [ReactiveFormsModule, MatButtonModule, ...SHARED], template: `
  <section class="panel"><h2>Documents for your review</h2><audit-state [loading]="ws.loading()" [error]="ws.error()" label="your shared documents" />
    <audit-command-message [message]="message()" [failed]="failed()" />
    @if (ws.data(); as w) {
      @for (d of w.documents; track d.reviewId) {
        <article class="document"><h3><a [href]="'/api/deliverables/' + d.deliverableId + '/download'">{{ d.title }} v{{ d.version }}</a></h3><p class="fingerprint">SHA-256 {{ d.sha256 }}</p>
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
          @for (s of w.signedLetters; track s.id) { @if (s.deliverableId === d.deliverableId) { <p><a [href]="'/api/representation-scans/' + s.id + '/download'">Management-signed PDF</a> <audit-status [value]="!s.current ? 'STALE' : s.verified ? 'VERIFIED' : 'PENDING_REVIEW'" /></p> } }
        </article>
      } @empty { <p>No reports or letters are shared with your current scope.</p> }
      @for (b of w.bundles; track b.id) { <p><a [href]="'/api/deliverable-bundles/' + b.id + '/download'">Download five-part final bundle</a><span class="fingerprint"> · SHA-256 {{ b.sha256 }}</span></p> }
    }
  </section>
`, styles: `.fingerprint { overflow-wrap: anywhere; } .document { padding-block: 1rem; border-bottom: 1px solid var(--mat-sys-outline-variant); } textarea { width: 100%; min-height: 6rem; }` })
export class PortalDocuments {
  private readonly api = inject(Api); private readonly session = inject(SessionService); private readonly fb = inject(FormBuilder);
  readonly ws = this.api.resource(() => '/api/ui/portal/documents', sharedDocuments);
  readonly review = this.fb.nonNullable.group({ body: ['', Validators.maxLength(4000)], reviewed: false });
  readonly signed = this.fb.nonNullable.group({ signatory: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(200)]] });
  readonly selected = signal(''); readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal(''); readonly failed = signal(false); readonly file = signal<File | null>(null);
  constructor() { effect(() => { this.session.invalidation(); if (!this.ws.data()) { this.selected.set(''); this.review.reset(); this.signed.reset(); this.file.set(null); } }); }
  select(id: string): void { this.selected.set(id); this.review.reset(); this.signed.reset(); this.file.set(null); }
  choose(e: Event): void { const file = (e.target as HTMLInputElement).files?.[0]; this.file.set(file && file.size > 0 && file.size <= 10 * 1024 * 1024 ? file : null);
    if (!this.file()) { this.failed.set(true); this.message.set('Select a non-empty PDF of at most 10 MB.'); } }
  comment(id: string): void { if (!this.review.controls.body.value.trim() || this.review.controls.body.invalid) return;
    void this.run(`/api/ui/portal/documents/${id}/comment`, { body: this.review.controls.body.value }, 'Comment sent to the audit team.'); }
  acknowledge(id: string, sha: string): void { if (!this.review.controls.reviewed.value) return;
    void this.run(`/api/ui/portal/documents/${id}/acknowledge`, { sha256: sha, reviewed: true }, 'Acknowledged. The exact version reviewed is recorded.'); }
  upload(id: string, hash: string): void { const file = this.file(); if (!file || this.signed.invalid) return;
    const form = new FormData(); form.append('file', file); form.append('expectedHash', hash); form.append('signatory', this.signed.controls.signatory.value);
    void this.run(`/api/ui/portal/documents/${id}/signed-representation`, form, 'Signed PDF uploaded. Engagement Partner verification is pending.'); }
  private async run(url: string, body: unknown, success: string): Promise<void> {
    if (this.busy() || this.uncertain()) return; const generation = this.session.invalidation(); this.busy.set(true);
    try { const r = await this.api.command(url, body); if (generation !== this.session.invalidation()) return;
      this.failed.set(!r.ok); this.message.set(r.ok ? success : r.message); if (!r.ok && r.unknown) this.uncertain.set(true);
      if (r.ok || (!r.ok && r.unknown)) this.ws.reload();
    } finally { this.busy.set(false); }
  }
}
