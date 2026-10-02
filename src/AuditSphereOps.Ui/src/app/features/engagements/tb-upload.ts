import {
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { disabled, form, FormField } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, UNKNOWN_OUTCOME } from '../../core/api';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import {
  decodeUploadReview,
  uploadCheckpoint,
  UploadCheckpoint,
  UploadReview,
} from './tb-upload-contracts';

@Component({
  selector: 'audit-tb-upload',
  imports: [FormField, RouterLink, MatButtonModule, ...SHARED],
  template: `
    <section class="panel" aria-labelledby="upload-heading">
      <h2 id="upload-heading">Multi-period upload</h2>
      <p>Select → server preview → review → import → observe validation.</p>
      <p>
        Columns: PeriodCode, AccountCode, AccountName, NetClosingBalance (or Debit and Credit),
        Currency, Entity, MappingCode. Every period must pass preview before a new import starts.
        Each period has its own transaction and background validation; an interrupted batch may
        contain earlier successful periods.
      </p>
      <p>
        Transport limit: 25 MiB. CSV parser limit: 10,000,000 bytes. XLSX parsing remains bounded by
        the server.
      </p>
      <label
        >Trial balance file
        <input type="file" accept=".xlsx,.csv" (change)="pick($event)" [disabled]="busy()"
      /></label>
      @if (fileName()) {
        <p class="identity">Selected file: {{ fileName() }}</p>
      }
      @if (busy()) {
        <p role="status">Loading the current upload state…</p>
      }
      @if (message()) {
        <p role="status">{{ message() }}</p>
      }
      @if (hasFile() && !review() && !uncertain()) {
        <button matButton (click)="reconcile()" [disabled]="busy()">Refresh period receipts</button>
      }
      @if (uncertain()) {
        <p role="alert">
          The import outcome is unconfirmed. No automatic retry is sent. Reselect the same file if
          necessary and inspect its persisted period receipts.
        </p>
        <button matButton (click)="reconcile()" [disabled]="busy() || !hasFile()">
          Read persisted upload receipts
        </button>
        @if (reconciled()) {
          <button matButton (click)="acknowledge()">I reviewed the persisted receipts</button>
        }
      }
      @if (review(); as p) {
        <p class="identity">
          Client <code>{{ p.clientId }}</code> · Engagement <code>{{ p.engagementId }}</code>
        </p>
        <p class="identity">
          Original file SHA-256 <code>{{ p.fileSha256 }}</code>
        </p>
        <div class="table-scroll">
          <table aria-label="Periods in the file">
            <thead>
              <tr>
                <th>Period / identity</th>
                <th>Rows</th>
                <th>Currency</th>
                <th>Exact net</th>
                <th>Preview</th>
                <th>Persisted source / validation</th>
              </tr>
            </thead>
            <tbody>
              @for (r of p.periods; track r.periodCode) {
                <tr>
                  <td>
                    {{ r.periodCode }}<br /><code>{{ r.periodId ?? 'No reporting period' }}</code>
                  </td>
                  <td>{{ r.rowCount }}</td>
                  <td>{{ r.currency ?? 'Unknown' }}</td>
                  <td class="number">{{ r.netTotal ?? 'Unknown' }}</td>
                  <td [class.error-text]="r.error">
                    {{ r.error ?? (r.balanced ? 'Ready' : 'Not balanced') }}
                  </td>
                  <td>
                    @if (r.datasetId) {
                      <code>{{ r.datasetId }}</code
                      ><br /><audit-status [value]="r.importState ?? 'UNKNOWN'" />
                      <audit-status [value]="r.validationStatus ?? 'UNKNOWN'" />
                      @if (r.operationId) {
                        <br /><code>{{ r.operationId }}</code> · {{ r.operationState }}
                      } @else {
                        <p>No validation operation observed yet.</p>
                      }
                    } @else {
                      Not imported
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <p>
          SEALED means source rows are persisted. Only ACCEPTED validation allows mapping; GL
          completeness and source acceptance remain separate server gates.
        </p>
        @if (availableDraft()) {
          <p role="status">
            A matching upload checkpoint is available in this browser tab. File bytes and review
            assent were not saved.
          </p>
          <button matButton (click)="recover()">Recover upload checkpoint</button>
          <button matButton (click)="discardCheckpoint()">Discard upload checkpoint</button>
        }
        <div class="toolbar">
          <button matButton (click)="saveCheckpoint()" [disabled]="busy()">
            Save upload checkpoint in this tab
          </button>
          <button matButton (click)="reconcile()" [disabled]="busy() || !hasFile()">
            Refresh period receipts
          </button>
        </div>
        @if (allImported()) {
          <p role="status">
            Every period has a persisted source receipt. Observe validation before mapping.
          </p>
        } @else {
          <form (submit)="$event.preventDefault(); importFile()">
            <label
              ><input type="checkbox" [formField]="fields.reviewed" (change)="assent($event)" />I
              reviewed this exact file, every period and the current reporting-period
              identities.</label
            >
            <button matButton="filled" type="submit" [disabled]="!canImport() || !model().reviewed">
              Import reviewed periods
            </button>
          </form>
        }
        <a routerLink="/app/accounting">Open source records and acceptance</a>
      }
    </section>
  `,
  styles: [
    `
      :host {
        display: block;
        min-width: 0;
      }
      .identity,
      code {
        overflow-wrap: anywhere;
      }
      .toolbar {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
      }
      .table-scroll {
        max-width: 100%;
        overflow: auto;
      }
      table {
        width: 100%;
        min-width: 800px;
      }
      td:first-child {
        min-width: 12rem;
      }
      td,
      th {
        padding: 0.6rem;
        text-align: left;
      }
      label {
        display: block;
        margin-block: 1rem;
      }
    `,
  ],
})
export class TrialBalanceUpload {
  readonly engagementId = input.required<string>();
  readonly clientId = input.required<string>();
  readonly imported = output<void>();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly review = signal<UploadReview | null>(null);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly reconciled = signal(false);
  readonly availableDraft = signal(false);
  readonly fileName = signal('');
  readonly hasFile = signal(false);
  readonly message = signal('');
  readonly model = signal({ reviewed: false });
  readonly fields = form(this.model, (p) => disabled(p.reviewed, () => !this.canImport()));
  readonly allImported = computed(
    () => !!this.review()?.periods.length && this.review()!.periods.every((p) => !!p.datasetId),
  );
  readonly canImport = computed(
    () => !!this.review()?.canImport && !this.busy() && !this.uncertain() && !this.allImported(),
  );
  private file: File | null = null;
  private request = 0;
  private destroyed = false;
  private assentRevision = '';
  private dirty = false;
  private pendingMetadata: UploadCheckpoint | null = null;
  constructor() {
    let context = '',
      epoch = this.session.invalidation();
    effect(() => {
      const next = this.engagementId() + this.clientId(),
        n = this.session.invalidation();
      if (context !== next || epoch !== n) {
        context = next;
        epoch = n;
        untracked(() => this.clear());
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request++;
      this.file = null;
    });
  }
  private entity() {
    return `tb-upload/${this.engagementId()}`;
  }
  private scope() {
    return { entity: this.entity(), baseRevision: this.review()?.revision ?? '' };
  }
  private metadata() {
    return this.file && this.review()
      ? {
          fileName: this.file.name,
          byteCount: this.file.size,
          fileSha256: this.review()!.fileSha256,
        }
      : null;
  }
  private clear() {
    this.request++;
    this.file = null;
    this.hasFile.set(false);
    this.fileName.set('');
    this.review.set(null);
    this.fields.reviewed().reset(false);
    this.uncertain.set(false);
    this.reconciled.set(false);
    this.availableDraft.set(false);
    this.busy.set(false);
    this.message.set('');
    this.dirty = false;
    this.pendingMetadata = null;
  }
  assent(event: Event) {
    this.assentRevision = (event.target as HTMLInputElement).checked
      ? (this.review()?.revision ?? '')
      : '';
  }
  async pick(event: Event) {
    if (this.busy()) return;
    const input = event.target as HTMLInputElement,
      file = input.files?.[0] ?? null;
    if (!file) return;
    if (this.dirty && !this.uncertain() && !(await this.confirmNavigation())) {
      input.value = '';
      return;
    }
    if (
      !/\.(csv|xlsx)$/i.test(file.name) ||
      file.name.length > 255 ||
      !file.size ||
      file.size > 25 * 1024 * 1024 ||
      (/\.csv$/i.test(file.name) && file.size > 10000000)
    ) {
      this.message.set('Choose a nonempty XLSX up to 25 MiB or CSV up to 10,000,000 bytes.');
      input.value = '';
      return;
    }
    this.file = file;
    this.hasFile.set(true);
    this.fileName.set(file.name);
    this.dirty = true;
    await this.read(this.uncertain() ? 'reconcile' : 'preview');
    input.value = ''; // Reselecting the same bytes always creates a fresh server preview.
  }
  private async read(action: 'preview' | 'reconcile') {
    if (!this.file || this.busy()) return;
    const previousHash = this.pendingMetadata?.fileSha256 ?? this.review()?.fileSha256,
      expectedFile = this.file;
    const seq = ++this.request,
      epoch = this.session.invalidation();
    this.review.set(null);
    this.fields.reviewed().reset(false);
    this.reconciled.set(false);
    this.availableDraft.set(false);
    this.busy.set(true);
    this.message.set('');
    const body = new FormData();
    body.set('file', expectedFile);
    const result = await this.api.upload(
      `/api/ui/engagements/${this.engagementId()}/tb-intake/${action}`,
      body,
    );
    if (seq !== this.request || epoch !== this.session.invalidation() || this.destroyed) return;
    this.busy.set(false);
    if (!result.ok) {
      this.message.set(result.message);
      return;
    }
    try {
      const value = decode(decodeUploadReview, result.value);
      if (
        value.engagementId !== this.engagementId() ||
        value.clientId !== this.clientId() ||
        !value.periods.length ||
        (this.uncertain() && previousHash && previousHash !== value.fileSha256)
      )
        throw new Error('Wrong upload context');
      this.review.set(value);
      const saved = this.drafts.read(this.scope(), uploadCheckpoint);
      if (
        saved.state === 'ready' &&
        saved.draft.value.fileSha256 === value.fileSha256 &&
        saved.draft.value.byteCount === expectedFile.size
      ) {
        this.availableDraft.set(true);
        if (saved.draft.submissionPending) {
          this.uncertain.set(true);
          this.pendingMetadata = saved.draft.value;
        }
      } else if (saved.state === 'stale' || saved.state === 'unavailable') {
        this.message.set(
          'The saved checkpoint is unavailable or belongs to changed inputs. Review the fresh server preview.',
        );
      }
      if (action === 'reconcile') this.reconciled.set(true);
    } catch {
      this.message.set('Unsupported or mismatched upload response. No import is enabled.');
    }
  }
  reconcile() {
    return this.read('reconcile');
  }
  acknowledge() {
    if (!this.reconciled() || !this.review() || this.busy()) return;
    this.uncertain.set(false);
    this.reconciled.set(false);
    this.fields.reviewed().reset(false);
    this.drafts.clear(this.entity());
    this.availableDraft.set(false);
    this.pendingMetadata = null;
    this.message.set(
      'Persisted receipts reviewed. A new attempt still requires fresh assent and reuses exact existing sources.',
    );
  }
  saveCheckpoint() {
    const metadata = this.metadata() ?? this.pendingMetadata;
    if (
      !metadata ||
      !this.drafts.save(this.scope(), metadata, uploadCheckpoint, this.uncertain())
    ) {
      this.message.set(
        'Tab storage is unavailable. Keep this page open until the upload is reconciled.',
      );
      return false;
    }
    this.dirty = false;
    this.message.set(
      'Metadata checkpoint saved in this tab. Reselect the same file to recover; review again.',
    );
    return true;
  }
  recover() {
    const saved = this.drafts.read(this.scope(), uploadCheckpoint),
      metadata = this.metadata();
    if (
      saved.state !== 'ready' ||
      !metadata ||
      saved.draft.value.fileSha256 !== metadata.fileSha256 ||
      saved.draft.value.byteCount !== metadata.byteCount
    )
      return;
    this.fields.reviewed().reset(false);
    this.uncertain.set(saved.draft.submissionPending || this.uncertain());
    this.availableDraft.set(false);
    this.dirty = false;
    this.message.set(
      'Checkpoint recovered against the reselected file. Fresh review is required; no upload was replayed.',
    );
  }
  discardCheckpoint() {
    if (this.uncertain()) {
      this.message.set(
        'Read and acknowledge persisted receipts before discarding an unconfirmed submission.',
      );
      return;
    }
    this.drafts.clear(this.entity());
    this.availableDraft.set(false);
  }
  async importFile() {
    const review = this.review(),
      metadata = this.metadata();
    if (
      !review ||
      !metadata ||
      !this.file ||
      !this.canImport() ||
      !this.model().reviewed ||
      this.assentRevision !== review.revision
    )
      return;
    const body = new FormData();
    body.set('file', this.file);
    body.set('fileSha256', review.fileSha256);
    body.set('revision', review.revision);
    body.set('reviewed', 'true');
    this.drafts.save(this.scope(), metadata, uploadCheckpoint, true);
    this.pendingMetadata = metadata;
    const seq = ++this.request,
      epoch = this.session.invalidation();
    this.busy.set(true);
    this.fields.reviewed().reset(false);
    this.message.set('');
    const result = await this.api.upload(
      `/api/ui/engagements/${this.engagementId()}/tb-intake/import`,
      body,
    );
    if (seq !== this.request || epoch !== this.session.invalidation() || this.destroyed) return;
    this.busy.set(false);
    if (!result.ok) {
      this.uncertain.set(result.unknown);
      this.message.set(result.message);
      // A definitive rejection can still follow earlier per-period publication. Reconcile explicitly.
      if (!result.unknown) {
        this.drafts.save(this.scope(), metadata, uploadCheckpoint, false);
        this.pendingMetadata = null;
        this.review.set(null);
      }
      return;
    }
    try {
      const value = decode(decodeUploadReview, result.value);
      if (
        value.engagementId !== review.engagementId ||
        value.clientId !== review.clientId ||
        value.fileSha256 !== review.fileSha256 ||
        value.periods.length !== review.periods.length ||
        value.periods.some((p) => !p.datasetId || p.importState !== 'SEALED') ||
        review.periods.some(
          (p) =>
            !value.periods.some((v) => v.periodCode === p.periodCode && v.periodId === p.periodId),
        )
      )
        throw new Error('Wrong receipt');
      this.review.set(value);
      this.drafts.clear(this.entity());
      this.dirty = false;
      this.pendingMetadata = null;
      this.availableDraft.set(false);
      this.message.set(
        'Each period has a persisted source receipt. Background validation is separate.',
      );
      this.imported.emit();
    } catch {
      this.uncertain.set(true);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  async confirmNavigation() {
    if (this.busy()) return false;
    if (!this.dirty && !this.uncertain()) return true;
    const choice = await firstValueFrom(
      this.dialog.open(UnsavedChangesDialog, { disableClose: true }).afterClosed(),
    );
    if (choice === 'save') return this.saveCheckpoint();
    if (choice === 'discard' && !this.uncertain()) {
      this.clear();
      return true;
    }
    return false;
  }
  @HostListener('window:beforeunload', ['$event'])
  protectReload(event: BeforeUnloadEvent) {
    if (this.dirty || this.uncertain() || this.busy()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
}
