import {
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { applyEach, disabled, form, FormField, maxLength, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { decode, nullable } from '../../core/decode';
import { SessionService } from '../../core/session';
import { PendingRequestReference, TabDrafts, pendingRequestReference } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import {
  BatchChange,
  DraftSplit,
  MappingEditableDraft,
  MappingPreview,
  MappingReceipt,
  decodeMappingEditor,
  decodeMappingPreview,
  decodeMappingReceipt,
  emptySplit,
  emptyMappingDraft,
  mappingEditableDraft,
  parseMappingPaste,
  splitError,
} from './mapping-draft-contracts';

@Component({
  selector: 'audit-mapping-draft',
  imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  templateUrl: './mapping-draft.html',
  styleUrl: './mapping-draft.scss',
})
export class MappingDraft implements NavigationProtected {
  readonly id = routeGuid();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts);
  private readonly dialog = inject(MatDialog);
  private readonly element: ElementRef<HTMLElement> = inject(ElementRef);
  readonly page = signal(1);
  readonly search = signal('');
  readonly filterModel = signal({ search: '' });
  readonly filterFields = form(this.filterModel, (p) => maxLength(p.search, 80));
  readonly editor = this.api.resource(
    () =>
      this.id()
        ? `${this.url()}/editor?page=${this.page()}&search=${encodeURIComponent(this.search())}`
        : null,
    (raw) => {
      const p = decodeMappingEditor(raw);
      if (p.baseMappingId !== this.id() || p.page !== this.page())
        throw new Error('Wrong mapping editor');
      return p;
    },
  );
  readonly changes = signal<BatchChange[]>([]);
  readonly selected = signal<string[]>([]);
  readonly active = signal<{ accountCode: string; splits: DraftSplit[] }>({
    accountCode: '',
    splits: [],
  });
  readonly fields = form(this.active, (p) =>
    applyEach(p.splits, (s) => {
      required(s.destinationCode);
      maxLength(s.destinationCode, 100);
      required(s.fraction);
      maxLength(s.fraction, 8);
      required(s.rationale);
      maxLength(s.rationale, 2000);
      maxLength(s.auditArea, 100);
    }),
  );
  readonly batch = signal({ destinationCode: '', rationale: '', auditArea: '' });
  readonly batchFields = form(this.batch, (p) => {
    required(p.destinationCode);
    required(p.rationale);
    maxLength(p.rationale, 2000);
    maxLength(p.auditArea, 100);
  });
  readonly paste = signal({ text: '' });
  readonly pasteFields = form(this.paste, (p) => maxLength(p.text, 100000));
  readonly localBatch = signal<BatchChange[]>([]);
  readonly preview = signal<MappingPreview | null>(null);
  readonly receipt = signal<MappingReceipt | null>(null);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly receiptRead = signal(false);
  readonly retryExact = signal(false);
  readonly stale = signal(false);
  readonly draftState = signal('');
  readonly message = signal('');
  readonly reviewModel = signal({ reviewed: false });
  readonly reviewFields = form(this.reviewModel, (p) =>
    disabled(p.reviewed, () => !this.canCreate()),
  );
  readonly resetGeneration = signal(0);
  readonly dirty = computed(
    () =>
      this.changes().length > 0 ||
      !!this.active().accountCode ||
      this.localBatch().length > 0 ||
      !!this.paste().text ||
      this.selected().length > 0 ||
      Object.values(this.batch()).some(Boolean),
  );
  readonly editable = computed(
    () => !!this.editor.data()?.canCreate && !this.busy() && !this.uncertain() && !this.stale(),
  );
  readonly canCreate = computed(
    () =>
      !!this.preview()?.canCreate &&
      !this.busy() &&
      !this.stale() &&
      !this.active().accountCode &&
      !this.localBatch().length &&
      !this.paste().text &&
      (!this.uncertain() || this.retryExact()),
  );
  readonly undoCount = signal(0);
  private undoBuffer: BatchChange[][] = [];
  private baseRevision = '';
  private assentRevision = '';
  private pending: PendingRequestReference | null = null;
  private intent: { requestId: string; baseRevision: string; changes: BatchChange[] } | null = null;
  private request = 0;
  private destroyed = false;
  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.request++;
    });
    let context = '';
    effect(() => {
      const s = this.session.current(),
        key = `${this.id()}:${s?.firmId}:${s?.userId}:${s?.generation}:${this.session.invalidation()}`;
      if (key === context) return;
      context = key;
      untracked(() => this.clearProtected());
    });
    effect(() => {
      const p = this.editor.data(),
        error = this.editor.error();
      untracked(() => {
        if (!p) {
          this.clearReview();
          if (error)
            this.message.set(
              'The current scoped base is unavailable. Refresh before another action.',
            );
          return;
        }
        if (!this.baseRevision) this.baseRevision = p.revision;
        else if (this.baseRevision !== p.revision) {
          if (this.dirty() || this.uncertain()) this.stale.set(true);
          else {
            this.baseRevision = p.revision;
            this.stale.set(false);
          }
          this.clearReview();
        }
        const pending = this.drafts.readPendingRequest({
          entity: this.pendingEntity(),
          baseRevision: p.revision,
        });
        if (pending.state === 'ready') {
          this.pending = pending.draft.value;
          this.uncertain.set(true);
          this.message.set(
            'An earlier creation request needs a persisted receipt read. It will not be repeated automatically.',
          );
        } else if (pending.state === 'stale' || pending.state === 'unavailable') {
          this.uncertain.set(true);
          this.message.set(
            'The pending checkpoint is unreadable. Keep this fence and restore tab storage before another action.',
          );
        }
        const draft = this.drafts.read(
          { entity: this.entity(), baseRevision: p.revision },
          mappingEditableDraft,
        );
        this.draftState.set(draft.state);
      });
    });
  }
  private url() {
    return `/api/ui/accounting/mappings/${this.id()}`;
  }
  private entity() {
    return `mapping-batch/${this.id()}/fields`;
  }
  private pendingEntity() {
    return `mapping-batch/${this.id()}/pending`;
  }
  private clearReview() {
    this.preview.set(null);
    this.intent = null;
    this.retryExact.set(false);
    this.clearAssent();
  }
  private clearAssent() {
    this.reviewFields.reviewed().reset(false);
    this.assentRevision = '';
    this.resetGeneration.update((n) => n + 1);
  }
  private clearProtected() {
    this.request++;
    this.busy.set(false);
    this.uncertain.set(false);
    this.receiptRead.set(false);
    this.receipt.set(null);
    this.stale.set(false);
    this.pending = null;
    this.baseRevision = '';
    this.changes.set([]);
    this.selected.set([]);
    this.active.set({ accountCode: '', splits: [] });
    this.batch.set({ destinationCode: '', rationale: '', auditArea: '' });
    this.paste.set({ text: '' });
    this.localBatch.set([]);
    this.undoBuffer = [];
    this.undoCount.set(0);
    this.draftState.set('');
    this.message.set('');
    this.clearReview();
  }
  private current(n: number, epoch: number) {
    return !this.destroyed && n === this.request && epoch === this.session.invalidation();
  }
  select(code: string, e: Event) {
    if (!this.editable()) return;
    const on = (e.target as HTMLInputElement).checked;
    if (on && this.selected().length >= 200) {
      this.message.set('Select at most 200 exact accounts.');
      (e.target as HTMLInputElement).checked = false;
      return;
    }
    this.selected.update((xs) => (on ? [...new Set([...xs, code])] : xs.filter((x) => x !== code)));
  }
  edit(code: string) {
    if (!this.editable() || this.active().accountCode) {
      this.message.set('Stage or cancel the active account edit first.');
      return;
    }
    const p = this.editor.data(),
      account = p?.accounts.find((x) => x.accountCode === code);
    if (!account) return;
    const staged = this.changes().find((x) => x.accountCode === code);
    this.active.set({
      accountCode: code,
      splits: staged
        ? structuredClone(staged.splits)
        : account.allocations.length
          ? account.allocations.map((a) => ({
              destinationCode: a.destinationCode,
              fraction: a.fraction,
              rationale: a.rationale,
              auditArea: a.auditArea ?? '',
            }))
          : [emptySplit()],
    });
    this.clearReview();
    setTimeout(() =>
      this.element.nativeElement.querySelector<HTMLElement>('#active-destination-0')?.focus(),
    );
  }
  addSplit() {
    if (this.editable() && this.active().splits.length < 20)
      this.active.update((x) => ({ ...x, splits: [...x.splits, emptySplit()] }));
  }
  removeSplit(i: number) {
    if (this.editable() && this.active().splits.length > 1)
      this.active.update((x) => ({ ...x, splits: x.splits.filter((_, n) => n !== i) }));
  }
  cancelEdit() {
    this.active.set({ accountCode: '', splits: [] });
    this.clearReview();
  }
  stage() {
    if (!this.editable() || !this.active().accountCode) return;
    const error = splitError(
      this.active().splits,
      this.editor.data()!.destinations.map((x) => x.code),
    );
    if (error) {
      this.message.set(error);
      return;
    }
    if (this.merge([structuredClone(this.active())])) {
      this.cancelEdit();
      this.message.set(
        'Account change staged locally. Review the complete server preview before creating a version.',
      );
    }
  }
  private merge(incoming: BatchChange[]) {
    const map = new Map(this.changes().map((x) => [x.accountCode, x]));
    incoming.forEach((x) => map.set(x.accountCode, x));
    if (map.size > 200) {
      this.message.set('A batch contains at most 200 distinct source accounts.');
      return false;
    }
    this.undoBuffer.push(structuredClone(this.changes()));
    if (this.undoBuffer.length > 20) this.undoBuffer.shift();
    this.undoCount.set(this.undoBuffer.length);
    this.changes.set([...map.values()].sort((a, b) => a.accountCode.localeCompare(b.accountCode)));
    this.clearReview();
    return true;
  }
  undo() {
    if (!this.editable() || this.active().accountCode) return;
    const value = this.undoBuffer.pop();
    if (!value) return;
    this.changes.set(value);
    this.undoCount.set(this.undoBuffer.length);
    this.clearReview();
  }
  removeChange(code: string) {
    if (!this.editable()) return;
    this.undoBuffer.push(structuredClone(this.changes()));
    if (this.undoBuffer.length > 20) this.undoBuffer.shift();
    this.undoCount.set(this.undoBuffer.length);
    this.changes.update((xs) => xs.filter((x) => x.accountCode !== code));
    this.clearReview();
  }
  previewSelected() {
    if (!this.editable() || !this.selected().length || this.active().accountCode) return;
    const split = { ...this.batch(), fraction: '1' };
    const error = splitError(
      [split],
      this.editor.data()!.destinations.map((x) => x.code),
    );
    if (error) {
      this.message.set(error);
      return;
    }
    this.localBatch.set(
      this.selected().map((accountCode) => ({ accountCode, splits: [{ ...split }] })),
    );
    this.clearReview();
  }
  previewPaste() {
    if (!this.editable() || this.active().accountCode) return;
    try {
      const changes = parseMappingPaste(this.paste().text);
      for (const c of changes) {
        const error = splitError(
          c.splits,
          this.editor.data()!.destinations.map((x) => x.code),
        );
        if (error) throw new Error(error);
      }
      this.localBatch.set(changes);
      this.clearReview();
    } catch (e) {
      this.localBatch.set([]);
      this.message.set(e instanceof Error ? e.message : 'The pasted rows are invalid.');
    }
  }
  stageBatch() {
    if (
      this.editable() &&
      this.localBatch().length &&
      this.merge(structuredClone(this.localBatch()))
    ) {
      this.localBatch.set([]);
      this.paste.set({ text: '' });
      this.selected.set([]);
    }
  }
  cancelBatch() {
    this.localBatch.set([]);
  }
  keyboard(e: KeyboardEvent, code: string) {
    if (e.key === 'Enter' || e.key === 'F2') {
      e.preventDefault();
      this.edit(code);
    }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const buttons = [
        ...this.element.nativeElement.querySelectorAll<HTMLButtonElement>('[data-account-edit]'),
      ];
      const i = buttons.indexOf(e.currentTarget as HTMLButtonElement);
      buttons[
        Math.max(0, Math.min(buttons.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))
      ]?.focus();
    }
  }
  filter() {
    if (
      this.busy() ||
      this.uncertain() ||
      this.active().accountCode ||
      this.filterFields().invalid()
    )
      return;
    this.page.set(1);
    this.search.set(this.filterModel().search);
    this.clearReview();
  }
  changePage(delta: number) {
    const p = this.editor.data();
    if (!p || this.busy() || this.uncertain() || this.active().accountCode) return;
    const next = p.page + delta;
    if (next < 1 || (next - 1) * 25 >= p.filteredCount) return;
    this.page.set(next);
    this.clearReview();
  }
  refresh() {
    if (this.busy()) return;
    this.clearReview();
    this.editor.reload();
  }
  reviewCurrentBase() {
    if (!this.editor.data() || this.busy() || this.uncertain()) return;
    this.baseRevision = this.editor.data()!.revision;
    this.stale.set(false);
    this.clearReview();
    this.message.set(
      'Retained edits now require a new complete server preview against the current base.',
    );
  }
  private editableDraft(): MappingEditableDraft {
    return {
      changes: structuredClone(this.changes()),
      active: this.active().accountCode ? structuredClone(this.active()) : null,
      selected: [...this.selected()],
      batch: { ...this.batch() },
      paste: this.paste().text,
      localBatch: structuredClone(this.localBatch()),
    };
  }
  saveDraft() {
    if (!this.editor.data() || this.busy() || this.uncertain()) return false;
    const ok = this.drafts.save(
      { entity: this.entity(), baseRevision: this.baseRevision },
      this.editableDraft(),
      mappingEditableDraft,
    );
    this.message.set(
      ok
        ? 'Convenience draft saved in this tab for four hours. No mapping version or approval was saved.'
        : 'Tab storage is unavailable or the draft is too large. Keep editing; no draft was saved.',
    );
    if (ok) this.draftState.set('ready');
    return ok;
  }
  recoverDraft() {
    const p = this.editor.data();
    if (!p || this.busy() || this.uncertain() || this.dirty()) return;
    const saved = this.drafts.read(
      { entity: this.entity(), baseRevision: p.revision },
      mappingEditableDraft,
    );
    if (saved.state !== 'ready' || saved.draft.submissionPending) {
      this.message.set(
        'Draft recovery requires the same identity, epoch and fresh base. Stale fields were not applied.',
      );
      return;
    }
    this.changes.set(saved.draft.value.changes);
    this.active.set(saved.draft.value.active ?? { accountCode: '', splits: [] });
    this.selected.set(saved.draft.value.selected);
    this.batch.set(saved.draft.value.batch);
    this.paste.set({ text: saved.draft.value.paste });
    this.localBatch.set(saved.draft.value.localBatch);
    this.baseRevision = p.revision;
    this.clearReview();
    this.message.set(
      'Recovered convenience fields. Review all changes again before creating a new version.',
    );
  }
  discardDraft() {
    if (this.busy() || this.uncertain() || !this.drafts.clear(this.entity())) return false;
    this.changes.set([]);
    this.cancelEdit();
    this.localBatch.set([]);
    this.paste.set({ text: '' });
    this.selected.set([]);
    this.batch.set({ destinationCode: '', rationale: '', auditArea: '' });
    this.undoBuffer = [];
    this.undoCount.set(0);
    this.draftState.set('absent');
    return true;
  }
  async reviewBatch() {
    if (
      !this.editable() ||
      !this.changes().length ||
      this.active().accountCode ||
      this.localBatch().length
    )
      return;
    await this.loadPreview({
      requestId: crypto.randomUUID(),
      baseRevision: this.baseRevision,
      changes: structuredClone(this.changes()),
    });
  }
  private async loadPreview(intent: NonNullable<MappingDraft['intent']>, expectedHash?: string) {
    const n = ++this.request,
      epoch = this.session.invalidation();
    this.busy.set(true);
    this.clearReview();
    this.message.set('');
    const r = await this.api.command(`${this.url()}/draft-preview`, intent);
    if (!this.current(n, epoch)) return;
    this.busy.set(false);
    if (!r.ok) {
      this.message.set(r.message);
      if (r.status === 409) {
        this.stale.set(true);
        this.editor.reload();
      }
      return;
    }
    try {
      const p = decode(decodeMappingPreview, r.value);
      if (
        p.baseMappingId !== this.id() ||
        p.requestId !== intent.requestId ||
        p.baseRevision !== intent.baseRevision ||
        p.changedAccountCount !== intent.changes.length ||
        p.changes.length !== intent.changes.length ||
        p.allocationCount > 5000 ||
        p.canCreate === !!p.blocker ||
        new Set(p.changes.map((x) => x.accountCode)).size !== p.changes.length ||
        p.changes.some(
          (x) =>
            !intent.changes.some((c) => c.accountCode === x.accountCode) ||
            x.after.some((a) => a.sourceAccountCode !== x.accountCode),
        ) ||
        (expectedHash && p.requestHash !== expectedHash)
      )
        throw new Error('Wrong batch preview');
      this.intent = intent;
      this.preview.set(p);
      this.retryExact.set(!!expectedHash);
    } catch {
      this.message.set(
        'The batch preview was unsupported or did not match the exact request. No version was created.',
      );
    }
  }
  assent(e: Event) {
    this.assentRevision = (e.target as HTMLInputElement).checked
      ? (this.preview()?.revision ?? '')
      : '';
  }
  async create() {
    const p = this.preview(),
      intent = this.intent,
      s = this.session.current();
    if (
      !p ||
      !intent ||
      !s ||
      !this.canCreate() ||
      !this.reviewModel().reviewed ||
      this.assentRevision !== p.revision
    )
      return;
    const wasUncertain = this.uncertain();
    if (
      wasUncertain &&
      (this.pending?.requestId !== intent.requestId || this.pending?.requestHash !== p.requestHash)
    )
      return;
    const reference = { requestId: intent.requestId, requestHash: p.requestHash };
    if (
      !this.drafts.save(
        { entity: this.entity(), baseRevision: intent.baseRevision },
        { ...emptyMappingDraft(), changes: intent.changes },
        mappingEditableDraft,
      ) ||
      !this.drafts.save(
        { entity: this.pendingEntity(), baseRevision: intent.baseRevision },
        reference,
        pendingRequestReference,
        true,
      )
    ) {
      this.message.set('Tab storage is unavailable. No mapping creation was sent.');
      return;
    }
    this.pending = reference;
    const n = ++this.request,
      epoch = this.session.invalidation();
    this.busy.set(true);
    this.clearAssent();
    const r = await this.api.command(`${this.url()}/draft`, {
      intent,
      revision: p.revision,
      reviewed: true,
    });
    if (!this.current(n, epoch)) return;
    this.busy.set(false);
    if (!r.ok) {
      this.uncertain.set(wasUncertain || r.unknown);
      this.retryExact.set(false);
      this.receiptRead.set(false);
      this.message.set(r.message);
      if (!this.uncertain() && !this.drafts.clear(this.pendingEntity())) this.uncertain.set(true);
      if (!this.uncertain()) this.pending = null;
      if (r.status === 409) this.stale.set(true);
      this.clearReview();
      this.editor.reload();
      return;
    }
    try {
      const receipt = decode(decodeMappingReceipt, r.value);
      this.validateReceipt(receipt);
      if (!this.clearCheckpoints()) throw new Error('Storage fence');
      this.changes.set([]);
      this.active.set({ accountCode: '', splits: [] });
      this.localBatch.set([]);
      this.selected.set([]);
      this.paste.set({ text: '' });
      this.batch.set({ destinationCode: '', rationale: '', auditArea: '' });
      this.undoBuffer = [];
      this.undoCount.set(0);
      this.receipt.set(receipt);
      this.uncertain.set(false);
      this.pending = null;
      this.clearReview();
      this.message.set('New mapping version retained. Independent approval is a separate review.');
      this.editor.reload();
    } catch {
      this.uncertain.set(true);
      this.retryExact.set(false);
      this.receiptRead.set(false);
      this.message.set(UNKNOWN_OUTCOME);
    }
  }
  private validateReceipt(r: MappingReceipt) {
    if (
      !this.pending ||
      r.baseMappingId !== this.id() ||
      r.requestId !== this.pending.requestId ||
      r.requestHash !== this.pending.requestHash ||
      r.createdBy !== this.session.current()?.userId ||
      r.mappingId === r.baseMappingId ||
      r.version <= (this.editor.data()?.baseVersion ?? Number.MAX_SAFE_INTEGER)
    )
      throw new Error('Wrong receipt');
  }
  private clearCheckpoints() {
    // Delete editable convenience data first. A second storage failure must retain
    // the metadata fence so reloading still resolves the accepted request by receipt.
    return this.drafts.clear(this.entity()) && this.drafts.clear(this.pendingEntity());
  }
  async readReceipt() {
    if (!this.editor.data() || !this.pending || this.busy()) return;
    const n = ++this.request,
      epoch = this.session.invalidation();
    this.busy.set(true);
    this.receiptRead.set(false);
    this.clearReview();
    try {
      const receipt = await this.api.get(
        `${this.url()}/draft-receipts/${this.pending.requestId}`,
        nullable(decodeMappingReceipt),
      );
      if (!this.current(n, epoch)) return;
      if (receipt) this.validateReceipt(receipt);
      this.receipt.set(receipt);
      this.receiptRead.set(true);
      this.message.set(
        receipt
          ? 'Persisted creation receipt loaded. Acknowledge it before leaving this fence.'
          : 'No receipt is confirmed yet. Read again, or explicitly review the same retained request. A new request is blocked.',
      );
    } catch {
      if (this.current(n, epoch))
        this.message.set(
          'The persisted receipt could not be verified. Keep this fence and retry the read.',
        );
    } finally {
      if (this.current(n, epoch)) this.busy.set(false);
    }
  }
  acknowledgeReceipt() {
    if (!this.receiptRead() || !this.receipt() || this.busy() || !this.clearCheckpoints()) return;
    this.uncertain.set(false);
    this.pending = null;
    this.changes.set([]);
    this.cancelEdit();
    this.localBatch.set([]);
    this.selected.set([]);
    this.paste.set({ text: '' });
    this.batch.set({ destinationCode: '', rationale: '', auditArea: '' });
    this.undoBuffer = [];
    this.undoCount.set(0);
    this.stale.set(false);
    this.baseRevision = this.editor.data()?.revision ?? '';
    this.message.set(
      'Persisted creation acknowledged. Independent approval remains a separate action.',
    );
  }
  async reviewSameRequest() {
    const p = this.editor.data();
    if (!p || !this.pending || !this.receiptRead() || this.receipt() || this.busy()) return;
    const saved = this.drafts.read(
      { entity: this.entity(), baseRevision: p.revision },
      mappingEditableDraft,
    );
    if (saved.state !== 'ready' || saved.draft.value.active || !saved.draft.value.changes.length) {
      this.message.set(
        'The exact original fields are unavailable against the current base. Keep the fence and read the receipt again.',
      );
      return;
    }
    this.changes.set(saved.draft.value.changes);
    this.baseRevision = p.revision;
    this.stale.set(false);
    await this.loadPreview(
      {
        requestId: this.pending.requestId,
        baseRevision: p.revision,
        changes: saved.draft.value.changes,
      },
      this.pending.requestHash,
    );
  }
  async confirmNavigation() {
    if (this.busy() || this.uncertain()) {
      this.message.set(
        'Resolve the persisted creation receipt before leaving an unconfirmed request.',
      );
      return false;
    }
    if (!this.dirty()) return true;
    const decision = await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    return decision === 'save'
      ? this.saveDraft()
      : decision === 'discard'
        ? this.discardDraft()
        : false;
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(e: BeforeUnloadEvent) {
    if (this.busy() || this.uncertain() || this.dirty()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
}
