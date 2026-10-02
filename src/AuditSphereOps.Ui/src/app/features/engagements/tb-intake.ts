import { Component, DestroyRef, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import {
  arr,
  dec,
  guid,
  instant,
  nat,
  nullable,
  obj,
  text,
} from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { IntakeCurrencyReview } from './currency-review';
import { TrialBalanceUpload } from './tb-upload';
export { decodeCurrencyReview } from './currency-review';

export const decodeIntake = obj({
  engagementId: guid,
  clientId: guid,
  datasets: arr(obj({ id: guid, periodCode: text, currency: text, importedAt: instant }), 50),
});
const allocation = obj({
  sourceAccountCode: text,
  destinationCode: text,
  statementSection: text,
  fraction: dec,
  rationale: text,
});
export const decodeMemory = obj({
  datasetId: guid,
  sourceMappingVersionId: nullable(guid),
  sourceMappingVersion: nullable(nat),
  proposals: arr(
    obj({
      accountCode: text,
      accountName: text,
      status: text,
      priorAccountName: nullable(text),
      allocations: arr(allocation, 100),
    }),
    20000,
  ),
});

@Component({
  selector: 'audit-tb-intake',
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    IntakeCurrencyReview,
    TrialBalanceUpload,
    ...SHARED,
  ],
  template: `
    <audit-page-header
      title="Trial balance intake"
      eyebrow="Engagement"
      description="Upload one Excel or CSV file with several periods, reuse approved mappings, and review currency conversion and movements before mapping approval."
    />
    <a [routerLink]="['/app/engagements', id()]">← Back to engagement</a>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="trial balance intake" />
    @if (ws.data(); as w) {
      <audit-tb-upload
        [engagementId]="w.engagementId"
        [clientId]="w.clientId"
        (imported)="refreshDatasets()"
      />
      <section class="panel" aria-labelledby="mm-heading">
        <h2 id="mm-heading">Mapping memory</h2>
        <label
          >Dataset
          <select [(ngModel)]="datasetId" name="dataset" (ngModelChange)="resetMemory()">
            <option value="">Select</option>
            @for (d of availableDatasets(); track d.id) {
              <option [value]="d.id">
                {{ d.periodCode }} · {{ d.currency }} ·
                {{ d.importedAt.slice(0, 16).replace('T', ' ') }}
              </option>
            }
          </select></label
        >
        <div class="inline-form">
          <button matButton="outlined" (click)="propose()" [disabled]="cmd.busy() || !datasetId">
            Propose mapping from history
          </button>
        </div>
        @if (readError()) {
          <p role="alert" class="error-text">{{ readError() }}</p>
        }
        @if (memory(); as m) {
          <p>
            {{
              m.sourceMappingVersion === null
                ? 'No approved mapping exists for this client yet; every account is new.'
                : 'Proposals from approved mapping v' + m.sourceMappingVersion + '.'
            }}
          </p>
          <div class="table-scroll">
            <table aria-label="Mapping proposals">
              <thead>
                <tr>
                  <th>Account</th>
                  <th>Name</th>
                  <th>Status</th>
                  <th>Proposed destination</th>
                </tr>
              </thead>
              <tbody>
                @for (p of m.proposals; track p.accountCode) {
                  <tr>
                    <td>{{ p.accountCode }}</td>
                    <td>
                      {{ p.accountName
                      }}{{
                        p.priorAccountName && p.status === 'NAME_CHANGED'
                          ? ' (was ' + p.priorAccountName + ')'
                          : ''
                      }}
                    </td>
                    <td><audit-status [value]="p.status" /></td>
                    <td>
                      @if (p.allocations.length) {
                        {{ describe(p.allocations) }}
                      } @else {
                        <input
                          [attr.aria-label]="'Destination for ' + p.accountCode"
                          placeholder="Destination"
                          [(ngModel)]="entry(p.accountCode).destination"
                          [name]="'d-' + p.accountCode"
                        />
                        <input
                          [attr.aria-label]="'Section for ' + p.accountCode"
                          placeholder="Section"
                          [(ngModel)]="entry(p.accountCode).section"
                          [name]="'s-' + p.accountCode"
                        />
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="inline-form">
            <label>Taxonomy version <input [(ngModel)]="taxonomy" name="taxonomy" /></label>
            <button matButton="filled" (click)="createDraft(m.datasetId)" [disabled]="cmd.busy()">
              Create draft mapping for approval
            </button>
          </div>
        }
      </section>
      <audit-intake-currency-review
        [datasetId]="datasetId"
        [clientId]="w.clientId"
        [engagementId]="w.engagementId"
      />
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class TrialBalanceIntake {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly id = routeGuid();
  readonly ws = this.api.resource(
    () => (this.id() ? `/api/ui/engagements/${this.id()}/tb-intake` : null),
    decodeIntake,
    'Trial balance intake requires an accounting or engagement assignment.',
  );
  readonly cmd = new CommandState(this.api);
  readonly memory = signal<ReturnType<typeof decodeMemory> | null>(null);
  readonly readError = signal('');
  private readonly upload = viewChild(TrialBalanceUpload);
  readonly availableDatasets = signal<ReturnType<typeof decodeIntake>['datasets']>([]);
  private datasetRequest = 0;
  private memoryRequest = 0;
  private destroyed = false;
  private readonly entries = new Map<string, { destination: string; section: string }>();
  datasetId = '';
  taxonomy = 'tax-v1';

  entry(code: string) {
    let e = this.entries.get(code);
    if (!e) this.entries.set(code, (e = { destination: '', section: '' }));
    return e;
  }
  describe(a: { destinationCode: string; statementSection: string }[]): string {
    return a.map((x) => `${x.destinationCode} (${x.statementSection})`).join(', ');
  }
  confirmNavigation() {
    return this.upload()?.confirmNavigation() ?? true;
  }
  resetMemory() {
    this.memoryRequest++;
    this.memory.set(null);
    this.entries.clear();
    this.readError.set('');
  }
  constructor() {
    effect(() => {
      this.id();
      this.session.invalidation();
      untracked(() => {
        this.datasetRequest++;
        this.resetMemory();
        this.datasetId = '';
        this.availableDatasets.set([]);
      });
    });
    effect(() => {
      const w = this.ws.data();
      untracked(() => this.availableDatasets.set(w?.datasets ?? []));
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.memoryRequest++;
      this.datasetRequest++;
    });
  }
  async refreshDatasets(): Promise<void> {
    const id = this.id(),
      context = this.ws.data(),
      request = ++this.datasetRequest;
    if (!id || !context) return;
    try {
      const w = await this.api.get(`/api/ui/engagements/${id}/tb-intake`, decodeIntake);
      if (this.destroyed || request !== this.datasetRequest || this.id() !== id || !this.ws.data())
        return;
      if (w.engagementId !== id || w.clientId !== context.clientId)
        throw new Error('Unsupported intake context.');
      this.availableDatasets.set(w.datasets);
    } catch (e) {
      if (!this.destroyed && request === this.datasetRequest)
        this.readError.set((e as Error).message);
    }
  }
  async propose(): Promise<void> {
    this.resetMemory();
    const selected = this.datasetId,
      request = this.memoryRequest;
    try {
      const result = await this.api.get(
        `/api/ui/datasets/${selected}/mapping-memory`,
        decodeMemory,
      );
      if (this.destroyed || request !== this.memoryRequest || this.datasetId !== selected) return;
      if (result.datasetId !== selected) throw new Error('Unsupported dataset context.');
      this.memory.set(result);
    } catch (e) {
      if (!this.destroyed && request === this.memoryRequest)
        this.readError.set((e as Error).message);
    }
  }
  createDraft(datasetId: string): void {
    const allocations = [...this.entries]
      .filter(([, v]) => v.destination.trim())
      .map(([accountCode, v]) => ({ accountCode, destination: v.destination, section: v.section }));
    this.cmd.run(
      `/api/ui/datasets/${datasetId}/draft-mapping`,
      { taxonomyVersion: this.taxonomy, allocations },
      'Draft mapping created; a reviewer must approve it before it becomes current.',
    );
  }
}
