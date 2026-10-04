import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { decode, guid, nat, obj, sha256, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { portalRequest } from './contracts';

const startReceipt = obj({ uploadIntentId: guid, pbcRequestId: guid, receivedByteCount: nat, capability: sha256 });
const resumeReceipt = obj({ uploadIntentId: guid, pbcRequestId: guid, state: text, receivedByteCount: nat,
  nextChunkIndex: nat, capability: sha256 });
type PortalUploadRow = { id: string; fileName: string; receivedByteCount: string; declaredByteCount: string; state: string; sha256: string };
const MAX_FILE = 250 * 1024 * 1024;
const CHUNK = 8 * 1024 * 1024;
const fingerprint = async (bytes: ArrayBuffer): Promise<string> =>
  Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('');

@Component({ selector: 'audit-client-request', imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatProgressBarModule, ...SHARED],
  template: `
    <nav aria-label="Request location"><a routerLink="/portal">Client portal</a> / <span>Request</span></nav>
    <audit-page-header title="PBC request" description="Files and conversation with your audit team. Uploading bytes does not mark the request received or accepted." />
    <button matButton (click)="refresh()" [disabled]="busy() || ws.loading()">Refresh request</button>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="your assigned request" />
    <audit-command-message [message]="message()" [failed]="failed()" />
    @if (ws.data(); as w) {
      <section class="panel"><h2>{{ w.area }}</h2><p>{{ w.objective }}</p><p>{{ w.instructions }}</p>
        <p>Requested format: {{ w.requestedFormat }} · due {{ w.dueDate }} · revision {{ w.revision }}</p><audit-status [value]="w.state" /></section>
      <section class="panel" aria-labelledby="upload-heading"><h2 id="upload-heading">Upload requested files</h2>
        @if (!w.firstSignIn.completed) { <p role="status">Uploads open after your first portal sign-in is complete.</p><a routerLink="/portal">Complete first sign-in</a> }
        @else if (!w.canWrite) { <p role="status">Uploads and replies are closed for this request. Released documents remain available.</p> }
        <label for="portal-file">Choose a file (up to 250 MB)</label>
        <input id="portal-file" type="file" (change)="choose($event)" [disabled]="busy() || !w.canWrite || !w.firstSignIn.completed" />
        @if (file(); as f) { <p>{{ f.name }} · {{ f.size }} bytes</p><p class="fingerprint">SHA-256 {{ hash() || 'Calculating fingerprint…' }}</p> }
        <button matButton="filled" (click)="upload()" [disabled]="busy() || !hash() || !w.canWrite || !w.firstSignIn.completed || uncertain() || uploadUncertain()">{{ busy() ? 'Working…' : 'Upload file' }}</button>
        @if (sent() > 0) { <mat-progress-bar [value]="progress()" aria-label="Upload progress" /><p role="status">{{ sent() }} of {{ file()?.size ?? sent() }} bytes staged. Trusted staff completion is still required.</p> }
      </section>
      @if (w.canDelegate) {
        <section class="panel" aria-labelledby="delegate-heading"><h2 id="delegate-heading">Delegate this request</h2>
          <p>Only colleagues with current access to this engagement are eligible. Delegation permits uploads and replies for this request.</p>
          @for (d of w.delegations; track d.id) { <p>{{ d.delegateName }} · {{ d.createdAt }}
            <button matButton (click)="revoke(d.id)" [disabled]="busy() || uncertain()" [attr.aria-label]="'Revoke delegation for ' + d.delegateName">Revoke</button></p> }
          <form [formGroup]="delegation" (ngSubmit)="delegate()"><label for="portal-delegate">Colleague</label>
            <select id="portal-delegate" formControlName="userId"><option value="">Select a colleague</option>
              @for (c of w.candidates; track c.userId) { <option [value]="c.userId">{{ c.name }}</option> }</select>
            <button matButton="filled" [disabled]="delegation.invalid || busy() || uncertain()">Delegate</button>
          </form>
          @if (!w.candidates.length) { <p>No other eligible client users are available.</p> }
        </section>
      }
      <section class="panel" aria-labelledby="conversation-heading"><h2 id="conversation-heading">Conversation timeline</h2>
        <ol class="conversation">@for (m of timeline(); track m.id) { <li><strong>{{ m.speaker }}</strong> <time [attr.datetime]="m.at">{{ m.at }}</time><p>{{ m.body }}</p></li> }
          @empty { <li>No messages or files yet.</li> }
        </ol>
        @if (w.canWrite) { <form [formGroup]="reply" (ngSubmit)="sendReply()">
          <label for="portal-reply">Reply to the audit team</label><textarea id="portal-reply" formControlName="body" maxlength="4000"></textarea>
          @if (reply.controls.body.touched && reply.invalid) { <p role="alert">Enter a reply of up to 4000 characters.</p> }
          <button matButton="filled" [disabled]="reply.invalid || busy() || uncertain()">Send reply</button>
        </form> }
      </section>
      <section class="panel"><h2>Transfer receipts</h2>
        @for (u of w.uploads; track u.id) { <article class="receipt"><h3>{{ u.fileName }}</h3><audit-status [value]="u.state" />
          <p>{{ u.receivedByteCount }} / {{ u.declaredByteCount }} bytes · provider {{ u.transferState ?? 'Not dispatched' }}</p><p class="fingerprint">SHA-256 {{ u.sha256 }}</p>
          @if (w.canWrite && w.firstSignIn.completed && canResume(u)) {
            <p>To continue safely, choose the exact same file again. Its name, size, and SHA-256 must match this receipt.</p>
            <button matButton (click)="resumeUpload(u)" [disabled]="busy() || !matchesUpload(u)">Resume staged upload</button>
          }
        </article> } @empty { <p>No transfer intents are recorded.</p> }
      </section>
    }
  `, styles: `input[type=file], button, select { min-height: 44px; } textarea { width: 100%; min-height: 7rem; } .fingerprint { overflow-wrap: anywhere; } .conversation { padding-left: 1.25rem; } time { display: block; font-size: .875rem; } .receipt { padding-block: 1rem; border-bottom: 1px solid var(--mat-sys-outline-variant); }`
})
export class ClientPbcRequest {
  private readonly api = inject(Api); private readonly session = inject(SessionService);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => this.id() ? `/api/ui/portal/requests/${this.id()}` : null, portalRequest, 'This request is not available in your current access scope.');
  private readonly fb = inject(FormBuilder);
  readonly reply = this.fb.nonNullable.group({ body: ['', [Validators.required, Validators.maxLength(4000)]] });
  readonly delegation = this.fb.nonNullable.group({ userId: ['', Validators.required] });
  readonly file = signal<File | null>(null); readonly hash = signal(''); readonly sent = signal(0);
  readonly busy = signal(false); readonly uncertain = signal(false); readonly uploadUncertain = signal(false);
  readonly failed = signal(false); readonly message = signal('');
  private abort = new AbortController(); private selection = 0;
  constructor() {
    effect(() => { this.id(); this.session.invalidation(); if (!this.session.current() || !this.ws.data()) {
      this.abort.abort(); this.abort = new AbortController(); this.selection++; this.file.set(null); this.hash.set('');
      this.reply.reset(); this.delegation.reset(); this.sent.set(0);
      if (!this.session.current() || !this.id()) { this.uploadUncertain.set(false); this.uncertain.set(false); }
    } });
    inject(DestroyRef).onDestroy(() => this.abort.abort());
  }
  timeline(): { id: string; at: string; speaker: string; body: string }[] {
    const w = this.ws.data(); if (!w) return [];
    return [...w.conversation, ...w.uploads.map(u => ({ id: u.id, at: u.createdAt, speaker: 'Client uploaded ' + u.fileName,
      body: `${u.receivedByteCount} of ${u.declaredByteCount} bytes · ${u.state}` }))].sort((a, b) => a.at.localeCompare(b.at) || a.id.localeCompare(b.id));
  }
  progress(): number { return this.file()?.size ? this.sent() * 100 / this.file()!.size : 0; }
  canResume(upload: PortalUploadRow): boolean {
    return (upload.state === 'STARTED' || upload.state === 'CHUNKING') && BigInt(upload.receivedByteCount) < BigInt(upload.declaredByteCount);
  }
  matchesUpload(upload: PortalUploadRow): boolean {
    const file = this.file(), hash = this.hash();
    return !!file && !!hash && file.name === upload.fileName && String(file.size) === upload.declaredByteCount &&
      hash.toLowerCase() === upload.sha256.toLowerCase();
  }
  refresh(): void { if (this.busy()) return; this.ws.reload(); /* unknown mutations remain fenced; verify with the audit team before repeating */ }
  async choose(event: Event): Promise<void> {
    const selected = (event.target as HTMLInputElement).files?.[0] ?? null; const sequence = ++this.selection;
    this.hash.set(''); this.file.set(null); this.sent.set(0); this.failed.set(false); this.message.set('');
    if (!selected) return;
    if (!selected.size || selected.size > MAX_FILE) { this.failed.set(true); this.message.set('Select a non-empty file of up to 250 MB.'); return; }
    this.file.set(selected);
    try { const sha = await fingerprint(await selected.arrayBuffer()); if (sequence === this.selection) this.hash.set(sha); }
    catch { this.failed.set(true); this.message.set('The fingerprint could not be calculated. Select the file again.'); }
  }
  private async command(path: string, body: unknown, success: string): Promise<void> {
    if (this.busy() || this.uncertain()) return;
    const generation = this.session.invalidation(); this.busy.set(true); this.message.set('');
    try {
      const r = await this.api.command(path, body);
      if (generation !== this.session.invalidation()) return;
      this.failed.set(!r.ok); this.message.set(r.ok ? success : r.message);
      if (!r.ok && r.unknown) this.uncertain.set(true);
      if (r.ok) { this.reply.reset(); this.delegation.reset(); }
      this.ws.reload();
    } finally { this.busy.set(false); }
  }
  sendReply(): void { if (this.reply.invalid || !this.reply.controls.body.value.trim()) return;
    void this.command(`/api/ui/portal/requests/${this.id()}/reply`, this.reply.getRawValue(), 'Reply added to the request timeline.'); }
  delegate(): void { if (this.delegation.invalid) return;
    void this.command(`/api/ui/portal/requests/${this.id()}/delegations`, this.delegation.getRawValue(), 'Delegation recorded.'); }
  revoke(id: string): void { void this.command(`/api/ui/portal/requests/${this.id()}/delegations/${id}/revoke`, {}, 'Delegation revoked.'); }
  async upload(): Promise<void> {
    const file = this.file(), hash = this.hash(), requestId = this.id();
    if (!file || !hash || !requestId || this.busy() || this.uncertain() || !this.ws.data()?.canWrite) return;
    const generation = this.session.invalidation(), abort = this.abort; this.busy.set(true); this.message.set(''); this.sent.set(0);
    try {
      const started = await this.api.command(`/api/ui/portal/requests/${requestId}/uploads`, { fileName: file.name,
        contentType: file.type || 'application/octet-stream', byteCount: String(file.size), sha256: hash });
      if (generation !== this.session.invalidation() || abort.signal.aborted) return;
      if (!started.ok) { this.failed.set(true); this.message.set(started.message); if (started.unknown) this.uploadUncertain.set(true); return; }
      const intent = decode(startReceipt, started.value);
      if (intent.pbcRequestId !== requestId || intent.receivedByteCount !== 0) throw new Error('Unsupported upload receipt');
      // The capability remains only in this call; it is never stored in drafts, browser storage or URLs.
      this.uploadUncertain.set(false);
      await this.sendChunks(file, hash, requestId, intent.uploadIntentId, intent.capability, 0, 0, generation, abort);
    } catch { if (!abort.signal.aborted) { this.failed.set(true); this.uploadUncertain.set(true); this.message.set(UNKNOWN_OUTCOME); } }
    finally { this.busy.set(false); if (generation === this.session.invalidation()) this.ws.reload(); }
  }
  async resumeUpload(upload: PortalUploadRow): Promise<void> {
    const file = this.file(), hash = this.hash(), requestId = this.id(), current = this.ws.data();
    if (!file || !hash || !requestId || !current?.canWrite || !current.firstSignIn.completed || !this.canResume(upload) ||
        !this.matchesUpload(upload) || this.busy()) return;
    const generation = this.session.invalidation(), abort = this.abort;
    this.busy.set(true); this.failed.set(false); this.message.set('');
    try {
      const resumed = await this.api.command(`/api/ui/portal/requests/${requestId}/uploads/${upload.id}/resume`, {
        fileName: file.name, byteCount: String(file.size), sha256: hash });
      if (generation !== this.session.invalidation() || abort.signal.aborted) return;
      if (!resumed.ok) {
        this.failed.set(true); this.message.set(resumed.message);
        if (resumed.unknown) this.uploadUncertain.set(true);
        return;
      }
      const intent = decode(resumeReceipt, resumed.value);
      if (intent.uploadIntentId !== upload.id || intent.pbcRequestId !== requestId ||
          intent.receivedByteCount > file.size || intent.receivedByteCount === file.size) throw new Error('Unsupported upload resume receipt');
      this.uploadUncertain.set(false);
      await this.sendChunks(file, hash, requestId, intent.uploadIntentId, intent.capability,
        intent.receivedByteCount, intent.nextChunkIndex, generation, abort);
    } catch { if (!abort.signal.aborted) { this.failed.set(true); this.uploadUncertain.set(true); this.message.set(UNKNOWN_OUTCOME); } }
    finally { this.busy.set(false); if (generation === this.session.invalidation()) this.ws.reload(); }
  }
  private async sendChunks(file: File, hash: string, requestId: string, uploadId: string, capability: string,
    offset: number, nextChunkIndex: number, generation: number, abort: AbortController): Promise<void> {
    if (!/^[0-9a-f]{64}$/i.test(capability) || !Number.isSafeInteger(offset) || !Number.isSafeInteger(nextChunkIndex) ||
        offset < 0 || offset >= file.size || nextChunkIndex < 0) throw new Error('Unsupported upload capability or offset');
    this.sent.set(offset);
    for (let chunk = nextChunkIndex, position = offset; position < file.size; chunk++) {
      const bytes = await file.slice(position, Math.min(position + CHUNK, file.size)).arrayBuffer(); const sha = await fingerprint(bytes);
      if (generation !== this.session.invalidation() || abort.signal.aborted) return;
      const response = await fetch(`/api/pbc/uploads/${uploadId}/chunks/${chunk}`, { method: 'POST', credentials: 'same-origin', signal: abort.signal,
        headers: { 'Content-Type': 'application/octet-stream', 'X-Upload-Offset': String(position), 'X-Content-SHA256': sha, 'X-Pbc-Upload-Capability': capability }, body: bytes });
      if (!response.ok) {
        this.failed.set(true);
        this.uploadUncertain.set(response.status === 0 || response.status >= 500 || response.status === 409);
        this.message.set(response.status === 401 || response.status === 403
          ? 'Upload access changed. Refresh your session before continuing.'
          : 'Transfer stopped. Refresh the request and resume from the verified receipt; the request is not yet received.');
        return;
      }
      position += bytes.byteLength; this.sent.set(position);
    }
    this.failed.set(false);
    this.message.set('File bytes staged. Your audit team must verify trusted completion and suitability before the request is received or accepted.');
  }
}
