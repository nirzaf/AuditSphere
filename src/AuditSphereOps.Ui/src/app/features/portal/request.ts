import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { decode, guid, nat, obj, sha256, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { clientRequestStatusLabel, portalRequest, portalRequestHistory } from './contracts';

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
    <audit-state [loading]="ws.loading() || history.loading()" [error]="history.error() || ws.error()" label="your assigned request" />
    <audit-command-message [message]="message()" [failed]="failed()" />
    @if (authorizedRequest(); as w) {
      <section class="panel"><h2>{{ w.area }}</h2><p>{{ w.objective }}</p><p>{{ w.instructions }}</p>
        <p>Requested format: {{ w.requestedFormat }} · due {{ w.dueDate }} · revision {{ w.revision }}</p>
        <audit-status [value]="statusLabel(w.state)" />
        @if (w.clarificationReason) {
          <aside class="panel clarification-callout" role="alert">
            <strong>Auditor note / rejection reason:</strong>
            <p>{{ w.clarificationReason }}</p>
          </aside>
        }
      </section>
      <section class="panel" aria-labelledby="upload-heading"><h2 id="upload-heading">Upload requested files</h2>
        @if (!w.firstSignIn.completed) { <p role="status">Uploads open after your first portal sign-in is complete.</p><a routerLink="/portal">Complete first sign-in</a> }
        @else if (!w.canWrite) { <p role="status">Uploads and replies are closed for this request. Released documents remain available.</p> }
        <div class="drop-zone" [class.drop-zone-active]="dragOver()"
          [class.drop-zone-disabled]="busy() || !w.canWrite || !w.firstSignIn.completed"
          tabindex="0" role="button" aria-describedby="portal-drop-help" aria-controls="portal-file"
          [attr.aria-disabled]="busy() || !w.canWrite || !w.firstSignIn.completed"
          (click)="openFilePicker($event, fileInput)" (keydown)="activateFilePicker($event, fileInput)"
          (dragenter)="dragEnter($event)" (dragover)="dragEnter($event)" (dragleave)="dragLeave()" (drop)="drop($event)">
          <strong>Drop a file here</strong> <span class="muted">or</span>
          <span class="drop-zone-choose">choose a file</span>
          <span id="portal-drop-help">One file at a time, up to 250 MB.</span>
        </div>
        <label class="sr-only" for="portal-file">Choose a file (up to 250 MB)</label>
        <input #fileInput id="portal-file" class="sr-only" type="file" (change)="choose($event)"
          [disabled]="busy() || !w.canWrite || !w.firstSignIn.completed" />
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
        @if (requestHistory(); as h) {
          <ol class="conversation">@for (m of h.conversation; track m.id) { <li><strong>{{ m.speaker }}</strong> <time [attr.datetime]="m.at">{{ m.at }}</time><p>{{ m.body }}</p></li> }
            @empty { <li>{{ history.loading() ? 'Loading conversation…' : 'No messages on this page.' }}</li> }
          </ol>
          <nav class="history-pages" aria-label="Conversation pages">
            <button matButton (click)="previousConversationPage()" [disabled]="conversationPage() === 0 || history.loading()">Previous messages</button>
            <span aria-live="polite">Page {{ conversationPage() + 1 }} · {{ h.conversation.length }} shown</span>
            <button matButton (click)="nextConversationPage(h.hasMoreConversation)" [disabled]="!h.hasMoreConversation || history.loading()">Older messages</button>
          </nav>
        }
        @if (w.canWrite) { <form [formGroup]="reply" (ngSubmit)="sendReply()">
          @let replyInvalid = reply.controls.body.touched && reply.controls.body.invalid;
          <label for="portal-reply">Reply to the audit team</label><textarea id="portal-reply" formControlName="body" maxlength="4000"
            [attr.aria-invalid]="replyInvalid ? 'true' : null" [attr.aria-describedby]="replyInvalid ? 'portal-reply-error' : null"></textarea>
          @if (replyInvalid) { <p id="portal-reply-error" role="alert">Enter a reply of up to 4000 characters.</p> }
          <button matButton="filled" [disabled]="reply.invalid || busy() || uncertain()">Send reply</button>
        </form> }
      </section>
      @if (requestHistory(); as h) {
      <section class="panel"><h2>Transfer receipts</h2>
        @for (u of h.uploads; track u.id) { <article class="receipt"><h3>{{ u.fileName }}</h3><audit-status [value]="u.state" />
          <p>{{ u.receivedByteCount }} / {{ u.declaredByteCount }} bytes · provider {{ u.transferState ?? 'Not dispatched' }}</p><p class="fingerprint">SHA-256 {{ u.sha256 }}</p>
          @if (w.canWrite && w.firstSignIn.completed && canResume(u)) {
            <p>To continue safely, choose the exact same file again. Its name, size, and SHA-256 must match this receipt.</p>
            <button matButton (click)="resumeUpload(u)" [disabled]="busy() || !matchesUpload(u)">Resume staged upload</button>
          }
        </article> } @empty { <p>{{ history.loading() ? 'Loading transfer receipts…' : 'No transfer intents on this page.' }}</p> }
        <nav class="history-pages" aria-label="Transfer receipt pages">
          <button matButton (click)="previousUploadPage()" [disabled]="uploadPage() === 0 || history.loading()">Previous uploads</button>
          <span aria-live="polite">Page {{ uploadPage() + 1 }} · {{ h.uploads.length }} shown</span>
          <button matButton (click)="nextUploadPage(h.hasMoreUploads)" [disabled]="!h.hasMoreUploads || history.loading()">Older uploads</button>
        </nav>
      </section>
      }
    }
  `, styles: `input[type=file], button, select { min-height: 44px; } textarea { width: 100%; min-height: 7rem; } .fingerprint { overflow-wrap: anywhere; } .conversation { padding-left: 1.25rem; } .history-pages { display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; margin-block: .75rem; } time { display: block; font-size: .875rem; } .receipt { padding-block: 1rem; border-bottom: 1px solid var(--mat-sys-outline-variant); } .drop-zone { border: 2px dashed var(--mat-sys-outline-variant); border-radius: 8px; padding: 1.25rem 1rem; text-align: center; margin-block: .75rem; cursor: pointer; display: grid; gap: .35rem; } .drop-zone:focus-visible, .drop-zone-active { border-color: var(--mat-sys-primary); background: color-mix(in srgb, var(--mat-sys-primary) 6%, transparent); outline: 2px solid var(--mat-sys-primary); outline-offset: 2px; } .drop-zone-choose { color: var(--mat-sys-primary); text-decoration: underline; } .drop-zone-disabled { cursor: not-allowed; opacity: .65; }`
})
export class ClientPbcRequest {
  private readonly api = inject(Api); private readonly session = inject(SessionService);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => this.id() ? `/api/ui/portal/requests/${this.id()}` : null, portalRequest, 'This request is not available in your current access scope.');
  readonly uploadPage = signal(0); readonly conversationPage = signal(0); readonly historyActive = signal(false);
  readonly history = this.api.resource(() => this.historyActive() && this.id()
    ? `/api/ui/portal/requests/${this.id()}/history?uploadPage=${this.uploadPage()}&conversationPage=${this.conversationPage()}&pageSize=25`
    : null, portalRequestHistory, 'This request history is unavailable in your current access scope.');
  private readonly fb = inject(FormBuilder);
  readonly reply = this.fb.nonNullable.group({ body: ['', [Validators.required, Validators.maxLength(4000), Validators.pattern(/\S/)]] });
  readonly delegation = this.fb.nonNullable.group({ userId: ['', Validators.required] });
  readonly file = signal<File | null>(null); readonly hash = signal(''); readonly sent = signal(0);
  readonly dragOver = signal(false);
  readonly busy = signal(false); readonly uncertain = signal(false); readonly uploadUncertain = signal(false);
  readonly failed = signal(false); readonly message = signal('');
  private abort = new AbortController(); private selection = 0;
  private lastRequestId: string | null = null;
  private lastDraftContext: { identity: string; authorization: string } | null = null;
  statusLabel(state: string): string { return clientRequestStatusLabel(state); }
  constructor() {
    effect(() => {
      const requestId = this.id();
      if (requestId !== this.lastRequestId) {
        this.lastRequestId = requestId;
        this.uploadPage.set(0); this.conversationPage.set(0); this.historyActive.set(false);
      }
    });
    effect(() => {
      const requestId = this.id();
      const generation = this.session.invalidation();
      const actor = this.session.current();
      const error = this.ws.error() || this.history.error();
      const workspace = this.ws.data();
      if (!requestId || !actor || error || workspace && workspace.id !== requestId) {
        this.clearDrafts();
        this.lastDraftContext = null;
        return;
      }

      const identity = `${generation}:${actor.userId}:${actor.firmId}:${actor.generation}:${requestId}`;
      // Api.resource intentionally drops decoded data during reload. Keep the last authorized context
      // across that pending interval, but only while route and session identity remain unchanged.
      if (!workspace) {
        if (this.lastDraftContext && this.lastDraftContext.identity !== identity) {
          this.clearDrafts();
          this.lastDraftContext = null;
        }
        return;
      }

      const authorization = `${workspace.canWrite}:${workspace.canDelegate}:${workspace.firstSignIn.completed}`;
      if (this.lastDraftContext && (this.lastDraftContext.identity !== identity || this.lastDraftContext.authorization !== authorization)) {
        this.clearDrafts();
      }
      this.lastDraftContext = { identity, authorization };
    });
    const interval = setInterval(() => {
      if (!this.busy() && !this.ws.loading() && this.session.current()) {
        this.ws.reload();
      }
    }, 30_000);
    inject(DestroyRef).onDestroy(() => { this.abort.abort(); clearInterval(interval); });
  }
  private clearDrafts(): void {
    this.abort.abort(); this.abort = new AbortController(); this.selection++;
    this.file.set(null); this.hash.set(''); this.sent.set(0); this.dragOver.set(false);
    this.reply.reset(); this.delegation.reset();
    this.uploadUncertain.set(false); this.uncertain.set(false);
  }
  private currentWorkspace(): { id: string; canWrite: boolean; canDelegate: boolean; firstSignIn: { completed: boolean } } | null {
    const workspace = this.ws.data();
    return this.session.current() && !this.ws.error() && !this.history.error() && !this.ws.loading() && workspace?.id === this.id()
      ? workspace : null;
  }
  authorizedRequest() { return this.ws.error() || this.history.error() ? null : this.ws.data(); }
  requestHistory() {
    const workspace = this.ws.data(); if (!workspace || this.ws.error() || this.history.error()) return null;
    if (this.historyActive()) {
      const page = this.history.data();
      if (!page || page.requestId !== workspace.id || page.uploadPage !== this.uploadPage() || page.conversationPage !== this.conversationPage()) return null;
      return page;
    }
    return { requestId: workspace.id, uploadPage: 0, conversationPage: 0, pageSize: 25,
      uploads: workspace.uploads, hasMoreUploads: workspace.hasMoreUploads,
      conversation: workspace.conversation, hasMoreConversation: workspace.hasMoreConversation };
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
  refresh(): void {
    if (this.busy() || this.ws.loading() || this.history.loading()) return;
    this.ws.reload();
    if (this.historyActive()) this.history.reload();
    /* unknown mutations remain fenced; verify with the audit team before repeating */
  }
  previousUploadPage(): void { this.historyActive.set(true); this.uploadPage.update(p => Math.max(0, p - 1)); }
  nextUploadPage(hasMore: boolean): void { if (hasMore) { this.historyActive.set(true); this.uploadPage.update(p => Math.min(10000, p + 1)); } }
  previousConversationPage(): void { this.historyActive.set(true); this.conversationPage.update(p => Math.max(0, p - 1)); }
  nextConversationPage(hasMore: boolean): void { if (hasMore) { this.historyActive.set(true); this.conversationPage.update(p => Math.min(10000, p + 1)); } }
  async choose(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const selected = input.files?.[0] ?? null;
    input.value = '';
    await this.selectFile(selected);
  }
  openFilePicker(event: MouseEvent, input: HTMLInputElement): void {
    event.preventDefault();
    if (this.canChooseFile()) input.click();
  }
  activateFilePicker(event: KeyboardEvent, input: HTMLInputElement): void {
    if (event.key !== 'Enter' && event.key !== ' ') return;
    event.preventDefault();
    if (this.canChooseFile()) input.click();
  }
  dragEnter(event: DragEvent): void {
    event.preventDefault();
    if (this.canChooseFile()) this.dragOver.set(true);
  }
  dragLeave(): void { this.dragOver.set(false); }
  drop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    if (!this.canChooseFile()) return;
    const files = event.dataTransfer?.files;
    if (!files?.length) return;
    if (files.length > 1) {
      this.failed.set(true);
      this.message.set('Drop one file at a time.');
      return;
    }
    void this.selectFile(files[0]);
  }
  private canChooseFile(): boolean {
    const request = this.ws.data();
    return !this.busy() && !!request?.canWrite && request.firstSignIn.completed;
  }
  private async selectFile(selected: File | null): Promise<void> {
    const sequence = ++this.selection;
    this.hash.set(''); this.file.set(null); this.sent.set(0); this.failed.set(false); this.message.set('');
    if (!selected) return;
    if (!selected.size || selected.size > MAX_FILE) { this.failed.set(true); this.message.set('Select a non-empty file of up to 250 MB.'); return; }
    this.file.set(selected);
    try { const sha = await fingerprint(await selected.arrayBuffer()); if (sequence === this.selection) this.hash.set(sha); }
    catch { if (sequence === this.selection) { this.failed.set(true); this.message.set('The fingerprint could not be calculated. Select the file again.'); } }
  }
  private async command(path: string, body: unknown, success: string): Promise<void> {
    if (this.busy() || this.uncertain() || !this.currentWorkspace()) return;
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
    if (!this.currentWorkspace()?.canWrite) return;
    void this.command(`/api/ui/portal/requests/${this.id()}/reply`, this.reply.getRawValue(), 'Reply added to the request timeline.'); }
  delegate(): void { if (this.delegation.invalid || !this.currentWorkspace()?.canDelegate) return;
    void this.command(`/api/ui/portal/requests/${this.id()}/delegations`, this.delegation.getRawValue(), 'Delegation recorded.'); }
  revoke(id: string): void { if (!this.currentWorkspace()?.canDelegate) return;
    void this.command(`/api/ui/portal/requests/${this.id()}/delegations/${id}/revoke`, {}, 'Delegation revoked.'); }
  async upload(): Promise<void> {
    const file = this.file(), hash = this.hash(), requestId = this.id();
    if (!file || !hash || !requestId || this.busy() || this.uncertain() || !this.currentWorkspace()?.canWrite) return;
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
    const file = this.file(), hash = this.hash(), requestId = this.id(), current = this.currentWorkspace();
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
