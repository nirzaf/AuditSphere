import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const versionSummary = obj({ programVersionId: guid, programCode: text, version: text, status: text, sourceHash: text, procedureCount: nat, sectionCount: nat, approvedAt: nullable(instant) });
export const decodeProgramLibrary = obj({ versions: arr(versionSummary, 500), selectedVersion: nullable(versionSummary),
  sections: arr(obj({ sectionNumber: int, sectionTitle: text, procedureCount: nat, assertions: arr(text, 100) }), 200) });
export const decodeSectionPage = obj({ sectionNumber: int, sectionTitle: text, totalCount: nat, page: nat, pageSize: nat,
  items: arr(obj({ procedureId: guid, sourceProcedureId: text, sectionNumber: int, sectionTitle: text, ordinal: int, sourceWording: text,
    applicabilityCondition: nullable(text), expectedEvidence: nullable(text) }), 1000) });

@Component({
  selector: 'audit-program-library',
  imports: [FormsModule, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app">← Back to portfolio</a>
    <audit-page-header title="Audit program library" eyebrow="Audit methodology" description="The controlled versioned program template library. Published versions are immutable; changes require a new version." />
    <button matButton="outlined" (click)="version.set(null); lib.reload()">Refresh library</button>
    <audit-state [loading]="lib.loading()" [error]="lib.error()" label="the audit program library" />
    @if (lib.data(); as l) {
      @if (!l.versions.length) {
        <section class="panel" role="status"><h2>No published library version</h2><p>No audit program library version exists for this firm yet. Publishing is performed from an engagement's fieldwork workspace, which adopts the exact published version.</p></section>
      } @else {
        <p role="status">{{ l.versions.length }} library versions · {{ l.selectedVersion?.procedureCount ?? 0 }} procedures and {{ l.selectedVersion?.sectionCount ?? 0 }} sections in the selected version</p>
        <section class="panel" aria-labelledby="version-heading">
          <h2 id="version-heading">Library versions</h2>
          <label>Version <select name="version" [ngModel]="l.selectedVersion?.version" (ngModelChange)="version.set($event)">
            @for (v of l.versions; track v.programVersionId) { <option [value]="v.version">{{ v.programCode }} v{{ v.version }} · {{ v.status }} · {{ v.procedureCount }} procedures</option> }</select></label>
          @if (l.selectedVersion; as s) {
            <dl class="facts"><dt>Status</dt><dd><audit-status [value]="s.status" /></dd><dt>Source hash</dt><dd><code>{{ s.sourceHash }}</code></dd><dt>Procedures</dt><dd>{{ s.procedureCount }}</dd>
              <dt>Sections</dt><dd>{{ s.sectionCount }}</dd><dt>Published</dt><dd>{{ s.approvedAt ? s.approvedAt.slice(0, 16).replace('T', ' ') : 'Not published' }}</dd></dl>
          }
        </section>
        <section class="panel" aria-labelledby="sections-heading">
          <h2 id="sections-heading">Sections</h2>
          <div class="table-scroll"><table><thead><tr><th scope="col">#</th><th scope="col">Section</th><th scope="col">Procedures</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
            <tbody>@for (s of l.sections; track s.sectionNumber) { <tr><td>{{ s.sectionNumber }}</td><td>{{ s.sectionTitle }}</td><td>{{ s.procedureCount }}</td>
              <td><button matButton="outlined" (click)="open(l.selectedVersion!.programVersionId, s.sectionNumber, '')" [disabled]="busy()">Browse</button></td></tr> }</tbody></table></div>
        </section>
        @if (sectionError()) { <p role="alert" class="error-text">{{ sectionError() }}</p> }
        @if (section(); as p) {
          <section class="panel" aria-labelledby="procedures-heading">
            <h2 id="procedures-heading">Section {{ p.sectionNumber }} — {{ p.sectionTitle }}</h2>
            <form class="inline-form" (submit)="$event.preventDefault(); open(l.selectedVersion!.programVersionId, p.sectionNumber, search)">
              <label>Search procedures <input name="search" [(ngModel)]="search" placeholder="AWP-02 or confirmations" maxlength="100" /></label>
              <button matButton="filled" type="submit" [disabled]="busy()">Search</button>
              @if (search) { <button matButton="outlined" type="button" (click)="search = ''; open(l.selectedVersion!.programVersionId, p.sectionNumber, '')" [disabled]="busy()">Clear</button> }
            </form>
            <div class="table-scroll"><table><thead><tr><th scope="col">Source ID</th><th scope="col">Exact source procedure</th></tr></thead>
              <tbody>@for (x of p.items; track x.procedureId) { <tr><td><code>{{ x.sourceProcedureId }}</code></td><td>{{ x.sourceWording }}</td></tr> }
              @empty { <tr><td colspan="2">No procedures in this section match the search term.</td></tr> }</tbody></table></div>
            <p><small>Showing {{ p.items.length }} of {{ p.totalCount }} procedures.</small></p>
          </section>
        }
      }
    }
  `,
})
export class AuditProgramLibrary {
  private readonly api = inject(Api);
  readonly version = signal<string | null>(null);
  readonly lib = this.api.resource(() => '/api/ui/audit/library' + (this.version() ? `?version=${encodeURIComponent(this.version()!)}` : ''), decodeProgramLibrary,
    'Sign in with an authorized internal staff identity to view the audit program library.');
  readonly section = signal<ReturnType<typeof decodeSectionPage> | null>(null);
  readonly sectionError = signal('');
  readonly busy = signal(false);
  search = '';
  constructor() { effect(() => { this.lib.data(); untracked(() => { this.section.set(null); this.search = ''; }); }); }
  async open(versionId: string, sectionNumber: number, term: string): Promise<void> {
    this.busy.set(true); this.sectionError.set('');
    try { this.section.set(await this.api.get(`/api/ui/audit/library/${versionId}/sections/${sectionNumber}` + (term.trim() ? `?search=${encodeURIComponent(term.trim())}` : ''), decodeSectionPage)); }
    catch (e) { this.section.set(null); this.sectionError.set((e as Error).message); }
    finally { this.busy.set(false); }
  }
}
