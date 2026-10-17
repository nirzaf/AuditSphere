import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavigationCancel, NavigationEnd, NavigationError, NavigationStart, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { SessionService } from '../../core/session';
import { Api } from '../../core/api';
import { Decoder, arr, bool, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const versionSummary = obj({ programVersionId: guid, programCode: text, version: text, status: text, sourceHash: text,
  contentDigest: nullable(text), procedureCount: nat, sectionCount: nat, createdByUserId: guid, approvedByUserId: nullable(guid),
  publicationReviewReference: nullable(text), createdAt: instant, approvedAt: nullable(instant) });
const libraryShape = obj({ canPrepare: bool, canPublish: bool, versions: arr(versionSummary, 500), selectedVersion: nullable(versionSummary),
  sections: arr(obj({ sectionNumber: int, sectionTitle: text, procedureCount: nat, assertions: arr(text, 100) }), 200) });
const sectionShape = obj({ sectionNumber: int, sectionTitle: text, totalCount: nat, page: nat, pageSize: nat,
  items: arr(obj({ procedureId: guid, sourceProcedureId: text, sectionNumber: int, sectionTitle: text, ordinal: int, sourceWording: text,
    applicabilityCondition: nullable(text), expectedEvidence: nullable(text) }), 1000) });

export const decodeProgramLibrary: Decoder<ReturnType<typeof libraryShape>> = (raw, path) => {
  const library = libraryShape(raw, path), selected = library.selectedVersion;
  if (new Set(library.versions.map(v => v.programVersionId)).size !== library.versions.length ||
    new Set(library.sections.map(s => s.sectionNumber)).size !== library.sections.length ||
    library.sections.some(s => s.sectionNumber < 1 || s.sectionNumber > 20) ||
    (library.versions.length > 0) !== !!selected || (!selected && library.sections.length > 0) ||
    (selected && !library.versions.some(v => JSON.stringify(v) === JSON.stringify(selected)))) throw new Error(`${path}: inconsistent library selection`);
  return library;
};
export const decodeSectionPage: Decoder<ReturnType<typeof sectionShape>> = (raw, path) => {
  const page = sectionShape(raw, path);
  if (page.sectionNumber < 1 || page.sectionNumber > 20 || page.page < 1 || page.pageSize < 1 || page.pageSize > 500 ||
    page.items.length > page.pageSize || page.totalCount < page.items.length ||
    new Set(page.items.map(x => x.procedureId)).size !== page.items.length ||
    page.items.some(x => x.sectionNumber !== page.sectionNumber || x.sectionTitle !== page.sectionTitle)) throw new Error(`${path}: inconsistent section page`);
  return page;
};

@Component({
  selector: 'audit-program-library',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app">← Back to portfolio</a>
    <audit-page-header title="Audit program library" eyebrow="Audit methodology" description="The controlled versioned program template library. Published versions are immutable; changes require a new version." />
    <button matButton="outlined" (click)="refresh()">Refresh library</button>
    @if (library()?.canPrepare) {
      <section class="panel" aria-labelledby="prepare-heading">
        <h2 id="prepare-heading">Prepare controlled source version</h2>
        <p>The source wording is fixed to the verified upload. Preparation creates an immutable draft; it does not publish or establish professional acceptance.</p>
        <form class="inline-form" (submit)="$event.preventDefault(); prepareVersionDraft()">
          <label>Version identifier <input name="prepare-version" [ngModel]="prepareVersion" (ngModelChange)="prepareVersion = $event" maxlength="100" required /></label>
          <button matButton="filled" type="submit" [disabled]="commandBusy() || !prepareVersion.trim()">Prepare source draft</button>
        </form>
        <p>Verified source SHA-256: <code>{{ sourceHash }}</code></p>
        @if (commandError()) { <p role="alert">{{ commandError() }}</p> }
      </section>
    }
    <audit-state [loading]="lib.loading()" [error]="lib.error()" label="the audit program library" />
    <audit-state [loading]="busy()" [error]="sectionError()" label="section procedures" />
    @if (blocked()) { <p role="note">The previous library content has been cleared. Refresh the library to check current access and try again.</p> }
    @if (library(); as l) {
      @if (!l.versions.length) {
        <section class="panel" role="status"><h2>No library version</h2><p>No controlled audit program version exists for this firm yet.</p></section>
      } @else {
        <p role="status">{{ l.versions.length }} library versions · {{ l.selectedVersion?.procedureCount ?? 0 }} procedures and {{ l.selectedVersion?.sectionCount ?? 0 }} sections in the selected version</p>
        <section class="panel" aria-labelledby="version-heading">
          <h2 id="version-heading">Library versions</h2>
          <label>Version <select name="version" [ngModel]="l.selectedVersion?.programVersionId" (ngModelChange)="selectVersion($event)">
            @for (v of l.versions; track v.programVersionId) { <option [value]="v.programVersionId">{{ v.programCode }} v{{ v.version }} · {{ v.status }} · {{ v.procedureCount }} procedures</option> }</select></label>
          @if (l.selectedVersion; as s) {
            <dl class="facts"><dt>Status</dt><dd><audit-status [value]="s.status" /></dd><dt>Source SHA-256</dt><dd><code>{{ s.sourceHash }}</code></dd>
              <dt>Content digest</dt><dd><code>{{ s.contentDigest ?? 'Legacy publication evidence not recorded' }}</code></dd><dt>Prepared by</dt><dd>{{ s.createdByUserId }}</dd>
              <dt>Procedures</dt><dd>{{ s.procedureCount }}</dd><dt>Sections</dt><dd>{{ s.sectionCount }}</dd>
              <dt>Published</dt><dd>{{ s.approvedAt ? s.approvedAt.slice(0, 16).replace('T', ' ') : 'Not published' }}</dd>
              <dt>Approved by</dt><dd>{{ s.approvedByUserId ?? 'Not published' }}</dd>
              <dt>Review reference</dt><dd>{{ s.publicationReviewReference ?? 'Not recorded' }}</dd></dl>
            @if (s.status === 'DRAFT' && s.contentDigest && l.canPublish) {
              <form class="inline-form" (submit)="$event.preventDefault(); publishSelected(s.programVersionId, s.contentDigest!)">
                <label>Publication review reference <input name="publication-review-reference" [ngModel]="publicationReviewReference" (ngModelChange)="publicationReviewReference = $event" maxlength="200" required /></label>
                <button matButton="filled" type="submit" [disabled]="commandBusy() || !publicationReviewReference.trim()">Publish reviewed version</button>
              </form>
              <p>Publication requires a different currently authorized firm-wide Partner or Administrator. The reference records the review trail; it does not certify professional qualification or methodology acceptance.</p>
            }
            @if (commandError()) { <p role="alert">{{ commandError() }}</p> }
          }
        </section>
        <section class="panel" aria-labelledby="sections-heading">
          <h2 id="sections-heading">Sections</h2>
          <div class="table-scroll"><table><thead><tr><th scope="col">#</th><th scope="col">Section</th><th scope="col">Procedures</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
            <tbody>@for (s of l.sections; track s.sectionNumber) { <tr><td>{{ s.sectionNumber }}</td><td>{{ s.sectionTitle }}</td><td>{{ s.procedureCount }}</td>
              <td><button matButton="outlined" (click)="open(l.selectedVersion!.programVersionId, s.sectionNumber, '')" [disabled]="!l.selectedVersion">Browse</button></td></tr> } @empty { <tr><td colspan="4">No sections in the selected version.</td></tr> }</tbody></table></div>
        </section>
        @if (activeSection(); as selectedSection) {
          <section class="panel" aria-labelledby="procedures-heading">
            <h2 id="procedures-heading">Section {{ selectedSection.sectionNumber }} — {{ selectedSection.sectionTitle }}</h2>
            <form class="inline-form" (submit)="$event.preventDefault(); searchSection()">
              <label>Search procedures <input name="search" [ngModel]="search" (ngModelChange)="search = $event" placeholder="AWP-02 or confirmations" /></label>
              <button matButton="filled" type="submit" [disabled]="busy()">Search</button>
              @if (search) { <button matButton="outlined" type="button" (click)="clearSearch()">Clear</button> }
            </form>
            @if (section(); as p) {
              <p role="status">{{ appliedSearch() ? 'Results for “' + appliedSearch() + '”' : 'All procedures in this section' }}</p>
              <div class="table-scroll"><table><thead><tr><th scope="col">Source ID</th><th scope="col">Exact source procedure</th></tr></thead>
                <tbody>@for (x of p.items; track x.procedureId) { <tr><td><code>{{ x.sourceProcedureId }}</code></td><td>{{ x.sourceWording }}
                  @if (x.applicabilityCondition) { <p>Applicability: {{ x.applicabilityCondition }}</p> }
                  @if (x.expectedEvidence) { <p>Expected evidence: {{ x.expectedEvidence }}</p> }</td></tr> }
                @empty { <tr><td colspan="2">No procedures in this section match the search term.</td></tr> }</tbody></table></div>
              <p><small>Showing {{ p.items.length }} of {{ p.totalCount }} procedures.</small></p>
              @if (p.items.length < p.totalCount) { <p role="note">More procedures match than are shown. Refine your search to narrow results.</p> }
            } @else if (!busy()) { <p role="status">Submit the search to load procedures for the current term.</p> }
          </section>
        }
      }
    }
  `,
})
export class AuditProgramLibrary {
  readonly sourceHash = '034d3b5023f5b498f0953ef6fea2af8439d72cc107075bd3336e53405091d987';
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly requested = signal<{ id: string; version: string } | null>(null);
  private readonly refreshGeneration = signal(0);
  private readonly active = signal(true);
  private readonly refreshing = signal(false);
  private readonly libraryActor = signal<ReturnType<SessionService['current']>>(null);
  private readonly libraryEpoch = signal(-1);
  readonly lib = this.api.resource(() => {
    this.refreshGeneration();
    return this.active() ? '/api/ui/audit/library' + (this.requested() ? `?version=${encodeURIComponent(this.requested()!.version)}` : '') : null;
  }, (raw, path) => {
    const library = decodeProgramLibrary(raw, path);
    if (this.requested() && library.selectedVersion?.programVersionId !== this.requested()!.id) throw new Error('The requested library version is unavailable.');
    this.libraryActor.set(this.session.current()); this.libraryEpoch.set(this.session.invalidation());
    return library;
  }, 'Sign in with an authorized internal staff identity to view the audit program library.');
  readonly blocked = signal(false);
  readonly library = computed(() => this.active() && this.session.current()?.staff && this.libraryActor() === this.session.current() && this.libraryEpoch() === this.session.invalidation() && !this.lib.loading() && !this.lib.error() && !this.blocked() && !this.refreshing() &&
    (!this.requested() || this.lib.data()?.selectedVersion?.programVersionId === this.requested()!.id) ? this.lib.data() : null);
  readonly section = signal<ReturnType<typeof decodeSectionPage> | null>(null);
  readonly activeSection = signal<ReturnType<typeof decodeProgramLibrary>['sections'][number] | null>(null);
  readonly sectionError = signal('');
  readonly busy = signal(false);
  readonly appliedSearch = signal('');
  readonly commandBusy = signal(false);
  readonly commandError = signal('');
  prepareVersion = '2026.1';
  publicationReviewReference = '';
  private readonly searchValue = signal('');
  private request = 0;
  private destroyed = false;
  private lastLibrary: ReturnType<typeof decodeProgramLibrary> | null = null;
  get search(): string { return this.searchValue(); }
  set search(value: string) {
    if (value === this.searchValue()) return;
    this.invalidateRead(false); this.searchValue.set(value);
  }
  constructor() {
    effect(() => {
      this.session.current(); this.session.invalidation();
      untracked(() => { this.requested.set(null); this.invalidateRead(); });
    });
    const route = inject(Router).events.subscribe(event => {
      if (event instanceof NavigationStart) { this.active.set(false); this.invalidateRead(); }
      else if (event instanceof NavigationCancel || event instanceof NavigationError) this.active.set(true);
      else if (event instanceof NavigationEnd) this.active.set(/^\/(?:ui\/)?app\/audit\/library$/.test(event.urlAfterRedirects.split(/[?#]/)[0]));
    });
    effect(() => {
      const data = this.lib.data(), loading = this.lib.loading(), error = this.lib.error();
      this.session.current(); this.session.invalidation();
      untracked(() => {
        this.invalidateRead();
        if (data && data !== this.lastLibrary && !loading && !error) { this.blocked.set(false); this.refreshing.set(false); }
        this.lastLibrary = data;
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.invalidateRead(); route.unsubscribe(); });
  }
  private invalidateRead(clearSelection = true): void {
    ++this.request; this.busy.set(false); this.section.set(null); this.sectionError.set(''); this.appliedSearch.set('');
    if (clearSelection) { this.activeSection.set(null); this.searchValue.set(''); }
  }
  refresh(): void {
    if (this.destroyed) return;
    this.invalidateRead(); this.refreshing.set(true); this.requested.set(null); this.refreshGeneration.update(n => n + 1);
  }
  async prepareVersionDraft(): Promise<void> {
    if (!this.library()?.canPrepare || this.commandBusy() || !this.prepareVersion.trim()) return;
    this.commandBusy.set(true); this.commandError.set('');
    try {
      const result = await this.api.command('/api/ui/audit/library/versions/prepare',
        { version: this.prepareVersion.trim(), sourceHash: this.sourceHash });
      if (!result.ok) { this.commandError.set(result.message); return; }
      this.refresh();
    } finally { this.commandBusy.set(false); }
  }
  async publishSelected(versionId: string, contentDigest: string): Promise<void> {
    if (!this.library()?.canPublish || this.commandBusy() || !this.publicationReviewReference.trim()) return;
    this.commandBusy.set(true); this.commandError.set('');
    try {
      const result = await this.api.command(`/api/ui/audit/library/versions/${versionId}/publish`,
        { expectedContentDigest: contentDigest, publicationReviewReference: this.publicationReviewReference.trim() });
      if (!result.ok) { this.commandError.set(result.message); return; }
      this.publicationReviewReference = ''; this.refresh();
    } finally { this.commandBusy.set(false); }
  }
  selectVersion(id: string): void {
    const chosen = this.library()?.versions.find(v => v.programVersionId === id);
    if (!chosen || this.destroyed) return;
    this.invalidateRead(); this.requested.set({ id, version: chosen.version });
  }
  async searchSection(): Promise<void> {
    const selected = this.library()?.selectedVersion, section = this.activeSection();
    if (selected && section) await this.open(selected.programVersionId, section.sectionNumber, this.search);
  }
  async clearSearch(): Promise<void> {
    this.search = ''; await this.searchSection();
  }
  async open(versionId: string, sectionNumber: number, term: string): Promise<void> {
    const library = this.library(), selected = library?.selectedVersion, summary = library?.sections.find(s => s.sectionNumber === sectionNumber);
    if (this.destroyed || !selected || selected.programVersionId !== versionId || !summary) return;
    this.invalidateRead(false); this.activeSection.set(summary); this.searchValue.set(term); this.busy.set(true);
    const request = this.request, actor = this.session.current(), epoch = this.session.invalidation(), search = term.trim();
    const valid = () => !this.destroyed && request === this.request && actor === this.session.current() && epoch === this.session.invalidation() &&
      this.library() === library && this.activeSection() === summary && this.search.trim() === search;
    try {
      const page = await this.api.get(`/api/ui/audit/library/${versionId}/sections/${sectionNumber}` + (search ? `?search=${encodeURIComponent(search)}` : ''), decodeSectionPage);
      if (!valid()) return;
      if (page.sectionNumber !== sectionNumber || page.sectionTitle !== summary.sectionTitle || page.page !== 1 || page.pageSize !== 100 ||
        page.items.length !== Math.min(page.totalCount, page.pageSize) || page.totalCount > summary.procedureCount ||
        page.items.some(x => x.sectionNumber !== sectionNumber || x.sectionTitle !== summary.sectionTitle)) throw new Error('The section response did not match the selected library source.');
      this.section.set(page); this.appliedSearch.set(search);
    } catch (error) {
      if (valid()) {
        this.section.set(null); this.activeSection.set(null); this.searchValue.set(''); this.blocked.set(true);
        this.sectionError.set(error instanceof Error ? error.message : 'The section is unavailable. Refresh the library.');
      }
    } finally { if (request === this.request) this.busy.set(false); }
  }
}
