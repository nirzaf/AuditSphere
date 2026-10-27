import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, CommandState } from '../../core/api';
import { arr, bool, date, dec, guid, int, nat, nullable, obj, oneOf, text, decimalInput } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeConsolidationGroupMembership = obj({
  groupId: guid, groupCode: text, groupName: text, groupRevision: int,
  page: nat, pageSize: nat, total: nat,
  members: arr(obj({ id: guid, clientId: guid, clientName: text, effectiveFrom: date, effectiveTo: nullable(date),
    controlMethod: text, ownershipPercent: dec, economicInterestPercent: dec, evidenceReference: text, status: text, revision: int }), 100),
  eligibleClients: arr(obj({ id: guid, legalName: text }), 10000),
  canAddMember: bool,
  accessMode: nullable(oneOf('READ_ONLY', 'OPERATIONAL')),
});

type MembershipWorkspace = ReturnType<typeof decodeConsolidationGroupMembership>;
type MembershipRow = MembershipWorkspace['members'][number];
type MembershipIntent = {
  clientId: string; effectiveFrom: string; effectiveTo: string | null; controlMethod: string;
  ownershipPercent: string; economicInterestPercent: string; evidenceReference: string;
};

/** Validates percentage points without converting through binary floating point. */
export function percentagePoints(value: string): string | null {
  const exact = decimalInput(value, 6);
  if (exact === null || exact.startsWith('-')) return null;
  const [wholeRaw, fraction = ''] = exact.split('.');
  const whole = wholeRaw.replace(/^0+(?=\d)/, '');
  if (whole.length > 3 || whole.length === 3 && (whole > '100' || whole === '100' && /[1-9]/.test(fraction))) return null;
  return exact;
}

function canonicalDecimal(value: string): string {
  const [wholeRaw, fractionRaw = ''] = value.split('.');
  const whole = wholeRaw.replace(/^0+(?=\d)/, '');
  const fraction = fractionRaw.replace(/0+$/, '');
  return fraction ? `${whole}.${fraction}` : whole;
}

function isDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(date.valueOf()) && date.toISOString().slice(0, 10) === value;
}

function sameIntent(row: MembershipRow, intent: MembershipIntent): boolean {
  return row.clientId === intent.clientId && row.effectiveFrom === intent.effectiveFrom &&
    row.effectiveTo === intent.effectiveTo && row.controlMethod === intent.controlMethod.toUpperCase() &&
    canonicalDecimal(row.ownershipPercent) === canonicalDecimal(intent.ownershipPercent) &&
    canonicalDecimal(row.economicInterestPercent) === canonicalDecimal(intent.economicInterestPercent) &&
    row.evidenceReference === intent.evidenceReference;
}

