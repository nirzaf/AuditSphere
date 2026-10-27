import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, CommandState } from '../../core/api';
import { arr, bool, date, dec, guid, int, nat, nullable, obj, oneOf, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { percentagePoints } from './group-membership';

const ownershipRow = obj({
  id: guid, parentClientId: guid, parentLegalName: text, childClientId: guid, childLegalName: text,
  effectiveFrom: date, effectiveTo: nullable(date), ownershipPercent: dec, economicInterestPercent: dec,
  controlAssessment: text, method: text, evidenceReference: text, status: text,
});
const membershipWindow = obj({ effectiveFrom: date, effectiveTo: nullable(date) });
const ownershipShape = obj({
  accessMode: nullable(oneOf('READ_ONLY', 'OPERATIONAL')),
  scopeId: guid, groupId: guid, groupName: text, scopeVersion: int, groupRevision: int, currentGroupRevision: int,
  scopeStatus: text, scopeMethod: text, canAddOwnership: bool,
  page: nat, pageSize: nat, total: nat, interests: arr(ownershipRow, 100),
  eligibleMembers: arr(obj({ clientId: guid, legalName: text, membershipWindows: arr(membershipWindow, 1000) }), 1000),
});
export function decodeOwnershipWorkspace(value: unknown, path = '') {
  const w = ownershipShape(value, path);
  if (w.scopeVersion < 1 || w.groupRevision < 1 || w.currentGroupRevision < 1 || w.pageSize < 1 || w.pageSize > 100 ||
      w.canAddOwnership && (w.scopeStatus !== 'DRAFT' || w.groupRevision !== w.currentGroupRevision) ||
      w.canAddOwnership && w.eligibleMembers.length < 2 ||
      w.eligibleMembers.reduce((total, member) => total + member.membershipWindows.length, 0) > 1000 ||
      new Set(w.eligibleMembers.map(x => x.clientId)).size !== w.eligibleMembers.length ||
      w.eligibleMembers.some(member => member.membershipWindows.length === 0 || member.membershipWindows.some(window =>
        window.effectiveTo !== null && window.effectiveTo < window.effectiveFrom)) ||
      new Set(w.interests.map(x => x.id)).size !== w.interests.length ||
      w.interests.some(x => x.parentClientId === x.childClientId || percentagePoints(x.ownershipPercent) === null ||
        percentagePoints(x.economicInterestPercent) === null || x.effectiveTo !== null && x.effectiveTo < x.effectiveFrom))
    throw new Error('Unsupported ownership workspace.');
  return w;
}
type OwnershipWorkspace = ReturnType<typeof decodeOwnershipWorkspace>;
type OwnershipIntent = {
  parentClientId: string; childClientId: string; effectiveFrom: string; effectiveTo: string | null;
  ownershipPercent: string; economicInterestPercent: string; controlAssessment: string; method: string;
  evidenceReference: string; expectedGroupRevision: number; reviewed: boolean;
};
function membershipCovers(member: OwnershipWorkspace['eligibleMembers'][number], from: string, to: string | null): boolean {
  return member.membershipWindows.some(window => window.effectiveFrom <= from &&
    (window.effectiveTo === null || to !== null && window.effectiveTo >= to));
}
function canonicalAmount(value: string): string {
  const [whole, fraction = ''] = value.split('.');
  const digits = whole.replace(/^0+(?=\d)/, '');
  const tail = fraction.replace(/0+$/, '');
  return tail ? `${digits}.${tail}` : digits;
}
function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const parsed = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}
function matches(row: OwnershipWorkspace['interests'][number], intent: OwnershipIntent): boolean {
  return row.parentClientId === intent.parentClientId && row.childClientId === intent.childClientId &&
    row.effectiveFrom === intent.effectiveFrom && row.effectiveTo === intent.effectiveTo &&
    canonicalAmount(row.ownershipPercent) === canonicalAmount(intent.ownershipPercent) &&
    canonicalAmount(row.economicInterestPercent) === canonicalAmount(intent.economicInterestPercent) &&
    row.controlAssessment === intent.controlAssessment.toUpperCase() && row.method === intent.method.toUpperCase() &&
    row.evidenceReference === intent.evidenceReference && row.status === 'APPROVED';
}

