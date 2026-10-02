import {
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { disabled, form, FormField } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { decodeLedgerCatalogue } from './general-ledger-contracts';
import { UploadCheckpoint } from './tb-upload-contracts';
import { decodeGlUpload, glUploadCheckpoint, GlUploadReview } from './gl-upload-contracts';

@Component({
  selector: 'audit-gl-upload',
  imports: [...SHARED, FormField, RouterLink, MatButtonModule],
  templateUrl: './gl-upload.html',
  styles: [
    `
      :host {
        display: block;
        min-width: 0;
      }
      .toolbar {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
      }
      label {
        display: block;
        margin-block: 1rem;
      }
      p,
      dd,
      code {
        overflow-wrap: anywhere;
      }
      .table-wrap {
        overflow: auto;
        max-width: 100%;
      }
      table {
        width: 100%;
        border-collapse: collapse;
      }
      th,
      td {
        padding: 0.6rem;
        text-align: start;
        white-space: nowrap;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
      }
      dl {
        display: grid;
        grid-template-columns: minmax(100px, 180px) minmax(0, 1fr);
        gap: 0.6rem;
      }
      dd {
        margin: 0;
      }
      @media (max-width: 500px) {
        dl {
          grid-template-columns: 1fr;
        }
        dd {
          margin-bottom: 0.6rem;
        }
      }
    `,
  ],
})
export class GeneralLedgerUpload implements NavigationProtected {
  readonly id = routeGuid();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  readonly catalogue = this.api.resource(
    () => (this.id() ? `/api/ui/engagements/${this.id()}/general-ledger?page=1` : null),
    (raw) => {
      const c = decodeLedgerCatalogue(raw);
      if (c.engagementId !== this.id()) throw new Error('Wrong engagement');
      return c;
    },
  );
  readonly review = signal<GlUploadReview | null>(null);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly reconciled = signal(false);
  readonly availableDraft = signal(false);
  readonly fileName = signal('');
  readonly hasFile = signal(false);
  readonly message = signal('');
  readonly model = signal({ reviewed: false });
  readonly canImport = computed(
    () =>
      !!this.catalogue.data() && !!this.review()?.canImport && !this.busy() && !this.uncertain(),
  );
  readonly fields = form(this.model, (p) => disabled(p.reviewed, () => !this.canImport()));
  private file: File | null = null;
  private request = 0;
  private destroyed = false;
  private assentRevision = '';
  private dirty = false;
  private pending: UploadCheckpoint | null = null;
  private scopeRevision = '';
  constructor() {
    let context = '';
    effect(() => {
      const c = this.catalogue.data(),
        s = this.session.current(),
        key = `${this.id()}:${c?.clientId}:${s?.userId}:${s?.generation}:${this.session.invalidation()}`;
      if (key === context) return;
      context = key;
      untracked(() => {
        this.clear();
        if (c && s?.staff) void this.restoreScope(key);
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request++;
      this.file = null;
    });
  }
  private entity() {
    return `gl-upload/${this.id()}`;
  }
  private scope() {
    return { entity: this.entity(), baseRevision: this.scopeRevision };
  }
  private current(n: number, g: number) {
    return (
      !this.destroyed &&
      this.request === n &&
      this.session.invalidation() === g &&
      !!this.catalogue.data() &&
      !!this.session.current()?.staff
    );
  }
  private async restoreScope(key: string) {
    const n = this.request,
      g = this.session.invalidation();
    const digest = await crypto.subtle.digest(
      'SHA-256',
      new TextEncoder().encode('gl-upload-checkpoint-v1:' + key),
    );
    if (!this.current(n, g)) return;
    this.scopeRevision = Array.from(new Uint8Array(digest), (b) =>
      b.toString(16).padStart(2, '0'),
    ).join('');
    const saved = this.drafts.read(this.scope(), glUploadCheckpoint);
    if (saved.state === 'ready') {
      this.availableDraft.set(true);
      this.pending = saved.draft.value;
      this.uncertain.set(saved.draft.submissionPending);
      this.message.set(
        'A metadata checkpoint is available. Reselect the exact file; no file bytes or assent were saved.',
      );
    } else if (saved.state === 'stale') {
      this.uncertain.set(true);
      this.message.set(
        'The old checkpoint is stale. Reselect the original file and review its persisted source before continuing.',
      );
    }
  }
  private clear() {
    this.request++;
    this.file = null;
    this.review.set(null);
    this.fields.reviewed().reset(false);
    this.assentRevision = '';
    this.busy.set(false);
    this.uncertain.set(false);
    this.reconciled.set(false);
    this.availableDraft.set(false);
    this.hasFile.set(false);
    this.fileName.set('');
    this.message.set('');
    this.pending = null;
    this.dirty = false;
    this.scopeRevision = '';
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
  assent(e: Event) {
    this.assentRevision = (e.target as HTMLInputElement).checked
      ? (this.review()?.revision ?? '')
      : '';
  }
  async pick(e: Event) {
    if (this.busy() || !this.catalogue.data()) return;
    const input = e.target as HTMLInputElement,
      file = input.files?.[0];
    if (!file) return;
    if (this.dirty && this.review() && !this.uncertain() && !(await this.confirmNavigation())) {
      input.value = '';
      return;
    }
    if (
      !/\.csv$/i.test(file.name) ||
      file.name.length > 255 ||
      file.size < 1 ||
      file.size > 10000000
    ) {
      this.message.set('Choose a GL CSV file up to 10,000,000 bytes.');
      input.value = '';
      return;
    }
    this.file = file;
    this.hasFile.set(true);
    this.fileName.set(file.name);
    this.dirty = true;
    await this.read(this.uncertain() ? 'reconcile' : 'preview');
    input.value = '';
  }
  private matches(p: GlUploadReview, file: File) {
    return (
      p.engagementId === this.id() &&
      p.clientId === this.catalogue.data()?.clientId &&
      (!this.pending ||
        (p.fileSha256 === this.pending.fileSha256 && file.size === this.pending.byteCount))
    );
  }
  private async read(action: 'preview' | 'reconcile') {
    if (!this.file || this.busy() || !this.catalogue.data()) return;
    const file = this.file,
      n = ++this.request,
      g = this.session.invalidation();
    this.review.set(null);
    this.fields.reviewed().reset(false);
    this.assentRevision = '';
    this.reconciled.set(false);
    this.busy.set(true);
    this.message.set('');
    const body = new FormData();
    body.set('file', file);
    const result = await this.api.upload(`${this.base()}/${action}`, body);
    if (!this.current(n, g)) return;
    this.busy.set(false);
    if (!result.ok) {
      this.message.set(result.message);
      return;
    }
    try {
      const p = decode(decodeGlUpload, result.value);
      if (!this.matches(p, file)) throw new Error('Wrong source');
      this.review.set(p);
      if (action === 'reconcile') this.reconciled.set(true);
    } catch {
      this.review.set(null);
      this.message.set(
        'Unsupported or mismatched GL file response. Reselect the original file; no import is enabled.',
      );
    }
  }
  private base() {
    return `/api/ui/engagements/${this.id()}/general-ledger`;
  }
  reconcile() {
    return this.read('reconcile');
  }
  acknowledge() {
    if (!this.reconciled() || !this.review() || this.busy()) return;
    if (!this.drafts.clear(this.entity())) {
      this.message.set('Cannot clear the pending checkpoint. Keep the write fence and retry.');
      return;
    }
    this.uncertain.set(false);
    this.reconciled.set(false);
    this.availableDraft.set(false);
    this.pending = null;
    this.dirty = !this.review()?.importBatchId;
    this.fields.reviewed().reset(false);
    this.assentRevision = '';
    this.message.set('Persisted source reviewed. Any new action requires fresh exact-file review.');
  }
  saveCheckpoint(pending = false) {
    const m = this.metadata();
    if (
      !m ||
      !this.scopeRevision ||
      !this.drafts.save(this.scope(), m, glUploadCheckpoint, pending || this.uncertain())
    ) {
      this.message.set('Tab storage is unavailable. No import was sent; keep this page open.');
      return false;
    }
    this.pending = m;
    this.dirty = false;
    this.availableDraft.set(true);
    if (!pending)
      this.message.set(
        'Metadata checkpoint saved. File bytes, financial rows and review assent are excluded.',
      );
    return true;
  }
  discardCheckpoint() {
    if (this.busy() || this.uncertain()) return;
    if (!this.drafts.clear(this.entity())) return;
    this.pending = null;
    this.availableDraft.set(false);
    this.file = null;
    this.hasFile.set(false);
    this.fileName.set('');
    this.review.set(null);
    this.fields.reviewed().reset(false);
    this.assentRevision = '';
    this.dirty = false;
  }
  async importFile() {
    const p = this.review(),
      file = this.file;
    if (
      !p ||
      !file ||
      !this.canImport() ||
      !this.model().reviewed ||
      this.assentRevision !== p.revision ||
      !this.saveCheckpoint(true)
    )
      return;
    const n = ++this.request,
      g = this.session.invalidation();
    this.busy.set(true);
    this.fields.reviewed().reset(false);
    this.assentRevision = '';
    this.message.set('');
    const body = new FormData();
    body.set('file', file);
    body.set('fileSha256', p.fileSha256);
    body.set('revision', p.revision);
    body.set('reviewed', 'true');
    const result = await this.api.upload(`${this.base()}/import`, body);
    if (!this.current(n, g)) return;
    this.busy.set(false);
    if (!result.ok) {
      this.uncertain.set(result.unknown);
      if (!result.unknown) {
        // This single-file import is atomic: a definitive refusal committed no source.
        // Allow an explicitly corrected file rather than fencing it to rejected bytes.
        this.drafts.clear(this.entity());
        this.pending = null;
        this.availableDraft.set(false);
        this.review.set(null);
      }
      this.message.set(result.message);
      return;
    }
    try {
      const value = decode(decodeGlUpload, result.value);
      if (
        !this.matches(value, file) ||
        value.periodId !== p.periodId ||
        value.bookId !== p.bookId ||
        value.fileSha256 !== p.fileSha256 ||
        !value.importBatchId ||
        value.importState !== 'SEALED'
      )
        throw new Error('Wrong receipt');
      this.review.set(value);
      if (!this.drafts.clear(this.entity())) throw new Error('Checkpoint retained');
      this.pending = null;
      this.dirty = false;
      this.availableDraft.set(false);
      this.message.set(
        'The exact GL source is sealed and retained. Independent acceptance and completeness are separate.',
      );
    } catch {
      this.uncertain.set(true);
      this.reconciled.set(false);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain() || this.dirty) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
  async confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set(
        'Read and acknowledge the persisted GL source before leaving an unconfirmed submission.',
      );
      return false;
    }
    if (!this.dirty) return true;
    const choice = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if (choice === 'save') return this.saveCheckpoint();
    if (choice === 'discard') {
      this.discardCheckpoint();
      return true;
    }
    return false;
  }
}
