import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, queryParam, routeGuid } from '../../core/api';
import { arr, bool, date, guid, nat, nullable, obj, str, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

export const decodeCatalogue = arr(obj({ id: guid, code: text, title: text, category: text, audience: text, publishedVersion: nullable(nat), hasDraft: bool }), 2000);
const version = obj({ id: guid, version: nat, status: text, effectiveFrom: date, sourceReference: text, contentSha256: text, body: nullable(str(200000)) });
export const decodeEntry = obj({ id: guid, code: text, title: text, category: text, version, history: arr(version, 500) });
export const decodeHits = arr(obj({ documentId: guid, code: text, title: text, category: text, version: nat, effectiveFrom: date, snippet: text }), 50);

@Component({
  selector: 'audit-technical-library',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <audit-page-header title="Technical library" eyebrow="Audit & assurance"
      description="IFRS, ISA and firm guidance as versioned entries. Search covers published versions you are allowed to read; every result names its version." />
    <form class="inline-form" role="search" (submit)="$event.preventDefault(); search()">
      <label>Search <input name="term" [(ngModel)]="term" maxlength="100" /></label>
      <button matButton="outlined" type="submit">Search library</button>
    </form>
    @if (searchError()) { <p role="alert" class="error-text">{{ searchError() }}</p> }
    @if (hits(); as list) {
      <ul aria-label="Library results">
        @for (h of list; track h.documentId) {
          <li><a [routerLink]="['/app/library', h.documentId]">{{ h.code }} — {{ h.title }}</a> <small>{{ h.category }} · v{{ h.version }} · effective {{ h.effectiveFrom }}</small><span>{{ h.snippet }}</span></li>
        } @empty { <li>No published entries match.</li> }
      </ul>
    }
    @if (id()) {
      <audit-state [loading]="entry.loading()" [error]="entry.error()" label="library entry" />
      @if (entry.data(); as e) {
        <section class="panel" aria-labelledby="entry-heading">
          <h2 id="entry-heading">{{ e.code }} — {{ e.title }}</h2>
          <p><small>{{ e.category }} · version {{ e.version.version }} ({{ e.version.status.toLowerCase() }}) · effective {{ e.version.effectiveFrom }} · source: {{ e.version.sourceReference }} · SHA-256 <code>{{ e.version.contentSha256.slice(0, 12) }}</code></small></p>
          <div class="library-body" style="white-space: pre-wrap">{{ e.version.body }}</div>
          <p>Versions: @for (v of e.history; track v.id) { <a [routerLink]="['/app/library', e.id]" [queryParams]="{ v: v.version }">v{{ v.version }} {{ v.status.toLowerCase() }}</a>&nbsp; }</p>
          @if (draftOf(e); as d) {
            <button matButton="filled" (click)="publish(d.id)" [disabled]="commandsBlocked()">Publish v{{ d.version }} (second approver)</button>
          }
          <details>
            <summary>Prepare a new version</summary>
            <form class="inline-form" (submit)="$event.preventDefault(); draft(e.id)">
              <label>Content <textarea name="body" [(ngModel)]="next.body" rows="6" maxlength="200000" required></textarea></label>
              <label>Source reference <input name="source" [(ngModel)]="next.source" maxlength="300" required /></label>
              <label>Effective from <input type="date" name="effective" [(ngModel)]="next.effective" required /></label>
              <button matButton="outlined" type="submit" [disabled]="commandsBlocked()">Save draft version</button>
            </form>
          </details>
        </section>
      }
    }
    <section class="panel" aria-labelledby="catalogue-heading">
      <h2 id="catalogue-heading">Catalogue</h2>
      <audit-state [loading]="catalogue.loading()" [error]="catalogue.error()" label="library catalogue" />
      <ul aria-label="Library catalogue">
        @for (item of catalogue.data() ?? []; track item.id) {
          <li><a [routerLink]="['/app/library', item.id]">{{ item.code }} — {{ item.title }}</a>
            <small>{{ item.category }} · {{ item.publishedVersion === null ? 'not yet published' : 'v' + item.publishedVersion }}{{ item.hasDraft ? ' · draft pending' : '' }}</small></li>
        }
      </ul>
      <details>
        <summary>Add an entry (Manager, Partner or administrator)</summary>
        <form class="inline-form" (submit)="$event.preventDefault(); create()">
          <label>Code <input name="code" [(ngModel)]="fresh.code" maxlength="40" required /></label>
          <label>Title <input name="title" [(ngModel)]="fresh.title" maxlength="200" required /></label>
          <label>Category <select name="category" [(ngModel)]="fresh.category"><option value="IFRS">IFRS</option><option value="ISA">ISA</option><option value="FIRM_GUIDANCE">Firm guidance</option></select></label>
          <label>Audience <select name="audience" [(ngModel)]="fresh.audience"><option value="ALL_STAFF">All staff</option><option value="PARTNERS_MANAGERS">Partners and managers</option></select></label>
          <label>Content <textarea name="body" [(ngModel)]="fresh.body" rows="5" maxlength="200000" required></textarea></label>
          <label>Authoritative source <input name="source" [(ngModel)]="fresh.source" maxlength="300" required /></label>
          <button matButton="filled" type="submit" [disabled]="commandsBlocked()">Create draft entry</button>
        </form>
      </details>
    </section>
    @if (cmd.uncertain()) {
      <section class="panel" aria-label="Unconfirmed library action">
        <h2>Library action outcome unconfirmed</h2>
        <p>{{ unresolvedAction() }}. Current content is not an original command receipt. Refreshing cannot prove whether this request committed; the request may still commit later. Do not repeat it blindly. No command will be retried automatically.</p>
        <button matButton="outlined" type="button" (click)="reconcile()" [disabled]="cmd.busy() || reconciling()">Refresh persisted library state</button>
        @if (reconciliationLoaded()) {
          <p>Current authorized state was refreshed. Inspect the catalogue and version history before continuing. The original outcome remains unconfirmed.</p>
          <button matButton="outlined" type="button" (click)="acknowledgeUnconfirmed()">Discard local command fields and acknowledge uncertainty</button>
        }
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class TechnicalLibrary {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private lifetime = 0;
  private searchRequest = 0;
  private destroyed = false;
  private observedActor: object | null = null;
  private observedEpoch = -1;
  readonly id = routeGuid();
  private readonly v = queryParam('v');
  readonly catalogue = this.api.resource(() => '/api/ui/library', decodeCatalogue);
  readonly entry = this.api.resource(() => {
    const id = this.id(); const v = this.v();
    return id ? `/api/ui/library/${id}` + (v && /^\d{1,10}$/.test(v) ? `?v=${v}` : '') : null;
  }, (raw, path) => this.checkedEntry(raw, path), 'This library entry is not available to you.');
  readonly cmd = new CommandState(this.api);
  readonly commandsBlocked = () => this.destroyed || !this.session.current()?.staff || this.cmd.busy() || this.cmd.uncertain();
  readonly reconciling = signal(false);
  readonly reconciliationLoaded = signal(false);
  readonly unresolvedAction = signal('An earlier library action');
  readonly hits = signal<ReturnType<typeof decodeHits> | null>(null);
  readonly searchError = signal('');
  term = '';
  next = { body: '', source: '', effective: new Date().toISOString().slice(0, 10) };
  fresh = { code: '', title: '', category: 'FIRM_GUIDANCE', audience: 'ALL_STAFF', body: '', source: '' };

  constructor() {
    const params = inject(ActivatedRoute).paramMap.subscribe(() => this.invalidate());
    const queries = inject(ActivatedRoute).queryParamMap.subscribe(() => this.invalidate());
    effect(() => {
      const actor = this.session.current(), epoch = this.session.invalidation();
      untracked(() => {
        if (actor !== this.observedActor || epoch !== this.observedEpoch) {
          this.observedActor = actor; this.observedEpoch = epoch; this.invalidate();
          this.term = ''; this.clearFields(); this.cmd.busy.set(false); this.cmd.uncertain.set(false);
          this.cmd.message.set(''); this.cmd.failed.set(false); this.unresolvedAction.set('An earlier library action');
        }
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.invalidate(); params.unsubscribe(); queries.unsubscribe(); });
  }
  private invalidate(): void {
    ++this.lifetime; ++this.searchRequest; this.cmd.busy.set(false); this.hits.set(null); this.searchError.set('');
    this.reconciling.set(false); this.reconciliationLoaded.set(false);
  }
  private ticket(): () => boolean {
    const lifetime = this.lifetime, actor = this.session.current(), epoch = this.session.invalidation(), id = this.id(), v = this.v();
    return () => !this.destroyed && lifetime === this.lifetime && actor === this.session.current() && epoch === this.session.invalidation() && id === this.id() && v === this.v();
  }
  private checkedEntry(raw: unknown, path: string) {
    const e = decodeEntry(raw, path), requested = this.v();
    if (e.id !== this.id() || requested !== null && (!/^\d{1,10}$/.test(requested) || e.version.version !== Number(requested)))
      throw new Error('Library document or version identity mismatch');
    return e;
  }
  private clearFields(): void {
    this.next = { body: '', source: '', effective: new Date().toISOString().slice(0, 10) };
    this.fresh = { code: '', title: '', category: 'FIRM_GUIDANCE', audience: 'ALL_STAFF', body: '', source: '' };
  }
  draftOf(e: ReturnType<typeof decodeEntry>) { return e.history.find((x) => x.status === 'DRAFT') ?? null; }
  async search(): Promise<void> {
    const request = ++this.searchRequest, valid = this.ticket();
    this.hits.set(null); this.searchError.set('');
    if (!this.session.current()?.staff || this.destroyed) return;
    try {
      const hits = await this.api.get(`/api/ui/library/search?term=${encodeURIComponent(this.term.trim())}`, decodeHits);
      if (valid() && request === this.searchRequest) this.hits.set(hits);
    } catch (e) { if (valid() && request === this.searchRequest) { this.hits.set(null); this.searchError.set((e as Error).message); } }
  }
  private refresh = () => { this.catalogue.reload(); this.entry.reload(); };
  async reconcile(): Promise<void> {
    if (!this.cmd.uncertain() || this.cmd.busy() || this.reconciling() || !this.session.current()?.staff) return;
    const valid = this.ticket(); this.reconciling.set(true); this.reconciliationLoaded.set(false);
    // Clear old protected snapshots before re-reading; these reads never resend the command.
    this.refresh();
    try {
      await this.api.get('/api/ui/library', decodeCatalogue);
      const id = this.id(), v = this.v();
      if (!valid()) return;
      if (id) await this.api.get(`/api/ui/library/${id}` + (v && /^\d{1,10}$/.test(v) ? `?v=${v}` : ''), (raw, path) => this.checkedEntry(raw, path));
      if (valid()) { this.reconciliationLoaded.set(true); this.cmd.message.set('Current state read; original command outcome remains unconfirmed.'); }
    } catch { if (valid()) { this.cmd.failed.set(true); this.cmd.message.set('Current library state could not be read. Keep this action unresolved and retry the read.'); } }
    finally { if (valid()) this.reconciling.set(false); }
  }
  acknowledgeUnconfirmed(): void {
    if (!this.cmd.uncertain() || !this.reconciliationLoaded() || this.reconciling() || this.catalogue.loading() || this.catalogue.error() ||
      this.id() && (this.entry.loading() || this.entry.error())) return;
    this.clearFields(); this.cmd.uncertain.set(false); this.reconciliationLoaded.set(false);
    this.cmd.message.set('Local command fields discarded. The original outcome remains unconfirmed; prepare any new action explicitly.');
  }
  private async mutate(url: string, body: unknown, success: string, action: string, after?: () => void): Promise<void> {
    if (this.commandsBlocked()) return;
    const valid = this.ticket(); this.cmd.busy.set(true); this.cmd.failed.set(false); this.cmd.message.set('');
    // Retain the unresolved barrier if navigation invalidates the dispatched response.
    this.cmd.uncertain.set(true); this.unresolvedAction.set(action); this.reconciliationLoaded.set(false);
    try {
      const result = await this.api.command(url, body);
      if (!valid()) return;
      this.cmd.failed.set(!result.ok); this.cmd.message.set(result.ok ? success : result.message);
      if (result.ok) { this.cmd.uncertain.set(false); after?.(); }
      else if (!result.unknown) this.cmd.uncertain.set(false);
      this.refresh();
    } finally { if (valid()) this.cmd.busy.set(false); }
  }
  publish(versionId: string): void {
    if (!this.entry.data()?.history.some(v => v.id === versionId && v.status === 'DRAFT')) return;
    void this.mutate(`/api/ui/library/versions/${versionId}/publish`, {}, 'Version published; the previous version is superseded.', `Publish version ${versionId}`);
  }
  draft(id: string): void {
    if (id !== this.id() || this.entry.data()?.id !== id) return;
    const sent = { ...this.next };
    void this.mutate(`/api/ui/library/${id}/versions`, { body: sent.body, sourceReference: sent.source, effectiveFrom: sent.effective },
      'Draft version saved; a Partner or administrator must publish it.', `Save draft for document ${id}`, () => {
        if (JSON.stringify(this.next) === JSON.stringify(sent)) this.next = { ...sent, body: '', source: '' };
      });
  }
  create(): void {
    const f = { ...this.fresh };
    void this.mutate('/api/ui/library', { code: f.code, title: f.title, category: f.category, audience: f.audience, body: f.body, sourceReference: f.source },
      'Entry created as a draft; a Partner or administrator must publish it.', `Create entry ${f.code}`, () => {
        if (JSON.stringify(this.fresh) === JSON.stringify(f)) this.fresh = { ...f, code: '', title: '', body: '', source: '' };
      });
  }
}