@Component({
  selector: 'audit-consolidation-ownership',
  imports: [FormsModule, ...SHARED],
  template: `
    <section class="panel" id="ownership" aria-labelledby="ownership-heading">
      <h2 id="ownership-heading">Ownership relationships</h2>
      <audit-state [loading]="workspace.loading()" [error]="workspace.error()" label="this scope's ownership relationships" />
      @if (workspace.data(); as w) {
        <p>{{ w.groupName }} · scope v{{ w.scopeVersion }} · pinned group revision {{ w.groupRevision }}
          · current revision {{ w.currentGroupRevision }}</p>
        <p><small>These scope relationships are immutable. Corrections require a new scope. They are separate from CRM relationships
          and component ownership percentages. Recording a relationship does not approve a consolidation method or enable unsupported nested calculations.</small></p>
        <div class="table-scroll"><table>
          <caption>Saved ownership relationships for this scope</caption>
          <thead><tr><th>Parent entity</th><th>Child entity</th><th>Effective period</th><th>Ownership</th><th>Economic interest</th><th>Assessment / method</th><th>Evidence / status</th></tr></thead>
          <tbody>@for (row of w.interests; track row.id) {
            <tr><td>{{ row.parentLegalName }}<small><code>{{ row.parentClientId }}</code></small></td>
              <td>{{ row.childLegalName }}<small><code>{{ row.childClientId }}</code></small></td>
              <td>{{ row.effectiveFrom }} – {{ row.effectiveTo ?? 'Open-ended' }}</td>
              <td>{{ row.ownershipPercent }}%</td><td>{{ row.economicInterestPercent }}%</td>
              <td>{{ row.controlAssessment }}<small><code>{{ row.method }}</code></small></td>
              <td>{{ row.evidenceReference }}<audit-status [value]="row.status" /></td></tr>
          } @empty { <tr><td colspan="7">No ownership relationships are saved in this scope.</td></tr> }</tbody>
        </table></div>
        <nav class="actions" aria-label="Ownership relationship pages">
          <button type="button" (click)="changePage(-1)" [disabled]="w.page === 0 || workspace.loading()">Previous relationships</button>
          <span>Page {{ w.page + 1 }} · {{ w.total }} relationships</span>
          <button type="button" (click)="changePage(1)" [disabled]="(w.page + 1) * w.pageSize >= w.total || workspace.loading()">Next relationships</button>
          <button type="button" (click)="workspace.reload()" [disabled]="workspace.loading()">Refresh relationships</button>
        </nav>
        @if (w.canAddOwnership) {
          <form (ngSubmit)="addOwnership()" aria-labelledby="add-ownership-heading">
            <h3 id="add-ownership-heading">Add a relationship to the current draft</h3>
            <fieldset [disabled]="cmd().busy() || cmd().uncertain()">
              <legend>Exact ownership evidence</legend>
              <label>Parent approved member * <select name="parent" [(ngModel)]="parentClientId" (ngModelChange)="reviewed = false" required>
                <option value="">Choose parent entity</option>
                @for (member of w.eligibleMembers; track member.clientId) { <option [value]="member.clientId">{{ member.legalName }} · {{ member.clientId }} · approved membership: {{ membershipSummary(member) }}</option> }
              </select></label>
              <label>Child approved member * <select name="child" [(ngModel)]="childClientId" (ngModelChange)="reviewed = false" required>
                <option value="">Choose child entity</option>
                @for (member of w.eligibleMembers; track member.clientId) { <option [value]="member.clientId">{{ member.legalName }} · {{ member.clientId }} · approved membership: {{ membershipSummary(member) }}</option> }
              </select></label>
              <label>Effective from * <input name="from" type="date" [(ngModel)]="effectiveFrom" (ngModelChange)="reviewed = false" required /></label>
              <label>Effective to <input name="to" type="date" [(ngModel)]="effectiveTo" (ngModelChange)="reviewed = false" /></label>
              <p><small>The full relationship period must fit within one approved membership window for each entity. An open-ended relationship requires an open-ended membership. Percentages are percentage points from 0 to 100 with at most six decimal places; 75.5 means 75.5%.</small></p>
              <label>Ownership percentage points * <input name="ownership" inputmode="decimal" [(ngModel)]="ownershipPercent" (ngModelChange)="reviewed = false" maxlength="10" required /></label>
              <label>Economic-interest percentage points * <input name="economic" inputmode="decimal" [(ngModel)]="economicInterestPercent" (ngModelChange)="reviewed = false" maxlength="10" required /></label>
              <label>Control assessment * <input name="assessment" [(ngModel)]="controlAssessment" (ngModelChange)="reviewed = false" maxlength="100" required /></label>
              <label>Method reference * <input name="method" [(ngModel)]="method" (ngModelChange)="reviewed = false" maxlength="100" required /></label>
              <label>Evidence reference * <input name="evidence" [(ngModel)]="evidenceReference" (ngModelChange)="reviewed = false" maxlength="2000" required /></label>
              <label><input name="reviewed" type="checkbox" [(ngModel)]="reviewed" /> I reviewed these entities, dates, exact percentage points and evidence for scope v{{ w.scopeVersion }} at group revision {{ w.groupRevision }}.</label>
              <button type="submit" [disabled]="!reviewed">{{ cmd().busy() ? 'Saving relationship…' : 'Add immutable ownership relationship' }}</button>
            </fieldset>
          </form>
        } @else {
          <p>Adding relationships requires a current draft scope, two approved members, and an explicit Manager, Partner or Administrator grant on this group.</p>
        }
      }
      @if (validation()) { <p role="alert">{{ validation() }}</p> }
      <audit-command-message [message]="cmd().message()" [failed]="cmd().failed()" />
      @if (cmd().uncertain()) {
        <p role="alert">The submitted relationship will not be resent automatically. Check its exact saved parent/child relationship before any further command.</p>
        <button type="button" (click)="recover()" [disabled]="recovering()">{{ recovering() ? 'Checking relationship…' : 'Check exact saved relationship' }}</button>
        @if (recoveryError()) { <p role="alert">{{ recoveryError() }}</p> }
      }
    </section>
  `,
  styles: [`:host { display: block; min-width: 0; } label { display: block; margin-block: .75rem; }
    input:not([type=checkbox]), select { display: block; max-width: 100%; } small { display: block; }`],
})
export class ConsolidationOwnership {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly scopeId = input.required<string>();
  readonly page = signal(0);
  readonly cmd = signal(new CommandState(this.api));
  readonly validation = signal('');
  readonly recoveryError = signal('');
  readonly recovering = signal(false);
  private readonly pending = signal<OwnershipIntent | null>(null);
  readonly workspace = this.api.resource(() => `/api/ui/consolidation/scopes/${this.scopeId()}/ownership-interests?page=${this.page()}&pageSize=25`,
    (value, path) => {
      const w = decodeOwnershipWorkspace(value, path);
      if (w.scopeId !== this.scopeId() || w.page !== this.page() || w.pageSize !== 25) throw new Error('Ownership response context changed.');
      return w;
    }, 'Ownership is unavailable in your current group grant.');
  parentClientId = '';
  childClientId = '';
  effectiveFrom = '';
  effectiveTo = '';
  ownershipPercent = '';
  economicInterestPercent = '';
  controlAssessment = '';
  method = '';
  evidenceReference = '';
  reviewed = false;