@Component({
  selector: 'audit-consolidation-group-membership',
  imports: [FormsModule, ...SHARED],
  template: `
    <section class="panel" id="group-membership-panel" aria-labelledby="membership-heading">
      <h2 id="membership-heading">Legal-entity perimeter</h2>
      <audit-state [loading]="workspace.loading()" [error]="workspace.error()" label="the authorized group membership history" />
      @if (workspace.data(); as w) {
        @if (w.accessMode === 'READ_ONLY') { <p role="status" class="hint">Read-only — Administrator oversight. Membership changes require explicit group authority.</p> }
        <p><strong>{{ w.groupName }} <code>{{ w.groupCode }}</code></strong></p>
        <p role="status">Group revision {{ w.groupRevision }} · {{ w.total }} membership records. Effective dates and approvals remain visible as history.</p>
        <p><small>Adding a membership increments the group revision. Scope versions pin the revision they were created against; stale scopes refuse perimeter-dependent preparation, calculation and approval until a current version is created.</small></p>
        <div class="table-scroll"><table aria-label="Effective-dated accounting group membership">
          <thead><tr><th>Legal entity</th><th>Effective period</th><th>Control method</th><th>Ownership</th><th>Economic interest</th><th>Evidence reference</th><th>Decision</th></tr></thead>
          <tbody>@for (m of w.members; track m.id) {
            <tr><td>{{ m.clientName }}<small><code>{{ m.clientId }}</code></small></td>
              <td>{{ m.effectiveFrom }} – {{ m.effectiveTo ?? 'Open-ended' }}<small>{{ periodState(m) }}</small></td>
              <td><code>{{ m.controlMethod }}</code></td><td>{{ m.ownershipPercent }}%</td><td>{{ m.economicInterestPercent }}%</td>
              <td>{{ m.evidenceReference }}</td><td><audit-status [value]="m.status" /></td></tr>
          } @empty { <tr><td colspan="7">No membership history is recorded for this group.</td></tr> }</tbody>
        </table></div>
        <nav class="actions" aria-label="Group membership history pages">
          <button type="button" (click)="changePage(page() - 1)" [disabled]="page() === 0">Previous memberships</button>
          <span>Page {{ page() + 1 }} of {{ pageCount(w) }}</span>
          <button type="button" (click)="changePage(page() + 1)" [disabled]="page() + 1 >= pageCount(w)">Next memberships</button>
        </nav>
        @if (w.canAddMember) {
          <form class="membership-form" (ngSubmit)="addMember()" aria-labelledby="add-membership-heading">
            <h3 id="add-membership-heading">Add effective-dated member</h3>
            <p><small>Only same-firm legal entities are available. Percentages use percentage points (for example, 75.5 means 75.5%), preserve up to six decimal places, and must be between 0 and 100. An overlapping period is refused by the server.</small></p>
            <label>Find a legal entity
              <input name="clientSearch" [ngModel]="clientSearch()" (ngModelChange)="clientSearch.set($event)" autocomplete="off" />
            </label>
            <label>Legal entity *
              <select name="clientId" [(ngModel)]="clientId" required>
                <option value="">Choose a same-firm legal entity</option>
                @for (client of filteredClients(); track client.id) { <option [value]="client.id">{{ client.legalName }}</option> }
              </select>
            </label>
            <label>Effective from * <input name="effectiveFrom" type="date" [(ngModel)]="effectiveFrom" required /></label>
            <label>Effective to <input name="effectiveTo" type="date" [(ngModel)]="effectiveTo" /></label>
            <label>Control method * <input name="controlMethod" [(ngModel)]="controlMethod" maxlength="100" required /></label>
            <label>Ownership percentage points * <input name="ownershipPercent" inputmode="decimal" [(ngModel)]="ownershipPercent" required /></label>
            <label>Economic-interest percentage points * <input name="economicInterestPercent" inputmode="decimal" [(ngModel)]="economicInterestPercent" required /></label>
            <label>Evidence reference * <input name="evidenceReference" [(ngModel)]="evidenceReference" maxlength="2000" required /></label>
            @if (validationMessage()) { <p role="alert">{{ validationMessage() }}</p> }
            <button type="submit" [disabled]="cmd.busy() || cmd.uncertain()">{{ cmd.busy() ? 'Adding membership…' : 'Add membership' }}</button>
          </form>
        } @else {
          <p>Membership changes require an explicit Manager, Partner, or Administrator grant on this accounting group.</p>
        }
      }
      @if (cmd.uncertain()) {
        <section class="recovery" aria-labelledby="membership-recovery-heading">
          <h3 id="membership-recovery-heading">Membership outcome needs review</h3>
          <p role="alert">{{ cmd.message() }} AuditSphere will not resend this membership automatically.</p>
          <button type="button" (click)="recoverMembership()" [disabled]="checkingRecovery()">{{ checkingRecovery() ? 'Checking saved membership…' : 'Check exact saved membership' }}</button>
          @if (recoveryError()) { <p role="alert">{{ recoveryError() }}</p> }
        </section>
      }
      @if (!cmd.uncertain()) { <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" /> }
    </section>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .membership-form label { display: block; margin-block: .75rem; }
    .membership-form input, .membership-form select { display: block; max-width: 100%; }
    .recovery { margin-block-start: 1rem; }
  `],
})
export class ConsolidationGroupMembership {
  private readonly api = inject(Api);
  readonly groupId = input.required<string>();
  readonly page = signal(0);
  readonly clientSearch = signal('');
  readonly checkingRecovery = signal(false);
  readonly recoveryError = signal('');
  readonly validationMessage = signal('');
  readonly pendingMembership = signal<MembershipIntent | null>(null);
  readonly cmd = new CommandState(this.api);
  readonly workspace = this.api.resource(() => {
    const groupId = this.groupId();
    return groupId ? `/api/ui/consolidation/groups/${groupId}/members?page=${this.page()}&pageSize=25` : null;
  }, decodeConsolidationGroupMembership, 'This group membership workspace is not available in the current firm and group grant.');
  readonly filteredClients = computed(() => {
    const clients = this.workspace.data()?.eligibleClients ?? [];
    const search = this.clientSearch().trim().toLocaleLowerCase();
    return search ? clients.filter(x => x.legalName.toLocaleLowerCase().includes(search)) : clients;
  });

