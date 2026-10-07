import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { PeriodLifecycle } from './lifecycle';
import { AccountingCharts } from './charts';
import { ClientOperationalJournals } from './operational-journals';
import { ClientCounterparties } from './counterparties';
import { ClientAccountRoles } from './account-roles';
import { SalesInvoiceDrafts } from './sales-invoice-drafts';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { exactDecimal, guidPattern } from '../../core/contracts';

interface ClientPage { items: { id: string; name: string; profileConfigured: boolean }[]; total: number; page: number; pageSize: number }
interface TaskOwnerSummary { hasAssignedEngagements: boolean; hasMoreOwners: boolean; owners: { owner: string; activeTaskCount: number }[] }
interface Profile { id: string; revision: string; jurisdiction: string; currency: string; fiscalMonth: number; fiscalDay: number; sourceSystem: string; sourceIdentifier: string; sourceMode: string; status: string }
interface Period { id: string; revision: string; code: string; start: string; end: string; basis: string; currency: string; status: string }
interface Book { id: string; periodId: string; revision: string; code: string; basis: string; inclusionRule: string; currency: string; status: string }
interface Amendment { id: string; periodId: string; previousRevision: string; revision: string; reason: string; actorId: string; recordedAt: string }
interface SourcePackage { id: string; hash: string; currency: string }
interface Opening { id: string; periodId: string; priorPeriodId: string | null; sourcePackageId: string | null; sourceHash: string; priorClosing: string; currentOpening: string; residual: string; status: string; evidence: string; approvedBy: string | null; approvedAt: string | null }
interface Workspace { clientId: string; name: string; profile: Profile | null; periods: Period[]; hasMorePeriods: boolean; books: Book[]; hasMoreBooks: boolean; amendments: Amendment[]; hasMoreAmendments: boolean; openingBridges: Opening[]; canReviewOpening: boolean }
export interface Dimension { id: string; dimensionType: string; code: string; name: string; status: string; revision: number; createdAt: string }
function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid response');
  return value as Record<string, unknown>;
}
export function decodeDimensions(value: unknown): Dimension[] {
  if (!Array.isArray(value) || value.length > 1001) throw new Error('Invalid dimensions');
  for (const d of value) {
    const item = record(d);
    if (typeof item['id'] !== 'string' || !guidPattern.test(item['id']) ||
      !['BRANCH', 'COST_CENTRE', 'DEPARTMENT', 'PROJECT', 'INTERCOMPANY_COUNTERPARTY'].includes(String(item['dimensionType'])) ||
      !['code', 'name', 'status', 'createdAt'].every(k => typeof item[k] === 'string') ||
      !String(item['code']).trim() || !String(item['name']).trim() ||
      !Number.isSafeInteger(item['revision']) || Number(item['revision']) < 1 ||
      Number.isNaN(Date.parse(String(item['createdAt'])))) {
      throw new Error('Invalid dimension');
    }
  }
  return value as Dimension[];
}
export function decodeSources(value: unknown, prior: Period): { items: SourcePackage[]; hasMore: boolean } {
  const v = record(value);
  if (v['periodId'] !== prior.id || v['revision'] !== prior.revision || typeof v['hasMore'] !== 'boolean' || !Array.isArray(v['items']) || v['items'].length > 100) throw new Error('Invalid source');
  for (const item of v['items']) { const p = record(item); if (typeof p['id'] !== 'string' || !guidPattern.test(p['id']) || typeof p['hash'] !== 'string' || !/^[a-f0-9]{64}$/.test(p['hash']) || p['currency'] !== prior.currency) throw new Error('Invalid source'); }
  return { items: v['items'] as SourcePackage[], hasMore: v['hasMore'] };
}
export function decodeClients(value: unknown): ClientPage {
  const v = record(value);
  if (!Array.isArray(v['items']) || !['total', 'page', 'pageSize'].every(k => Number.isSafeInteger(v[k]) && Number(v[k]) >= 0) ||
    Number(v['page']) > 10000 || Number(v['pageSize']) < 1 || Number(v['pageSize']) > 100 || v['items'].length > Number(v['pageSize'])) throw new Error('Invalid page');
  for (const item of v['items']) {
    const r = record(item);
    if (typeof r['id'] !== 'string' || !guidPattern.test(r['id']) || typeof r['name'] !== 'string' || typeof r['profileConfigured'] !== 'boolean') throw new Error('Invalid client');
  }
  return v as unknown as ClientPage;
}
function decodeTaskOwnerSummary(value: unknown): TaskOwnerSummary {
  const v = record(value);
  if (typeof v['hasAssignedEngagements'] !== 'boolean' || typeof v['hasMoreOwners'] !== 'boolean' ||
    !Array.isArray(v['owners']) || v['owners'].length > 100) throw new Error('Invalid task owner summary');
  for (const value of v['owners']) {
    const owner = record(value);
    if (typeof owner['owner'] !== 'string' || !owner['owner'].trim() || owner['owner'].length > 200 ||
      !Number.isSafeInteger(owner['activeTaskCount']) || Number(owner['activeTaskCount']) < 1 || Number(owner['activeTaskCount']) > 5000)
      throw new Error('Invalid task owner');
  }
  return v as unknown as TaskOwnerSummary;
}
export function decodeWorkspace(value: unknown): Workspace {
  const v = record(value);
  if (typeof v['clientId'] !== 'string' || !guidPattern.test(v['clientId']) || typeof v['name'] !== 'string' ||
    typeof v['canReviewOpening'] !== 'boolean' || !Array.isArray(v['openingBridges']) || v['openingBridges'].length > 100 || typeof v['hasMoreAmendments'] !== 'boolean' || !Array.isArray(v['amendments']) || v['amendments'].length > 100 || typeof v['hasMoreBooks'] !== 'boolean' || !Array.isArray(v['books']) || v['books'].length > 500 || typeof v['hasMorePeriods'] !== 'boolean' || !Array.isArray(v['periods']) || v['periods'].length > 100) throw new Error('Invalid workspace');
  const identity = (r: Record<string, unknown>) => typeof r['id'] === 'string' && guidPattern.test(r['id']) && typeof r['revision'] === 'string' && /^[1-9]\d{0,18}$/.test(r['revision']);
  if (v['profile'] !== null) {
    const p = record(v['profile']);
    if (!identity(p) || !['jurisdiction', 'currency', 'sourceSystem', 'sourceIdentifier', 'sourceMode', 'status'].every(k => typeof p[k] === 'string') ||
      !['EXTERNAL_SOURCE', 'NATIVE_BOOKKEEPING'].includes(String(p['sourceMode'])) ||
      !Number.isInteger(p['fiscalMonth']) || Number(p['fiscalMonth']) < 1 || Number(p['fiscalMonth']) > 12 ||
      !Number.isInteger(p['fiscalDay']) || Number(p['fiscalDay']) < 1 || Number(p['fiscalDay']) > 31) throw new Error('Invalid profile');
  }
  for (const item of v['periods']) {
    const p = record(item);
    if (!identity(p) || !['code', 'basis', 'currency', 'status'].every(k => typeof p[k] === 'string') ||
      !['start', 'end'].every(k => typeof p[k] === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(String(p[k])))) throw new Error('Invalid period');
  }
  for (const item of v['books'] as unknown[]) {
    const b = record(item);
    if (!identity(b) || typeof b['periodId'] !== 'string' || !guidPattern.test(b['periodId']) ||
      !(v['periods'] as Period[]).some(p => p.id === b['periodId']) ||
      !['code', 'basis', 'inclusionRule', 'currency', 'status'].every(k => typeof b[k] === 'string')) throw new Error('Invalid book');
  }
  for (const item of v['amendments'] as unknown[]) {
    const a = record(item);
    if (!identity(a) || typeof a['previousRevision'] !== 'string' || !/^[1-9]\d{0,18}$/.test(a['previousRevision']) ||
      typeof a['actorId'] !== 'string' || !guidPattern.test(a['actorId']) || typeof a['periodId'] !== 'string' ||
      !(v['periods'] as Period[]).some(p => p.id === a['periodId']) || typeof a['reason'] !== 'string' ||
      typeof a['recordedAt'] !== 'string' || !/^\d{4}-\d{2}-\d{2}T.*(?:Z|\+00:00)$/.test(a['recordedAt'])) throw new Error('Invalid amendment');
  }
  for (const item of v['openingBridges'] as unknown[]) {
    const b = record(item);
    if (typeof b['id'] !== 'string' || !guidPattern.test(b['id']) || typeof b['periodId'] !== 'string' || !(v['periods'] as Period[]).some(p => p.id === b['periodId']) ||
      !['priorClosing', 'currentOpening', 'residual'].every(k => exactDecimal(b[k])) || typeof b['sourceHash'] !== 'string' || !/^[a-f0-9]{64}$/.test(b['sourceHash']) ||
      !['status', 'evidence'].every(k => typeof b[k] === 'string') || !['priorPeriodId', 'sourcePackageId', 'approvedBy'].every(k => b[k] === null || (typeof b[k] === 'string' && guidPattern.test(String(b[k])))) ||
      !(b['approvedAt'] === null || typeof b['approvedAt'] === 'string')) throw new Error('Invalid opening bridge');
  }
  return v as unknown as Workspace;
}
@Component({
  selector: 'audit-accounting', imports: [AccountingCharts, SalesInvoiceDrafts, ClientAccountRoles, ClientCounterparties, ClientOperationalJournals, PeriodLifecycle, FormsModule, MatButtonModule, MatProgressBarModule],
  template: `
    <p class="eyebrow">Client-owned books · Explicit client scope</p><h1>Accounting workspace</h1>
    <p>Import-first preparation and reporting. Firm books and group consolidation remain separate.</p>
    <form (submit)="search($event, term.value)"><label for="accounting-search">Search client name</label>
      <input id="accounting-search" #term maxlength="100" /><button matButton type="submit">Search</button></form>
    @if (loading()) { <mat-progress-bar mode="indeterminate" aria-label="Loading accounting workspace" /> }
    @if (error()) { <p role="alert">{{ error() }}</p><button matButton (click)="load()">Retry client list</button> }
    @if (data(); as page) {
      <p role="status">{{ page.total }} clients in your accounting scope</p>
      @if (!page.items.length) { <p>No clients match this search.</p> }
      <div class="table-scroll"><table><caption>Authorized accounting clients</caption><thead><tr><th>Client</th><th>Profile</th></tr></thead>
        <tbody>@for (client of page.items; track client.id) { <tr><td><button matButton (click)="select(client.id)">{{ client.name }}</button></td>
          <td>{{ client.profileConfigured ? 'Configured' : 'Not configured' }}</td></tr> }</tbody></table></div>
      <nav aria-label="Accounting client pages"><button matButton [disabled]="page.page === 0" (click)="load(page.page - 1)">Previous</button>
        <span>Page {{ page.page + 1 }}</span><button matButton [disabled]="(page.page + 1) * page.pageSize >= page.total" (click)="load(page.page + 1)">Next</button></nav>
    }
    @if (taskOwnerLoading()) { <p role="status">Loading engagement task owners…</p> }
    @if (taskOwnerError()) { <p role="alert">{{ taskOwnerError() }}</p> }
    @if (taskOwnerSummary(); as summary) {
      @if (summary.hasAssignedEngagements) {
        <section aria-labelledby="accounting-task-owners">
          <h2 id="accounting-task-owners">Task owners in your assigned engagements</h2>
          <p>This summary follows your current engagement assignments. It does not grant access to client-wide books or period setup.</p>
          @if (!summary.owners.length) { <p>No active assigned tasks are available in your engagement scope.</p> }
          @else {
            <div class="table-scroll"><table><caption>Active task counts by assigned staff owner</caption>
              <thead><tr><th scope="col">Staff owner</th><th scope="col">Active tasks</th></tr></thead>
              <tbody>@for (owner of summary.owners; track $index) { <tr><td>{{ owner.owner }}</td><td>{{ owner.activeTaskCount }}</td></tr> }</tbody>
            </table></div>
            @if (summary.hasMoreOwners) { <p>Showing the first 100 task owners in your scope.</p> }
          }
        </section>
      }
    }
    @if (workspace(); as w) {
      <section aria-label="Selected client accounting"><h2>{{ w.name }}</h2>
        @defer (on interaction(chartTrigger)) { <audit-accounting-charts [clientId]="w.clientId" /> }
        @placeholder { <button matButton #chartTrigger type="button">Open chart of accounts</button> }
        @if (w.profile; as p) { <h3>Accounting profile</h3><dl><dt>Jurisdiction</dt><dd>{{ p.jurisdiction }}</dd>
          <dt>Functional currency</dt><dd>{{ p.currency }}</dd><dt>Fiscal year start</dt><dd>{{ p.fiscalMonth }}/{{ p.fiscalDay }}</dd>
          <dt>Book source mode</dt><dd>{{ p.sourceMode === 'NATIVE_BOOKKEEPING' ? 'Native client bookkeeping' : 'External source' }}</dd>
          <dt>Source system</dt><dd>{{ p.sourceSystem }}</dd><dt>Source identifier</dt><dd>{{ p.sourceIdentifier }}</dd>
          <dt>Status / revision</dt><dd>{{ p.status }} / {{ p.revision }}</dd></dl>
        } @else { <p>Accounting profile is not configured.</p> }
        <form #profileForm="ngForm" (ngSubmit)="profileForm.valid && saveProfile()"><h3>{{ w.profile ? 'Revise' : 'Create' }} accounting profile</h3>
          <label>Jurisdiction <input name="jurisdiction" [(ngModel)]="form.jurisdiction" required maxlength="100" /></label>
          <label>Functional currency <input name="currency" [(ngModel)]="form.currency" required pattern="[A-Z]{3}" maxlength="3" /></label>
          <label>Fiscal start month <input name="month" type="number" [(ngModel)]="form.month" required min="1" max="12" /></label>
          <label>Fiscal start day <input name="day" type="number" [(ngModel)]="form.day" required min="1" max="31" /></label>
          <label>Source system <input name="source" [(ngModel)]="form.source" required maxlength="100" /></label>
          <label>Source identifier <input name="identifier" [(ngModel)]="form.identifier" maxlength="200" /></label>
          <label>Book source mode <select name="sourceMode" [(ngModel)]="form.sourceMode" [disabled]="w.profile?.sourceMode === 'NATIVE_BOOKKEEPING'">
            <option value="EXTERNAL_SOURCE">External source</option><option value="NATIVE_BOOKKEEPING">Native client bookkeeping</option></select></label>
          <p>Native mode requires an accepted client-level BOOKKEEPING service decision. Once reporting periods exist, changing currency, fiscal calendar, source identity, jurisdiction or source mode requires a reviewed cutover. VAT/tax and ancillary modules remain optional.</p>
          <label><input name="reviewed" type="checkbox" [(ngModel)]="reviewed" /> I reviewed this client profile and revision {{ w.profile?.revision ?? '0' }}.</label>
          <button matButton type="submit" [disabled]="profileForm.invalid || !reviewed || saving() || uncertain()">Save profile</button>
          @if (uncertain()) { <p role="alert">The outcome is unconfirmed. Refresh the client and review persisted setup before another change.</p> }
        </form>
        @if (w.profile?.sourceMode === 'NATIVE_BOOKKEEPING') {
          <audit-client-counterparties [clientId]="w.clientId" />
          <audit-account-roles [clientId]="w.clientId" />
    <audit-sales-invoice-drafts [clientId]="w.clientId" [currency]="w.profile.currency" [periods]="w.periods" />
          <audit-client-operational-journals [clientId]="w.clientId" [periods]="w.periods" [bookCurrency]="w.profile.currency" />
        }
        <h3>Reporting periods</h3>@if (!w.periods.length) { <p>No reporting periods configured.</p> }
        <form #periodForm="ngForm" (ngSubmit)="periodForm.valid && createPeriod()"><h4>Create reporting period</h4>
          <label>Period code <input name="periodCode" [(ngModel)]="period.code" required maxlength="100" /></label>
          <label>Start date <input name="periodStart" type="date" [(ngModel)]="period.start" required /></label>
          <label>End date <input name="periodEnd" type="date" [(ngModel)]="period.end" required /></label>
          <label>Reporting basis <input name="periodBasis" [(ngModel)]="period.basis" required maxlength="100" /></label>
          <label>Reporting currency <input name="periodCurrency" [(ngModel)]="period.currency" required pattern="[A-Z]{3}" maxlength="3" /></label>
          <label>Prior period <select name="priorPeriod" [(ngModel)]="period.prior"><option value="">None</option>
            @for (p of w.periods; track p.id) { <option [value]="p.id">{{ p.code }} · {{ p.basis }} · {{ p.end }}</option> }</select></label>
          <p>This creates period setup only. Opening balances require their separate evidence and review workflow.</p>
          <label><input name="periodReviewed" type="checkbox" [(ngModel)]="periodReviewed" /> I reviewed this client, dates, basis, currency and prior period.</label>
          <button matButton type="submit" [disabled]="periodForm.invalid || !periodReviewed || saving() || uncertain()">Create period</button>
        </form>
        <div class="table-scroll"><table><caption>Latest reporting periods</caption><thead><tr><th>Period</th><th>Dates</th><th>Basis</th><th>Currency</th><th>Status / revision</th></tr></thead>
          <tbody>@for (p of w.periods; track p.id) { <tr><td>{{ p.code }}</td><td>{{ p.start }} – {{ p.end }}</td><td>{{ p.basis }}</td><td>{{ p.currency }}</td><td>{{ p.status }} / {{ p.revision }}</td></tr> }</tbody></table></div>
        <h3>Period decisions</h3>
        @for (p of w.periods; track p.id) { <button matButton (click)="decisionPeriod.set(p.id)">{{ p.code }} · close/reopen</button>
          @if (decisionPeriod() === p.id) { <audit-period-lifecycle [clientId]="w.clientId" [periodId]="p.id" [revision]="p.revision" [code]="p.code" (changed)="select(w.clientId)" /> }
        }
        <form #rollForm="ngForm" (ngSubmit)="rollForm.valid && rollforward()"><h3>Roll forward closed period</h3>
          <label for="roll-prior">Closed prior period</label><select id="roll-prior" name="rollPrior" [(ngModel)]="roll.prior" (ngModelChange)="clearSources()" required><option value="">Choose period</option>
            @for (p of w.periods; track p.id) { @if (p.status === 'CLOSED') { <option [value]="p.id">{{ p.code }} · {{ p.basis }} · revision {{ p.revision }}</option> } }</select>
          <button matButton type="button" [disabled]="!roll.prior || saving()" (click)="loadSources()">Load validated source packages</button>
          @if (sources(); as result) { <label for="roll-package">Validated source package</label>
            <select id="roll-package" name="rollPackage" [(ngModel)]="selectedSource" (ngModelChange)="chooseSource()"><option value="">External source evidence</option>
              @for (p of result; track p.id) { <option [value]="p.id">{{ p.id }} · {{ p.currency }}</option> }</select>
            @if (!result.length) { <p>No validated packages match this closed prior period and currency.</p> }
            @if (moreSources()) { <p>Showing the first 100 matching packages.</p> }
          }
          <label>New period code <input name="rollCode" [(ngModel)]="roll.code" required maxlength="100" /></label>
          <label>Next start date <input name="rollStart" type="date" [(ngModel)]="roll.start" required /></label>
          <label>Next end date <input name="rollEnd" type="date" [(ngModel)]="roll.end" required /></label>
          <label>Next reporting basis <input name="rollBasis" [(ngModel)]="roll.basis" required maxlength="100" /></label>
          <label>Next reporting currency <input name="rollCurrency" [(ngModel)]="roll.currency" required pattern="[A-Z]{3}" maxlength="3" /></label>
          <label>Prior closing amount <input name="rollClosing" [(ngModel)]="roll.closing" required pattern="-?[0-9]+(\\.[0-9]{1,2})?" inputmode="decimal" /></label>
          <label>Current opening amount <input name="rollOpening" [(ngModel)]="roll.opening" required pattern="-?[0-9]+(\\.[0-9]{1,2})?" inputmode="decimal" /></label>
          <label>Source SHA-256 <input name="rollHash" [(ngModel)]="roll.hash" required pattern="[a-fA-F0-9]{64}" maxlength="64" /></label>
          <label>Opening evidence reference <input name="rollEvidence" [(ngModel)]="roll.evidence" required maxlength="2000" /></label>
          <p>Choose a validated source package or provide external source evidence. Draft books are copied; approvals are not. Opening-balance review remains separate.</p>
          <label><input name="rollReviewed" type="checkbox" [(ngModel)]="rollReviewed" /> I reviewed the closed prior period, opening amounts and exact source evidence.</label>
          <button matButton type="submit" [disabled]="rollForm.invalid || !rollReviewed || saving() || uncertain()">Create next draft period</button>
        </form>
        <h3>Opening-balance evidence</h3>
        @if (!w.openingBridges.length) { <p>No opening bridges for displayed periods.</p> }
        @for (b of w.openingBridges; track b.id) { <section><h4>Opening bridge · {{ b.periodId }}</h4>
          <dl><dt>Prior closing / current opening / residual</dt><dd>{{ b.priorClosing }} / {{ b.currentOpening }} / {{ b.residual }}</dd>
            <dt>Source SHA-256</dt><dd>{{ b.sourceHash }}</dd><dt>Evidence</dt><dd>{{ b.evidence }}</dd><dt>Status</dt><dd>{{ b.status }}</dd>
            <dt>Approved by / UTC</dt><dd>{{ b.approvedBy ?? 'Not approved' }} / {{ b.approvedAt ?? 'Not approved' }}</dd></dl>
          @if (b.status === 'RECONCILED' && w.canReviewOpening) {
            <label><input type="checkbox" [checked]="openingReviewed === b.id" (change)="openingReviewed = $any($event.target).checked ? b.id : ''" /> I reviewed this opening bridge and exact source evidence.</label>
            <button matButton [disabled]="openingReviewed !== b.id || saving() || uncertain()" (click)="approveOpening(b)">Approve opening bridge</button>
          }
        </section> }
        <h3>Period amendment history</h3>
        @if (!w.amendments.length) { <p>No reopen amendments recorded for displayed periods.</p> }
        <div class="table-scroll"><table><caption>Immutable period amendments</caption><thead><tr><th>Period</th><th>Revision</th><th>Reason</th><th>Actor identity</th><th>Recorded UTC</th></tr></thead>
          <tbody>@for (a of w.amendments; track a.id) { <tr><td>{{ a.periodId }}</td><td>{{ a.previousRevision }} → {{ a.revision }}</td><td>{{ a.reason }}</td><td>{{ a.actorId }}</td><td>{{ a.recordedAt }}</td></tr> }</tbody></table></div>
        @if (w.hasMoreAmendments) { <p>Showing the latest 100 amendments for displayed periods.</p> }
        <h3>Reporting books</h3>
        @if (!w.books.length) { <p>No reporting books configured for these periods.</p> }
        <div class="table-scroll"><table><caption>Books for displayed periods</caption><thead><tr><th>Code</th><th>Period identity</th><th>Basis / inclusion</th><th>Currency</th><th>Status / revision</th></tr></thead>
          <tbody>@for (b of w.books; track b.id) { <tr><td>{{ b.code }}</td><td>{{ b.periodId }}</td><td>{{ b.basis }} / {{ b.inclusionRule }}</td><td>{{ b.currency }}</td><td>{{ b.status }} / {{ b.revision }}</td></tr> }</tbody></table></div>
        @if (w.hasMoreBooks) { <p>Showing the first 500 books for displayed periods.</p> }
        <form #bookForm="ngForm" (ngSubmit)="bookForm.valid && createBook()"><h4>Create reporting book</h4>
          <label for="book-period">Period</label><select id="book-period" name="bookPeriod" [(ngModel)]="book.periodId" required><option value="">Choose period</option>
            @for (p of w.periods; track p.id) { <option [value]="p.id" [disabled]="p.status === 'CLOSED'">{{ p.code }} · {{ p.basis }} · {{ p.status }}</option> }</select>
          <label>Book code <input name="bookCode" [(ngModel)]="book.code" required maxlength="100" /></label>
          <label>Basis <input name="bookBasis" [(ngModel)]="book.basis" required maxlength="100" /></label>
          <label>Inclusion rule <input name="bookInclusion" [(ngModel)]="book.inclusionRule" required maxlength="200" /></label>
          <label>Currency <input name="bookCurrency" [(ngModel)]="book.currency" required pattern="[A-Z]{3}" maxlength="3" /></label>
          <label><input name="bookReviewed" type="checkbox" [(ngModel)]="bookReviewed" /> I reviewed the client, period and book configuration.</label>
          <button matButton type="submit" [disabled]="bookForm.invalid || !bookReviewed || saving() || uncertain()">Create book</button>
        </form>
        @if (w.hasMorePeriods) { <p>Showing the latest 100 periods. Older periods are not included in this view.</p> }
        <h3>Accounting dimensions</h3>
        @if (dimensionsLoading()) { <p role="status">Loading accounting dimensions…</p> }
        @if (dimensionsError()) { <p role="alert">{{ dimensionsError() }}</p><button matButton type="button" (click)="loadDimensions(w.clientId, session.invalidation())">Retry dimensions</button> }
        @if (dimensions(); as dimList) {
          <div class="table-scroll"><table><caption>Client accounting dimension definitions</caption><thead><tr><th>Type</th><th>Code</th><th>Name</th><th>Status</th><th>Revision</th></tr></thead>
            <tbody>@for (d of dimList; track d.id) { <tr><td>{{ d.dimensionType }}</td><td>{{ d.code }}</td><td>{{ d.name }}</td><td>{{ d.status }}</td><td>{{ d.revision }}</td></tr> }</tbody></table></div>
          @if (!dimList.length) { <p>No accounting dimensions configured for this client.</p> }
          @if (dimensionsTruncated()) { <p>Showing the first 1,000 accounting dimensions for this client.</p> }
        }
        <form #dimForm="ngForm" (ngSubmit)="dimForm.valid && addDimension()"><h4>Add accounting dimension</h4>
          <label for="dim-type">Dimension type</label>
          <select id="dim-type" name="dimType" [(ngModel)]="dim.type" (ngModelChange)="dimReviewed = false" required>
            <option value="BRANCH">Branch</option>
            <option value="COST_CENTRE">Cost centre</option>
            <option value="DEPARTMENT">Department</option>
            <option value="PROJECT">Project</option>
            <option value="INTERCOMPANY_COUNTERPARTY">Intercompany counterparty</option>
          </select>
          <label>Code <input name="dimCode" [(ngModel)]="dim.code" (ngModelChange)="dimReviewed = false" required maxlength="100" /></label>
          <label>Name <input name="dimName" [(ngModel)]="dim.name" (ngModelChange)="dimReviewed = false" required maxlength="300" /></label>
          <label><input name="dimReviewed" type="checkbox" [(ngModel)]="dimReviewed" /> I reviewed this dimension definition for this client.</label>
          <button matButton type="submit" [disabled]="dimForm.invalid || dimensions() === null || dimensionsLoading() || !dimReviewed || saving() || uncertain()">Add dimension</button>
        </form>
      </section>
    }
  `,
})
export class AccountingWorkspace {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private taskOwnerRead?: Subscription;
  private detail?: Subscription;
  private write?: Subscription;
  private dimRead?: Subscription;
  readonly saving = signal(false);
  readonly decisionPeriod = signal('');
  readonly uncertain = signal(false);
  reviewed = false;
  readonly sources = signal<SourcePackage[] | null>(null);
  readonly moreSources = signal(false);
  selectedSource = '';
  private sourceRead?: Subscription;
  openingReviewed = '';
  rollReviewed = false;
  roll = { prior: '', code: '', start: '', end: '', basis: '', currency: '', closing: '', opening: '', hash: '', evidence: '' };
  bookReviewed = false;
  book = { periodId: '', code: '', basis: '', inclusionRule: '', currency: '' };
  periodReviewed = false;
  period = { code: '', start: '', end: '', basis: '', currency: '', prior: '' };
  form = { jurisdiction: '', currency: '', month: 1, day: 1, source: '', identifier: '', sourceMode: 'EXTERNAL_SOURCE' };
  dimReviewed = false;
  dim = { type: 'BRANCH', code: '', name: '' };
  readonly dimensions = signal<Dimension[] | null>(null);
  readonly dimensionsLoading = signal(false);
  readonly dimensionsError = signal('');
  readonly dimensionsTruncated = signal(false);
  private query = '';
  readonly data = signal<ClientPage | null>(null);
  readonly taskOwnerSummary = signal<TaskOwnerSummary | null>(null);
  readonly taskOwnerLoading = signal(false);
  readonly taskOwnerError = signal('');
  readonly workspace = signal<Workspace | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  constructor() {
    effect(() => {
      this.session.invalidation(); const staff = this.session.current()?.staff;
      untracked(() => { this.request?.unsubscribe(); this.taskOwnerRead?.unsubscribe(); this.detail?.unsubscribe(); this.write?.unsubscribe(); this.dimRead?.unsubscribe(); this.sourceRead?.unsubscribe(); this.taskOwnerSummary.set(null); this.taskOwnerLoading.set(false); this.taskOwnerError.set(''); this.sources.set(null); this.dimensions.set(null); this.dimensionsLoading.set(false); this.dimensionsError.set(''); this.dimensionsTruncated.set(false); this.selectedSource = ''; this.saving.set(false); this.reviewed = false; this.openingReviewed = ''; this.rollReviewed = false; this.dimReviewed = false; this.roll = { prior: '', code: '', start: '', end: '', basis: '', currency: '', closing: '', opening: '', hash: '', evidence: '' }; this.bookReviewed = false; this.book = { periodId: '', code: '', basis: '', inclusionRule: '', currency: '' }; this.periodReviewed = false; this.period = { code: '', start: '', end: '', basis: '', currency: '', prior: '' }; this.dim = { type: 'BRANCH', code: '', name: '' }; this.form = { jurisdiction: '', currency: '', month: 1, day: 1, source: '', identifier: '', sourceMode: 'EXTERNAL_SOURCE' }; this.data.set(null); this.workspace.set(null); this.clearSources(); this.decisionPeriod.set(''); this.error.set(''); this.loading.set(false); if (staff) this.load(); });
    });
    inject(DestroyRef).onDestroy(() => { this.request?.unsubscribe(); this.taskOwnerRead?.unsubscribe(); this.detail?.unsubscribe(); this.write?.unsubscribe(); this.dimRead?.unsubscribe(); this.sourceRead?.unsubscribe(); this.sources.set(null); this.taskOwnerSummary.set(null); this.dimensions.set(null); this.selectedSource = ''; });
  }
  search(event: Event, value: string): void { event.preventDefault(); this.query = value.trim(); this.load(); }
  load(page = 0): void {
    if (this.saving()) return;
    this.request?.unsubscribe(); this.detail?.unsubscribe(); this.data.set(null); this.workspace.set(null); this.clearSources(); this.decisionPeriod.set(''); this.error.set(''); this.loading.set(true);
    const generation = this.session.invalidation();
    this.loadTaskOwners(generation);
    this.request = this.http.get<unknown>('/api/ui/accounting/clients', { params: { search: this.query, page, pageSize: 25 } }).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; try { this.data.set(decodeClients(value)); } catch { this.error.set('Unsupported accounting response.'); } this.loading.set(false); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.loading.set(false); this.error.set(failure.status === 403
        ? 'Client-wide accounting setup requires a current client-level accounting grant.'
        : 'Accounting unavailable. Check your current client assignment or retry.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  private loadTaskOwners(generation: number): void {
    this.taskOwnerRead?.unsubscribe(); this.taskOwnerSummary.set(null); this.taskOwnerError.set(''); this.taskOwnerLoading.set(true);
    this.taskOwnerRead = this.http.get<unknown>('/api/ui/accounting/task-owners').pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return;
        try { this.taskOwnerSummary.set(decodeTaskOwnerSummary(value)); }
        catch { this.taskOwnerError.set('The engagement task summary returned an unsupported response.'); }
        this.taskOwnerLoading.set(false); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.taskOwnerLoading.set(false);
        if (failure.status === 401) { this.session.clear(); return; }
        if (failure.status !== 403) this.taskOwnerError.set('Assigned engagement task owners are temporarily unavailable. Retry the workspace.');
      },
    });
  }
  select(id: string): void {
    if (this.saving()) return;
    if (!this.data()?.items.some(c => c.id === id)) return;
    this.openingReviewed = ''; this.rollReviewed = false; this.roll = { prior: '', code: '', start: '', end: '', basis: '', currency: '', closing: '', opening: '', hash: '', evidence: '' }; this.bookReviewed = false; this.book = { periodId: '', code: '', basis: '', inclusionRule: '', currency: '' }; this.periodReviewed = false;
    this.period = { code: '', start: '', end: '', basis: '', currency: '', prior: '' };
    this.dimReviewed = false; this.dim = { type: 'BRANCH', code: '', name: '' };
    this.detail?.unsubscribe(); this.dimRead?.unsubscribe(); this.workspace.set(null); this.dimensions.set(null); this.clearSources(); this.decisionPeriod.set(''); this.error.set(''); this.loading.set(true);
    const generation = this.session.invalidation();
    this.detail = this.http.get<unknown>('/api/ui/accounting/clients/' + id).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; try { const w = decodeWorkspace(value); if (w.clientId !== id) throw new Error('Identity mismatch'); this.workspace.set(w); const p = w.profile; this.form = { jurisdiction: p?.jurisdiction ?? '', currency: p?.currency ?? '', month: p?.fiscalMonth ?? 1, day: p?.fiscalDay ?? 1, source: p?.sourceSystem ?? '', identifier: p?.sourceIdentifier ?? '', sourceMode: p?.sourceMode ?? 'EXTERNAL_SOURCE' }; this.reviewed = false; this.uncertain.set(false); this.loadDimensions(id, generation); } catch { this.error.set('Unsupported accounting response.'); } this.loading.set(false); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.loading.set(false); this.error.set('Client accounting unavailable. Refresh your assignments.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  loadDimensions(id: string, generation: number): void {
    this.dimRead?.unsubscribe();
    this.dimensions.set(null); this.dimensionsError.set(''); this.dimensionsLoading.set(true); this.dimensionsTruncated.set(false);
    this.dimRead = this.http.get<unknown>('/api/ui/accounting/clients/' + id + '/dimensions').pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || id !== this.workspace()?.clientId) return;
        try { const result = decodeDimensions(value); this.dimensionsTruncated.set(result.length > 1000); this.dimensions.set(result.slice(0, 1000)); }
        catch { this.dimensionsError.set('Accounting dimensions returned an unsupported response. Refresh before continuing.'); }
        this.dimensionsLoading.set(false); },
      error: () => { if (generation !== this.session.invalidation() || id !== this.workspace()?.clientId) return;
        this.dimensionsLoading.set(false); this.dimensionsError.set('Accounting dimensions are unavailable. Retry before continuing.'); },
    });
  }
  addDimension(): void {
    const w = this.workspace();
    if (!w || this.dimensions() === null || !this.dimReviewed || this.saving() || this.uncertain()) return;
    const generation = this.session.invalidation();
    this.saving.set(true); this.error.set('');
    this.write = this.http.post('/api/ui/accounting/clients/' + w.clientId + '/dimensions', {
      dimensions: [{ dimensionType: this.dim.type, code: this.dim.code.trim(), name: this.dim.name.trim() }],
      reviewed: true,
    }).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.dimReviewed = false; this.dim = { type: 'BRANCH', code: '', name: '' }; this.loadDimensions(w.clientId, generation); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.dimReviewed = false; this.uncertain.set(true); this.error.set('Dimension not confirmed. Refresh the client before another change.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  saveProfile(): void {
    const w = this.workspace();
    if (!w || !this.reviewed || this.saving() || this.uncertain()) return;
    const generation = this.session.invalidation();
    this.saving.set(true); this.error.set('');
    this.write = this.http.post('/api/ui/accounting/clients/' + w.clientId + '/profile', {
      profileId: w.profile?.id ?? null, revision: w.profile?.revision ?? '0', jurisdiction: this.form.jurisdiction,
      currency: this.form.currency, fiscalMonth: this.form.month, fiscalDay: this.form.day,
      sourceSystem: this.form.source, sourceIdentifier: this.form.identifier, sourceMode: this.form.sourceMode, reviewed: true,
    }).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.select(w.clientId); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.reviewed = false;
        this.uncertain.set(true); this.error.set('Profile save was not confirmed. Select the client again to refresh persisted state.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  createPeriod(): void {
    const w = this.workspace();
    if (!w || !this.periodReviewed || this.saving() || this.uncertain()) return;
    if (this.period.prior && !w.periods.some(p => p.id === this.period.prior)) return;
    const generation = this.session.invalidation();
    this.saving.set(true); this.error.set('');
    this.write = this.http.post('/api/ui/accounting/clients/' + w.clientId + '/periods', {
      code: this.period.code, start: this.period.start, end: this.period.end, basis: this.period.basis,
      currency: this.period.currency, priorPeriodId: this.period.prior || null, reviewed: true,
    }).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.select(w.clientId); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.openingReviewed = ''; this.rollReviewed = false; this.roll = { prior: '', code: '', start: '', end: '', basis: '', currency: '', closing: '', opening: '', hash: '', evidence: '' }; this.bookReviewed = false; this.book = { periodId: '', code: '', basis: '', inclusionRule: '', currency: '' }; this.periodReviewed = false;
        this.uncertain.set(true); this.error.set('Period creation was not confirmed. Select the client again to inspect persisted periods.'); if (failure.status === 401) this.session.clear(); },
    });
  }
  createBook(): void {
    const w = this.workspace();
    if (!w || !this.bookReviewed || this.saving() || this.uncertain() || !w.periods.some(p => p.id === this.book.periodId && p.status !== 'CLOSED')) return;
    const generation = this.session.invalidation();
    this.saving.set(true); this.error.set('');
    this.write = this.http.post('/api/ui/accounting/clients/' + w.clientId + '/books', { ...this.book, reviewed: true }).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.select(w.clientId); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.openingReviewed = ''; this.rollReviewed = false; this.roll = { prior: '', code: '', start: '', end: '', basis: '', currency: '', closing: '', opening: '', hash: '', evidence: '' }; this.bookReviewed = false; this.uncertain.set(true);
        this.error.set('Book creation was not confirmed. Select the client again to inspect persisted books.'); if (failure.status === 401) this.session.clear(); },
    });
  }

  rollforward(): void {
    const w = this.workspace(); const prior = w?.periods.find(p => p.id === this.roll.prior && p.status === 'CLOSED');
    if (!w || !prior || !this.rollReviewed || this.saving() || this.uncertain()) return;
    if (this.selectedSource && !this.sources()?.some(p => p.id === this.selectedSource && p.hash === this.roll.hash)) return;
    const generation = this.session.invalidation(); this.saving.set(true); this.error.set('');
    this.write = this.http.post('/api/ui/accounting/clients/' + w.clientId + '/rollforward', {
      priorPeriodId: prior.id, revision: prior.revision, code: this.roll.code, start: this.roll.start, end: this.roll.end,
      basis: this.roll.basis, currency: this.roll.currency, sourceHash: this.roll.hash, priorClosing: this.roll.closing,
      currentOpening: this.roll.opening, evidence: this.roll.evidence, sourcePackageId: this.selectedSource || null, reviewed: true,
    }).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.select(w.clientId); },
      error: failure => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.openingReviewed = ''; this.rollReviewed = false; this.uncertain.set(true);
        this.error.set('Roll-forward was not confirmed. Refresh persisted periods and opening evidence before another attempt.'); if (failure.status === 401) this.session.clear(); },
    });
  }

  approveOpening(bridge: Opening): void {
    const w = this.workspace();
    if (!w?.canReviewOpening || !w.openingBridges.some(b => b.id === bridge.id) || this.openingReviewed !== bridge.id || this.saving() || this.uncertain()) return;
    const generation = this.session.invalidation(); this.saving.set(true);
    this.write = this.http.post('/api/ui/accounting/clients/' + w.clientId + '/opening-bridges/' + bridge.id + '/approve', { sourceHash: bridge.sourceHash, reviewed: true }).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.select(w.clientId); },
      error: () => { if (generation !== this.session.invalidation()) return; this.saving.set(false); this.openingReviewed = ''; this.uncertain.set(true); this.error.set('Opening approval not confirmed. Refresh persisted evidence before another decision.'); },
    });
  }

  clearSources(): void { this.sourceRead?.unsubscribe(); this.sources.set(null); this.moreSources.set(false); this.selectedSource = ''; this.rollReviewed = false; }
  chooseSource(): void { const p = this.sources()?.find(p => p.id === this.selectedSource); if (p) { this.roll.hash = p.hash; this.roll.currency = p.currency; } this.rollReviewed = false; }
  loadSources(): void {
    const w = this.workspace(); const prior = w?.periods.find(p => p.id === this.roll.prior && p.status === 'CLOSED');
    if (!w || !prior || this.saving()) return;
    this.clearSources(); const generation = this.session.invalidation();
    this.sourceRead = this.http.get<unknown>('/api/ui/accounting/clients/' + w.clientId + '/periods/' + prior.id + '/sources').pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || this.roll.prior !== prior.id || this.workspace()?.clientId !== w.clientId) return;
        try { const v = decodeSources(value, prior); this.sources.set(v.items); this.moreSources.set(v.hasMore);
        } catch { this.error.set('Source packages unavailable or prior revision changed. Refresh client setup.'); }
      },
      error: () => { if (generation === this.session.invalidation()) this.error.set('Source packages unavailable. Retry or refresh the prior period.'); },
    });
  }

}