  membershipSummary(member: OwnershipWorkspace['eligibleMembers'][number]): string {
    return member.membershipWindows.map(window =>
      `${window.effectiveFrom} through ${window.effectiveTo ?? 'open-ended'}`).join('; ');
  }

  constructor() {
    effect(() => {
      this.scopeId();
      this.session.invalidation();
      untracked(() => {
        this.page.set(0);
        this.cmd.set(new CommandState(this.api));
        this.pending.set(null);
        this.recovering.set(false);
        this.validation.set('');
        this.recoveryError.set('');
        this.clearForm();
      });
    });
  }
  changePage(delta: number): void {
    const w = this.workspace.data();
    if (w && !this.workspace.loading() && w.page + delta >= 0 && (w.page + delta) * w.pageSize < w.total) this.page.set(w.page + delta);
  }
  async addOwnership(): Promise<void> {
    const w = this.workspace.data(), cmd = this.cmd();
    if (!w?.canAddOwnership || !this.reviewed || cmd.busy() || cmd.uncertain()) return;
    this.validation.set('');
    const parent = w.eligibleMembers.find(x => x.clientId === this.parentClientId);
    const child = w.eligibleMembers.find(x => x.clientId === this.childClientId);
    const ownership = percentagePoints(this.ownershipPercent), economic = percentagePoints(this.economicInterestPercent);
    const from = this.effectiveFrom.trim(), to = this.effectiveTo.trim() || null;
    if (!parent || !child || parent.clientId === child.clientId || !validDate(from) || to !== null && (!validDate(to) || to < from) ||
        !membershipCovers(parent, from, to) || !membershipCovers(child, from, to) || ownership === null || economic === null ||
        !this.controlAssessment.trim() || !this.method.trim() || !this.evidenceReference.trim() ||
        this.controlAssessment.trim().length > 100 || this.method.trim().length > 100 || this.evidenceReference.trim().length > 2000) {
      this.validation.set('Choose distinct approved members whose approved membership windows cover the full relationship period, exact percentages from 0 to 100, and bounded assessment, method and evidence references.');
      return;
    }
    const intent: OwnershipIntent = { parentClientId: parent.clientId, childClientId: child.clientId,
      effectiveFrom: from, effectiveTo: to, ownershipPercent: ownership, economicInterestPercent: economic,
      controlAssessment: this.controlAssessment.trim(), method: this.method.trim(), evidenceReference: this.evidenceReference.trim(),
      expectedGroupRevision: w.groupRevision, reviewed: true };
    this.pending.set(intent);
    this.reviewed = false;
    const scopeId = this.scopeId();
    const result = await cmd.run<unknown>(`/api/ui/consolidation/scopes/${scopeId}/ownership-interests`, intent, 'Ownership relationship saved in this draft scope.', value => {
      if (this.cmd() !== cmd || this.scopeId() !== scopeId) return;
      try { guid(value, 'ownershipReceipt'); }
      catch { cmd.uncertain.set(true); cmd.failed.set(true); cmd.message.set('The returned relationship identity could not be validated. Check the saved relationship.'); return; }
      this.pending.set(null);
      this.clearForm();
      this.reloadFirstPage();
    });
    if (this.cmd() !== cmd || this.scopeId() !== scopeId) return;
    if (!result && !cmd.uncertain()) { this.pending.set(null); this.workspace.reload(); }
  }
  async recover(): Promise<void> {
    const intent = this.pending(), cmd = this.cmd(), scopeId = this.scopeId();
    if (!intent || !cmd.uncertain() || this.recovering()) return;
    this.recovering.set(true);
    this.recoveryError.set('');
    try {
      const found = await this.api.get(`/api/ui/consolidation/scopes/${scopeId}/ownership-interests?page=0&pageSize=25&parentClientId=${intent.parentClientId}&childClientId=${intent.childClientId}`, decodeOwnershipWorkspace);
      if (this.cmd() !== cmd || this.scopeId() !== scopeId) return;
      if (found.scopeId === scopeId && found.total === 1 && found.interests.length === 1 && matches(found.interests[0], intent)) {
        cmd.uncertain.set(false); cmd.failed.set(false); cmd.message.set('The exact submitted relationship is saved. It was not resent.');
        this.pending.set(null); this.clearForm(); this.reloadFirstPage();
      } else this.recoveryError.set('No exact saved relationship is confirmed. The outcome remains unknown and further submission stays blocked.');
    } catch (error) {
      if (this.cmd() === cmd && this.scopeId() === scopeId) this.recoveryError.set(error instanceof Error ? error.message : 'The saved relationship could not be checked.');
    } finally { if (this.cmd() === cmd && this.scopeId() === scopeId) this.recovering.set(false); }
  }
  private clearForm(): void {
    this.parentClientId = ''; this.childClientId = ''; this.effectiveFrom = ''; this.effectiveTo = '';
    this.ownershipPercent = ''; this.economicInterestPercent = ''; this.controlAssessment = '';
    this.method = ''; this.evidenceReference = ''; this.reviewed = false;
  }
  private reloadFirstPage(): void { if (this.page() === 0) this.workspace.reload(); else this.page.set(0); }
}
