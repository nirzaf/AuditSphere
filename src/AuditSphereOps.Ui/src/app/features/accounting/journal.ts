import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, dec, guid, instant, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

export const decodeJournal = obj({ id: guid, journalNumber: text, status: text, revision: nat, baseDatasetId: guid, createdAt: instant, createdByUserId: guid,
  lines: arr(obj({ accountCode: text, debit: dec, credit: dec }), 20000), totalDebit: dec, totalCredit: dec,
  managementDecision: nullable(text), managementEvidenceMode: nullable(text), managementJournalRevision: nullable(nat), canPost: bool, canExport: bool });

@Component({
  selector: 'audit-adjustment-journal',
  imports: [RouterLink, MatButtonModule, ...SHARED],
  template: `
    <a routerLink="/app/accounting/journals">← Adjustment journals</a>
    <audit-page-header title="Adjustment journal" eyebrow="Accounting record" description="Detailed adjustment journal lines, posting controls, and exported instruction audit trail." />
    <button matButton="outlined" (click)="journal.reload()">Refresh journal</button>
    <audit-state [loading]="journal.loading()" [error]="journal.error()" label="journal" />
    @if (journal.data(); as j) {
      <section class="panel" aria-labelledby="journal-heading">
        <p class="eyebrow">Status: <audit-status [value]="j.status" /> · Rev {{ j.revision }}</p>
        <h2 id="journal-heading">{{ j.journalNumber }}</h2>
        <dl class="facts"><dt>Journal ID</dt><dd><code>{{ j.id }}</code></dd><dt>Base dataset ID</dt><dd><code>{{ j.baseDatasetId }}</code></dd>
          <dt>Created</dt><dd>{{ j.createdAt.slice(0, 16).replace('T', ' ') }} UTC</dd><dt>Created by</dt><dd><code>{{ j.createdByUserId }}</code></dd></dl>
      </section>
      <p role="status">{{ j.lines.length }} lines · debit total {{ j.totalDebit | money }} · credit total {{ j.totalCredit | money }}</p>
      <section class="panel" aria-labelledby="lines-heading">
        <h2 id="lines-heading">Journal lines</h2>
        <div class="table-scroll"><table><caption class="sr-only">Adjustment journal lines</caption>
          <thead><tr><th scope="col">Account code</th><th scope="col" class="number">Debit</th><th scope="col" class="number">Credit</th></tr></thead>
          <tbody>@for (l of j.lines; track $index) { <tr><td><code>{{ l.accountCode }}</code></td><td class="number">{{ l.debit | money }}</td><td class="number">{{ l.credit | money }}</td></tr> }</tbody>
          <tfoot><tr><th>Total</th><td class="number"><strong>{{ j.totalDebit | money }}</strong></td><td class="number"><strong>{{ j.totalCredit | money }}</strong></td></tr></tfoot>
        </table></div>
      </section>
      @if (j.status === 'Draft') {
        <section class="panel" aria-labelledby="post-heading">
          <h2 id="post-heading">Review & post</h2>
          <p>Posting freezes the journal lines immutably. Separation of duties is enforced: a preparer cannot post their own journal.</p>
          @if (j.canPost) { <button matButton="filled" (click)="post(j.id)" [disabled]="cmd.busy()">Post adjustment journal</button> }
          @else { <p>You must have the AccountingReviewer, Partner or Manager role (and be a different user from the preparer) to post this journal.</p> }
        </section>
      }
      @if (j.status !== 'Void' && j.status !== 'ReflectedInSource') {
        <section class="panel" aria-labelledby="export-heading">
          <h2 id="export-heading">Controlled adjustment instructions</h2>
          <p>The export rechecks the exact source receipt, reporting context, journal revision and management evidence. It is an instruction file only and is not proof of external posting.</p>
          @if (j.managementDecision) { <p><small>Management evidence: {{ j.managementDecision }} ({{ j.managementEvidenceMode }}), revision {{ j.managementJournalRevision }}.</small></p> }
          @if (j.canExport) { <button matButton="filled" (click)="export(j.id)" [disabled]="exporting()">{{ exporting() ? 'Preparing export…' : 'Download adjustment instructions' }}</button> }
          @else { <p>Export is unavailable until accepted or partial management evidence is recorded for this journal revision.</p> }
          @if (exportStatus()) { <p role="status" aria-live="polite">{{ exportStatus() }}</p> }
        </section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class AdjustmentJournal {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly journal = this.api.resource(() => (this.id() ? `/api/ui/accounting/journals/${this.id()}` : null), decodeJournal,
    'The adjustment journal was not found in the current firm scope.');
  readonly cmd = new CommandState(this.api);
  readonly exporting = signal(false);
  readonly exportStatus = signal('');
  post(id: string): void { void this.cmd.run(`/api/ui/accounting/journals/${id}/post`, {}, 'Adjustment journal was posted and frozen.').finally(() => this.journal.reload()); }
  async export(id: string): Promise<void> {
    if (this.exporting()) return;
    this.exporting.set(true); this.exportStatus.set('Rechecking source, scope and management evidence…');
    try {
      const r = await this.api.download(`/api/ui/accounting/journals/${id}/instructions`);
      this.exportStatus.set(r.ok ? `Downloaded revision ${r.value.headers['x-journal-revision'] ?? ''}. This file is instruction-only; external posting evidence must be returned separately.`
        : `Export blocked: ${r.message}`);
    } finally { this.exporting.set(false); }
  }
}