  clientId = '';
  effectiveFrom = new Date().toISOString().slice(0, 10);
  effectiveTo = '';
  controlMethod = 'CONTROLLED';
  ownershipPercent = '100';
  economicInterestPercent = '100';
  evidenceReference = '';

  pageCount(w: MembershipWorkspace): number { return Math.max(1, Math.ceil(w.total / w.pageSize)); }
  changePage(page: number): void {
    const w = this.workspace.data();
    if (w && page >= 0 && page < this.pageCount(w)) this.page.set(page);
  }
  periodState(row: MembershipRow): string {
    const today = new Date().toISOString().slice(0, 10);
    if (row.effectiveFrom > today) return 'Future';
    if (row.effectiveTo !== null && row.effectiveTo < today) return 'Historical';
    return 'Current';
  }

  async addMember(): Promise<void> {
    this.validationMessage.set('');
    const from = this.effectiveFrom.trim();
    const to = this.effectiveTo.trim() || null;
    const ownership = percentagePoints(this.ownershipPercent);
    const economic = percentagePoints(this.economicInterestPercent);
    if (!this.workspace.data()?.canAddMember || this.cmd.busy() || this.cmd.uncertain()) return;
    if (!this.clientId || !isDate(from) || to !== null && !isDate(to) ||
        to !== null && to < from || !this.controlMethod.trim() || this.controlMethod.trim().length > 100 ||
        ownership === null || economic === null || !this.evidenceReference.trim() || this.evidenceReference.trim().length > 2000) {
      this.validationMessage.set('Choose a same-firm legal entity, valid non-overlapping dates, percentages from 0 to 100 with at most six decimal places, a control method and evidence reference.');
      return;
    }
    const intent: MembershipIntent = {
      clientId: this.clientId, effectiveFrom: from, effectiveTo: to,
      controlMethod: this.controlMethod.trim(), ownershipPercent: ownership,
      economicInterestPercent: economic, evidenceReference: this.evidenceReference.trim(),
    };
    this.pendingMembership.set(intent);
    const accepted = await this.cmd.run(this.membersUrl(), intent, 'Membership approved and saved.', () => {
      this.pendingMembership.set(null);
      this.resetPageAndReload();
    });
    if (!accepted && !this.cmd.uncertain()) this.pendingMembership.set(null);
  }

  async recoverMembership(): Promise<void> {
    const intent = this.pendingMembership();
    if (!intent || !this.cmd.uncertain() || this.checkingRecovery()) return;
    this.checkingRecovery.set(true);
    this.recoveryError.set('');
    const filter = `?page=0&pageSize=25&clientId=${encodeURIComponent(intent.clientId)}&effectiveFrom=${encodeURIComponent(intent.effectiveFrom)}`;
    try {
      const saved = await this.api.get(this.membersUrl() + filter, decodeConsolidationGroupMembership);
      if (saved.members.some(row => sameIntent(row, intent))) {
        this.cmd.uncertain.set(false);
        this.cmd.failed.set(false);
        this.cmd.message.set('The exact submitted membership is present in the saved group history. It was not resent.');
        this.pendingMembership.set(null);
        this.resetPageAndReload();
      } else {
        this.recoveryError.set('No exact matching membership is visible yet. The command outcome remains unknown; do not submit it again. Refresh and review the group history before any further action.');
      }
    } catch (error) {
      this.recoveryError.set(error instanceof Error ? error.message : 'The saved membership could not be checked. The command remains fenced.');
    } finally {
      this.checkingRecovery.set(false);
    }
  }

  private membersUrl(): string { return `/api/ui/consolidation/groups/${this.groupId()}/members`; }
  private resetPageAndReload(): void {
    if (this.page() === 0) this.workspace.reload();
    else this.page.set(0);
  }
}
