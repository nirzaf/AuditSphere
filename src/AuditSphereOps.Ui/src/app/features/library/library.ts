import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, queryParam, routeGuid } from '../../core/api';
import { arr, bool, date, guid, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeCatalogue = arr(obj({ id: guid, code: text, title: text, category: text, audience: text, publishedVersion: nullable(nat), hasDraft: bool }), 2000);
const version = obj({ id: guid, version: nat, status: text, effectiveFrom: date, sourceReference: text, contentSha256: text, body: nullable(text) });
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
            <button matButton="filled" (click)="publish(d.id)" [disabled]="cmd.busy()">Publish v{{ d.version }} (second approver)</button>
          }
          <details>
            <summary>Prepare a new version</summary>
            <form class="inline-form" (submit)="$event.preventDefault(); draft(e.id)">
              <label>Content <textarea name="body" [(ngModel)]="next.body" rows="6" maxlength="200000"></textarea></label>
              <label>Source reference <input name="source" [(ngModel)]="next.source" maxlength="300" /></label>
              <label>Effective from <input type="date" name="effective" [(ngModel)]="next.effective" /></label>
              <button matButton="outlined" type="submit" [disabled]="cmd.busy()">Save draft version</button>
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
          <label>Code <input name="code" [(ngModel)]="fresh.code" maxlength="40" /></label>
          <label>Title <input name="title" [(ngModel)]="fresh.title" maxlength="200" /></label>
          <label>Category <select name="category" [(ngModel)]="fresh.category"><option value="IFRS">IFRS</option><option value="ISA">ISA</option><option value="FIRM_GUIDANCE">Firm guidance</option></select></label>
          <label>Audience <select name="audience" [(ngModel)]="fresh.audience"><option value="ALL_STAFF">All staff</option><option value="PARTNERS_MANAGERS">Partners and managers</option></select></label>
          <label>Content <textarea name="body" [(ngModel)]="fresh.body" rows="5" maxlength="200000"></textarea></label>
          <label>Authoritative source <input name="source" [(ngModel)]="fresh.source" maxlength="300" /></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy()">Create draft entry</button>
        </form>
      </details>
    </section>
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class TechnicalLibrary {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  private readonly v = queryParam('v');
  readonly catalogue = this.api.resource(() => '/api/ui/library', decodeCatalogue);
  readonly entry = this.api.resource(() => {
    const id = this.id(); const v = this.v();
    return id ? `/api/ui/library/${id}` + (v && /^\d{1,6}$/.test(v) ? `?v=${v}` : '') : null;
  }, decodeEntry, 'This library entry is not available to you.');
  readonly cmd = new CommandState(this.api);
  readonly hits = signal<ReturnType<typeof decodeHits> | null>(null);
  readonly searchError = signal('');
  term = '';
  next = { body: '', source: '', effective: new Date().toISOString().slice(0, 10) };
  fresh = { code: '', title: '', category: 'FIRM_GUIDANCE', audience: 'ALL_STAFF', body: '', source: '' };

  draftOf(e: ReturnType<typeof decodeEntry>) { return e.history.find((x) => x.status === 'DRAFT') ?? null; }
  async search(): Promise<void> {
    this.searchError.set('');
    try { this.hits.set(await this.api.get(`/api/ui/library/search?term=${encodeURIComponent(this.term.trim())}`, decodeHits)); }
    catch (e) { this.hits.set(null); this.searchError.set((e as Error).message); }
  }
  private refresh = () => { this.catalogue.reload(); this.entry.reload(); };
  publish(versionId: string): void {
    this.cmd.run(`/api/ui/library/versions/${versionId}/publish`, {}, 'Version published; the previous version is superseded.').finally(this.refresh);
  }
  draft(id: string): void {
    this.cmd.run(`/api/ui/library/${id}/versions`, { body: this.next.body, sourceReference: this.next.source, effectiveFrom: this.next.effective },
      'Draft version saved; a Partner or administrator must publish it.', () => (this.next = { ...this.next, body: '', source: '' })).finally(this.refresh);
  }
  create(): void {
    const f = this.fresh;
    this.cmd.run('/api/ui/library', { code: f.code, title: f.title, category: f.category, audience: f.audience, body: f.body, sourceReference: f.source },
      'Entry created as a draft; a Partner or administrator must publish it.', () => (this.fresh = { ...f, code: '', title: '', body: '', source: '' })).finally(this.refresh);
  }
}
